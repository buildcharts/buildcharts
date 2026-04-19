using McMaster.Extensions.CommandLineUtils;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace BuildCharts.Tool.Commands;

[Command(Name = "version", Description = "Show version information")]
public class VersionCommand
{
    public int OnExecute()
    {
        try
        {
            var assembly = typeof(Program).Assembly;
            var version = GetProductVersion(assembly);
            var buildDate = GetBuildDate(assembly);
            var os = RuntimeInformation.OSDescription.Trim();
            var arch = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
            var totalMemory = GetTotalPhysicalMemory();
            var memoryDisplay = totalMemory.HasValue ? FormatBytes(totalMemory.Value) : "unknown";

            Console.WriteLine("buildcharts");
            Console.WriteLine($" version:       {version}");
            Console.WriteLine($" built:         {buildDate}");
            Console.WriteLine($" os/arch:       {os}/{arch}");
            Console.WriteLine($" cpu/mem:       {Environment.ProcessorCount} cores/{memoryDisplay}");
            Console.WriteLine($" .NET version:  {Environment.Version}");
            Console.WriteLine("");

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    private static string GetProductVersion(Assembly assembly)
    {
        var infoVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(infoVersion))
        {
            return infoVersion;
        }

        var fallbackVersion = assembly.GetName().Version?.ToString() ?? "unknown";
        var assemblyLocation = assembly.Location;
        if (string.IsNullOrWhiteSpace(assemblyLocation))
        {
            return fallbackVersion;
        }

        var fvi = FileVersionInfo.GetVersionInfo(assemblyLocation);
        return !string.IsNullOrWhiteSpace(fvi.ProductVersion) ? fvi.ProductVersion : fallbackVersion;
    }

    private static string GetBuildDate(Assembly assembly)
    {
        var buildPath = assembly.Location;

        if (string.IsNullOrWhiteSpace(buildPath))
        {
            buildPath = Environment.ProcessPath;
        }

        if (string.IsNullOrWhiteSpace(buildPath) || !File.Exists(buildPath))
        {
            return "unknown";
        }

        return File.GetLastWriteTimeUtc(buildPath).ToString("yyyy-MM-ddTHH:mm:ssZ");
    }

    private static long? GetTotalPhysicalMemory()
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var info = new NativeMethods.MEMORYSTATUSEX
                {
                    dwLength = (uint)Marshal.SizeOf<NativeMethods.MEMORYSTATUSEX>()
                };

                if (NativeMethods.GlobalMemoryStatusEx(ref info))
                {
                    return (long)info.ullTotalPhys;
                }
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                const string memInfoPath = "/proc/meminfo";
                if (File.Exists(memInfoPath))
                {
                    foreach (var line in File.ReadLines(memInfoPath))
                    {
                        if (line.StartsWith("MemTotal:", StringComparison.OrdinalIgnoreCase))
                        {
                            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length >= 2 && long.TryParse(parts[1], out var value))
                            {
                                return value * 1024;
                            }

                            break;
                        }
                    }
                }
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "/usr/sbin/sysctl",
                    Arguments = "hw.memsize",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });

                var output = process?.StandardOutput.ReadToEnd();
                process?.WaitForExit();

                if (!string.IsNullOrWhiteSpace(output))
                {
                    var separatorIndex = output.IndexOf(':');
                    if (separatorIndex > -1)
                    {
                        var valuePart = output[(separatorIndex + 1)..].Trim();
                        if (long.TryParse(valuePart, out var bytes))
                        {
                            return bytes;
                        }
                    }
                }
            }
        }
        catch
        {
            // ignored: fall back to GC heuristics below
        }

        var gcAvailable = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        if (gcAvailable is > 0 and not long.MaxValue)
        {
            return gcAvailable;
        }

        return null;
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0)
        {
            return "0 B";
        }

        string[] units = { "B", "KB", "MB", "GB", "TB", "PB" };
        double value = bytes;
        var unitIndex = 0;

        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return string.Format(CultureInfo.InvariantCulture, "{0:0.##} {1}", value, units[unitIndex]);
    }

    private static class NativeMethods
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        internal struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }
    }
}
