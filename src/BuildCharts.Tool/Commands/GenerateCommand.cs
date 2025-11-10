using BuildCharts.Tool.Chart;
using BuildCharts.Tool.Configuration;
using BuildCharts.Tool.Generate;
using BuildCharts.Tool.Plugins;
using McMaster.Extensions.CommandLineUtils;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace BuildCharts.Tool.Commands;

[Command(Name = "generate", Description = "Generate build using declarative metadata")]
public class GenerateCommand
{
    [Argument(0, Name = "use-inline-dockerfiles", Description = "Use inlined dockerfiles")]
    public bool UseInlineDockerFile { get; set; } = false;

    private readonly DockerHclGenerator _dockerHclGenerator = new();

    public async Task<int> OnExecuteAsync(CommandLineApplication app, CancellationToken ct)
    {
        try
        {
            if (Directory.Exists(".buildcharts"))
            {
                Directory.Delete(".buildcharts", true);
            }

            if (!File.Exists(ConfigurationManager.CHART_CONFIG_PATH))
            {
                Console.Error.WriteLine($"Error: Could not find {ConfigurationManager.CHART_CONFIG_PATH}. Run this command from the repository root.");
                return 1;
            }

            if (!File.Exists("Chart.lock"))
            {
                File.Create("Chart.lock");
            }

            var (_, buildConfig) = await ConfigurationManager.ReadBuildConfigAsync(ct);
            var (_, chartConfig) = await ConfigurationManager.ReadChartConfigAsync(ct);
            var (_, chartLock) = await ConfigurationManager.ReadChartLockAsync(ct);
            
            ChartValidator.ValidateConfig(buildConfig);
            //await ChartManager.ValidateLockFile(chartConfig, chartLock, ct);

            var plugins = PluginManager.LoadPlugins(buildConfig.Plugins);

            Console.WriteLine("Pulling charts...");
            await ChartManager.UpdateAsync(chartConfig, chartLock, ct: ct);

            foreach (var plugin in plugins)
            {
                await plugin.OnBeforeGenerateAsync(buildConfig, ct);
            }
          
            var hclStringBuilder = await _dockerHclGenerator.GenerateAsync(buildConfig, chartConfig, UseInlineDockerFile);

            foreach (var plugin in plugins)
            {
                await plugin.OnAfterGenerateAsync(buildConfig, chartConfig, hclStringBuilder, ct);
            }
            
            await File.WriteAllTextAsync(Path.Join(".buildcharts", "docker-bake.hcl"), hclStringBuilder.ToString(), ct);

            Console.WriteLine("");
            Console.WriteLine("✅ Generated files:");
            Console.WriteLine("   • \u001b[2mdocker-bake.hcl\u001b[22m");
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
