using OrasProject.Oras.Registry.Remote.Auth;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace BuildCharts.Tool.Oras;

public static class DockerCredentialHelper
{
    public static async Task<SingleRegistryCredentialProvider> GetCredentialAsync(string registry)
    {
        var dockerConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".docker", "config.json");

        if (!File.Exists(dockerConfigPath))
        {
            return null;
        }

        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(dockerConfigPath));
        var root = doc.RootElement;

        // Try specific credHelper for the registry.
        if (root.TryGetProperty("credHelpers", out var helpers) && helpers.TryGetProperty(registry, out var helperName))
        {
            return await RunCredentialHelper(helperName.GetString(), registry);
        }

        // Try default credsStore.
        if (root.TryGetProperty("credsStore", out var storeName))
        {
            return await RunCredentialHelper(storeName.GetString(), registry);
        }

        return null;
    }

    private static async Task<SingleRegistryCredentialProvider> RunCredentialHelper(string helperName, string registry)
    {
        var exe = $"docker-credential-{helperName}";

        if (!await HelperHasCredentialsAsync(exe, registry))
        {
            return null;
        }

        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = exe,
            Arguments = "get",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        });

        if (process == null)
        {
            return null;
        }

        await process.StandardInput.WriteLineAsync(registry);
        process.StandardInput.Close();

        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            var msg = string.IsNullOrWhiteSpace(error) ? output : error;
            Console.Error.WriteLine($"Credential helper '{exe}' failed (exit {process.ExitCode}): {msg}");
            return null;
        }

        try
        {
            var result = JsonDocument.Parse(output);

            var root = result.RootElement;
            var username = root.GetProperty("Username").GetString();
            var secret = root.GetProperty("Secret").GetString();

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(secret))
            {
                return null;
            }

            return new SingleRegistryCredentialProvider(registry, new Credential
            {
                Username = username,
                Password = secret,
            });
        }
        catch (JsonException)
        {
            Console.Error.WriteLine($"Invalid JSON from credential helper '{exe}': {output}");
            return null;
        }
    }

    private static async Task<bool> HelperHasCredentialsAsync(string exe, string registry)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = exe,
            Arguments = "list",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        });

        if (process is null)
        {
            return false;
        }

        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();


        if (process.ExitCode != 0)
        {
            var msg = string.IsNullOrWhiteSpace(error) ? output : error;
            Console.Error.WriteLine($"Credential helper '{exe}' failed (exit {process.ExitCode}): {msg}");
            return false;
        }

        try
        {
            using var result = JsonDocument.Parse(output);

            if (result.RootElement.EnumerateObject()
                .Any(property => property.Name.Contains(registry, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }
        catch (Exception)
        {
            return false;
        }

        return false;
    }
}