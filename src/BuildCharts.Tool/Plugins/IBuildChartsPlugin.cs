using BuildCharts.Tool.Configuration.Models;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BuildCharts.Tool.Plugins;

public interface IBuildChartsPlugin
{
    string Name { get; }
    Task OnBeforeGenerateAsync(BuildConfig buildConfig, CancellationToken ct);
    Task OnAfterGenerateAsync(BuildConfig buildConfig, ChartConfig cartConfig, StringBuilder hclStringBuilder, CancellationToken ct);
}