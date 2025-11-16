using BuildCharts.Tool.Configuration.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BuildCharts.Tool.Generate;

public class DockerHclGenerator
{
    private readonly HashSet<string> _usedNames = [];
    
    public async Task<StringBuilder> GenerateAsync(BuildConfig buildConfig, ChartConfig chartConfig, bool useInlineDockerFile)
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

        // Emit targets as matrix per type
        var groupedTargetsByType = buildConfig.Targets
            .SelectMany(kvp => kvp.Value.Select(def => new { kvp.Key, def }))
            .GroupBy(x => x.def.Type)
            .ToList();

        foreach (var targetGroup in groupedTargetsByType)
        {
            var type = targetGroup.Key;
            var chartAlias = chartConfig.Dependencies.FirstOrDefault(d => d.Alias.Equals(type, StringComparison.OrdinalIgnoreCase))?.Name;
            var targets = targetGroup.Select(x => new TargetItem(x.Key, x.def, ExtractArgs(x.def.With))).ToList();
            var args = targets.SelectMany(t => t.Args.Keys).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();

            sb.AppendLine($"target \"{type}\" {{");
            sb.AppendLine($"  inherits = [\"_common\"]");
            sb.AppendLine($"  target = \"{type}\"");
            sb.AppendLine($"  name = \"${{item.name}}\"");

            if (type == "build")
            {
                sb.AppendLine("  context = \".\"");
            }

            // Emit output
            sb.AppendLine(type == "docker" ?
                "  output = [\"type=docker\"]" :
                "  output = [\"type=cacheonly,mode=max\"]");

            // Emit targets in type matrix
            sb.AppendLine("  matrix = {");
            sb.AppendLine("    item = [");

            foreach (var item in targets)
            {
                var targetName = CreateUniqueName(buildConfig, item.Key, type);
                typedTargets.Add(new TypedTarget(targetName, type));

                sb.AppendLine("      {");
                sb.AppendLine($"        name = \"{targetName}\",");
                sb.AppendLine($"        src  = \"{item.Key}\"");

                // Emit args in matrix
                if (args.Count > 0)
                {
                    foreach (var argKey in args)
                    {
                        sb.AppendLine($"        {argKey} = \"{FormatArgValue(item.Args, argKey)}\"");
                    }
                }

                // Emit tags in matrix
                if (targets.Any(x => x.Definition.With.ContainsKey("tags")))
                {
                    if (item.Definition.With.TryGetValue("tags", out var rawTags) && rawTags is List<object> tagList)
                    {
                        var tags = tagList.Cast<string>().Select(t => $"\"{t}\"");
                        sb.AppendLine($"        tags = [{string.Join(",", tags)}]");
                    }
                    else
                    {
                        sb.AppendLine($"        tags = []");
                    }
                }

                // Emit base in matrix
                if (targets.Any(x => x.Definition.With.ContainsKey("base")))
                {
                    if (item.Definition.With.TryGetValue("base", out var baseImage))
                    {
                        sb.AppendLine($"        base = \"{baseImage}\"");
                    }
                    else
                    {
                        sb.AppendLine($"        base = \"\"");
                    }
                }

                // Emit allow in matrix
                if (targets.Any(x => x.Definition.With.ContainsKey("allow")))
                {
                    if (item.Definition.With.TryGetValue("allow", out var rawAllows) && rawAllows is List<object> allowList)
                    {
                        var allows = allowList.Cast<string>().Select(t => $"\"{t}\"");
                        sb.AppendLine($"        allow = [{string.Join(",", allows)}]");
                    }
                    else
                    {
                        sb.AppendLine($"        allow = []");
                    }
                }

                sb.AppendLine("      },");
            }

            sb.AppendLine("    ]");
            sb.AppendLine("  }");

            // Emit args
            sb.AppendLine("  args = {");
            sb.AppendLine("    BUILDCHARTS_SRC = item.src");
            sb.AppendLine("    BUILDCHARTS_TYPE = \"" + type + "\"");
            if (args.Count > 0)
            {
                foreach (var argKey in args)
                {
                    sb.AppendLine($"    {argKey.ToUpperInvariant()} = \"${{item.{argKey}}}\"");
                }
            }

            sb.AppendLine("  }");

            // Emit tags
            if (targets.Any(x => x.Definition.With.ContainsKey("tags")))
            {
                sb.AppendLine("  tags = \"${item.tags}\"");
            }

            // Emit contexts
            sb.AppendLine("  contexts = {");
            if (type != "build") // Target build cannot link to itself.
            {
                sb.AppendLine("    build = \"target:build\"");
            }
            if (targets.Any(x => x.Definition.With.ContainsKey("base")))
            {
                sb.AppendLine("    base = \"docker-image://${item.base}\"");
            }
            sb.AppendLine("  }");

            // Emit entitlements
            if (targets.Any(x => x.Definition.With.ContainsKey("allow")))
            {
                sb.AppendLine("  allow = \"${item.allow}\"");
            }
            
            if (useInlineDockerFile)
            {
                var dockerfilePath = $".buildcharts/{chartAlias}/Dockerfile";
                if (!File.Exists(dockerfilePath))
                {
                    throw new FileNotFoundException($"Missing Dockerfile: {dockerfilePath}");
                }

                var dockerfileContent = await File.ReadAllTextAsync(dockerfilePath);
                sb.AppendLine("  dockerfile-inline = <<EOF");
                sb.AppendLine(dockerfileContent.TrimEnd());
                sb.AppendLine("EOF");
            }
            else
            {
                sb.AppendLine($"  dockerfile = \"./.buildcharts/{chartAlias}/Dockerfile\"");
            }

            sb.AppendLine("}\n");
        }

