using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace BuildCharts.Tool.Init.Generation;

public sealed record TargetConfig(List<string> Types, Dictionary<string, object> With);

public static class DotNet
{
    public static async Task<Dictionary<string, TargetConfig>> CreateBuildConfig(string outputPath, CancellationToken ct)
    {
        var projectTypeMap = new Dictionary<string, TargetConfig>();

        var csprojFiles = Directory.GetFiles(".", "*.csproj", SearchOption.AllDirectories);
        var sdkVersion = await DetectSdkVersion(ct, csprojFiles) ?? "9.0";

        var sb = new StringBuilder();
        sb.AppendLine("version: v1beta");
        sb.AppendLine();
        sb.AppendLine("variables:");
        sb.AppendLine("  - VERSION");
        sb.AppendLine("  - COMMIT");
        sb.AppendLine();
        sb.AppendLine("targets:");
        
        var slnFile = Directory.GetFiles(".", "*.sln", SearchOption.AllDirectories).FirstOrDefault()
                      ?? Directory.GetFiles(".", "*.slnx", SearchOption.AllDirectories).FirstOrDefault()
                      ?? throw new FileNotFoundException("No .sln file found in the root or src directory.");

        var slnRelPath = Path.GetRelativePath(Directory.GetCurrentDirectory(), slnFile).Replace("\\", "/");

        // Add .sln build target.
        projectTypeMap[slnRelPath] = new TargetConfig(["build"], new Dictionary<string, object>
        {
            ["base"] = $"mcr.microsoft.com/dotnet/sdk:{sdkVersion}",
        });

        // Add projects with type detection.
        foreach (var file in csprojFiles)
        {
            var content = await File.ReadAllTextAsync(file, ct);
            var relPath = Path.GetRelativePath(Directory.GetCurrentDirectory(), file).Replace("\\", "/");

            var types = new List<string>();
            var with = new Dictionary<string, object>();

            if (content.Contains("Microsoft.NET.Test.Sdk") ||
                content.Contains("<PackageReference Include=\"xunit") ||
                content.Contains("<PackageReference Include=\"nunit"))
            {
                types.Add("test");
            }

            if (content.Contains("<PackageId>") ||
                content.Contains("<GeneratePackageOnBuild>true</GeneratePackageOnBuild>"))
            {
                types.Add("nuget");
            }

            var dockerfilePath = Path.Combine(Path.GetDirectoryName(file)!, "Dockerfile");
            if (File.Exists(dockerfilePath))
            {
                types.Add("docker");

                var projectName = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
                with["base"] = $"mcr.microsoft.com/dotnet/aspnet:{sdkVersion}";
                with["tags"] = new[] { $"docker.io/username/{projectName}:${{VERSION}}-${{COMMIT}}" };
            }

            if (types.Count > 0)
            {
                projectTypeMap[relPath] = new TargetConfig(types, with);
            }
        }

        // Emit targets.
        foreach (var (relPath, (types, with)) in projectTypeMap.OrderBy(x => x.Value.Types.First() switch
         {
             "build" => 0,
             "test" => 1,
             "nuget" => 2,
             "docker" => 3,
             _ => 0,
         }))
        {
            sb.AppendLine($"  {relPath}:");

            if (types.Count == 1)
            {
                sb.AppendLine($"    type: {types[0]}");
            }
            else
            {
                sb.AppendLine($"    type: [{string.Join(", ", types)}]");
            }

            if (with.Count > 0)
            {
                sb.AppendLine("    with:");
                foreach (var kvp in with)
                {
                    sb.Append($"      {kvp.Key}: ");

                    if (kvp.Value is string s)
                    {
                        sb.AppendLine(s);
                    }
                    else if (kvp.Value is IEnumerable<string> stringList)
                    {
                        sb.AppendLine($"[{string.Join(", ", stringList.Select(t => $"\"{t}\""))}]");
                    }
                    else
                    {
                        sb.AppendLine(kvp.Value.ToString());
                    }
                }
            }

            sb.AppendLine();
        }

        await File.WriteAllTextAsync(outputPath, sb.ToString(), ct);

        return projectTypeMap;
    }

    private static async Task<string> DetectSdkVersion(CancellationToken ct, string[] csprojFiles)
    {
        var globalJsonPath = Path.Combine(Directory.GetCurrentDirectory(), "global.json");

        if (File.Exists(globalJsonPath))
        {
            using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(globalJsonPath, ct));

            if (doc.RootElement.TryGetProperty("sdk", out var sdk) && sdk.TryGetProperty("version", out var versionProp))
            {
                var version = versionProp.GetString(); // e.g. "9.0.302"
                if (!string.IsNullOrWhiteSpace(version))
                {
                    // keep only the major.minor part -> "9.0"
                    var parts = version.Split('.');
                    if (parts.Length >= 2)
                    {
                        return $"{parts[0]}.{parts[1]}";
                    }
                }
            }
        }

        foreach (var file in csprojFiles)
        {
            var content = await File.ReadAllTextAsync(file, ct);

            var match = Regex.Match(content, @"<TargetFramework>net(\d+\.\d+)");
            if (!match.Success)
            {
                continue;
            }

            return match.Groups[1].Value;
        }

        return null;
    }
}
