using System.Collections.Generic;

namespace BuildCharts.Tool.Configuration.Models;

public class ChartLock
{
    public List<ChartLockDependency> Dependencies { get; set; } = [];
    public int LockVersion { get; set; } = 1;
}

public class ChartLockDependency
{
    public string Name { get; set; }
    public string Version { get; set; }
    public string Repository { get; set; }
    public string Digest { get; set; }
}
