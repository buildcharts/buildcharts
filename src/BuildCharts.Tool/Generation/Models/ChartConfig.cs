using System.Collections.Generic;

namespace BuildCharts.Tool.Generation.Models;

public class ChartConfig
{
    public string ApiVersion { get; set; }
    public string Name { get; set; }
    public string Version { get; set; }
    public string Description { get; set; }
    public string Type { get; set; }
    public List<ChartDependency> Dependencies { get; set; }
}

public class ChartDependency
{
    public string Name { get; set; }
    public string Alias { get; set; }
    public string Version { get; set; }
    public string Repository { get; set; }
}
