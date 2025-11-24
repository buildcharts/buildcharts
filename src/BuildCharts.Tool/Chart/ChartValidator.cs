using BuildCharts.Tool.Configuration.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BuildCharts.Tool.Chart;

public static class ChartValidator
{
    public static Task ValidateConfigAsync(BuildConfig buildConfig)
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

        return Task.CompletedTask;
    }

    public static Task ValidateLockFileAsync(ChartConfig chartConfig, ChartLock chartLock, bool useLockFile, CancellationToken ct = default)
    {
        if (!useLockFile)
        {
            return Task.CompletedTask;
        }

        var mismatches = CalculateChartLockMismatches(chartConfig, chartLock);
        if (mismatches.Count <= 0)
        {
            return Task.CompletedTask;
        }

        var sb = new StringBuilder();
        sb.AppendLine("Chart.lock is out of sync with charts/buildcharts/Chart.yaml:");
        foreach (var mismatch in mismatches)
        {
            sb.AppendLine($"  - {mismatch}");
        }
        sb.Append("Run `buildcharts update` to refresh the lock file.");
     
        throw new InvalidOperationException(sb.ToString());
    }

    public static List<string> CalculateChartLockMismatches(ChartConfig chartConfig, ChartLock chartLock)
    {
        var issues = new List<string>();
        var configDependencies = chartConfig?.Dependencies ?? [];
        var lockDependencies = chartLock?.Dependencies ?? [];

        var normalizedLockDeps = lockDependencies
            .Select(ld => new
            {
                Dependency = ld,
                Repository = NormalizeRepository(ld.Repository),
            })
            .ToList();

        foreach (var dependency in configDependencies)
        {
            var expectedRepo = NormalizeRepository(BuildRepository(dependency));
            if (string.IsNullOrWhiteSpace(expectedRepo))
            {
                continue;
            }

            var lockEntry = normalizedLockDeps
                .FirstOrDefault(ld =>
                    string.Equals(ld.Repository, expectedRepo, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(ld.Dependency.Name, dependency.Name, StringComparison.OrdinalIgnoreCase))
                ?.Dependency;

            if (lockEntry == null)
            {
                issues.Add($"Missing entry for {dependency.Name}@{dependency.Version} ({expectedRepo})");
                continue;
            }

            if (!string.Equals(lockEntry.Version, dependency.Version, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add($"Version mismatch for {dependency.Name}: Chart.yaml={dependency.Version}, Chart.lock={lockEntry.Version}");
            }
        }

        foreach (var lockDep in normalizedLockDeps)
        {
            var hasMatch = configDependencies.Any(dep =>
                string.Equals(NormalizeRepository(BuildRepository(dep)), lockDep.Repository, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(dep.Name, lockDep.Dependency.Name, StringComparison.OrdinalIgnoreCase));

            if (!hasMatch)
            {
                issues.Add($"Orphaned lock entry {lockDep.Dependency.Name}@{lockDep.Dependency.Version} ({lockDep.Dependency.Repository})");
            }
        }

        return issues;
    }

    private static string BuildRepository(ChartDependency dependency)
    {
        if (dependency == null || string.IsNullOrWhiteSpace(dependency.Repository) || string.IsNullOrWhiteSpace(dependency.Name))
        {
            return string.Empty;
        }

        var baseRepo = dependency.Repository.Trim().TrimEnd('/');
        return $"{baseRepo}/{dependency.Name}".TrimEnd('/');
    }

    private static string NormalizeRepository(string repository)
    {
        return string.IsNullOrWhiteSpace(repository)
            ? string.Empty
            : repository.Trim().TrimEnd('/');
    }
}
