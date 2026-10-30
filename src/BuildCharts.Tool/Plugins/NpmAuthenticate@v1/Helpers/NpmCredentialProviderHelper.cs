using BuildCharts.Tool.Plugins.NuGetAuthenticate_v1.Helpers;
using System;
using System.Linq;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace BuildCharts.Tool.Plugins.NpmAuthenticate_v1.Helpers;

public static class NpmCredentialProviderHelper
{
    public static async Task<string> FetchCredentialsAsync(string folder, string registryConfiguration, CancellationToken ct)
    {
        var version = Environment.GetEnvironmentVariable("NPM_AUTHENTICATE_HELPER_VERSION")?.Trim();
        if (!string.IsNullOrWhiteSpace(version) && !Regex.IsMatch(version, @"\Av?[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?\z"))
        {
            throw new InvalidOperationException("NPM_AUTHENTICATE_HELPER_VERSION must be an exact Microsoft Credential Provider release version.");
        }

        folder = Path.Combine(folder, string.IsNullOrWhiteSpace(version) ? "latest" : version.TrimStart('v'));
        await MicrosoftCredentialProviderHelper.EnsureInstalledAsync(folder, ct, version);

        var registries = registryConfiguration.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => new Uri(line[(line.IndexOf('=') + 1)..].Trim()))
            .Distinct();
        var builder = new StringBuilder();

        foreach (var registry in registries)
        {
            var token = await MicrosoftCredentialProviderHelper.FetchCredentialsAsync(folder, registry, isRetry: false, ct);
            builder.Append(FormatCredentials(registry, token));
        }

        return builder.ToString();
    }

    public static string FormatCredentials(Uri registry, string token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Contains('\r') || token.Contains('\n'))
        {
            throw new InvalidOperationException("The Microsoft Credential Provider returned an invalid npm credential.");
        }

        var password = Convert.ToBase64String(Encoding.UTF8.GetBytes(token));
        var registryPath = registry.AbsolutePath.TrimEnd('/') + "/";
        var paths = registryPath.EndsWith("/npm/registry/", StringComparison.OrdinalIgnoreCase)
            ? new[] { registryPath, registryPath[..^"registry/".Length] }
            : new[] { registryPath };
        var builder = new StringBuilder("; begin auth token\n");

        foreach (var path in paths)
        {
            var prefix = "//" + registry.Authority.ToLowerInvariant() + path;
            builder.AppendLine($"{prefix}:username=buildcharts");
            builder.AppendLine($"{prefix}:_password={password}");
            builder.AppendLine($"{prefix}:email=unused@example.com");
        }

        builder.AppendLine("; end auth token");
        return builder.ToString();
    }
}
