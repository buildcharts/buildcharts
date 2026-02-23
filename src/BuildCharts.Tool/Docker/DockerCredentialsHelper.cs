using OrasProject.Oras.Registry.Remote.Auth;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace BuildCharts.Tool.Docker;

public static class DockerCredentialHelper
{
    public static async Task<SingleRegistryCredentialProvider> GetCredentialAsync(string registry)
    {
        var dockerConfigPath = ResolveDockerConfigPath();

        if (!File.Exists(dockerConfigPath))
        {
            return null;
        }

        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(dockerConfigPath));
        var root = doc.RootElement;

        // Try specific credHelper for the registry.
        if (root.TryGetProperty("credHelpers", out var helpers) && helpers.TryGetProperty(registry, out var helperName))
        {
            var provider = await RunCredentialHelper(helperName.GetString(), registry);
            if (provider is not null)
            {
                return provider;
            }
        }

        // Tre auths entries
        if (root.TryGetProperty("auths", out var auths) &&  auths.TryGetProperty(registry, out var authEntry) && TryReadAuthEntry(registry, authEntry, out var authCredential))
        {
            return new SingleRegistryCredentialProvider(registry, authCredential);
        }

        // Try default credsStore.
        if (root.TryGetProperty("credsStore", out var storeName))
        {
            var provider = await RunCredentialHelper(storeName.GetString(), registry);
            if (provider is not null)
            {
                return provider;
            }
        }

        return null;
    }

    private static string ResolveDockerConfigPath()
    {
        var dockerConfigDir = Environment.GetEnvironmentVariable("DOCKER_CONFIG");
        if (!string.IsNullOrWhiteSpace(dockerConfigDir))
        {
            return Path.Combine(dockerConfigDir, "config.json");
        }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".docker", "config.json");
    }

    private static bool TryReadAuthEntry(string registry, JsonElement authEntry, out Credential credential)
    {
        credential = default;

        // Azure ACR on Linux agents often stores token under identitytoken with auth "000...:".
        if (registry.EndsWith(".azurecr.io", StringComparison.OrdinalIgnoreCase) &&
            authEntry.TryGetProperty("identitytoken", out var identityTokenElement))
        {
            var identityToken = identityTokenElement.GetString();
            if (!string.IsNullOrWhiteSpace(identityToken))
            {
                credential = new Credential
                {
                    Username = "00000000-0000-0000-0000-000000000000",
                    RefreshToken = identityToken,
                };

                return true;
            }
        }

        if (!authEntry.TryGetProperty("auth", out var authElement))
        {
            return false;
        }

        var authValue = authElement.GetString();
        if (string.IsNullOrWhiteSpace(authValue))
        {
            return false;
        }

        try
        {
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(authValue));
            var separatorIndex = decoded.IndexOf(':');
            if (separatorIndex <= 0)
            {
                return false;
            }

            var username = decoded[..separatorIndex];
            var password = decoded[(separatorIndex + 1)..];

            if (string.IsNullOrWhiteSpace(username))
            {
                return false;
            }

            credential = new Credential
            {
                Username = username,
                Password = string.IsNullOrWhiteSpace(password) ? null : password,
            };

            // Pass 00000000-0000-0000-0000-000000000000 as username according to docs.
            // https://learn.microsoft.com/en-us/azure/container-registry/container-registry-authentication?tabs=azure-cli
            if (registry.EndsWith(".azurecr.io", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(username, "00000000-0000-0000-0000-000000000000", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(password))
            {
                credential.RefreshToken = password;
                credential.Password = null;
            }

            return true;
        }
        catch (FormatException)
        {
            return false;
        }
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
            var msg = string.IsNullOrWhiteSpace(error) ? output.Trim() : error;
            if (msg != "credentials not found in native keychain") // Ignore, public container registries always gives this error.
            {
                Console.Error.WriteLine($"Credential helper '{exe}' failed (exit {process.ExitCode}): {msg}");
            }
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

            var credential = new Credential
            {
                Username = username,
                Password = secret,
            };

            // Azure ACR expects ACR refresh tokens in the RefreshToken field.
            if (registry.EndsWith(".azurecr.io", StringComparison.OrdinalIgnoreCase) && string.Equals(username, "<token>", StringComparison.OrdinalIgnoreCase))
            {
                credential.RefreshToken = secret;
                credential.Password = null;
                credential.Username = "00000000-0000-0000-0000-000000000000";
            }

            return new SingleRegistryCredentialProvider(registry, credential);
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
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
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
