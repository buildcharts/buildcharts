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
        ["TestcontainersDinD@v1"] = typeof(TestcontainersDinDPlugin),
    };

    public static List<IBuildChartsPlugin> LoadPlugins(IEnumerable<string> entries)
    {
        var result = new List<IBuildChartsPlugin>();

        foreach (var entry in entries.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!_builtIns.TryGetValue(entry, out var type))
            {
                Console.WriteLine($"\u001b[33mWarning: Plugin '{entry}' not found\u001b[0m");
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