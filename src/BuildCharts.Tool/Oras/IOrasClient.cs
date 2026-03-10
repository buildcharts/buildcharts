using BuildCharts.Tool.Chart;
using System.Threading;
using System.Threading.Tasks;

namespace BuildCharts.Tool.Oras;

public interface IOrasClient
{
    Task<string> Pull(string reference, bool untar, string untarDir, string outputDir, CancellationToken ct = default);
    Task<string> GetManifestDigestAsync(ChartReference chartReference, CancellationToken ct = default);
}