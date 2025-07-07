using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace BuildCharts.Tool.Plugins.NuGetAuthenticate;

public class NuGetAuthenticatePlugin
{
    public string Name { get; set; } = "nuget-authenticate";

    public async Task OnExecuteAsync(CancellationToken cancellationToken)
    {
        try
        {
            Console.WriteLine($"Running plugin: {Name}");

            var sources = GetNuGetSources();
            if (!sources.Any())
            {
                Console.WriteLine("No Azure DevOps package sources found in NuGet.Config.");
                return;
            }

            // Ensure the Azure Artifacts Credential Provider is downloaded
            var targetFolder = Path.Combine(Directory.GetCurrentDirectory(), ".buildcharts", "plugins", Name, "microsoft-artifacts-credprovider");
            await EnsureCredentialProviderInstalledAsync(targetFolder, cancellationToken);

            var token = await InvokeCredentialProviderAsync(targetFolder, sources.FirstOrDefault(), cancellationToken);

            // Generate the feed-endpoints JSON used by the credentials provider bundled in SDK docker image.
            var endpointCredentials = sources.Select(src => new
            {
                endpoint = src,
                username = "docker",
                password = token,
            });

            var feedEndpoints = JsonSerializer.Serialize(new
            {
               endpointCredentials,
            });

            Console.WriteLine("Successfully fetched credentials via Azure Artifacts Credential Provider");

            WriteValueToFile("VSS_NUGET_EXTERNAL_FEED_ENDPOINTS", feedEndpoints);
            WriteValueToFile("VSS_NUGET_ACCESSTOKEN", token);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"NuGetAuthenticate Plugin failed with error: {ex}");
            throw;
        }
    }

    private static async Task EnsureCredentialProviderInstalledAsync(string folder, CancellationToken ct)
    {
        if (folder == null)
        {
            throw new ArgumentNullException(nameof(folder));
        }

        if (Directory.Exists(folder) && Directory.EnumerateFiles(folder).Any())
        {
            return;
        }

        Directory.CreateDirectory(folder);

        var zipUrl = "https://github.com/microsoft/artifacts-credprovider/releases/latest/download/Microsoft.Net8.NuGet.CredentialProvider.zip";
        var zipFile = Path.Combine(folder, "microsoft-artifacts-credprovider.zip");

        var data = await new HttpClient().GetByteArrayAsync(zipUrl, ct);
        await File.WriteAllBytesAsync(zipFile, data, ct);
        ZipFile.ExtractToDirectory(zipFile, folder, overwriteFiles: true);
    }

    private static async Task<string> InvokeCredentialProviderAsync(string cpFolder, string feedUrl, CancellationToken ct)
    {
        Console.WriteLine($"Fetching credentials via Azure Artifacts Credential Provider for {feedUrl}");

        var exeName = OperatingSystem.IsWindows() ?
            "CredentialProvider.Microsoft.exe" :
            "CredentialProvider.Microsoft";
        
        // Locate the credential provider executable.
        var exe = Directory.EnumerateFiles(cpFolder, exeName, SearchOption.AllDirectories).FirstOrDefault() 
            ?? throw new FileNotFoundException("Credential Provider exe not found in " + cpFolder);

        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = exe,
            ArgumentList =
            {
                "-U", feedUrl,
                "-F", "Json",
            },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        }) ?? throw new InvalidOperationException($"Failed to start process: {exe}.");

        var outputBuilder = new StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                outputBuilder.AppendLine(e.Data);
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                Console.Error.WriteLine(e.Data);
            }
        };

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        
        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Credential Provider failed (exit code {process.ExitCode})");
        }

        // The provider emits JSON: { "Username": "...", "Password": "..." }
        var output = outputBuilder.ToString();
        using var doc = JsonDocument.Parse(output);
        return doc.RootElement.GetProperty("Password").GetString();
    }
    
    private static List<string> GetNuGetSources()
    {
        var result = new List<string>();

        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = "nuget list source --format short",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            WorkingDirectory = Directory.GetCurrentDirectory(),
        }) ?? throw new InvalidOperationException($"Failed to start process: 'dotnet nuget list source --format short'.");

        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        var lines = output.Split('\r', StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines.Select(l => l.Trim()))
        {
            var m = Regex.Match(line, @"^[ED]\s+(?<url>https?://\S+)", RegexOptions.IgnoreCase);
            if (!m.Success)
            {
                continue;
            }

            var url = m.Groups["url"].Value;
            result.Add(url);
        }

        result = result
            .Where(s => s.IndexOf("pkgs.dev.azure.com", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        s.IndexOf("pkgs.visualstudio.com", StringComparison.OrdinalIgnoreCase) >= 0)
            .ToList();

        return result;
    }

    public static void WriteValueToFile(string key, string value)
    {
        var folder = Path.Combine(Directory.GetCurrentDirectory(), ".buildcharts\\secrets");
        var filePath = Path.Combine(folder, key);

        Directory.CreateDirectory(folder);
        File.WriteAllText(filePath, value, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        Console.WriteLine($"Successfully created secret file '{Path.GetRelativePath(Directory.GetCurrentDirectory(), filePath)}/{key}'");
    }
}
