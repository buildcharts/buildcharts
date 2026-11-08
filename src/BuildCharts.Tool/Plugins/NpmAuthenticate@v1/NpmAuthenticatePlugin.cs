using BuildCharts.Tool.Configuration.Models;
using BuildCharts.Tool.Plugins.Helpers;
using BuildCharts.Tool.Plugins.NpmAuthenticate_v1.Helpers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace BuildCharts.Tool.Plugins.NpmAuthenticate_v1;

/// <summary>
/// Obtains npm credentials for Azure Artifacts feeds by invoking the cross-platform Azure Artifacts Credential Provider.
///
/// The helper delegates all authentication to MSAL-enabled flows supported by the provider and automatically tries, in order:
/// - Environment-supplied service-principal or managed-identity tokens.
/// - Silent Integrated Windows Authentication or cached MSAL tokens.
/// - Interactive browser or device-code sign-in as a last resort.
/// </summary>
public class NpmAuthenticatePlugin : IBuildChartsPlugin
{
    public string Name => "NpmAuthenticate@v1";
    public string[] FilterDomains { get; set; } = ["pkgs.dev.azure.com", "pkgs.visualstudio.com"];

    public async Task OnBeforeGenerateAsync(BuildConfig buildConfig, CancellationToken ct)
    {
        try
        {
            var (sources, lines) = await GetNpmSources(ct);
            if (!sources.Any())
            {
                Console.WriteLine("No Azure DevOps package sources found in npm config");
                return;
            }

            foreach (var source in sources)
            {
                Console.WriteLine($"Found feed for {source}");
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
                    var normalized = new UriBuilder(uri) { Path = uri.AbsolutePath.TrimEnd('/') + "/" }.Uri;
                    var source = sources.FirstOrDefault(source => source == normalized);
                    if (source != null)
                    {
                        var username = endpoint.TryGetProperty("username", out var value) ? value.GetString() : "buildcharts";
                        WriteCredentials(lines, source, username, endpoint.GetProperty("password").GetString());
                    }
                }
            }
            else if (!string.IsNullOrWhiteSpace(tokenEnv))
            {
                Console.WriteLine($"Using {tokenSource} from environment");
                foreach (var source in sources)
                {
                    WriteCredentials(lines, source, "buildcharts", tokenEnv);
                }
            }
            else
            {
                var missingSources = sources.Where(source => !HasCredentials(lines, source)).ToArray();
                if (missingSources.Length > 0)
                {
                    // Ensure the credential provider is downloaded and fetch token
                    var folder = Path.Combine(Directory.GetCurrentDirectory(), ".buildcharts", "plugins", Name, "microsoft-artifacts-credprovider");
                    var version = Environment.GetEnvironmentVariable("NPM_AUTHENTICATE_HELPER_VERSION");
                    folder = await NpmCredentialProviderHelper.EnsureInstalledAsync(folder, ct, version);

                    // Generate the npmrc credentials used by npm when installing packages.
                    foreach (var sourceByOrganizationUrl in missingSources.GroupBy(GetOrganizationUrl, StringComparer.OrdinalIgnoreCase))
                    {
                        // Only fetch credentials once per organization.
                        Console.WriteLine($"Fetching credentials for {sourceByOrganizationUrl.Key} via Azure Artifacts Credential Provider");
                        var token = await FetchTokenForFeedAsync(folder, sourceByOrganizationUrl.First(), ct);

                        foreach (var source in sourceByOrganizationUrl)
                        {
                            WriteCredentials(lines, source, "buildcharts", token);
                        }
                    }
                }
            }

            WriteValueToFile("npmrc", string.Join('\n', lines).TrimEnd('\n') + "\n");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"{Name} failed with error: {ex}");
            throw;
        }
    }

    public Task OnAfterGenerateAsync(BuildConfig buildConfig, ChartConfig cartConfig, StringBuilder hclStringBuilder, CancellationToken ct)
    {
        var secretPath = Path.Combine(".buildcharts", "secrets", "npmrc");
        var workingFile = Environment.GetEnvironmentVariable("NPM_AUTHENTICATE_WORKING_FILE") ?? ".npmrc";
        if (!File.Exists(secretPath) && !File.Exists(workingFile))
        {
            return Task.CompletedTask;
        }

        if (!File.Exists(secretPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(secretPath));
            File.Copy(workingFile, secretPath);
        }
        BakeHclHelper.AddSecret(hclStringBuilder, "type=file,id=npmrc,src=.buildcharts/secrets/npmrc");
        return Task.CompletedTask;
    }

    private static async Task<string> FetchTokenForFeedAsync(string targetFolder, Uri source, CancellationToken ct)
    {
        var token = await NpmCredentialProviderHelper.FetchCredentialsAsync(targetFolder, source, isRetry: false, ct);
        if (!await IsTokenValidAsync(source, token, ct))
        {
            if (Environment.GetEnvironmentVariable("BUILDSCHARTS_VERBOSE") == "1")
            {
                Console.WriteLine("Credential Provider returned invalid credentials. Retrying with IsRetry forcing a refresh...");
            }

            token = await NpmCredentialProviderHelper.FetchCredentialsAsync(targetFolder, source, isRetry: true, ct);

            if (!await IsTokenValidAsync(source, token, ct))
            {
                throw new InvalidOperationException($"Credential Provider returned invalid credentials after retry for {source}.");
            }
        }

        return token;
    }

    private static async Task<bool> IsTokenValidAsync(Uri feedUrl, string token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        using var http = new HttpClient();
        var authValue = Convert.ToBase64String(Encoding.ASCII.GetBytes($"docker:{token}"));

        using var request = new HttpRequestMessage(HttpMethod.Get, feedUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", authValue);

        using var response = await http.SendAsync(request, ct);
        return response.StatusCode == HttpStatusCode.OK;
    }

    private static void WriteValueToFile(string key, string value)
    {
        var folder = Path.Combine(Directory.GetCurrentDirectory(), ".buildcharts", "secrets");
        var filePath = Path.Combine(folder, key);

        Directory.CreateDirectory(folder);
        File.WriteAllText(filePath, value, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        Console.WriteLine($"Successfully created file '{Path.GetRelativePath(Directory.GetCurrentDirectory(), filePath)}'");
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

    private async Task<(Uri[] Sources, List<string> Lines)> GetNpmSources(CancellationToken ct)
    {
        var file = new FileInfo(Environment.GetEnvironmentVariable("NPM_AUTHENTICATE_WORKING_FILE") ?? ".npmrc");
        if (!file.Exists)
        {
            return ([], []);
        }
        if (!file.Name.EndsWith(".npmrc", StringComparison.OrdinalIgnoreCase) || file.LinkTarget != null)
        {
            throw new InvalidOperationException("NpmAuthenticate requires a regular .npmrc file.");
        }

        var lines = (await File.ReadAllTextAsync(file.FullName, ct)).Replace("\r\n", "\n").Split('\n').ToList();
        var sources = new Dictionary<string, Uri>(StringComparer.Ordinal);
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i].Trim();
            var separator = line.IndexOf('=');
            if (line.StartsWith('#') || line.StartsWith(';') || separator < 0)
            {
                continue;
            }
            var key = line[..separator].Trim();
            if (!key.Equals("registry", StringComparison.OrdinalIgnoreCase) && !(key.StartsWith('@') && key.EndsWith(":registry", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }
            var value = Regex.Match(line[(separator + 1)..].Trim(), "^(?:\"(?<value>[^\"]*)\"|'(?<value>[^']*)'|(?<value>[^;#]*))").Groups["value"].Value.Trim();
            if (!Uri.TryCreate(value, UriKind.Absolute, out var source) || source.Scheme is not ("http" or "https") || source.UserInfo.Length > 0)
            {
                throw new InvalidOperationException("An npm registry must be an absolute HTTP or HTTPS URL without embedded credentials.");
            }
            sources[key] = new UriBuilder(source) { Path = source.AbsolutePath.TrimEnd('/') + "/" }.Uri;
            lines[i] = $"{key}={sources[key].AbsoluteUri}";
        }
        if (sources.Values.GroupBy(source => source.Authority, StringComparer.OrdinalIgnoreCase).Any(group => group.Select(source => source.Scheme).Distinct().Count() > 1))
        {
            throw new InvalidOperationException("npm credentials cannot be scoped by scheme. Do not mix HTTP and HTTPS registries on the same host and port.");
        }
        return (sources.Values
            .Where(source => FilterDomains.Any(domain => source.Host.Equals(domain, StringComparison.OrdinalIgnoreCase) || source.Host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase)))
            .Distinct()
            .ToArray(), lines);
    }

    private static bool HasCredentials(IEnumerable<string> lines, Uri source)
    {
        var prefix = "//" + source.Authority.ToLowerInvariant() + source.AbsolutePath.TrimEnd('/') + "/:";
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines.Select(line => line.Trim()).Where(line => line.StartsWith(prefix, StringComparison.Ordinal)))
        {
            var separator = line.IndexOf('=', prefix.Length);
            if (separator >= 0)
            {
                values[line[prefix.Length..separator].Trim()] = line[(separator + 1)..].Trim().Trim('"', '\'');
            }
        }
        return !string.IsNullOrWhiteSpace(values.GetValueOrDefault("_authToken")) || !string.IsNullOrWhiteSpace(values.GetValueOrDefault("_auth")) || (!string.IsNullOrWhiteSpace(values.GetValueOrDefault("username")) && !string.IsNullOrWhiteSpace(values.GetValueOrDefault("_password")));
    }

    private static void WriteCredentials(List<string> lines, Uri source, string username, string token)
    {
        if (new[] { username, token }.Any(value => string.IsNullOrWhiteSpace(value) || value.Contains('\r') || value.Contains('\n')))
        {
            throw new InvalidOperationException("An npm credential is missing or contains a line break.");
        }
        var registryPath = source.AbsolutePath.TrimEnd('/') + "/";
        var paths = registryPath.EndsWith("/npm/registry/", StringComparison.OrdinalIgnoreCase) ? new[] { registryPath, registryPath[..^"registry/".Length] } : new[] { registryPath };
        var password = Convert.ToBase64String(Encoding.UTF8.GetBytes(token));
        foreach (var path in paths)
        {
            var prefix = "//" + source.Authority.ToLowerInvariant() + path + ":";
            lines.RemoveAll(line => line.TrimStart().StartsWith(prefix, StringComparison.Ordinal) && Regex.IsMatch(line.TrimStart()[prefix.Length..], @"^(?:_authToken|_auth|_password|username|email|always-auth)\s*=", RegexOptions.IgnoreCase));
            lines.Add($"{prefix}username={username}");
            lines.Add($"{prefix}_password={password}");
            lines.Add($"{prefix}email=docker");
        }
    }
}
