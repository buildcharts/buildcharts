using BuildCharts.Tool.Configuration.Models;
using System;
using System.Linq;

namespace BuildCharts.Tool.Chart;

public static class ChartValidator
{
    public static void ValidateConfig(BuildConfig buildConfig)
    {
        var totalBuildTargets = buildConfig.Targets.SelectMany(x => x.Value).Count(x => x.Type == "build");
        if (totalBuildTargets == 0)
        {
            throw new InvalidOperationException("Invalid build.yaml - Missing build target.");
        }

        if (totalBuildTargets > 1)
        {
            throw new InvalidOperationException("Invalid build.yaml - Only 1 build target is supported.");
        }
    }
}