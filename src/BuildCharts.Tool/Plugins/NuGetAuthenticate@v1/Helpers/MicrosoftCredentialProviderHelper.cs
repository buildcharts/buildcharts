using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BuildCharts.Tool.Plugins.NuGetAuthenticate_v1.Helpers;

public static class MicrosoftCredentialProviderHelper
{
    public static async Task EnsureInstalledAsync(string folder, CancellationToken ct)
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
        var data = await new HttpClient().GetByteArrayAsync(zipUrl, ct);

        var zipFile = Path.Combine(folder, "microsoft-artifacts-credprovider.zip");
        await File.WriteAllBytesAsync(zipFile, data, ct);
        ZipFile.ExtractToDirectory(zipFile, folder, overwriteFiles: true);
    }

    public static async Task<string> FetchCredentialsAsync(string cpFolder, Uri feedUrl, CancellationToken ct)
    {
        Console.WriteLine($"Fetching credentials for {feedUrl.Host} via Azure Artifacts Credential Provider");

        var exeName = OperatingSystem.IsWindows() 
            ? "CredentialProvider.Microsoft.exe"
            : "CredentialProvider.Microsoft";
        
        // Locate the credential provider executable.
        var exe = Directory.EnumerateFiles(cpFolder, exeName, SearchOption.AllDirectories).FirstOrDefault() 
            ?? throw new FileNotFoundException("Credential Provider exe not found in " + cpFolder);

        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = exe,
            ArgumentList =
            {
                "-U", feedUrl.ToString(),
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
}
