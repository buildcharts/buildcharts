using BuildCharts.Tool.Chart;
using BuildCharts.Tool.Configuration;
using BuildCharts.Tool.Configuration.Models;
using McMaster.Extensions.CommandLineUtils;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace BuildCharts.Tool.Commands;

[Command("update", Description = "Download OCI chart dependencies and refresh Chart.lock")]
public class UpdateCommand
{
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
            await ChartManager.UpdateAsync(chartConfig, chartLock, ct: ct);
            Console.WriteLine("Dependency update complete. Chart.lock is up to date.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }
}
