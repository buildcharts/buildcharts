using BuildCharts.Tool.Configuration;
using BuildCharts.Tool.Summary;
using McMaster.Extensions.CommandLineUtils;
using System;
using System.IO;
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

            var summaryStringBuilder = await _summaryGenerator.GenerateAsync(buildConfig, buildYaml, chartConfig, chartYaml, ct);
            
            await File.WriteAllTextAsync("SUMMARY.md", summaryStringBuilder.ToString(), ct);

            Console.WriteLine("");
            Console.WriteLine("✅ Generated files:");
            Console.WriteLine("   • \u001b[2mSUMMARY.md\u001b[22m");
            Console.WriteLine("");

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }
}
