using BuildCharts.Tool.Configuration.Models;
using BuildCharts.Tool.Docker;
using BuildCharts.Tool.Docker.Models;
using BuildCharts.Tool.Summary.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace BuildCharts.Tool.Summary;

/// <summary>
/// https://github.com/docker/actions-toolkit/blob/e6e18dee2531192e00536d6f21c792e6d01c0b3a/src/github.ts#L272-L299
/// </summary>
public class SummaryGenerator
{
    public async Task<StringBuilder> GenerateAsync(BuildConfig buildConfig, string buildYaml, ChartConfig chartConfig, string chartYaml, List<BuildxHistoryRecord> history, CancellationToken ct)
    {
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

        foreach (var (buildId, record, inspect, log, logs) in history.OrderByYamlAlias(chartConfig, x => x.Inspect.BuildArgs.FirstOrDefault(y => y.Name == "BUILDCHARTS_TYPE")?.Value))
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

            var argSource = inspect.BuildArgs.FirstOrDefault(x => x.Name == "BUILDCHARTS_SRC");
            var argType = inspect.BuildArgs.FirstOrDefault(x => x.Name == "BUILDCHARTS_TYPE");

            var name = inspect.Name;

            if (argSource != null && argType != null)
            {
                name = $"`{argType.Value}` {argSource.Value}";
            }

            sb.AppendLine($"| `{buildId[..7].ToUpper()}` | <b>{name}</b> | {status} | {Math.Round(cacheRatio * 100, 0)}% | {duration:0.0}s |");

        }

        sb.AppendLine("<details><summary><strong>Build metadata definition</strong></summary>");
        sb.AppendLine("");
        sb.AppendLine("```yaml");
        sb.AppendLine(buildYaml.TrimEnd());
        sb.AppendLine("```");
        sb.AppendLine("</details>");
        sb.AppendLine("<details><summary><strong>Chart definition</strong></summary>");
        sb.AppendLine("");
        sb.AppendLine("```yaml");
        sb.AppendLine(chartYaml.TrimEnd());
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

            ////if (inspect.Target == null)
            //{
            //    foreach (var context in inspect.NamedContexts.Where(x => x.Value.StartsWith("input")))
            //    {
            //        var target = context.Name;
            //        var contextHistory = history.First(x => x.Inspect.Target == target);
            //        filteredDuration += contextHistory.Inspect.Duration;

            //        foreach (var contextVertex in contextHistory.Log.Vertexes)
            //        {
            //            var vertex = filteredLog.FirstOrDefault(x => x.Digest == contextVertex.Digest);
            //            if (vertex is not null)
            //            {
            //                //var nameStr = Regex.Replace(vertex.Name, @"^\[(.*?)\]", m => $"[{target} {m.Groups[1].Value}]");
            //                vertex.Target = target;
            //                vertex.Cached = true;
            //            }
            //            //filteredLog = filteredLog.Where(x => x.Digest != contextVertex.Digest).ToList();
            //        }
            //    }
            //}

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

            var argSource = inspect.BuildArgs.FirstOrDefault(x => x.Name == "BUILDCHARTS_SRC");
            var argType = inspect.BuildArgs.FirstOrDefault(x => x.Name == "BUILDCHARTS_TYPE");

            var name = inspect.Name;

            if (argSource != null && argType != null)
            {
                name = $"{argSource.Value} ({argType.Value})";
            }

            sb.AppendLine("    <tr>");
            sb.AppendLine($"     <th align=\"left\"><h3>{status} {name}</h3></th>");
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

        return await Task.FromResult(sb);
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
}
