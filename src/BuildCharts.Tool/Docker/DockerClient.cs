using BuildCharts.Tool.Docker.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BuildCharts.Tool.Docker;

public record BuildxHistoryRecord(string BuildId, BuildxHistory History, BuildxInspect Inspect, BuildxLog Log, List<string> Logs);

public static class DockerClient
{
    public static async Task<List<BuildxHistoryRecord>> FetchLatestBuildxHistory(CancellationToken ct)
    {
        var result = new List<BuildxHistoryRecord>();

        var latestInspect = await InspectBuildAsync(null, ct);
        var latestContextId = latestInspect.Config.RestRaw["local-sessionid:context"];

        Console.WriteLine($"Generating summary for build: {latestInspect.Ref[..7]} ({latestInspect.Name})");

        var records = await GetBuildHistoryAsync(ct);

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = Environment.ProcessorCount,
            CancellationToken = ct,
        };

        await Parallel.ForEachAsync(records, parallelOptions, async (record, cancellationToken) =>
        {
            var buildId = record.Ref[(record.Ref.LastIndexOf('/') + 1)..];
            var inspect = await InspectBuildAsync(buildId, cancellationToken);

            if (!inspect.Config.RestRaw.TryGetValue("local-sessionid:context", out var contextId) || contextId != latestContextId)
            {
                return;
            }

            Console.WriteLine($"- {buildId[..7].ToUpper()} {inspect.Name} {inspect.Duration / 1_000_000_000d:0.0}s");

            var logsRawTask = GetBuildLogRawAsync(buildId, cancellationToken);
            var logsTextErr = GetBuildLogTextAsync(buildId, cancellationToken);

            await Task.WhenAll(logsRawTask, logsTextErr);

            var logsRaw = await logsRawTask;
            var logsText = await logsTextErr;

            var unique = logsRaw.Vertexes
                .GroupBy(v => v.Digest)
                .Select(g => g.Where(x => x.Completed != null).OrderBy(v => v.Completed).First())
                //.Select(g => g.OrderBy(x => x.Completed).First(x => x.Completed != null))
                .OrderBy(x => x.Completed)
                .ToList();

            logsRaw.Vertexes = unique;

            var logs = logsText.Split("\n").Select(x => x.TrimEnd()).ToList();

            result.Add(new BuildxHistoryRecord(buildId, record, inspect, logsRaw, logs));
        });

        //result.ForEach(x => x.Inspect.Config = null);
        //var x = JsonSerializer.Serialize(result);
        return result.OrderBy(x => x.Inspect.BuildArgs.FirstOrDefault(y => y.Name == "BUILDCHARTS_SRC")?.Value).ToList();
    }

    /// <summary>
    /// buildx history ls --format json
    /// </summary>
    /// <param name="ct"></param>
    /// <returns></returns>
    public static async Task<List<BuildxHistory>> GetBuildHistoryAsync(CancellationToken ct)
    {
        var (stdOut, _) = await RunAsync("docker", "buildx history ls --format json", ct);

        var result = stdOut
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => JsonSerializer.Deserialize<BuildxHistory>(line))
            .ToList();

        return result;
    }

    /// <summary>
    /// buildx history inspect {buildId} --format json
    /// </summary>
    /// <param name="buildId"></param>
    /// <param name="ct"></param>
    /// <returns></returns>
    public static async Task<BuildxInspect> InspectBuildAsync(string buildId, CancellationToken ct)
    {
        var (stdOut, _) = await RunAsync("docker", $"buildx history inspect {buildId} --format json", ct);
        var result = JsonSerializer.Deserialize<BuildxInspect>(stdOut);
        return result;
    }

    /// <summary>
    /// buildx history logs {buildId} --progress rawjson
    /// </summary>
    /// <param name="buildId"></param>
    /// <param name="ct"></param>
    /// <returns></returns>
    public static async Task<BuildxLog> GetBuildLogRawAsync(string buildId, CancellationToken ct)
    {
        var result = new BuildxLog();

        var (_, errOut) = await RunAsync("docker", $"buildx history logs {buildId} --progress rawjson", ct);

        // Handle buildx history sometimes responding with multiple JSON documents.
        foreach (var line in errOut.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.TrimStart();
            if (!trimmed.StartsWith('{'))
            {
                continue;
            }

            try
            {
                var partial = JsonSerializer.Deserialize<BuildxLog>(trimmed);
                if (partial is null)
                {
                    continue;
                }

                result.Vertexes.AddRange(partial.Vertexes);
                result.Statuses.AddRange(partial.Statuses);
            }
            catch (JsonException ex)
            {
                Console.Error.WriteLine($"⚠️  Skipped malformed JSON: {ex.Message}");
            }
        }

        return result; 
    }

    /// <summary>
    /// buildx history logs {buildId}
    /// </summary>
    /// <param name="buildId"></param>
    /// <param name="ct"></param>
    /// <returns></returns>
    public static async Task<string> GetBuildLogTextAsync(string buildId, CancellationToken ct)
    {
        var (_, errOut) = await RunAsync("docker", $"buildx history logs {buildId}", ct);
        return errOut;
    }

    /// <summary>
    /// docker buildx history export --finalize -o {outputPath} {buildIds...}
    /// </summary>
    /// <param name="outputPath">Destination file path.</param>
    /// <param name="buildIds">One or more build identifiers to export.</param>
    /// <param name="ct">Cancellation token.</param>
    public static async Task ExportBuildHistoryAsync(string outputPath, IEnumerable<string> buildIds, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentNullException.ThrowIfNull(buildIds);

        var ids = buildIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList();
        if (ids.Count == 0)
        {
            throw new ArgumentException("At least one build ID is required to export history.", nameof(buildIds));
        }

        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var fullOutputPath = Path.GetFullPath(outputPath);
        var idsArg = string.Join(' ', ids);

        await RunAsync("docker", $"buildx history export --finalize -o \"{fullOutputPath}\" {idsArg}", ct);
    }


    private static async Task<(string, string)> RunAsync(string file, string args, CancellationToken ct)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = file,
            Arguments = args,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        }) ?? throw new InvalidOperationException($"Failed to start process: {file}.");

        var stdOutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stdErrTask = process.StandardError.ReadToEndAsync(ct);

        await Task.WhenAll(stdOutTask, stdErrTask);
        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0)
        {
            var error = (await stdErrTask).TrimEnd();
            throw new InvalidOperationException(error);
        }

        return (await stdOutTask, await stdErrTask);
    }
}
