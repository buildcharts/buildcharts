using BuildCharts.Tool.Configuration.Models;
using BuildCharts.Tool.Plugins.Helpers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace BuildCharts.Tool.Plugins.MavenAuthenticate_v1;

/// <summary>
/// Obtains Maven credentials for Azure Artifacts feeds by invoking the cross-platform Azure Artifacts Credential Provider.
///
/// The helper delegates all authentication to MSAL-enabled flows supported by the provider and automatically tries, in order:
/// - Environment-supplied service-principal or managed-identity tokens.
/// - Silent Integrated Windows Authentication or cached MSAL tokens.
/// - Interactive browser or device-code sign-in as a last resort.
/// </summary>
public class MavenAuthenticatePlugin : IBuildChartsPlugin
{
    public string Name => "MavenAuthenticate@v1";
    public string[] FilterDomains { get; set; } = ["pkgs.dev.azure.com", "pkgs.visualstudio.com"];

    public async Task OnBeforeGenerateAsync(BuildConfig buildConfig, CancellationToken ct)
    {
        try
        {
            var sources = await GetPomSources(ct);
            if (!sources.Any())
            {
                Console.WriteLine("No Azure DevOps package sources found in Maven config");
                return;
            }

            foreach (var source in sources.Select(source => source.Value).Distinct())
            {
                Console.WriteLine($"Found feed for {source}");
            }

            // Preserve proxies, mirrors, and profiles when adding credentials, as Azure Pipelines does.
            var workingFile = new FileInfo(Environment.GetEnvironmentVariable("MAVEN_AUTHENTICATE_WORKING_FILE") ?? ".mvn/settings.xml");
            XDocument settings;
            if (workingFile.Exists)
            {
                if (!workingFile.Name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) || workingFile.LinkTarget != null)
                {
                    throw new InvalidOperationException("MavenAuthenticate requires a regular Maven settings XML file.");
                }

                await using var settingsStream = workingFile.OpenRead();
                settings = await XDocument.LoadAsync(settingsStream, LoadOptions.None, ct);
                if (settings.Root.Name.LocalName != "settings")
                {
                    throw new InvalidOperationException("Expected a Maven settings document in the working file.");
                }
            }
            else
            {
                settings = new XDocument(new XElement(XName.Get("settings", "http://maven.apache.org/SETTINGS/1.0.0")));
            }

            var ns = settings.Root.Name.Namespace;
            var servers = settings.Root.Element(ns + "servers");
            if (servers == null)
            {
                servers = new XElement(ns + "servers");
                settings.Root.Add(servers);
            }

            // Prefer environment-supplied credentials (non-interactive in CI) to avoid device flow.
            var (endpointsEnv, endpointsSource) = GetEnv("ARTIFACTS_CREDENTIALPROVIDER_EXTERNAL_FEED_ENDPOINTS", "VSS_NUGET_EXTERNAL_FEED_ENDPOINTS");
            var (tokenEnv, tokenSource) = GetEnv("ARTIFACTS_CREDENTIALPROVIDER_ACCESSTOKEN", "VSS_NUGET_ACCESSTOKEN", "SYSTEM_ACCESSTOKEN", "AZURE_ARTIFACTS_ENV_ACCESS_TOKEN");

            if (!string.IsNullOrWhiteSpace(endpointsEnv))
            {
                Console.WriteLine($"Using {endpointsSource} from environment");

                using var document = JsonDocument.Parse(endpointsEnv);
                foreach (var endpoint in document.RootElement.GetProperty("endpointCredentials").EnumerateArray())
                {
                    var uri = new Uri(endpoint.GetProperty("endpoint").GetString());
                    var normalized = new UriBuilder(uri) { Path = uri.AbsolutePath.TrimEnd('/') + "/" }.Uri.AbsoluteUri;
                    foreach (var source in sources.Where(source => source.Value.AbsoluteUri == normalized))
                    {
                        var username = endpoint.TryGetProperty("username", out var value) ? value.GetString() : "AzureDevOps";
                        WriteCredentials(servers, source.Key, username, endpoint.GetProperty("password").GetString());
                    }
                }
            }
            else if (!string.IsNullOrWhiteSpace(tokenEnv))
            {
                Console.WriteLine($"Using {tokenSource} from environment");

                foreach (var source in sources)
                {
                    WriteCredentials(servers, source.Key, "AzureDevOps", tokenEnv);
                }
            }
            else
            {
                var serverEntries = servers.Elements(ns + "server");
                var missingSources = sources.Where(source => !HasCredentials(serverEntries, source.Key)).ToArray();
                if (missingSources.Length > 0)
                {
                    // Ensure the credential provider is downloaded and fetch token
                    var folder = Path.Combine(Directory.GetCurrentDirectory(), ".buildcharts", "plugins", Name, "microsoft-artifacts-credprovider");
                    var version = Environment.GetEnvironmentVariable("MAVEN_AUTHENTICATE_HELPER_VERSION");
                    folder = await AzureArtifactsCredentialProvider.EnsureInstalledAsync(folder, ct, version);
                    var hasFeedIdentities = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ARTIFACTS_CREDENTIALPROVIDER_FEED_ENDPOINTS"));

                    // Generate the settings.xml credentials used by Maven when resolving packages.
                    // Fetch credentials once per organization, or once per feed when feed identities are configured.
                    var sourceGroups = hasFeedIdentities
                        ? missingSources.GroupBy(source => source.Value.AbsoluteUri, StringComparer.Ordinal)
                        : missingSources.GroupBy(source => GetOrganizationUrl(source.Value), StringComparer.OrdinalIgnoreCase);
                    foreach (var group in sourceGroups)
                    {
                        Console.WriteLine($"Fetching credentials for {group.Key} via Azure Artifacts Credential Provider");
                        var feed = group.First().Value;
                        var path = feed.AbsolutePath.TrimEnd('/');
                        var endpoint = path.EndsWith("/maven/v1", StringComparison.Ordinal) ? new Uri(feed, path[..^"/maven/v1".Length] + "/nuget/v3/index.json") : feed;
                        var token = await AzureArtifactsCredentialProvider.FetchTokenForFeedAsync(folder, endpoint, ct);

                        foreach (var source in group)
                        {
                            WriteCredentials(servers, source.Key, "AzureDevOps", token);
                        }
                    }
                }
            }

            WriteValueToFile("settings.xml", settings.ToString());
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"{Name} failed with error: {ex}");
            throw;
        }
    }

    public Task OnAfterGenerateAsync(BuildConfig buildConfig, ChartConfig chartConfig, StringBuilder hclStringBuilder, CancellationToken ct)
    {
        var secretPath = Path.Combine(".buildcharts", "secrets", "settings.xml");
        var workingFile = Environment.GetEnvironmentVariable("MAVEN_AUTHENTICATE_WORKING_FILE") ?? ".mvn/settings.xml";
        if (!File.Exists(secretPath) && !File.Exists(workingFile))
        {
            return Task.CompletedTask;
        }

        if (!File.Exists(secretPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(secretPath));
            File.Copy(workingFile, secretPath);
        }

        BakeHclHelper.AddSecret(hclStringBuilder, "type=file,id=settings.xml,src=.buildcharts/secrets/settings.xml");
        return Task.CompletedTask;
    }

    private static void WriteValueToFile(string key, string value)
    {
        var folder = Path.Combine(".buildcharts", "secrets");
        var filePath = Path.Combine(folder, key);

        Directory.CreateDirectory(folder);
        File.WriteAllText(filePath, value);

        Console.WriteLine($"Successfully created file '{filePath}'");
    }

    private static (string Value, string Source) GetEnv(params string[] names)
    {
        foreach (var name in names)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return (value, name);
            }
        }

        return (null, null);
    }

    private static string GetOrganizationUrl(Uri source)
    {
        if (source == null)
        {
            return "";
        }

        if (source.Host.EndsWith("visualstudio.com", StringComparison.OrdinalIgnoreCase))
        {
            var subdomain = source.Host.Split('.', 2)[0];
            return $"{source.Scheme}://{subdomain}.visualstudio.com";
        }

        var segments = source.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var org = segments.Length > 0 ? segments[0] : "";
        return $"{source.Scheme}://{source.Host}/{org}";
    }

    private async Task<KeyValuePair<string, Uri>[]> GetPomSources(CancellationToken ct)
    {
        var file = new FileInfo(Environment.GetEnvironmentVariable("MAVEN_AUTHENTICATE_POM_FILE") ?? "pom.xml");
        if (!file.Exists)
        {
            return [];
        }
        if (!file.Name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) || file.LinkTarget != null)
        {
            throw new InvalidOperationException($"MavenAuthenticate requires a {file.Name} file.");
        }

        await using var stream = file.OpenRead();
        var project = (await XDocument.LoadAsync(stream, LoadOptions.None, ct)).Root;
        var ns = project.Name.Namespace;
        var sources = new Dictionary<string, Uri>(StringComparer.Ordinal);
        foreach (var repository in project.Element(ns + "repositories")?.Elements(ns + "repository") ?? [])
        {
            var id = ((string)repository.Element(ns + "id"))?.Trim();
            if (string.IsNullOrEmpty(id))
            {
                continue;
            }

            var value = ((string)repository.Element(ns + "url"))?.Trim();
            if (!Uri.TryCreate(value, UriKind.Absolute, out var source) || source.Scheme is not ("http" or "https") || source.UserInfo.Length > 0)
            {
                throw new InvalidOperationException("A Maven repository must be an absolute HTTP or HTTPS URL without embedded credentials.");
            }
            sources[id] = new UriBuilder(source) { Path = source.AbsolutePath.TrimEnd('/') + "/" }.Uri;
        }
        if (sources.Values.GroupBy(source => source.Authority, StringComparer.OrdinalIgnoreCase).Any(group => group.Select(source => source.Scheme).Distinct().Count() > 1))
        {
            throw new InvalidOperationException("Do not mix HTTP and HTTPS Maven repositories on the same host and port.");
        }

        return sources
            .Where(source => FilterDomains.Any(domain => source.Value.Host.Equals(domain, StringComparison.OrdinalIgnoreCase) || source.Value.Host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
    }

    private static bool HasCredentials(IEnumerable<XElement> servers, string id)
    {
        var server = servers.SingleOrDefault(element => (string)element.Element(element.Name.Namespace + "id") == id);
        var ns = server?.Name.Namespace;
        return !string.IsNullOrWhiteSpace((string)server?.Element(ns + "username"))
            && !string.IsNullOrWhiteSpace((string)server?.Element(ns + "password"));
    }

    private static void WriteCredentials(XElement servers, string id, string username, string password)
    {
        if (new[] { username, password }.Any(value => string.IsNullOrWhiteSpace(value) || value.Contains('\r') || value.Contains('\n')))
        {
            throw new InvalidOperationException("A Maven credential is missing or contains a line break.");
        }

        var ns = servers.Name.Namespace;
        var server = servers.Elements(ns + "server").SingleOrDefault(element => (string)element.Element(ns + "id") == id);
        if (server == null)
        {
            server = new XElement(ns + "server", new XElement(ns + "id", id));
            servers.Add(server);
        }

        server.SetElementValue(ns + "username", username);
        server.SetElementValue(ns + "password", password);
    }
}
