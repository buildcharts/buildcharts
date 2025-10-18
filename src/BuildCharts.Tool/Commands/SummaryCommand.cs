using BuildCharts.Tool.Configuration;
using BuildCharts.Tool.Docker;
using BuildCharts.Tool.Summary;
using McMaster.Extensions.CommandLineUtils;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BuildCharts.Tool.Commands;

[Command(Name = "summary", Description = "Generate summary from last docker build")]
public class SummaryCommand
{
    private readonly SummaryGenerator _summaryGenerator = new();

    public async Task<int> OnExecuteAsync(CommandLineApplication app, CancellationToken ct)
    {
        try
        {
            var (buildYaml, buildConfig) = await ConfigurationManager.ReadBuildConfigAsync(ct);
            var (chartYaml, chartConfig) = await ConfigurationManager.ReadChartConfigAsync(ct);

            var history = await DockerClient.FetchLatestBuildxHistory(ct);
            if (history.Count == 0)
            {
                throw new InvalidOperationException("Unable to find docker build history. Run a build before generating the summary.");
            }
            
            var exportPathDockerBuild = Path.Join(".buildcharts", "output", "buildcharts.dockerbuild");
            var exportPathSummary = Path.Join(".buildcharts", "output", "SUMMARY.md");
            
            await DockerClient.ExportBuildHistoryAsync(exportPathDockerBuild, history.Select(x => x.BuildId), ct);

            var summaryStringBuilder = await _summaryGenerator.GenerateAsync(buildConfig, buildYaml, chartConfig, chartYaml, history, ct);
            await File.WriteAllTextAsync(exportPathSummary, summaryStringBuilder.ToString(), ct);

            Console.WriteLine("");
            Console.WriteLine("✅ Generated files:");
            Console.WriteLine("   • \u001b[2mSUMMARY.md\u001b[22m");
            Console.WriteLine("   • \u001b[2mbuildcharts.dockerbuild\u001b[22m");
            Console.WriteLine("");
            
            var isAzure = string.Equals(Environment.GetEnvironmentVariable("TF_BUILD"), "true", StringComparison.OrdinalIgnoreCase);
            if (isAzure)
            {
                Console.WriteLine($"##vso[artifact.upload artifactname=buildcharts;]{Path.GetFullPath(exportPathDockerBuild)}");
                Console.WriteLine($"##vso[artifact.upload artifactname=buildcharts;]{Path.GetFullPath(exportPathSummary)}");
                Console.WriteLine($"##vso[task.uploadsummary]{Path.GetFullPath(exportPathSummary)}");
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }
}
