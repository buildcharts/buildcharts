using BuildCharts.Tool.Plugins.NuGetAuthenticate_v1;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BuildCharts.Tool.Plugins;

public static class PluginManager
{
    private static readonly Dictionary<string, Type> _builtIns = new(StringComparer.OrdinalIgnoreCase)
    {
        ["NuGetAuthenticate@v1"] = typeof(NuGetAuthenticatePlugin),
    };

    public static List<IBuildChartsPlugin> LoadPlugins(IEnumerable<string> entries)
    {
        var result = new List<IBuildChartsPlugin>();

        foreach (var entry in entries.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!_builtIns.TryGetValue(entry, out var type))
            {
                Console.WriteLine($"Warning: Plugin '{entry}' not found");
                continue;
            }

            if (Activator.CreateInstance(type) is IBuildChartsPlugin plugin)
            {
                result.Add(plugin);
            }
        }

        return result;
    }
}