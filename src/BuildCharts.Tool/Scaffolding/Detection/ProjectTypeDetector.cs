using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BuildCharts.Tool.Scaffolding.Detection;

public static class ProjectTypeDetector
{
    public static Task<ProjectType> DetectAsync(CancellationToken ct)
    {
        var sln = Directory.GetFiles(".", "*.sln", SearchOption.AllDirectories).FirstOrDefault();
        if (sln != null)
        {
            return Task.FromResult(ProjectType.DotNet);
        }

        return Task.FromResult(ProjectType.Unknown);
    }
}