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
using System.Formats.Tar;

namespace BuildCharts.Tool.Plugins.NuGetAuthenticate_v1.Helpers;

public static class MicrosoftCredentialProviderHelper
{
    public static async Task EnsureInstalledAsync(string folder, CancellationToken ct)
    {
        if (folder == null)
        {
            throw new ArgumentNullException(nameof(folder));
        }

        if (Directory.Exists(folder) && Directory.EnumerateFiles(folder, "CredentialProvider.Microsoft.dll", SearchOption.AllDirectories).Any())
        {
            return;
        }

        Directory.CreateDirectory(folder);

        var isWindows = OperatingSystem.IsWindows();

        // Prefer tar.gz on Unix-like systems to match upstream installer; use zip on Windows
        var assetName = isWindows
            ? "Microsoft.Net8.NuGet.CredentialProvider.zip"
            : "Microsoft.Net8.NuGet.CredentialProvider.tar.gz";
        var downloadUrl = $"https://github.com/microsoft/artifacts-credprovider/releases/latest/download/{assetName}";

        using var http = new HttpClient();
        var bytes = await http.GetByteArrayAsync(downloadUrl, ct);

        var archivePath = Path.Combine(folder, assetName);
        await File.WriteAllBytesAsync(archivePath, bytes, ct);

        if (isWindows)
        {
            ZipFile.ExtractToDirectory(archivePath, folder, overwriteFiles: true);
        }
        else
        {
            await using var fs = File.OpenRead(archivePath);
            await using var gz = new GZipStream(fs, CompressionMode.Decompress);
            await TarFile.ExtractToDirectoryAsync(gz, folder, overwriteFiles: true, ct);
        }

        // Ensure DLL exists after extraction.
        var dllOk = Directory.EnumerateFiles(folder, "CredentialProvider.Microsoft.dll", SearchOption.AllDirectories).Any();
        if (!dllOk)
        {
            throw new FileNotFoundException("Credential Provider DLL not found after extraction: " + folder);
        }
    }

    public static async Task<string> FetchCredentialsAsync(string cpFolder, Uri feedUrl, CancellationToken ct)
    {
        Console.WriteLine($"Fetching credentials for {feedUrl.Host} via Azure Artifacts Credential Provider");

        // Always run the provider via 'dotnet' using the DLL for cross-platform consistency.
        var dllPath = Directory.EnumerateFiles(cpFolder, "CredentialProvider.Microsoft.dll", SearchOption.AllDirectories).FirstOrDefault()  
            ?? throw new FileNotFoundException("Credential Provider DLL not found in " + cpFolder);

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            ArgumentList =
            {
                dllPath,
                "-U",
                feedUrl.ToString(),
                "-F", "Json",
            },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var process = Process.Start(psi) ?? throw new InvalidOperationException($"Failed to start process. 'dotnet {dllPath} {string.Join(" ", psi.ArgumentList)}'");

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
