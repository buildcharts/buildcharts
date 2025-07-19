using BuildCharts.Tool.Configuration.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BuildCharts.Tool.Summary.Extensions;

public static class SummaryOrderingExtensions
{
    public static IOrderedEnumerable<T> OrderByYamlAlias<T>(this IEnumerable<T> items, ChartConfig chartConfig, Func<T, string> aliasSelector)
    {
        if (items is null)
        {
            throw new ArgumentNullException(nameof(items));
        }

        if (chartConfig is null)
        {
            throw new ArgumentNullException(nameof(chartConfig));
        }

        if (aliasSelector is null)
        {
            throw new ArgumentNullException(nameof(aliasSelector));
        }

        // Build a map: alias → zero‑based position in YAML.
        var aliasPosition = chartConfig.Dependencies
            .Select((d, idx) => new { d.Alias, idx })
            .ToDictionary(x => x.Alias, x => x.idx, StringComparer.OrdinalIgnoreCase);

        // Sort by that map.
        return items.OrderBy(item =>
        {
            var alias = aliasSelector(item);

            // If alias is null, empty, or whitespace → push to the top.
            return string.IsNullOrWhiteSpace(alias) 
                ? 0
                : aliasPosition.GetValueOrDefault(alias, 0);
        });
    }
}