using BuildCharts.Tool.Docker;
using BuildCharts.Tool.Docker.Models;
using McMaster.Extensions.CommandLineUtils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace BuildCharts.Tool.Commands;

/// <summary>
/// https://github.com/docker/actions-toolkit/blob/e6e18dee2531192e00536d6f21c792e6d01c0b3a/src/github.ts#L272-L299
/// </summary>
[Command(Name = "summary", Description = "Generate summary from last build")]
public class SummaryCommand
{
    public async Task<int> OnExecuteAsync(CommandLineApplication app, CancellationToken ct)
    {
        try
        {
            var history = await FetchLatestBuildxHistory(ct);

            File.Delete("SUMMARY.md");

            var sb = new StringBuilder();

            var buildInfo = history.First().Inspect;
            var repository = buildInfo.VCSRepository.Replace(".git", "");
            var revision = buildInfo.VCSRevision;

            sb.AppendLine("## BuildCharts Build Summary");
            sb.AppendLine();
            sb.AppendLine($"- Repository: [{repository}]({repository})");
            sb.AppendLine($"- Commit: [{revision[..7]}]({repository}/commit/{revision})");
            sb.AppendLine($"");
            sb.AppendLine($"### Overview");

            sb.AppendLine($"| ID | Name | Status | Cached | Duration |");
            sb.AppendLine($"| --------| ----------------------| ----------------| --------| ---------:|");

            foreach (var (buildId, record, inspect, log, logs) in history.OrderBy(x => x.Inspect.StartedAt))
            {
                var cacheRatio = (double)inspect.NumCachedSteps / inspect.NumTotalSteps;
                var duration = inspect.Duration / 1_000_000_000d;
                var status = inspect.Status?.ToLowerInvariant() switch
                {
                    "completed" => "✅ completed",
                    "failed" => "❌ failed",
                    "canceled" => "🚫 canceled",
                    _ => "❓ unknown",
                };

                sb.AppendLine($"| <code>{buildId[..7].ToUpper()}</code> | <b>{inspect.Name}</b> | {status} | {Math.Round(cacheRatio * 100, 0)}% | {duration:0.0}s |");
            }

            sb.AppendLine("<details><summary><strong>Build metadata definition</strong></summary>");
            var code = await File.ReadAllTextAsync("build.yml", ct);
            sb.AppendLine("");
            sb.AppendLine("```yaml");
            sb.AppendLine(code.TrimEnd());
            sb.AppendLine("```");
            sb.AppendLine("</details>");
            sb.AppendLine("");
            sb.AppendLine("### Logs");
            sb.AppendLine("");
            sb.AppendLine("<table style=\"width:100%\">");
            sb.AppendLine("  <tbody>");

            foreach (var (buildId, record, inspect, log, logs) in history)
            {
                var filteredLog = log.Vertexes;
                long filteredDuration = 0;

                //if (inspect.Target == null)
                {
                    foreach (var context in inspect.NamedContexts.Where(x => x.Value.StartsWith("input")))
                    {
                        var target = context.Name;
                        var contextHistory = history.First(x => x.Inspect.Target == target);
                        filteredDuration += contextHistory.Inspect.Duration;

                        foreach (var contextVertex in contextHistory.Log.Vertexes)
                        {
                            var vertex = filteredLog.FirstOrDefault(x => x.Digest == contextVertex.Digest);
                            if (vertex is not null)
                            {
                                //var nameStr = Regex.Replace(vertex.Name, @"^\[(.*?)\]", m => $"[{target} {m.Groups[1].Value}]");
                                vertex.Target = target;
                                vertex.Cached = true;
                            }
                            //filteredLog = filteredLog.Where(x => x.Digest != contextVertex.Digest).ToList();
                        }
                    }
                }

                var jobDuration = (inspect.Duration - filteredDuration) / 1_000_000_000d;
                var jobDurationStr = $"{jobDuration:0.0}s";

                //var jobDuration = Math.Max((record.CompletedAt - record.CreatedAt).TotalSeconds, 0);
                //var jobDurationStr = $"{jobDuration:0.#}s";

                var status = inspect.Status?.ToLowerInvariant() switch
                {
                    "completed" => "✅",
                    "failed" => "❌",
                    "canceled" => "🚫",
                    _ => "❓ unknown",
                };

                sb.AppendLine("    <tr>");
                sb.AppendLine($"     <th align=\"left\"><h3>{status} {inspect.Name}</h3></th>");
                sb.AppendLine($"     <th align=\"right\"><b>&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;{jobDurationStr}</b></th>");
                sb.AppendLine("    </tr>");

                var vertexes = filteredLog
                    .Where(x => x.Completed is not null)
                    .OrderBy(x => x.Completed)
                    .ToList();
                
                foreach (var vertex in vertexes)
                {
                    //if (vertex.Inputs != null)
                    //{
                    //    var currentVertex = vertex.Inputs.First();
                    //    var x = vertexes.First(x => x.Digest == currentVertex);
                    //    if (x.Inputs == null)
                    //    {
                    //        var y = history.SelectMany(h => h.Log.Vertexes.Where(v => v.Inputs.Contains(currentVertex)));
                    //    }
                    //}

                    var nameStr = Regex.Replace(vertex.Name, @"^\[(.*?)\]", m => $"<code>{m.Groups[1].Value}</code>");

                    if (!string.IsNullOrEmpty(vertex.Target))
                    {
                        nameStr = $"<code>{vertex.Target}</code> {nameStr}";
                    }

                    var cached = vertex.Cached;
                    var cachedStr = cached == true ? "<code>CACHED</code>" : "";

                    var duration = cached == true ? 0 : Math.Max((vertex.Completed!.Value - vertex.Started!.Value).TotalSeconds, 0);
                    var durationStr = $"{duration:0.0}s";

                    var (hasDetails, str) = VertexHasDetails(vertex, logs);
                    if (hasDetails)
                    {
                        sb.AppendLine("    <tr>");
                        //sb.AppendLine($"     <td><ul><li>- [x] {nameStr}</li></ul></td>");
                        sb.AppendLine($"     <td><details><summary>{nameStr}</summary><p><pre>{str.TrimEnd()}</pre></p></details></td>");
                        sb.AppendLine($"     <td align=\"right\">{cachedStr}&nbsp;&nbsp;{durationStr}</td>");
                        sb.AppendLine("    </tr>");
                    }
                    else
                    {
                        sb.AppendLine("    <tr>");
                        sb.AppendLine($"     <td><details><summary>{nameStr}</summary></details></td>");
                        //sb.AppendLine($"     <td>{nameStr}</td>");
                        sb.AppendLine($"     <td align=\"right\">{cachedStr} {durationStr}</td>");
                        sb.AppendLine("    </tr>");
                    }
                }
            }
            sb.AppendLine("  </tbody>");
            sb.AppendLine("<table>");

            await File.AppendAllTextAsync("SUMMARY.md", sb.ToString(), ct);

            Console.WriteLine("Generated SUMMARY.md");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    private static (bool, string) VertexHasDetails(Vertex vertex, List<string> logs)
    {
        var result = false;
        var sb = new StringBuilder();

        var log = logs.FirstOrDefault(x => x.EndsWith(vertex.Name));
        if (log == null)
        {
            return (result, sb.ToString());
        }

        var currentIndex = logs.IndexOf(log) + 1;

        while (logs[currentIndex] != "")
        {
            var line = logs[currentIndex];

            line = Regex.Replace(line, @"^#\d+\s+", "");
            line = Regex.Replace(line, @"^\d+\.\d+\s+", "");

            if (!Regex.IsMatch(line, @"^(DONE|CACHED)\b", RegexOptions.IgnoreCase))
            {
                sb.AppendLine(line);
                result = true;
            }

            currentIndex++;
        }

        return (result, sb.ToString());
    }

    private static async Task<List<BuildxHistoryRecord>> FetchLatestBuildxHistory(CancellationToken ct)
    {
        var result = new List<BuildxHistoryRecord>();

        Console.WriteLine("Fetching latest build history...");
        var records = await DockerClient.GetBuildHistoryAsync(ct);

        var latestInspect = await DockerClient.InspectBuildAsync(null, ct);
        var latestContextId = latestInspect.Config.RestRaw["local-sessionid:context"];

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = Environment.ProcessorCount,
            CancellationToken = ct,
        };

        await Parallel.ForEachAsync(records, parallelOptions, async (record, cancellationToken) =>
        {
            var buildId = record.Ref[(record.Ref.LastIndexOf('/') + 1)..];
            var inspect = await DockerClient.InspectBuildAsync(buildId, cancellationToken);

            if (!inspect.Config.RestRaw.TryGetValue("local-sessionid:context", out var contextId) || contextId != latestContextId)
            {
                return;
            }

            Console.WriteLine($"Build: {buildId} {inspect.Name}");

            var logsRawTask = DockerClient.GetBuildLogRawAsync(buildId, cancellationToken);
            var logsTextErr = DockerClient.GetBuildLogTextAsync(buildId, cancellationToken);

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
        return result;
    }

    public record BuildxHistoryRecord(string BuildId, BuildxHistory History, BuildxInspect Inspect, BuildxLog Log, List<string> Logs);
}
