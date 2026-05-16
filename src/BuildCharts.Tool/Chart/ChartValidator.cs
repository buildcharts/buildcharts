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
    private static readonly HashSet<string> _reservedContextNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "build",
    };

    public static Task ValidateConfigAsync(BuildConfig buildConfig, ChartConfig chartConfig)
    {
        var totalBuildTargets = buildConfig.Targets.SelectMany(x => x.Value).Count(x => x.Type == "build");
        if (totalBuildTargets == 0)
        {
            throw new InvalidOperationException("Invalid build.yml - Missing build target.");
        }

        if (totalBuildTargets > 1)
        {
            throw new InvalidOperationException("Invalid build.yml - Only 1 build target is supported.");
        }

        var supportedTypes = (chartConfig?.Dependencies ?? [])
            .Select(d => d.Alias)
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (supportedTypes.Count > 0)
        {
            var unknownTypes = buildConfig.Targets
                .SelectMany(x => x.Value)
                .Select(x => x.Type)
                .Where(t => !string.IsNullOrWhiteSpace(t) && !supportedTypes.Contains(t))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (unknownTypes.Count > 0)
            {
                throw new InvalidOperationException($"Invalid build.yml - Unknown target type(s): {string.Join(", ", unknownTypes)}. Add the type(s) to charts/buildcharts/Chart.yaml or fix build.yml.");
            }
        }

        foreach (var (type, typeDefinition) in buildConfig.Types)
        {
            ValidateContextEntries($"types.{type}.contexts", typeDefinition.Contexts);
        }

        foreach (var (target, definitions) in buildConfig.Targets)
        {
            foreach (var definition in definitions)
            {
                if (definition.With.TryGetValue("contexts", out var rawContexts))
                {
                    ValidateTargetContexts($"targets.{target}.with.contexts", rawContexts);
                }
            }
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

    private static void ValidateTargetContexts(string location, object rawContexts)
    {
        if (rawContexts is Dictionary<object, object> objectContexts)
        {
            ValidateContextEntries(location, objectContexts.ToDictionary(k => k.Key?.ToString() ?? string.Empty, v => Convert.ToString(v.Value) ?? string.Empty));
            return;
        }

        if (rawContexts is Dictionary<string, object> stringObjectContexts)
        {
            ValidateContextEntries(location, stringObjectContexts.ToDictionary(k => k.Key, v => Convert.ToString(v.Value) ?? string.Empty));
            return;
        }

        if (rawContexts is Dictionary<string, string> stringContexts)
        {
            ValidateContextEntries(location, stringContexts);
            return;
        }

        throw new InvalidOperationException($"Invalid build.yml - {location} must be a mapping.");
    }

    private static void ValidateContextEntries(string location, IReadOnlyDictionary<string, string> contexts)
    {
        foreach (var (key, value) in contexts)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new InvalidOperationException($"Invalid build.yml - {location} contains an empty context name.");
            }

            if (_reservedContextNames.Contains(key))
            {
                throw new InvalidOperationException($"Invalid build.yml - {location}.{key} is reserved.");
            }

            if (!IsValidHclIdentifier(key))
            {
                throw new InvalidOperationException($"Invalid build.yml - {location}.{key} must be a valid HCL identifier.");
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException($"Invalid build.yml - {location}.{key} must not be empty.");
            }
        }
    }

    private static bool IsValidHclIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !(char.IsLetter(value[0]) || value[0] == '_'))
        {
            return false;
        }

        return value.All(c => char.IsLetterOrDigit(c) || c == '_');
    }
}
