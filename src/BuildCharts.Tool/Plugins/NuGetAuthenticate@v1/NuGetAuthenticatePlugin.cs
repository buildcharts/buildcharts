using BuildCharts.Tool.Configuration.Models;
using BuildCharts.Tool.Plugins.NuGetAuthenticate_v1.Helpers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BuildCharts.Tool.Plugins.NuGetAuthenticate_v1;

/// <summary>
/// Obtains NuGet credentials for Azure Artifacts feeds by invoking the cross-platform Azure Artifacts Credential Provider.
///
/// The helper delegates all authentication to MSAL-enabled flows supported by the provider and automatically tries, in order:
/// - Environment-supplied service-principal or managed-identity tokens.
/// - Silent Integrated Windows Authentication or cached MSAL tokens.
/// - Interactive browser or device-code sign-in as a last resort.
/// </summary>
public class NuGetAuthenticatePlugin : IBuildChartsPlugin
{
    public string Name { get; set; } = "NuGetAuthenticate@v1";
    public string[] FilterDomains { get; set; } = ["pkgs.dev.azure.com", "pkgs.visualstudio.com"];

    public async Task OnBeforeGenerateAsync(BuildConfig buildConfig, CancellationToken ct)
    {
        try
        {
            var sources = await GetNuGetSources(ct);
            if (!sources.Any())
            {
                Console.WriteLine("No Azure DevOps package sources found in nuget config");
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

                WriteValueToFile("ARTIFACTS_CREDENTIALPROVIDER_EXTERNAL_FEED_ENDPOINTS", endpointsEnv);
            }
            else if (!string.IsNullOrWhiteSpace(tokenEnv))
            {
                Console.WriteLine($"Using {tokenSource} from environment");

                var endpointCredentials = sources.Select(src => new
                {
                    endpoint = src,
                    username = "docker",
                    password = tokenEnv,
                });

                var feedEndpoints = JsonSerializer.Serialize(new { endpointCredentials }, new JsonSerializerOptions { WriteIndented = true });
                WriteValueToFile("ARTIFACTS_CREDENTIALPROVIDER_EXTERNAL_FEED_ENDPOINTS", feedEndpoints);
            }
            else
            {
                // Ensure the credential provider is downloaded and fetch token
                var targetFolder = Path.Combine(Directory.GetCurrentDirectory(), ".buildcharts", "plugins", Name, "microsoft-artifacts-credprovider");
                await MicrosoftCredentialProviderHelper.EnsureInstalledAsync(targetFolder, ct);

                // Generate the feed-endpoints JSON used by the Credential provider when restoring nuget.
                var tokens = new List<(Uri Source, string Token)>();

                foreach (var sourceByOrganizationUrl in sources.GroupBy(GetOrganizationUrl))
                {
                    // Only fetch credentials once per organization.
                    Console.WriteLine($"Fetching credentials for {sourceByOrganizationUrl.Key} via Azure Artifacts Credential Provider");

                    var token = await FetchTokenForFeedAsync(targetFolder, sourceByOrganizationUrl.First(), ct);

                    foreach (var source in sourceByOrganizationUrl)
                    {
                        tokens.Add((source, token));
                    }
                }

                var endpointCredentials = tokens.Select(item => new
                {
                    endpoint = item.Source,
                    username = "docker",
                    password = item.Token,
                });

                var feedEndpoints = JsonSerializer.Serialize(new { endpointCredentials }, new JsonSerializerOptions { WriteIndented = true });
                WriteValueToFile("ARTIFACTS_CREDENTIALPROVIDER_EXTERNAL_FEED_ENDPOINTS", feedEndpoints);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"{Name} failed with error: {ex}");
            throw;
        }
    }

    public Task OnAfterGenerateAsync(BuildConfig buildConfig, ChartConfig cartConfig, StringBuilder hclStringBuilder, CancellationToken ct)
    {
        BakeHclPatchHelper.Execute(hclStringBuilder);
        return Task.CompletedTask;
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

    private async Task<List<Uri>> GetNuGetSources(CancellationToken ct)
    {
        var result = new List<Uri>();

        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = "nuget list source --format short",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = Directory.GetCurrentDirectory(),
        }) ?? throw new InvalidOperationException($"Failed to start process: 'dotnet nuget list source --format short'.");

        var output = await process.StandardOutput.ReadToEndAsync(ct);
        var error = await process.StandardError.ReadToEndAsync(ct);

        await process.WaitForExitAsync(ct);

        if (!string.IsNullOrWhiteSpace(error))
        {
            await Console.Error.WriteLineAsync($"Error when running 'dotnet nuget list source'. Error: {error}");
        }

        // Split output into lines in a cross-platform way (handles \n and \r\n)
        var lines = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines.Select(l => l.Trim()))
        {
            // Expected format:  "E  https://host/_packaging/feed/nuget/v3/index.json"
            var trimmed = line.Trim();
            var space = trimmed.IndexOf(' ');
            if (space < 0)
            {
                continue;
            }

            var candidate = trimmed[(space + 1)..];

            if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
            {
                continue;
            }

            if (FilterDomains.Any(x => uri.Host.EndsWith(x)))
            {
                result.Add(uri);
            }
        }

        return result;
    }

    private static void WriteValueToFile(string key, string value)
    {
        var folder = Path.Combine(Directory.GetCurrentDirectory(), ".buildcharts", "secrets");
        var filePath = Path.Combine(folder, key);

        Directory.CreateDirectory(folder);
        File.WriteAllText(filePath, value, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        Console.WriteLine($"Successfully created file '{Path.GetRelativePath(Directory.GetCurrentDirectory(), filePath)}'");
    }

    private static async Task<string> FetchTokenForFeedAsync(string targetFolder, Uri source, CancellationToken ct)
    {
        var token = await MicrosoftCredentialProviderHelper.FetchCredentialsAsync(targetFolder, source, isRetry: false, ct);
        if (!await IsTokenValidAsync(source, token, ct))
        {
            if (Environment.GetEnvironmentVariable("BUILDSCHARTS_VERBOSE") == "1")
            {
                Console.WriteLine("Credential Provider returned invalid credentials. Retrying with IsRetry forcing a refresh...");
            }

            token = await MicrosoftCredentialProviderHelper.FetchCredentialsAsync(targetFolder, source, isRetry: true, ct);

            if (!await IsTokenValidAsync(source, token, ct))
            {
                throw new InvalidOperationException($"Credential Provider returned invalid credentials after retry for {source}.");
            }
        }

        return token;
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
}
