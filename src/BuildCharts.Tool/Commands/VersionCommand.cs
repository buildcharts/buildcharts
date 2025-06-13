using McMaster.Extensions.CommandLineUtils;
using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace BuildCharts.Tool.Commands;

[Command(Name = "version", Description = "Show version information")]
public class VersionCommand
{
    public int OnExecute()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var version = assembly.GetName().Version;
        var location = assembly.Location;
        
        var dotnetVersion = Environment.Version.ToString();
        var gitCommit = version!.Build; 
        var buildDate = File.GetLastWriteTimeUtc(location).ToString("yyyy-MM-ddTHH:mm:ssZ");
        var os = RuntimeInformation.OSDescription.Trim();
        var arch = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();

        Console.WriteLine("buildcharts");
        Console.WriteLine($" version:       {version}");
        Console.WriteLine($" commit:        {gitCommit}");
        Console.WriteLine($" built:         {buildDate}");
        Console.WriteLine($" os/arch:       {os}/{arch}");
        Console.WriteLine($" .NET version:  {dotnetVersion}");

        return 0;
    }
}