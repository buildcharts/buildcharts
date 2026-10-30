using System;
using System.Diagnostics;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace BuildCharts.Tool.Plugins.NpmAuthenticate_v1.Helpers;

public static class VstsNpmAuthHelper
{
    public const string Version = "0.43.0";

    public static async Task<string> EnsureInstalledAsync(HttpClient http, string folder, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(folder);

        var version = Environment.GetEnvironmentVariable("NPM_AUTHENTICATE_HELPER_VERSION");
        version = string.IsNullOrWhiteSpace(version) ? Version : version.Trim();

        if (!Regex.IsMatch(version, @"\A[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?\z"))
        {
            throw new InvalidOperationException("NPM_AUTHENTICATE_HELPER_VERSION must be an exact package version, for example 0.43.0.");
        }

        var versionFolder = Path.Combine(folder, version);
        var executable = Path.Combine(versionFolder, "package", "bin", "vsts-npm-auth.exe");

        if (File.Exists(executable))
        {
            return executable;
        }

        var assetName = $"vsts-npm-auth-{version}.tgz";
        var downloadUrl = $"https://registry.npmjs.org/vsts-npm-auth/-/{assetName}";

        Console.WriteLine($"Downloading vsts-npm-auth {version}");
        var bytes = await http.GetByteArrayAsync(downloadUrl, ct);

        Directory.CreateDirectory(versionFolder);
        var archivePath = Path.Combine(versionFolder, assetName);
        await File.WriteAllBytesAsync(archivePath, bytes, ct);

        await using var fs = File.OpenRead(archivePath);
        await using var gz = new GZipStream(fs, CompressionMode.Decompress);
        await TarFile.ExtractToDirectoryAsync(gz, versionFolder, overwriteFiles: true, cancellationToken: ct);

        // Ensure the executable exists after extraction.
        if (!File.Exists(executable))
        {
            throw new FileNotFoundException("The downloaded package does not contain vsts-npm-auth.exe.", executable);
        }

        return executable;
    }

    public static async Task<string> FetchCredentialsAsync(HttpClient http, string folder, string registryConfiguration, CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Local vsts-npm-auth sign-in requires Windows. Supply npm credentials through an authenticated working file or environment variables on this platform.");
        }

        var executable = await EnsureInstalledAsync(http, folder, ct);
        var sourceFile = Path.GetFullPath(Path.Combine(folder, "feeds.npmrc"));
        var targetFile = Path.GetFullPath(Path.Combine(folder, "credentials.npmrc"));
        await File.WriteAllTextAsync(sourceFile, registryConfiguration, ct);
        File.Delete(targetFile);

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = Path.GetFullPath(executable),
                UseShellExecute = false,
                ArgumentList =
                {
                    "-Config", sourceFile,
                    "-TargetConfig", targetFile,
                    "-Verbosity", "quiet",
                },
            };

            Console.WriteLine("Authenticating Azure Artifacts npm feeds with vsts-npm-auth. Sign in if prompted.");
            using var process = Process.Start(psi) ?? throw new InvalidOperationException("Could not start vsts-npm-auth.");

            try
            {
                await process.WaitForExitAsync(ct);
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
                await process.WaitForExitAsync(CancellationToken.None);
                throw;
            }

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"vsts-npm-auth failed with exit code {process.ExitCode}.");
            }

            return await File.ReadAllTextAsync(targetFile, ct);
        }
        finally
        {
            File.Delete(sourceFile);
            File.Delete(targetFile);
        }
    }
}
