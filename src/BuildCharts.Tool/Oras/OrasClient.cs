using BuildCharts.Tool.Docker;
using ICSharpCode.SharpZipLib.GZip;
using ICSharpCode.SharpZipLib.Tar;
using OrasProject.Oras.Oci;
using OrasProject.Oras.Registry;
using OrasProject.Oras.Registry.Remote;
using OrasProject.Oras.Registry.Remote.Auth;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace BuildCharts.Tool.Oras;

public static class OrasClient
{
    public static async Task Pull(string reference, string outputDir = ".buildcharts")
    {
        var tag = "latest";

        // Strip "oci://" prefix.
        if (reference.StartsWith("oci://", StringComparison.OrdinalIgnoreCase))
        {
            reference = reference.Substring("oci://".Length);
        }

        // Validate input.
        var firstSlash = reference.IndexOf('/');
        if (firstSlash == -1)
        {
            throw new ArgumentException("Invalid reference. Use format: oci://registry/repository[:tag]");
        }

        var tagIndex = reference.LastIndexOf(':');
        var hasTag = tagIndex > reference.LastIndexOf('/');

        string registry, repo;
        if (hasTag)
        {
            tag = reference[(tagIndex + 1)..];
            var path = reference[..tagIndex];
            registry = path[..path.IndexOf('/')];
            repo = path[(path.IndexOf('/') + 1)..];
        }
        else
        {
            registry = reference[..firstSlash];
            repo = reference[(firstSlash + 1)..];
        }

        await Pull(registry, repo, tag, outputDir);
    }

    public static async Task Pull(string registry, string repository, string tag, string outputDir = ".buildcharts")
    {
        try
        {
            //Console.WriteLine($"Pulling {repository}:{tag}");

            var client = new Client
            {
                CredentialProvider = await DockerCredentialHelper.GetCredentialAsync(registry),
            };

            var orasRepository = new Repository(new RepositoryOptions
            {
                Client = client,
                Reference = new Reference(registry, repository, tag),
            });

            //Console.WriteLine("Fetching manifest...");
            var (manifestDescriptor, manifestStream) = await orasRepository.FetchAsync(tag);

            using var manifestJson = await JsonDocument.ParseAsync(manifestStream);
            var layers = manifestJson.RootElement.GetProperty("layers");

            if (layers.GetArrayLength() == 0)
            {
                throw new Exception("No layers found in the Helm chart manifest.");
            }

            var chartLayer = layers[0];
            var chartDigest = chartLayer.GetProperty("digest").GetString();
            var chartSize = chartLayer.GetProperty("size").GetInt64();

            //Console.WriteLine($"Downloading layers ({chartSize} bytes)...");

            await using var chartStream = await orasRepository.FetchAsync(new Descriptor
            {
                MediaType = "application/tar+gzip",
                Digest = chartDigest!,
                Size = chartSize,
            });

            var tgzFilePath = Path.Combine(outputDir, $"{repository.Replace('/', '-')}-{tag}.tgz");
            Directory.CreateDirectory(outputDir);

            if (File.Exists(tgzFilePath))
            {
                File.Delete(tgzFilePath);
            }

            await using (var fileStream = File.Create(tgzFilePath))
            {
                await chartStream.CopyToAsync(fileStream);
            }

            //Console.WriteLine(Path.GetFullPath(tgzFilePath));
            //Console.WriteLine($"Download complete");

            Console.WriteLine($"Pulled: {registry}/{repository}:{tag} ({manifestDescriptor.Size} bytes)");
            Console.WriteLine($"Digest: {manifestDescriptor.Digest}");

            await using (var tgzStream = File.OpenRead(tgzFilePath))
            await using (var gzipStream = new GZipInputStream(tgzStream))
            using (var tarArchive = TarArchive.CreateInputTarArchive(gzipStream, Encoding.UTF8))
            {
                tarArchive.ExtractContents(outputDir);
            }

            if (File.Exists(tgzFilePath))
            {
                File.Delete(tgzFilePath);
            }
        }
        catch (ResponseException e)
        {
            var errors = e.Errors?.Select(x => $"{x.Code}: {x.Message}") ?? new List<string>();
            Console.WriteLine($"Error pulling image: {e.RequestUri} {string.Join(",", errors)}");
            throw;
        }
    }
}