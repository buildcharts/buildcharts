using System.Collections.Generic;

namespace BuildCharts.Tool.Configuration.Models;

public class BuildConfig
{
    public string Version { get; set; }
    public Dictionary<string, VariableDefinition> Variables { get; set; } = [];
    public List<string> Plugins { get; set; } = [];
    public Dictionary<string, List<TargetDefinition>> Targets { get; set; } = [];
}

public class VariableDefinition
{
    public string Default { get; set; } = string.Empty;
}

public class TargetDefinition
{
    public string Type { get; set; }
    public Dictionary<string, object> With { get; set; } = new();
}