        // Emit output target
        sb.AppendLine("target \"output\" {");
        sb.AppendLine("  output = [\"type=local,dest=.buildcharts/output\"]");
        sb.AppendLine("  contexts = {");

        foreach (var target in typedTargets.Where(x => x.Type is not "docker" and not "build"))
        {
            sb.AppendLine($"    {target.Name} = \"target:{target.Name}\"");
        }
        
        sb.AppendLine("  }");
        sb.AppendLine("  dockerfile-inline = <<EOF");
        sb.AppendLine("FROM scratch AS output");

        foreach (var target in typedTargets.Where(x => x.Type is not "docker" and not "build"))
        {
            sb.AppendLine($"COPY --link --from={target.Name} /output /{target.Type}");
        }
        
        sb.AppendLine("EOF");
        sb.AppendLine("}");

        //// Emit groups
        //sb.AppendLine();
        //foreach (var typeGroup in typedTargets.GroupBy(x => x.Type))
        //{
        //    sb.AppendLine($"group \"{typeGroup.Key}\" {{");
        //    sb.AppendLine($"  targets = [");
        //    sb.AppendLine($"    {string.Join(",\n    ", typeGroup.Select(x => $"\"{x.Name}\""))}");
        //    sb.AppendLine("   ]");
        //    sb.AppendLine("}");
        //}

        sb.AppendLine();
        sb.AppendLine("group \"default\" {");
        sb.AppendLine($"  targets = [{string.Join(", ", groupedTargetsByType.GroupBy(x => x.Key).Select(x => $"\"{x.Key}\""))}, \"output\"]");
        sb.AppendLine("}");

        return sb;
    }

    private static List<string> CollectArgKeys(IEnumerable<TargetItem> targets)
    {
        return targets
            .SelectMany(t => t.Args.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static Dictionary<string, object> ExtractArgs(IReadOnlyDictionary<string, object> with)
    {
        if (with.TryGetValue("args", out var rawArgs) && rawArgs is Dictionary<object, object> args)
        {
            return args.ToDictionary(k => k.Key.ToString(), v => v.Value);
        }

        return [];
    }

    private static string FormatArgValue(IReadOnlyDictionary<string, object> args, string key)
    {
        if (!args.TryGetValue(key, out var value) || value is null)
        {
            return string.Empty;
        }

        if (value is not IEnumerable<object> list)
        {
            return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        }

        var items = list
            .Select(v => Convert.ToString(v, CultureInfo.InvariantCulture))
            .Where(s => !string.IsNullOrEmpty(s));
        return string.Join(",", items);

    }
    
    private string CreateUniqueName(BuildConfig buildConfig, string src, string type)
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

    private record TargetItem(string Key, TargetDefinition Definition, Dictionary<string, object> Args);
    private record TypedTarget(string Name, string Type);
}
