using BuildCharts.Tool.Configuration;
using BuildCharts.Tool.Configuration.Models;
using BuildCharts.Tool.Oras;
using ICSharpCode.SharpZipLib.GZip;
using ICSharpCode.SharpZipLib.Tar;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BuildCharts.Tool.Chart;

public class ChartManager
{
    public static async Task UpdateAsync(ChartConfig chartConfig, ChartLock chartLock, string outputDir = ".buildcharts", bool useLockFile = true, CancellationToken ct = default)
    {
        if (chartConfig.Dependencies == null || chartConfig.Dependencies.Count == 0)
        {
            Console.WriteLine($"No dependencies declared in {ConfigurationManager.CHART_CONFIG_PATH}.");
            return;
        }

        var results = new ConcurrentBag<(ChartReference, string)>();

        await Parallel.ForEachAsync(chartConfig.Dependencies, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(Environment.ProcessorCount, chartConfig.Dependencies.Count), CancellationToken = ct }, async (dependency, cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(dependency.Repository) || string.IsNullOrWhiteSpace(dependency.Name) || string.IsNullOrWhiteSpace(dependency.Version))
            {
                Console.WriteLine("  - Skipping dependency with missing repository, name, or version.");
                return;
            }

            var reference = $"{dependency.Repository}/{dependency.Name}:{dependency.Version}";

            if (!ChartReference.TryParse(reference, out var chartReference))
            {
                throw new ArgumentException("Invalid chart reference");
            }

            // Cache folder use to cache new digest or fetch existing ones.
            var cacheRoot = Path.Combine(Path.GetTempPath(), "buildcharts", "oci", "blobs");
            Directory.CreateDirectory(cacheRoot);

            string digest = null;
            string cachedBlobFilePath = null;
            var cacheHit = false;

            if (useLockFile && chartLock != null)
            {
                // Check existing Chart.lock for digest, to force update pass an empty chart lock file.
                var lockEntry = FindChartLockDependencyForCache(chartLock, chartReference);
                digest = lockEntry?.Digest;
                cacheHit = TryGetBlobCacheForDigest(digest, cacheRoot, out cachedBlobFilePath);
            }

            if (!cacheHit)
            {
                // Fetch manifest digest to see if the blob is already cached locally.
                digest = await OrasClient.GetManifestDigestAsync(reference, cancellationToken);
                cacheHit = TryGetBlobCacheForDigest(digest, cacheRoot, out cachedBlobFilePath);
            }

            if (cacheHit)
            {
                Console.WriteLine($"Pulled: {chartReference.Registry}/{chartReference.RepositoryPath}:{chartReference.Tag} ({new FileInfo(cachedBlobFilePath).Length} bytes) (cached)");
                Console.WriteLine($"Digest: {digest} (cached)");

                // Untar the chart to output directory.
                await using var tgzStream = File.OpenRead(cachedBlobFilePath);
                await using var gzipStream = new GZipInputStream(tgzStream);
                using var tarArchive = TarArchive.CreateInputTarArchive(gzipStream, Encoding.UTF8);
                tarArchive.ExtractContents(outputDir);
            }
            else
            {
                digest = await OrasClient.Pull(reference, untar: true, untarDir: outputDir, outputDir: cacheRoot, useDigestName: true, cancellationToken);
            }

            results.Add((chartReference, digest));
        });
  
        if (useLockFile)
        {
            await UpdateChartLockAsync(chartLock, results, ct);
        }
    }

    private static ChartLockDependency FindChartLockDependencyForCache(ChartLock chartLock, ChartReference chartReference)
    {
        var targetRepository = NormalizeRepository(chartReference.RepositoryFullPath);
        return chartLock.Dependencies
            .FirstOrDefault(x =>
                string.Equals(x.Name, chartReference.ChartName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.Version, chartReference.Tag, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(NormalizeRepository(x.Repository), targetRepository, StringComparison.OrdinalIgnoreCase));
    }

    private static ChartLockDependency FindChartLockDependencyForUpdate(ChartLock chartLock, ChartReference chartReference)
    {
        var targetRepository = NormalizeRepository(chartReference.RepositoryFullPath);
        return chartLock.Dependencies
            .FirstOrDefault(x =>
                string.Equals(x.Name, chartReference.ChartName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(NormalizeRepository(x.Repository), targetRepository, StringComparison.OrdinalIgnoreCase));
    }

    private static async Task UpdateChartLockAsync(ChartLock chartLock, ConcurrentBag<(ChartReference, string)> result, CancellationToken ct)
    {
        foreach (var (chartReference, digest) in result.OrderBy(x => x.Item1.ChartName))
        {
            var dependency = FindChartLockDependencyForUpdate(chartLock, chartReference);
            if (dependency == null)
            {
                dependency = new ChartLockDependency
                {
                    Name = chartReference.ChartName,
                    Version = chartReference.Tag,
                    Repository = chartReference.RepositoryFullPath,
                    Digest = digest,
                };
                chartLock.Dependencies.Add(dependency);
            }
            else
            {
                dependency.Version = chartReference.Tag;
                dependency.Repository = string.IsNullOrWhiteSpace(dependency.Repository) ? chartReference.RepositoryFullPath : dependency.Repository;
                dependency.Digest = digest;
            }
        }
        
        await ConfigurationManager.SaveChartLockAsync(chartLock, ct);
    }

    private static bool TryGetBlobCacheForDigest(string lockDigest, string cacheRoot, out string path)
    {
        path = string.Empty;
        if (string.IsNullOrWhiteSpace(lockDigest))
        {
            return false;
        }

        var digestParts = lockDigest.Split(':', 2, StringSplitOptions.RemoveEmptyEntries);
        var digestAlgorithm = digestParts.Length > 1 ? digestParts[0] : "sha256";
        var digestValue = digestParts.Length > 1 ? digestParts[1] : lockDigest;

        cacheRoot = Path.Combine(cacheRoot, digestAlgorithm);
        Directory.CreateDirectory(cacheRoot);

        path = Path.Combine(cacheRoot, digestValue);

        return File.Exists(path);
    }

    private static string NormalizeRepository(string repository)
    {
        return string.IsNullOrWhiteSpace(repository)
            ? string.Empty
            : repository.Trim().TrimEnd('/');
    }
}
