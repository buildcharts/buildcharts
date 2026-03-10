using BuildCharts.Tool.Chart;
using BuildCharts.Tool.Configuration;
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

public class OrasClient : IOrasClient
{
    public async Task<string> Pull(string reference, bool untar, string untarDir, string outputDir, CancellationToken ct = default)
    {
        if (!ChartReference.TryParse(reference, out var chartReference))
        {
            throw new ArgumentException("Invalid chart reference: either tag or digest must be set.");
        }

        try
        {
            var client = new Client
            {
                CredentialProvider = await DockerCredentialHelper.GetCredentialAsync(chartReference.Registry),
            };

            var orasRepository = new Repository(new RepositoryOptions
            {
                Client = client,
                Reference = new Reference(chartReference.Registry, chartReference.RepositoryPath),
            });

            var manifestReference = chartReference.IsDigest ? chartReference.Digest : chartReference.Tag;
            var (manifestDescriptor, manifestStream) = await orasRepository.Manifests.FetchAsync(manifestReference, ct);
            
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

            var fileName = Path.Join(outputDir, chartReference.Filename);

            await using var blobFile = File.Create(fileName);
            await chartStream.CopyToAsync(blobFile, ct);
            var referenceSuffix = chartReference.IsDigest ? $"@{chartReference.Digest}" : $":{chartReference.Tag}";
            Console.WriteLine($"Pulled: {chartReference.Registry}/{chartReference.RepositoryPath}{referenceSuffix} ({blobSize} bytes)");
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

    public async Task<string> GetManifestDigestAsync(ChartReference chartReference, CancellationToken ct = default)
    {
        var client = new Client
        {
            CredentialProvider = await DockerCredentialHelper.GetCredentialAsync(chartReference.Registry),
        };

        var orasRepository = new Repository(new RepositoryOptions
        {
            Client = client,
            Reference = new Reference(chartReference.Registry, chartReference.RepositoryPath),
        });

        var manifestReference = chartReference.IsDigest ? chartReference.Digest : chartReference.Tag;
        var (manifestDescriptor, manifestStream) = await orasRepository.Manifests.FetchAsync(manifestReference, ct);
        await manifestStream.DisposeAsync();

        return manifestDescriptor.Digest;
    }

}
