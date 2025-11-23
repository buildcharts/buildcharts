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
    public const string BUILD_CONFIG_PATH = "build.yml";
    public const string CHART_CONFIG_PATH = "charts/buildcharts/Chart.yaml";
    public const string CHART_LOCK_PATH = "charts/buildcharts/Chart.lock";

    private static readonly IDeserializer _deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .WithTypeConverter(new BuildVariablesYamlTypeConverter())
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
        var yaml = await File.ReadAllTextAsync(BUILD_CONFIG_PATH, ct);
        var config = _deserializer.Deserialize<BuildConfig>(yaml) ?? new BuildConfig();

        return (yaml, config);
    }

    public static async Task<(string, ChartConfig)> ReadChartConfigAsync(CancellationToken ct)
    {
        var yaml = await File.ReadAllTextAsync(CHART_CONFIG_PATH, ct);
        var config = _deserializer.Deserialize<ChartConfig>(yaml) ?? new ChartConfig();

        return (yaml, config);
    }

    public static async Task<(string, ChartLock)> ReadChartLockAsync(CancellationToken ct)
    {
        var yaml = await File.ReadAllTextAsync(CHART_LOCK_PATH, ct);
        var config = _deserializer.Deserialize<ChartLock>(yaml) ?? new ChartLock();

        return (yaml, config);
    }

    public static async Task SaveChartLockAsync(ChartLock chartLock, CancellationToken ct)
    {
        var lockDir = Path.GetDirectoryName(CHART_LOCK_PATH);
        if (!string.IsNullOrWhiteSpace(lockDir))
        {
            Directory.CreateDirectory(lockDir);
        }
        var yaml = _serializer.Serialize(chartLock);
        await File.WriteAllTextAsync(CHART_LOCK_PATH, yaml, ct);
    }
}
