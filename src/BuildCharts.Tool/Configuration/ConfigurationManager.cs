using BuildCharts.Tool.Configuration.Models;
using BuildCharts.Tool.Configuration.YamlTypeConverters;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace BuildCharts.Tool.Configuration;

public static class ConfigurationManager
{
    private static readonly IDeserializer _deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .WithTypeConverter(new FlexibleListYamlTypeConverter<TargetDefinition>())
        .IgnoreUnmatchedProperties()
        .Build();

    public static async Task<(string, BuildConfig)> ReadBuildConfigAsync(CancellationToken ct)
    {
        var yaml = await File.ReadAllTextAsync("build.yml", ct);
        var config = _deserializer.Deserialize<BuildConfig>(yaml);

        return (yaml, config);
    }

    public static async Task<(string, ChartConfig)> ReadChartConfigAsync(CancellationToken ct)
    {
        var yaml = await File.ReadAllTextAsync("charts/buildcharts/Chart.yaml", ct);
        var config = _deserializer.Deserialize<ChartConfig>(yaml);

        return (yaml, config);
    }
}