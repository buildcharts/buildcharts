using System.IO;

namespace BuildCharts.Tool.Chart;

public class ChartOptions
{
    public string CachePath { get; set; } = Path.Combine(Path.GetTempPath(), "buildcharts", "oci", "blobs");
}