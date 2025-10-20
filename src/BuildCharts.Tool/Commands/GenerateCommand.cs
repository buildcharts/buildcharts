using BuildCharts.Tool.Configuration;
using BuildCharts.Tool.Generate;
using BuildCharts.Tool.Oras;
using BuildCharts.Tool.Plugins;
using McMaster.Extensions.CommandLineUtils;
using System;
using System.IO;
using System.Linq;
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
            var (_, buildConfig) = await ConfigurationManager.ReadBuildConfigAsync(ct);
            var (_, chartConfig) = await ConfigurationManager.ReadChartConfigAsync(ct);

            _dockerHclGenerator.Validate(buildConfig);
            
            var plugins = PluginManager.LoadPlugins(buildConfig.Plugins);

            Console.WriteLine("Pulling charts...");
            var pullTasks = chartConfig.Dependencies.Select(dependency => OrasClient.Pull($"{dependency.Repository}/{dependency.Name}:{dependency.Version}"));
            await Task.WhenAll(pullTasks);

            var onBeforeGeneratePluginTasks = plugins.Select(x => x.OnBeforeGenerateAsync(buildConfig, ct));
            await Task.WhenAll(onBeforeGeneratePluginTasks);

            var hclStringBuilder = await _dockerHclGenerator.GenerateAsync(buildConfig, chartConfig, UseInlineDockerFile);
           
            var onAfterGeneratePluginTasks = plugins.Select(x => x.OnAfterGenerateAsync(buildConfig, chartConfig, hclStringBuilder, ct));
            await Task.WhenAll(onAfterGeneratePluginTasks);
            
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