using BuildCharts.Tool.Configuration.YamlTypeConverters;
using System.Collections.Generic;

namespace BuildCharts.Tool.Configuration.Models;

public class BuildConfig
{
    public string Version { get; set; }
    public List<string> Environment { get; set; } = [];
    public List<string> Plugins { get; set; } = [];
    public Dictionary<string, FlexibleList<TargetDefinition>> Targets { get; set; } = [];
}

public class TargetDefinition
{
    public string Type { get; set; }
    public Dictionary<string, object> With { get; set; } = new();
}