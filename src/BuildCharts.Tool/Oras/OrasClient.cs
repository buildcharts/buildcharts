using BuildCharts.Tool.Chart;
using BuildCharts.Tool.Configuration;
using BuildCharts.Tool.Configuration.Models;
using BuildCharts.Tool.Docker;
using ICSharpCode.SharpZipLib.GZip;
using ICSharpCode.SharpZipLib.Tar;
using OrasProject.Oras.Oci;
using OrasProject.Oras.Registry;
using OrasProject.Oras.Registry.Remote;
using OrasProject.Oras.Registry.Remote.Auth;
using OrasProject.Oras.Registry.Remote.Exceptions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BuildCharts.Tool.Oras;

public static class OrasClient
{
    private static readonly SemaphoreSlim _semaphore = new(1, 1);
    private static bool _lockSyncChecked;


    public static async Task<string> Pull(string reference, bool untar, string untarDir, string outputDir, bool useDigestName = false, CancellationToken ct = default)
    {
        try
        {
            if (!ChartReference.TryParse(reference, out var chartReference))
            {
                throw new ArgumentException("Invalid chart reference");
            }

            var client = new Client
            {
                CredentialProvider = await DockerCredentialHelper.GetCredentialAsync(chartReference.Registry),
            };

            var orasRepository = new Repository(new RepositoryOptions
            {
                Client = client,
                Reference = new Reference(chartReference.Registry, chartReference.RepositoryPath),
            });

            var (manifestDescriptor, manifestStream) = await orasRepository.Manifests.FetchAsync(chartReference.Tag, ct);
            
            using var manifestJson = await JsonDocument.ParseAsync(manifestStream, cancellationToken: ct);

            var layers = manifestJson.RootElement.GetProperty("layers");
            if (layers.GetArrayLength() == 0)
            {
                throw new Exception("No layers found in the Helm chart manifest.");
            }

            var blobDigest = layers[0].GetProperty("digest").GetString()!;
            var blobSize = layers[0].GetProperty("size").GetInt64();

            await using var chartStream = await orasRepository.Blobs.FetchAsync(new Descriptor
            {
                MediaType = "application/tar+gzip",
                Digest = blobDigest,
                Size = blobSize,
            }, ct);

            var fileName = useDigestName
                ? Path.Join(outputDir, "sha256", manifestDescriptor.Digest.Split(':', 2, StringSplitOptions.RemoveEmptyEntries)[1])
                : Path.Join(outputDir, $"{chartReference.ChartName}.tgz");

            await using var blobFile = File.Create(fileName);
            await chartStream.CopyToAsync(blobFile, ct);
            Console.WriteLine($"Pulled: {chartReference.Registry}/{chartReference.RepositoryPath}:{chartReference.Tag} ({blobSize} bytes)");
            Console.WriteLine($"Digest: {manifestDescriptor.Digest}");
  
            if (untar)
            {
                // Untar the chart to output directory.
                blobFile.Position = 0;
                await using var tgzStream = blobFile;
                await using var gzipStream = new GZipInputStream(tgzStream);
                using var tarArchive = TarArchive.CreateInputTarArchive(gzipStream, Encoding.UTF8);
                tarArchive.ExtractContents(untarDir);
            }

            return manifestDescriptor.Digest;

        }
        catch (ResponseException e)
        {
            var errors = e.Errors?.Select(x => $"{x.Code}: {x.Message}") ?? new List<string>();
            Console.WriteLine($"Error pulling image: {e.RequestUri} {string.Join(",", errors)}");
            throw;
        }
    }

    private static async Task EnsureChartLockSyncAsync(CancellationToken ct)
    {
        if (_lockSyncChecked)
        {
            return;
        }

        await _semaphore.WaitAsync(ct);
        try
        {
            if (_lockSyncChecked)
            {
                return;
            }

            if (!File.Exists(ConfigurationManager.CHART_CONFIG_PATH) || !File.Exists(ConfigurationManager.CHART_LOCK_PATH))
            {
                _lockSyncChecked = true;
                return;
            }

            var (_, chartConfig) = await ConfigurationManager.ReadChartConfigAsync(ct);
            var (_, chartLock) = await ConfigurationManager.ReadChartLockAsync(ct);

            var mismatches = CalculateChartLockMismatches(chartConfig, chartLock);
            if (mismatches.Count > 0)
            {
                Console.WriteLine("Warning: Chart.lock is out of sync with charts/buildcharts/Chart.yaml:");
                foreach (var mismatch in mismatches)
                {
                    Console.WriteLine($"  - {mismatch}");
                }
                Console.WriteLine("Run `buildcharts update` to refresh the lock file.");
            }

            _lockSyncChecked = true;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private static List<string> CalculateChartLockMismatches(ChartConfig chartConfig, ChartLock chartLock)
    {
        var issues = new List<string>();
        var configDependencies = chartConfig?.Dependencies ?? new List<ChartDependency>();
        var lockDependencies = chartLock?.Dependencies ?? new List<ChartLockDependency>();

        var normalizedLockDeps = lockDependencies
            .Select(ld => new
            {
                Dependency = ld,
                Repository = NormalizeRepository(ld.Repository),
            })
            .ToList();

        foreach (var dependency in configDependencies)
        {
            var expectedRepo = NormalizeRepository(BuildRepository(dependency));
            if (string.IsNullOrWhiteSpace(expectedRepo))
            {
                continue;
            }

            var lockEntry = normalizedLockDeps
                .FirstOrDefault(ld =>
                    string.Equals(ld.Repository, expectedRepo, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(ld.Dependency.Name, dependency.Name, StringComparison.OrdinalIgnoreCase))
                ?.Dependency;

            if (lockEntry == null)
            {
                issues.Add($"Missing entry for {dependency.Name}@{dependency.Version} ({expectedRepo})");
                continue;
            }

            if (!string.Equals(lockEntry.Version, dependency.Version, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add($"Version mismatch for {dependency.Name}: Chart.yaml={dependency.Version}, Chart.lock={lockEntry.Version}");
            }
        }

        foreach (var lockDep in normalizedLockDeps)
        {
            var hasMatch = configDependencies.Any(dep =>
                string.Equals(NormalizeRepository(BuildRepository(dep)), lockDep.Repository, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(dep.Name, lockDep.Dependency.Name, StringComparison.OrdinalIgnoreCase));

            if (!hasMatch)
            {
                issues.Add($"Orphaned lock entry {lockDep.Dependency.Name}@{lockDep.Dependency.Version} ({lockDep.Dependency.Repository})");
            }
        }

        return issues;
    }

    private static string BuildRepository(ChartDependency dependency)
    {
        if (dependency == null || string.IsNullOrWhiteSpace(dependency.Repository) || string.IsNullOrWhiteSpace(dependency.Name))
        {
            return string.Empty;
        }

        var baseRepo = dependency.Repository.Trim().TrimEnd('/');
        return $"{baseRepo}/{dependency.Name}".TrimEnd('/');
    }

    private static string NormalizeRepository(string repository)
    {
        return string.IsNullOrWhiteSpace(repository)
            ? string.Empty
            : repository.Trim().TrimEnd('/');
    }
}
