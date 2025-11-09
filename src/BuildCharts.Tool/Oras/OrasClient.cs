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
    private static readonly SemaphoreSlim _semaphore = new (1,1);

    public static async Task Pull(string reference, string outputDir = ".buildcharts", CancellationToken ct = default)
    {
        if (!ChartReference.TryParse(reference, out var parsedReference))
        {
            throw new ArgumentException("Invalid chart reference");
        }

        await Pull(parsedReference, outputDir, ct);
    }

    private static async Task Pull(ChartReference chartReference, string outputDir = ".buildcharts", CancellationToken ct = default)
    {
        try
        {
            //Console.WriteLine($"Pulling {repository}:{tag}");

            var client = new Client
            {
                CredentialProvider = await DockerCredentialHelper.GetCredentialAsync(chartReference.Registry),
            };

            var orasRepository = new Repository(new RepositoryOptions
            {
                Client = client,
                Reference = new Reference(chartReference.Registry, chartReference.RepositoryPath),
            });

            var cacheRoot = Path.Combine(Path.GetTempPath(), "buildcharts", "oci", "blobs");
            Directory.CreateDirectory(cacheRoot);

            // Check existing Chart.lock for digest
            var digest = await ReadChartLockDigestAsync(chartReference, ct);
            if (!string.IsNullOrWhiteSpace(digest) && TryGetBlobCacheForDigest(digest, cacheRoot, out var cachedBlobFilePath))
            {
                Console.WriteLine($"Pulled: {chartReference.Registry}/{chartReference.RepositoryPath}:{chartReference.Tag} ({new FileInfo(cachedBlobFilePath).Length} bytes) (cached)");
                Console.WriteLine($"Digest: {digest} (cached)");
            }
            else
            {
                var (manifestDescriptor, manifestStream) = await orasRepository.Manifests.FetchAsync(chartReference.Tag, ct);
                digest = manifestDescriptor.Digest;

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
                
                TryGetBlobCacheForDigest(digest, cacheRoot, out cachedBlobFilePath);

                await using var blobFile = File.Create(cachedBlobFilePath);
                await chartStream.CopyToAsync(blobFile, ct);

                Console.WriteLine($"Pulled: {chartReference.Registry}/{chartReference.RepositoryPath}:{chartReference.Tag} ({blobSize} bytes)");
                Console.WriteLine($"Digest: {manifestDescriptor.Digest}");
            }

            // Unzip the chart to output directory.
            await using var tgzStream = File.OpenRead(cachedBlobFilePath);
            await using var gzipStream = new GZipInputStream(tgzStream);
            using var tarArchive = TarArchive.CreateInputTarArchive(gzipStream, Encoding.UTF8);

            await _semaphore.WaitAsync(ct);
            try
            {
                tarArchive.ExtractContents(outputDir);
                await UpdateChartLockAsync(chartReference, digest, ct);
            }
            finally
            {
                _semaphore.Release();
            }
        }
        catch (ResponseException e)
        {
            var errors = e.Errors?.Select(x => $"{x.Code}: {x.Message}") ?? new List<string>();
            Console.WriteLine($"Error pulling image: {e.RequestUri} {string.Join(",", errors)}");
            throw;
        }
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

    private static async Task<string> ReadChartLockDigestAsync(ChartReference chartReference, CancellationToken ct)
    {
        var (_, chartLock) = await ConfigurationManager.ReadChartLockAsync(ct);
        var dependency = FindChartLockDependency(chartLock, chartReference);
        return dependency?.Digest;
    }

    private static async Task UpdateChartLockAsync(ChartReference chartReference, string digest, CancellationToken ct)
    {
        var (_, chartLock) = await ConfigurationManager.ReadChartLockAsync(ct);

        var dependency = FindChartLockDependency(chartLock, chartReference);
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

        await ConfigurationManager.SaveChartLockAsync(chartLock, ct);
    }
    
    private static ChartLockDependency FindChartLockDependency(ChartLockFile chartLock, ChartReference chartReference)
    {
        var dependency = chartLock.Dependencies
            .Where(x => string.Equals(x.Name, chartReference.ChartName))
            .Where(x => string.Equals(x.Repository, chartReference.RepositoryFullPath))
            .FirstOrDefault();
        return dependency;
    }
}
