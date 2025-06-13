using BuildCharts.Tool.Generation.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BuildCharts.Tool.Generation;

public class BakeGenerator
{
    private readonly HashSet<string> _usedNames = [];
    private readonly bool _inlineDockerFile = true;

    public async Task GenerateAsync(string outputPath, BuildConfig buildConfig, ChartConfig chartConfig)
    {
        var sb = new StringBuilder();

        // Emit variables block
        foreach (var param in buildConfig.Environment)
        {
            sb.AppendLine($"variable \"{param.ToUpperInvariant()}\" {{}}");
        }

        sb.AppendLine();

        // Emit common target
        sb.AppendLine("target \"_common\" {");
        sb.AppendLine("  args = {");

        foreach (var param in buildConfig.Environment)
        {
            sb.AppendLine($"    {param.ToUpperInvariant()} = \"${{{param.ToUpperInvariant()}}}\"");
        }

        sb.AppendLine("  }");
        sb.AppendLine("}\n");
    
        var typedTargets = new List<TypedTarget>();

        // Emit targets
        foreach (var (src, definitions) in buildConfig.Targets)
        {
            foreach (var def in definitions)
            {
                var type = def.Type;
                var chartAlias = chartConfig.Dependencies.FirstOrDefault(d => d.Alias.Equals(type, StringComparison.OrdinalIgnoreCase))?.Name;
                var target = UniqueName(buildConfig, src, type);

                typedTargets.Add(new TypedTarget(target, type));

                sb.AppendLine($"target \"{target}\" {{");
                sb.AppendLine($"  inherits = [\"_common\"]");

                if (type == "build")
                {
                    sb.AppendLine("  context = \".\"");
                }

                sb.AppendLine($"  target = \"{type}\"");

                if (_inlineDockerFile)
                {
                    var dockerfilePath = $".buildcharts/{chartAlias}/Dockerfile";
                    if (!File.Exists(dockerfilePath))
                    {
                        throw new FileNotFoundException($"Missing Dockerfile: {dockerfilePath}");
                    }

                    var dockerfileContent = await File.ReadAllTextAsync(dockerfilePath);
                    sb.AppendLine("  dockerfile-inline = <<BUILDCHARTS_EOF");
                    sb.AppendLine(dockerfileContent.TrimEnd());
                    sb.AppendLine("BUILDCHARTS_EOF");
                }
                else
                {
                    sb.AppendLine($"  dockerfile = \"./.buildcharts/{chartAlias}/Dockerfile\"");
                }

                sb.AppendLine("  args = {");
                sb.AppendLine($"    BUILDCHARTS_SRC = \"{src}\"");
                sb.AppendLine($"    BUILDCHARTS_TYPE = \"{type}\"");
                sb.AppendLine("  }");

                sb.AppendLine("  contexts = {");
                if (type != "build")
                {
                    sb.AppendLine($"    build = \"target:build\"");
                }

                // Add custom context if defined
                if (def.With.TryGetValue("base", out var baseImage))
                {
                    sb.AppendLine($"    base = \"docker-image://{baseImage}\"");
                }
                sb.AppendLine("  }");

                // Add custom tags if defined
                if (def.With.TryGetValue("tags", out var rawTags) && rawTags is List<object> tagList)
                {
                    var tags = tagList.Cast<string>().Select(t => $"\"{t}\"");
                    sb.AppendLine($"  tags = [\n    {string.Join(",\n    ", tags)}\n  ]");
                }

                if (type == "docker")
                {
                    sb.AppendLine("  output = [\"type=docker\"]");
                }
                else
                {
                    sb.AppendLine("  output = [\"type=cacheonly\"]");
                }

                sb.AppendLine("}\n");
            }
        }

        // Emit output target
        var inlineDockerfile = new List<string> { "FROM scratch AS output" };

        foreach (var target in typedTargets.Where(x => x.Type is not "docker" and not "build"))
        {
            inlineDockerfile.Add($"COPY --link --from={target.Name} /output /{target.Type}");
        }

        sb.AppendLine("target \"output\" {");
        sb.AppendLine("  dockerfile-inline = <<BUILDCHARTS_EOF");
        sb.AppendLine(string.Join(Environment.NewLine, inlineDockerfile));
        sb.AppendLine("BUILDCHARTS_EOF");
        sb.AppendLine("  contexts = {");

        foreach (var target in typedTargets.Where(x => x.Type is not "docker" and not "build"))
        {
            sb.AppendLine($"    {target.Name} = \"target:{target.Name}\"");
        }

        sb.AppendLine("  }");
        sb.AppendLine("  output = [");
        sb.AppendLine("    \"type=local,dest=.buildcharts/output\"");
        sb.AppendLine("  ]");
        sb.AppendLine("}\n");

        // Emit group
        sb.AppendLine("group \"default\" {");
        sb.AppendLine($"  targets = [{string.Join(", ", typedTargets.Select(x => $"\"{x.Name}\""))}, \"output\"]");
        sb.AppendLine("}");

        await File.WriteAllTextAsync(outputPath, sb.ToString());
    }

    private string UniqueName(BuildConfig buildConfig, string src, string type)
    {
        // Count how many targets share this type
        var totalOfType = buildConfig.Targets
            .SelectMany(kvp => kvp.Value)
            .Count(def => def.Type.Equals(type, StringComparison.OrdinalIgnoreCase));

        // If there's only one, keep it short
        if (totalOfType == 1)
        {
            return type.ToLowerInvariant();
        }

        // Normalize src (remove extension and sanitize path)
        var cleanSrc = src
            .Replace("\\", "/") // normalize slashes
            .TrimEnd('/')
            .Replace("/", "-")
            .Replace(".", "-");

        var name = $"{type}_{cleanSrc}".ToLowerInvariant();

        // Ensure uniqueness in case of collisions
        var originalName = name;
        int counter = 1;
        while (!_usedNames.Add(name))
        {
            name = $"{originalName}-{counter++}";
        }

        return name;
    }

    private record TypedTarget(string Name, string Type);
}