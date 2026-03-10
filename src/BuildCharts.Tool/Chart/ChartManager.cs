using BuildCharts.Tool.Configuration;
using BuildCharts.Tool.Configuration.Models;
using BuildCharts.Tool.Oras;
using ICSharpCode.SharpZipLib.GZip;
using ICSharpCode.SharpZipLib.Tar;
using Microsoft.Extensions.Options;
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
    private readonly IOrasClient _orasClient;
    private readonly ChartOptions _options;

    public ChartManager(IOrasClient orasClient, IOptions<ChartOptions> options)
    {
        _orasClient = orasClient;
        _options = options.Value;
    }

    public async Task UpdateAsync(ChartConfig chartConfig, ChartLock chartLock, string outputDir = ".buildcharts", bool useLockFile = true, bool updateChartLockFile = true, CancellationToken ct = default)
    {
        if (chartConfig.Dependencies == null || chartConfig.Dependencies.Count == 0)
        {
            Console.WriteLine($"No dependencies declared in {ConfigurationManager.CHART_CONFIG_PATH}.");
            return;
        }

        var results = new ConcurrentBag<(ChartReference, string)>();
        var digestMismatches = false;

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

            // Folder to cache charts digest.
            Directory.CreateDirectory(_options.CachePath);

            // Resolve registry digest to detect new digest and assume version immutability.
            var digest = await _orasClient.GetManifestDigestAsync(chartReference, cancellationToken);
            var digestReference = $"{chartReference.Registry}/{chartReference.RepositoryPath}@{digest}";

            if (useLockFile)
            {
                // Verify digest in lock file and registry.
                var lockEntry = FindChartLockDependencyForCache(chartLock, chartReference);
                if (lockEntry != null && (string.IsNullOrWhiteSpace(lockEntry.Digest) || !string.Equals(lockEntry.Digest, digest, StringComparison.OrdinalIgnoreCase)))
                {
                    Console.Error.WriteLine($"Digest mismatch for {chartReference.ChartName}@{chartReference.Tag}: Chart.lock={lockEntry?.Digest ?? "NULL"}, registry={digest}");
                    digestMismatches = true;
                    return;
                }
            }
            
            var cacheHit = TryGetBlobCacheForDigest(digestReference, _options.CachePath, out var cachedBlobFilePath);
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
                digest = await _orasClient.Pull(digestReference, untar: true, untarDir: outputDir, outputDir: _options.CachePath, ct: cancellationToken);
            }

            results.Add((chartReference, digest));
        });

        if (digestMismatches)
        {
            throw new InvalidOperationException("Chart.lock is out of sync (digest mismatch). Run `buildcharts update` to refresh.");
        }

        if (updateChartLockFile)
        {
            await UpdateChartLockAsync(chartLock, results, ct);
        }
    }

    public bool TryGetBlobCacheForDigest(string reference, string cacheRoot, out string path)
    {
        path = string.Empty;
        if (string.IsNullOrWhiteSpace(reference))
        {
            return false;
        }

        if (!ChartReference.TryParse(reference, out var chartReference) || !chartReference.IsDigest)
        {
            return false;
        }

        Directory.CreateDirectory(cacheRoot);

        path = Path.Combine(cacheRoot, chartReference.Filename);
        if (!File.Exists(path))
        {
            return false;
        }

        return true;
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

    private static string NormalizeRepository(string repository)
    {
        return string.IsNullOrWhiteSpace(repository)
            ? string.Empty
            : repository.Trim().TrimEnd('/');
    }
}
