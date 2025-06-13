using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace BuildCharts.Tool.Scaffolding.Generation;

public static class BuildConfig
{
    public static async Task<Dictionary<string, (List<string> Types, Dictionary<string, object> With)>> CreateBuildConfig(string outputPath, CancellationToken ct)
    {
        var projectTypeMap = new Dictionary<string, (List<string> Types, Dictionary<string, object> With)>();

        var csprojFiles = Directory.GetFiles(".", "*.csproj", SearchOption.AllDirectories);

        var slnFile = Directory.GetFiles(".", "*.sln", SearchOption.TopDirectoryOnly).FirstOrDefault()
            ?? throw new FileNotFoundException("No .sln file found in the root directory.");

        // Detect SDK version.
        var sdkVersion = "9.0"; // fallback default
        foreach (var file in csprojFiles)
        {
            var content = await File.ReadAllTextAsync(file, ct);

            var match = Regex.Match(content, @"<TargetFramework>net(\d+\.\d+)");
            if (!match.Success)
            {
                continue;
            }

            sdkVersion = match.Groups[1].Value;
            break;
        }

        var sb = new StringBuilder();
        sb.AppendLine("version: latest");
        sb.AppendLine();
        sb.AppendLine("environment:");
        sb.AppendLine("  - VERSION");
        sb.AppendLine("  - COMMIT");
        sb.AppendLine();
        sb.AppendLine("targets:");

        var slnRelPath = Path.GetRelativePath(Directory.GetCurrentDirectory(), slnFile).Replace("\\", "/");

        // Add .sln build target.
        projectTypeMap[slnRelPath] = (["build"], new Dictionary<string, object>
        {
            ["base"] = $"mcr.microsoft.com/dotnet/sdk:{sdkVersion}",
        });

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
                projectTypeMap[relPath] = (types, with);
            }
        }

        // Emit targets.
        foreach (var (relPath, (types, with)) in projectTypeMap)
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
}