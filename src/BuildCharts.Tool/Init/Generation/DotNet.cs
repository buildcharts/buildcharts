using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        var sdkVersion = await DetectSdkVersion(ct, csprojFiles) ?? "10.0";

        var sb = new StringBuilder();
        sb.AppendLine("# yaml-language-server: $schema=https://raw.githubusercontent.com/eddietisma/buildcharts/main/schemas/v1beta.json");
        sb.AppendLine();
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
            ["contexts"] = new Dictionary<string, string>
            {
                ["base"] = $"docker-image://mcr.microsoft.com/dotnet/sdk:{sdkVersion}",
            },
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
                var (runtimeImage, runtimeTag) = await DetectRuntimeAsync(file, ct);
                with["contexts"] = new Dictionary<string, string>
                {
                    ["base"] = $"docker-image://mcr.microsoft.com/dotnet/{runtimeImage}:{runtimeTag}",
                };
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
                    if (kvp.Value is IReadOnlyDictionary<string, string> stringDictionary)
                    {
                        sb.AppendLine($"      {kvp.Key}:");
                        foreach (var item in stringDictionary)
                        {
                            sb.AppendLine($"        {item.Key}: {item.Value}");
                        }
                    }
                    else if (kvp.Value is string s)
                    {
                        sb.Append($"      {kvp.Key}: ");
                        sb.AppendLine(s);
                    }
                    else if (kvp.Value is IEnumerable<string> stringList)
                    {
                        sb.Append($"      {kvp.Key}: ");
                        sb.AppendLine($"[{string.Join(", ", stringList.Select(t => $"\"{t}\""))}]");
                    }
                    else
                    {
                        sb.Append($"      {kvp.Key}: ");
                        sb.AppendLine(kvp.Value.ToString());
                    }
                }
            }

            sb.AppendLine();
        }

        await File.WriteAllTextAsync(outputPath, sb.ToString(), ct);

        return projectTypeMap;
    }

    private static async Task<(string Image, string Tag)> DetectRuntimeAsync(string project, CancellationToken ct)
    {
        using var evaluation = await EvaluateProjectAsync(project, null, ct);
        var properties = evaluation.RootElement.GetProperty("Properties");
        var framework = properties.GetProperty("TargetFramework").GetString();
        if (string.IsNullOrWhiteSpace(framework))
        {
            // A single Docker target uses the first declared framework.
            framework = properties.GetProperty("TargetFrameworks").GetString()?.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        }

        var version = Regex.Match(framework ?? "", @"^net(\d+\.\d+)(?:-|$)");
        if (!version.Success)
        {
            throw new InvalidOperationException($"Cannot select a .NET runtime image for '{project}' targeting '{framework}'. Specify a supported TargetFramework.");
        }

        using var targetEvaluation = string.IsNullOrWhiteSpace(properties.GetProperty("TargetFramework").GetString())
            ? await EvaluateProjectAsync(project, framework, ct)
            : null;
        var references = (targetEvaluation ?? evaluation).RootElement.GetProperty("Items").GetProperty("FrameworkReference");
        var image = references.EnumerateArray().Any(reference => string.Equals(reference.GetProperty("Identity").GetString(), "Microsoft.AspNetCore.App", StringComparison.OrdinalIgnoreCase))
            ? "aspnet"
            : "runtime";

        return (image, version.Groups[1].Value);
    }

    private static async Task<JsonDocument> EvaluateProjectAsync(string project, string framework, CancellationToken ct)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("msbuild");
        startInfo.ArgumentList.Add(project);
        startInfo.ArgumentList.Add("-nologo");
        startInfo.ArgumentList.Add("-property:Configuration=Release");
        startInfo.ArgumentList.Add("-getProperty:TargetFramework,TargetFrameworks");
        startInfo.ArgumentList.Add("-getItem:FrameworkReference");
        if (framework != null)
        {
            startInfo.ArgumentList.Add($"-property:TargetFramework={framework}");
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start dotnet msbuild.");
        using var cancellation = ct.Register(() =>
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        });
        var output = process.StandardOutput.ReadToEndAsync(ct);
        var error = process.StandardError.ReadToEndAsync(ct);
        await Task.WhenAll(output, error);
        await process.WaitForExitAsync(ct);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Cannot evaluate '{project}': {(await error).Trim()} {(await output).Trim()}");
        }

        return JsonDocument.Parse(await output);
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
                    // keep only the major.minor part -> "10.0"
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
