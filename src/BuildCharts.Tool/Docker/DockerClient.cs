using BuildCharts.Tool.Docker.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BuildCharts.Tool.Docker;

public static class DockerClient
{
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
        var (_, errOut) = await RunAsync("docker", $"buildx history logs {buildId} --progress rawjson", ct);
        var result = JsonSerializer.Deserialize<BuildxLog>(errOut);
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