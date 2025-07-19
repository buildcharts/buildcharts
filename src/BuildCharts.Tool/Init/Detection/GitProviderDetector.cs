using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace BuildCharts.Tool.Init.Detection;

public static partial class GitProviderDetector
{
    [GeneratedRegex("^git@(?<host>[^:]+):")]
    private static partial Regex GitSshRegex();

    public static async Task<GitProvider> DetectAsync(CancellationToken ct)
    {
        var gitConfigPath = Path.Combine(".git", "config");

        if (!File.Exists(gitConfigPath))
        {
            return GitProvider.Unknown;
        }

        var lines = await File.ReadAllLinesAsync(gitConfigPath, ct);

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("url", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var parts = trimmed.Split('=', 2);
            if (parts.Length != 2)
            {
                continue;
            }

            var url = parts[1].Trim();

            // Normalize git@ to https:// style
            if (url.StartsWith("git@"))
            {
                url = GitSshRegex().Replace(url, "https://$1/");
            }

            var host = TryParseHost(url) ?? url.ToLowerInvariant();

            return host switch
            {
                "github.com" => GitProvider.GitHub,
                "gitlab.com" => GitProvider.GitLab,
                "bitbucket.org" => GitProvider.Bitbucket,
                "dev.azure.com" or "visualstudio.com" => GitProvider.AzureDevOps,
                _ => GitProvider.Unknown,
            };
        }

        return GitProvider.Unknown;
    }

    private static string TryParseHost(string url)
    {
        try
        {
            return new Uri(url).Host.ToLowerInvariant();
        }
        catch
        {
            return null;
        }
    }
}