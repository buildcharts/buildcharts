using BuildCharts.Tool.Generation;
using BuildCharts.Tool.Generation.Models;
using BuildCharts.Tool.Generation.YamlTypeConverters;
using BuildCharts.Tool.Oras;
using BuildCharts.Tool.Plugins.NuGetAuthenticate;
using McMaster.Extensions.CommandLineUtils;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace BuildCharts.Tool.Commands;

[Command(Name = "generate", Description = "Generate build using declarative metadata")]
public class GenerateCommand
{
    [Argument(0, Name = "use-inline-dockerfiles", Description = "Use inlined dockerfiles")]
    public bool UseInlineDockerFile { get; set; } = false;

    private readonly BakeGenerator _bakeGenerator;

    public GenerateCommand()
    {
        _bakeGenerator = new BakeGenerator();
    }

    public async Task<int> OnExecuteAsync(CommandLineApplication app, CancellationToken ct)
    {
        try
        {
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .WithTypeConverter(new FlexibleListYamlTypeConverter<TargetDefinition>())
                .IgnoreUnmatchedProperties()
                .Build();

            var buildYaml = await File.ReadAllTextAsync("build.yml", ct);
            var chartYaml = await File.ReadAllTextAsync("charts/buildcharts/Chart.yaml", ct);

            var buildConfig = deserializer.Deserialize<BuildConfig>(buildYaml);
            var chartConfig = deserializer.Deserialize<ChartConfig>(chartYaml);


            var pluginTasks = buildConfig.Plugins.Select(plugin =>
            {
                switch (plugin)
                {
                    case "nuget-authenticate":
                        var nuGetAuthenticatePlugin = new NuGetAuthenticatePlugin();
                        return nuGetAuthenticatePlugin.OnExecuteAsync(ct);
                    default:
                        return Task.CompletedTask;
                }
            });

            await Task.WhenAll(pluginTasks);

            var pullTasks = chartConfig.Dependencies.Select(dependency => OrasClient.Pull($"{dependency.Repository}/{dependency.Name}:{dependency.Version}"));
            await Task.WhenAll(pullTasks);

            await _bakeGenerator.GenerateAsync("buildcharts.hcl", buildConfig, chartConfig, UseInlineDockerFile);
            Console.WriteLine("Generated buildcharts.hcl");

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }
}