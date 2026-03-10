using BuildCharts.Tool.Chart;
using BuildCharts.Tool.Configuration;
using BuildCharts.Tool.Configuration.Models;
using McMaster.Extensions.CommandLineUtils;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace BuildCharts.Tool.Commands;

[Command(
    "update",
    Description = "Download OCI chart dependencies and refresh Chart.lock",
    ExtendedHelpText = @"
Environment variables:
  DOCKER_CONFIG                  Override path to Docker config."
)]
public class UpdateCommand
{
    private readonly ChartManager _chartManager;

    public UpdateCommand(ChartManager chartManager)
    {
        _chartManager = chartManager;
    }

    public async Task<int> OnExecuteAsync(CommandLineApplication app, CancellationToken ct)
    {
        try
        {
            if (!File.Exists(ConfigurationManager.CHART_CONFIG_PATH))
            {
                Console.Error.WriteLine($"Error: Could not find {ConfigurationManager.CHART_CONFIG_PATH}. Run this command from the repository root.");
                return 1;
            }

            if (!File.Exists(ConfigurationManager.CHART_LOCK_PATH))
            {
                var lockDir = Path.GetDirectoryName(ConfigurationManager.CHART_LOCK_PATH);
                if (!string.IsNullOrWhiteSpace(lockDir))
                {
                    Directory.CreateDirectory(lockDir);
                }
                await using var file = File.Create(ConfigurationManager.CHART_LOCK_PATH);
            }

            var (_, chartConfig) = await ConfigurationManager.ReadChartConfigAsync(ct);
            var chartLock = new ChartLock(); // Always force a new Chart.Lock file on update.

            Console.WriteLine($"Updating {chartConfig.Dependencies.Count} dependencies...");
            await _chartManager.UpdateAsync(chartConfig, chartLock, ct: ct);

            Console.WriteLine("");
            Console.WriteLine("✅ Generated files:");
            Console.WriteLine($"   • \u001b[2m{ConfigurationManager.CHART_LOCK_PATH}\u001b[22m");

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }
}
