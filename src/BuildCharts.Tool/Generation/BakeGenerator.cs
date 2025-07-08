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

    public async Task GenerateAsync(string outputPath, BuildConfig buildConfig, ChartConfig chartConfig, bool useInlineDockerFile)
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
                sb.AppendLine($"  target = \"{type}\"");

                if (type == "build")
                {
                    sb.AppendLine("  context = \".\"");

                    if (buildConfig.Plugins.Contains("nuget-authenticate"))
                    {
                        sb.AppendLine("  secret = [");
                        sb.AppendLine("    \"type=file,id=VSS_NUGET_EXTERNAL_FEED_ENDPOINTS,src=.buildcharts/secrets/VSS_NUGET_EXTERNAL_FEED_ENDPOINTS\",");
                        sb.AppendLine("    \"type=file,id=VSS_NUGET_ACCESSTOKEN,src=.buildcharts/secrets/VSS_NUGET_ACCESSTOKEN\"");
                        sb.AppendLine("  ]");
                    }
                }

                // Emit args
                sb.AppendLine("  args = {");
                sb.AppendLine($"    BUILDCHARTS_SRC = \"{src}\"");
                sb.AppendLine($"    BUILDCHARTS_TYPE = \"{type}\"");
                sb.AppendLine("  }");

                // Emit output
                sb.AppendLine(type == "docker" ?
                    "  output = [\"type=docker\"]" :
                    "  output = [\"type=cacheonly,mode=max\"]");

                // Emit custom tags if defined
                if (def.With.TryGetValue("tags", out var rawTags) && rawTags is List<object> tagList)
                {
                    var tags = tagList.Cast<string>().Select(t => $"\"{t}\"");
                    sb.AppendLine($"  tags = [\n    {string.Join(",\n    ", tags)}\n  ]");
                }

                // Emit contexts
                sb.AppendLine("  contexts = {");
                if (type != "build")
                {
                    sb.AppendLine("    build = \"target:build\"");
                }

                if (def.With.TryGetValue("base", out var baseImage)) // Add custom context if defined
                {
                    sb.AppendLine($"    base = \"docker-image://{baseImage}\"");
                }
                sb.AppendLine("  }");


                if (useInlineDockerFile)
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
        sb.AppendLine("  output = [");
        sb.AppendLine("    \"type=local,dest=.buildcharts/output\"");
        sb.AppendLine("  ]");
        sb.AppendLine("  contexts = {");

        foreach (var target in typedTargets.Where(x => x.Type is not "docker" and not "build"))
        {
            sb.AppendLine($"    {target.Name} = \"target:{target.Name}\"");
        }

        sb.AppendLine("  }");
        sb.AppendLine("  dockerfile-inline = <<BUILDCHARTS_EOF");
        sb.AppendLine(string.Join(Environment.NewLine, inlineDockerfile));
        sb.AppendLine("BUILDCHARTS_EOF");
        sb.AppendLine("}");
        sb.AppendLine("");

        // Emit groups
        foreach (var typeGroup in typedTargets.GroupBy(x => x.Type).Where(x => x.Key is not "build"))
        {
            sb.AppendLine($"group \"{typeGroup.Key}\" {{");
            sb.AppendLine($"  targets = [");
            sb.AppendLine($"    {string.Join(",\n    ", typeGroup.Select(x => $"\"{x.Name}\""))}");
            sb.AppendLine("   ]");
            sb.AppendLine("}");
        }

        sb.AppendLine("group \"default\" {");
        sb.AppendLine($"  targets = [{string.Join(", ", typedTargets.GroupBy(x => x.Type).Select(x => $"\"{x.Key}\""))}, \"output\"]");
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

        var name = $"{type}__{cleanSrc}".ToLowerInvariant();

        // Ensure uniqueness in case of collisions
        var originalName = name;
        var counter = 1;

        while (!_usedNames.Add(name))
        {
            name = $"{originalName}-{counter++}";
        }

        return name;
    }

    private record TypedTarget(string Name, string Type);
}