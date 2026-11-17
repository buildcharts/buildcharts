using System;
using System.Text;
using System.Text.RegularExpressions;

namespace BuildCharts.Tool.Plugins.Helpers;

public static class BakeHclHelper
{
    private const string TARGET_MARKER = "target \"test\"";

    /// <summary>
    /// Ensures DOCKER_HOST and TESTCONTAINERS_HOST_OVERRIDE in args of target "test"
    /// and maps host.docker.internal to the detected Docker host gateway IP.
    /// Modifies the supplied StringBuilder in place.
    /// </summary>
    public static void AddExtraHosts(StringBuilder sb, string testcontainersHostOverride, string hostGatewayIp)
    {
        if (sb == null)
        {
            throw new ArgumentNullException(nameof(sb));
        }

        var text = sb.ToString();

        // 1) Locate target "test"
        var targetMarkerIndex = text.IndexOf(TARGET_MARKER, StringComparison.OrdinalIgnoreCase);
        if (targetMarkerIndex < 0)
        {
            Console.WriteLine($"Skipped updating docker-bake.hcl due to missing test target.");
            return;
        }

        // 2) Find '{' after marker
        var targetOpenBrace = text.IndexOf('{', targetMarkerIndex);
        if (targetOpenBrace < 0)
        {
            throw new InvalidOperationException($"Malformed HCL: missing '{{' after {TARGET_MARKER}.");
        }

        // 3) Find matching '}'
        var targetCloseBrace = FindClosingDelimiter(text, targetOpenBrace, '{', '}');
        if (targetCloseBrace < 0)
        {
            throw new InvalidOperationException($"Malformed HCL: unmatched braces in {TARGET_MARKER}.");
        }

        var blockStart = targetOpenBrace + 1; // first char after '{'
        var blockEnd = targetCloseBrace;

        // Snapshot just the target body for searches
        var body = sb.ToString(blockStart, blockEnd - blockStart);

        // ---- A) Ensure Docker and Testcontainers arguments inside args = { ... } ----
        var argsMatch = Regex.Match(body, @"(?m)^\s*args\s*=\s*\{");
        if (!argsMatch.Success)
        {
            throw new InvalidOperationException("Expected an existing `args = { ... }` block in target \"test\".");
        }

        // absolute positions for the args block braces
        var argsStartAbs = blockStart + argsMatch.Index;
        var argsOpenBrace = sb.ToString().IndexOf('{', argsStartAbs);
        if (argsOpenBrace < 0)
        {
            throw new InvalidOperationException("Malformed HCL: args block missing '{'.");
        }

        var argsCloseBrace = FindClosingDelimiter(sb.ToString(), argsOpenBrace, '{', '}');
        if (argsCloseBrace < 0)
        {
            throw new InvalidOperationException("Malformed HCL: unmatched braces in args block.");
        }

        var argsInner = sb.ToString(argsOpenBrace + 1, argsCloseBrace - argsOpenBrace - 1);
        // Connect to DinD directly without changing where host.docker.internal points.
        var dockerHost = new UriBuilder("tcp", testcontainersHostOverride, 2375).Uri.GetLeftPart(UriPartial.Authority);
        var arguments = new StringBuilder();
        foreach (var (name, value) in new[]
        {
            ("DOCKER_HOST", dockerHost),
            ("TESTCONTAINERS_HOST_OVERRIDE", testcontainersHostOverride)
        })
        {
            // Preserve an existing override for either argument; add only missing values.
            if (!Regex.IsMatch(argsInner, $@"(?m)^\s*{name}\s*=", RegexOptions.CultureInvariant))
            {
                arguments.Append($"  {name} = \"{value}\"\n  ");
            }
        }
        // Account for inserted arguments so extra-hosts stays inside the test target.
        sb.Insert(argsCloseBrace, arguments);
        blockEnd += arguments.Length;

        // ---- B) Preserve host access separately from the DinD connection ----
        // If there's already any "extra-hosts =" attribute in this target, fail (same behavior as your secret snippet).
        if (body.IndexOf("extra-hosts =", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            throw new InvalidOperationException("Malformed HCL: extra-hosts already exists in target \"test\".");
        }

        // 5) Insert new extra hosts before the closing brace
        sb.Insert(blockEnd, "  extra-hosts = {\n" +
            $"    \"host.docker.internal\" = \"{hostGatewayIp}\"\n" +
            "  }\n");
    }

    /// <summary>
    /// Adds a secret to target "_common", creating its secret list if needed.
    /// Preserves existing secrets and skips an identical entry.
    /// Modifies the supplied StringBuilder in place.
    /// </summary>
    public static void AddSecret(StringBuilder builder, string secret)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var text = Regex.Replace(builder.ToString(), @"""(?:\\.|[^""\\])*""|#[^\r\n]*|//[^\r\n]*|/\*[\s\S]*?\*/", match => match.Value.StartsWith('"') ? match.Value : Regex.Replace(match.Value, @"[^\r\n]", " "));
        var target = Regex.Match(text, "(?m)^target\\s+\"_common\"\\s*\\{");
        if (!target.Success)
        {
            throw new InvalidOperationException("Could not find target \"_common\".");
        }
        var open = target.Index + target.Length - 1;
        var close = FindClosingDelimiter(text, open, '{', '}');
        if (close < 0)
        {
            throw new InvalidOperationException("Malformed HCL secret target.");
        }
        var block = text[(open + 1)..close];
        var attribute = Regex.Match(block, @"(?m)^\s*secret\s*=\s*\[");
        if (!attribute.Success)
        {
            builder.Insert(close, "  secret = [\n" + Quote(secret) + "\n  ]\n");
            return;
        }
        var arrayOpen = open + 1 + attribute.Index + attribute.Length - 1;
        var arrayClose = FindClosingDelimiter(text, arrayOpen, '[', ']');
        if (arrayClose < 0 || arrayClose > close)
        {
            throw new InvalidOperationException("Malformed HCL secret target.");
        }
        var existing = text[(arrayOpen + 1)..arrayClose];
        if (existing.Contains(Quote(secret).Trim(), StringComparison.Ordinal))
        {
            return;
        }
        var separator = string.IsNullOrWhiteSpace(existing) || existing.TrimEnd().EndsWith(',') ? string.Empty : ",";
        builder.Insert(arrayClose, separator + "\n" + Quote(secret) + "\n  ");
    }

    public static int FindClosingDelimiter(string text, int open, char opening, char closing)
    {
        var depth = 0;
        var quoted = false;
        for (var i = open; i < text.Length; i++)
        {
            if (quoted && text[i] == '\\')
            {
                i++;
                continue;
            }
            if (text[i] == '"')
            {
                quoted = !quoted;
            }
            if (quoted)
            {
                continue;
            }
            if (text[i] == opening)
            {
                depth++;
            }
            else if (text[i] == closing && --depth == 0)
            {
                return i;
            }
        }
        return -1;
    }

    private static string Quote(string secret)
    {
        return "    \"" + secret.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }
}
