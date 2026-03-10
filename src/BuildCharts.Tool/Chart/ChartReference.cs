using System;

namespace BuildCharts.Tool.Chart;

public readonly record struct ChartReference(string Original, string Registry, string RepositoryPath, string ChartName, string RepositoryParentPath, string Alias, string Tag, string Digest)
{
    public string RepositoryFullPath => $"oci://{Registry}/{RepositoryPath}";
    public bool IsDigest => !string.IsNullOrWhiteSpace(Digest);
    public string Filename => IsDigest
        ? $"{ChartName}@{Digest.Replace(':', '-')}.tgz"
        : $"{ChartName}-{Tag}.tgz";

    public static bool TryParse(string reference, out ChartReference chartReference)
    {
        chartReference = default;
        if (string.IsNullOrWhiteSpace(reference))
        {
            return false;
        }

        var working = reference.Trim();
        string alias = null;

        var equalsIndex = working.IndexOf('=');
        if (equalsIndex > 0 && !working.StartsWith("oci://", StringComparison.OrdinalIgnoreCase))
        {
            alias = working[..equalsIndex].Trim();
            working = working[(equalsIndex + 1)..].Trim();
        }

        if (working.StartsWith("oci://", StringComparison.OrdinalIgnoreCase))
        {
            working = working["oci://".Length..];
        }

        var firstSlash = working.IndexOf('/');
        if (firstSlash <= 0 || firstSlash == working.Length - 1)
        {
            return false;
        }

        var registry = working[..firstSlash];
        var repositoryAndMaybeTag = working[(firstSlash + 1)..];

        if (string.IsNullOrWhiteSpace(repositoryAndMaybeTag))
        {
            return false;
        }

        var tagIndex = repositoryAndMaybeTag.LastIndexOf(':');
        var lastSlash = repositoryAndMaybeTag.LastIndexOf('/');
        var digestIndex = repositoryAndMaybeTag.LastIndexOf('@');

        string tag;
        string digest = null;
        if (digestIndex > lastSlash)
        {
            digest = repositoryAndMaybeTag[(digestIndex + 1)..];
            repositoryAndMaybeTag = repositoryAndMaybeTag[..digestIndex];

            // Docker-compatible syntax allows repo:tag@sha256:...
            // If a tag exists before '@', strip it from repository path and ignore it for resolution.
            var tagBeforeDigestIndex = repositoryAndMaybeTag.LastIndexOf(':');
            var lastSlashBeforeDigest = repositoryAndMaybeTag.LastIndexOf('/');
            if (tagBeforeDigestIndex > lastSlashBeforeDigest)
            {
                repositoryAndMaybeTag = repositoryAndMaybeTag[..tagBeforeDigestIndex];
            }

            tag = null;

            if (string.IsNullOrWhiteSpace(digest))
            {
                return false;
            }
        }
        else if (tagIndex > lastSlash)
        {
            tag = repositoryAndMaybeTag[(tagIndex + 1)..];
            repositoryAndMaybeTag = repositoryAndMaybeTag[..tagIndex];
            if (string.IsNullOrWhiteSpace(tag))
            {
                return false;
            }
        }
        else
        {
            tag = "latest";
        }

        if (repositoryAndMaybeTag.Length == 0)
        {
            return false;
        }

        var chartSeparator = repositoryAndMaybeTag.LastIndexOf('/');
        string chartName;
        string repositoryParentPath = null;
        if (chartSeparator >= 0)
        {
            chartName = repositoryAndMaybeTag[(chartSeparator + 1)..];
            repositoryParentPath = repositoryAndMaybeTag[..chartSeparator];
        }
        else
        {
            chartName = repositoryAndMaybeTag;
        }

        if (string.IsNullOrWhiteSpace(chartName))
        {
            return false;
        }

        chartReference = new ChartReference(reference, registry, repositoryAndMaybeTag, chartName, repositoryParentPath, alias, tag, digest);

        return true;
    }
}
