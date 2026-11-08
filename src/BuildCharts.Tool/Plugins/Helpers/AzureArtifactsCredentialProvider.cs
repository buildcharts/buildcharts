using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Formats.Tar;

namespace BuildCharts.Tool.Plugins.Helpers;

public static class AzureArtifactsCredentialProvider
{
    public static async Task<string> EnsureInstalledAsync(string folder, CancellationToken ct, string version = null)
    {
        if (folder == null)
        {
            throw new ArgumentNullException(nameof(folder));
        }

        version = version?.Trim();
        if (!string.IsNullOrWhiteSpace(version) && !Regex.IsMatch(version, @"\Av?[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?\z"))
        {
            throw new InvalidOperationException("Version must be an exact Azure Artifacts Credential Provider release version.");
        }

        folder = Path.Combine(folder, string.IsNullOrWhiteSpace(version) ? "latest" : version.TrimStart('v'));
        if (Directory.Exists(folder) && Directory.EnumerateFiles(folder, "CredentialProvider.Microsoft.dll", SearchOption.AllDirectories).Any())
        {
            return folder;
        }

        Directory.CreateDirectory(folder);

        var isWindows = OperatingSystem.IsWindows();

        // Prefer tar.gz on Unix-like systems to match upstream installer; use zip on Windows
        var assetName = isWindows
            ? "Microsoft.Net8.NuGet.CredentialProvider.zip"
            : "Microsoft.Net8.NuGet.CredentialProvider.tar.gz";
        var releasePath = string.IsNullOrWhiteSpace(version) ? "latest/download" : $"download/v{version.TrimStart('v')}";
        var downloadUrl = $"https://github.com/microsoft/artifacts-credprovider/releases/{releasePath}/{assetName}";

        using var http = new HttpClient();
        var bytes = await http.GetByteArrayAsync(downloadUrl, ct);

        var archivePath = Path.Combine(folder, assetName);
        await File.WriteAllBytesAsync(archivePath, bytes, ct);

        if (isWindows)
        {
            await ZipFile.ExtractToDirectoryAsync(archivePath, folder, overwriteFiles: true, cancellationToken: ct);
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

        return folder;
    }

    public static async Task<string> FetchTokenForFeedAsync(string targetFolder, Uri source, CancellationToken ct)
    {
        var token = await FetchCredentialsAsync(targetFolder, source, isRetry: false, ct);
        if (!await IsTokenValidAsync(source, token, ct))
        {
            if (Environment.GetEnvironmentVariable("BUILDSCHARTS_VERBOSE") == "1")
            {
                Console.WriteLine("Credential Provider returned invalid credentials. Retrying with IsRetry forcing a refresh...");
            }

            token = await FetchCredentialsAsync(targetFolder, source, isRetry: true, ct);

            if (!await IsTokenValidAsync(source, token, ct))
            {
                throw new InvalidOperationException($"Credential Provider returned invalid credentials after retry for {source}.");
            }
        }

        return token;
    }

    public static async Task<string> FetchCredentialsAsync(string cpFolder, Uri feedUrl, bool isRetry, CancellationToken ct)
    {
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

        if (Environment.GetEnvironmentVariable("BUILDSCHARTS_VERBOSE") == "1")
        {
            psi.ArgumentList.Add("-V");
            psi.ArgumentList.Add("verbose");
        }

        if (isRetry)
        {
            psi.ArgumentList.Add("-IsRetry");
            psi.ArgumentList.Add("true");
        }

        Console.WriteLine($"Running command: dotnet {string.Join(" ", psi.ArgumentList)}");
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
                Console.WriteLine($"{DateTimeOffset.Now:HH:mm:ss.fff} {e.Data}");
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
