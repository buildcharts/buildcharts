using System;

namespace BuildCharts.Tool.Chart;

internal readonly record struct ChartReference(string Original, string Registry, string RepositoryPath, string ChartName, string RepositoryParentPath, string Alias, string Tag)
{
    public string RepositoryFullPath => $"oci://{Registry}/{RepositoryPath}";

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

        string tag;
        if (tagIndex > lastSlash)
        {
            tag = repositoryAndMaybeTag[(tagIndex + 1)..];
            repositoryAndMaybeTag = repositoryAndMaybeTag[..tagIndex];
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

        chartReference = new ChartReference(reference, registry, repositoryAndMaybeTag, chartName, repositoryParentPath, alias, tag);

        return true;
    }
}