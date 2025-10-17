using BuildCharts.Tool.Configuration.Models;
using BuildCharts.Tool.Plugins.NuGetAuthenticate_v1.Helpers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
            Console.WriteLine($"\u001b[2mRunning plugin: {Name}\u001b[22m");

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
            
            // Ensure the credential provider is downloaded.
            var targetFolder = Path.Combine(Directory.GetCurrentDirectory(), ".buildcharts", "plugins", Name, "microsoft-artifacts-credprovider");
            await MicrosoftCredentialProviderHelper.EnsureInstalledAsync(targetFolder, ct);

            var token = await MicrosoftCredentialProviderHelper.FetchCredentialsAsync(targetFolder, sources.FirstOrDefault(), ct);

            // Generate the feed-endpoints JSON used by the credential provider bundled in SDK docker image.
            var endpointCredentials = sources.Select(src => new
            {
                endpoint = src,
                username = "docker",
                password = token,
            });

            var feedEndpoints = JsonSerializer.Serialize(new { endpointCredentials }, new JsonSerializerOptions { WriteIndented = true });

            WriteValueToFile("VSS_NUGET_EXTERNAL_FEED_ENDPOINTS", feedEndpoints);
            WriteValueToFile("VSS_NUGET_ACCESSTOKEN", token);

            Console.WriteLine($"\u001b[2mPlugin complete: {Name}\u001b[22m");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"NuGetAuthenticate Plugin failed with error: {ex}");
            throw;
        }
    }

    public Task OnAfterGenerateAsync(BuildConfig buildConfig, ChartConfig cartConfig, StringBuilder hclStringBuilder, CancellationToken ct)
    {
        BakeHclPatchHelper.AddSecretsToBuildTarget(hclStringBuilder);
        return Task.CompletedTask;
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

        Console.WriteLine($"Successfully created secret file '{Path.GetRelativePath(Directory.GetCurrentDirectory(), filePath)}'");
    }
}
