using BuildCharts.Tool.Generation;
using BuildCharts.Tool.Generation.Models;
using BuildCharts.Tool.Generation.YamlTypeConverters;
using BuildCharts.Tool.Oras;
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
    private readonly BakeGenerator _bakeGenerator;

    public GenerateCommand()
    {
        _bakeGenerator = new BakeGenerator();
    }

    public async Task<int> OnExecuteAsync(CommandLineApplication app, CancellationToken ct)
    {
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .WithTypeConverter(new FlexibleListYamlTypeConverter<TargetDefinition>())
            .IgnoreUnmatchedProperties()
            .Build();

        var buildYaml = File.ReadAllText("build.yml");
        var chartYaml = File.ReadAllText("charts/buildcharts/Chart.yaml");

        var buildConfig = deserializer.Deserialize<BuildConfig>(buildYaml);
        var chartConfig = deserializer.Deserialize<ChartConfig>(chartYaml);

        var pullTasks = chartConfig.Dependencies.Select(dependency => OrasClient.Pull($"{dependency.Repository}/{dependency.Name}:{dependency.Version}"));
        await Task.WhenAll(pullTasks);

        await _bakeGenerator.GenerateAsync("buildcharts.hcl", buildConfig, chartConfig);
        Console.WriteLine("Generated buildcharts.hcl");

        return 0;
    }
}