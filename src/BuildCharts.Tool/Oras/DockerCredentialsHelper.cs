using OrasProject.Oras.Registry.Remote.Auth;
using System;
using System.Diagnostics;
using System.IO;
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
        if (root.TryGetProperty("credHelpers", out var helpers) &&
            helpers.TryGetProperty(registry, out var helperName))
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
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        using var proc = Process.Start(psi);
        if (proc == null)
        {
            return null;
        }

        await proc.StandardInput.WriteLineAsync(registry);
        proc.StandardInput.Close();

        var output = await proc.StandardOutput.ReadToEndAsync();
        var error = await proc.StandardError.ReadToEndAsync();
        await proc.WaitForExitAsync();

        if (proc.ExitCode != 0)
        {
            Console.Error.WriteLine($"Credential helper '{exe}' failed: {error}");
            return null;
        }

        JsonDocument resultDoc;
        try
        {
            resultDoc = JsonDocument.Parse(output);
        }
        catch (JsonException)
        {
            Console.Error.WriteLine($"Invalid JSON from credential helper '{exe}': {output}");
            return null;
        }

        var root = resultDoc.RootElement;
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
}
