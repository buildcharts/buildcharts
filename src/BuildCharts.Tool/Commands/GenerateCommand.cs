using BuildCharts.Tool.Chart;
using BuildCharts.Tool.Configuration;
using BuildCharts.Tool.Configuration.Models;
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
    [Option("--use-inline-dockerfiles", Description = "Use inlined dockerfiles instead of referencing external Dockerfiles.")]
    public bool UseInlineDockerFile { get; set; } = false;

    [Option("--use-lock-file", Description = "Enables chart lock file to be generated and used when pulling charts.")]
    public bool UseLockFile { get; set; } = false;

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
          
            if (!File.Exists(ConfigurationManager.CHART_LOCK_PATH) && UseLockFile)
            {
                Console.Error.WriteLine($"Error: Could not find {ConfigurationManager.CHART_LOCK_PATH}. Run `buildcharts update` to refresh the lock file.");
                return 1;
            }

            var (_, buildConfig) = await ConfigurationManager.ReadBuildConfigAsync(ct);
            var (_, chartConfig) = await ConfigurationManager.ReadChartConfigAsync(ct);
            var (_, chartLock) = UseLockFile ? await ConfigurationManager.ReadChartLockAsync(ct) : (null, new ChartLock());

            await ChartValidator.ValidateConfigAsync(buildConfig);
            await ChartValidator.ValidateLockFileAsync(chartConfig, chartLock, UseLockFile, ct);

            Console.WriteLine("Pulling charts...");
            await ChartManager.UpdateAsync(chartConfig, chartLock, useLockFile: UseLockFile, ct: ct);

            var plugins = PluginManager.LoadPlugins(buildConfig.Plugins);
            foreach (var plugin in plugins)
            {
                Console.WriteLine("");
                Console.WriteLine($"\u001b[2mRunning plugin: {plugin.Name}\u001b[22m");
                await plugin.OnBeforeGenerateAsync(buildConfig, ct);
            }
          
            var hclStringBuilder = await _dockerHclGenerator.GenerateAsync(buildConfig, chartConfig, UseInlineDockerFile);

            foreach (var plugin in plugins)
            {
                await plugin.OnAfterGenerateAsync(buildConfig, chartConfig, hclStringBuilder, ct);
                Console.WriteLine($"\u001b[2mPlugin complete: {plugin.Name}\u001b[22m");
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
