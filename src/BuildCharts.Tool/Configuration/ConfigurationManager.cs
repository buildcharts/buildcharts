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

    private static readonly ISerializer _serializer = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitEmptyCollections | DefaultValuesHandling.OmitNull)
        .DisableAliases()
        .Build();

    public static async Task<(string, BuildConfig)> ReadBuildConfigAsync(CancellationToken ct)
    {
        var yaml = await File.ReadAllTextAsync("build.yml", ct);
        var config = _deserializer.Deserialize<BuildConfig>(yaml) ?? new BuildConfig();

        return (yaml, config);
    }

    public static async Task<(string, ChartConfig)> ReadChartConfigAsync(CancellationToken ct)
    {
        var yaml = await File.ReadAllTextAsync("charts/buildcharts/Chart.yaml", ct);
        var config = _deserializer.Deserialize<ChartConfig>(yaml) ?? new ChartConfig();

        return (yaml, config);
    }

    public static async Task<(string, ChartLockFile)> ReadChartLockAsync(CancellationToken ct)
    {
        var yaml = await File.ReadAllTextAsync("Chart.lock", ct);
        var config = _deserializer.Deserialize<ChartLockFile>(yaml) ?? new ChartLockFile();

        return (yaml, config);
    }

    public static async Task SaveChartLockAsync(ChartLockFile chartLockFile, CancellationToken ct)
    {
        var yaml = _serializer.Serialize(chartLockFile);
        await File.WriteAllTextAsync("Chart.lock", yaml, ct);
    }
}