using System;
using System.Diagnostics;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace BuildCharts.Tool.Plugins.NpmAuthenticate_v1.Helpers;

public static class VstsNpmAuthHelper
{
    public const string Version = "0.43.0";
    private const string PackageSha512 = "ROZc5HXaWNNO++tVnS5PNzEqI1cLEiaUq0uPJ+azGRTVJAVvECzGu8ideed0iDFg0/sNuzUp5gshfenDyAIrMQ==";

    public static async Task<string> EnsureInstalledAsync(HttpClient http, string folder, CancellationToken ct)
    {
        var versionFolder = Path.Combine(folder, Version);
        var executable = Path.Combine(versionFolder, "package", "bin", "vsts-npm-auth.exe");
        var installedMarker = Path.Combine(versionFolder, "installed.sha512");
        if (File.Exists(executable) && File.Exists(installedMarker) && await File.ReadAllTextAsync(installedMarker, ct) == PackageSha512)
        {
            return executable;
        }

        var packageName = $"vsts-npm-auth-{Version}.tgz";
        Console.WriteLine($"Downloading vsts-npm-auth {Version}");
        var bytes = await http.GetByteArrayAsync($"https://registry.npmjs.org/vsts-npm-auth/-/{packageName}", ct);
        if (!CryptographicOperations.FixedTimeEquals(SHA512.HashData(bytes), Convert.FromBase64String(PackageSha512)))
        {
            throw new InvalidOperationException("The vsts-npm-auth download does not match the pinned SHA-512 checksum.");
        }

        Directory.CreateDirectory(versionFolder);
        await File.WriteAllBytesAsync(Path.Combine(versionFolder, packageName), bytes, ct);
        await using var archive = new MemoryStream(bytes);
        await using var gzip = new GZipStream(archive, CompressionMode.Decompress);
        await TarFile.ExtractToDirectoryAsync(gzip, versionFolder, overwriteFiles: true, cancellationToken: ct);
        if (!File.Exists(executable))
        {
            throw new FileNotFoundException("The downloaded package does not contain vsts-npm-auth.exe.", executable);
        }
        await File.WriteAllTextAsync(installedMarker, PackageSha512, ct);
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
            var startInfo = new ProcessStartInfo
            {
                FileName = Path.GetFullPath(executable),
                UseShellExecute = false,
                ArgumentList = { "-Config", sourceFile, "-TargetConfig", targetFile, "-Verbosity", "quiet" },
            };
            Console.WriteLine("Authenticating Azure Artifacts npm feeds with vsts-npm-auth. Sign in if prompted.");
            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start vsts-npm-auth.");
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
