using System;
using System.Text;
using System.Text.RegularExpressions;

namespace BuildCharts.Tool.Plugins.Helpers;

public static class BakeHclHelper
{
    private const string TARGET_MARKER = "target \"test\"";
    private const string EXTRA_HOSTS_SNIPPET =
        "  extra-hosts = {\n" +
        "    \"host.docker.internal\" = \"host-gateway\"\n" +
        "  }\n";

    /// <summary>
    /// Ensures TESTCONTAINERS_HOST_OVERRIDE in args of target "test"
    /// and ensures extra-hosts = { "host.docker.internal" = "host-gateway" } exists,
    /// using a snippet-based insertion similar to SECRET_SNIPPET.
    /// Modifies the supplied StringBuilder in place.
    /// </summary>
    public static void AddExtraHosts(StringBuilder sb, string testcontainersHostOverride)
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

        // ---- A) Ensure TESTCONTAINERS_HOST_OVERRIDE inside existing args = { ... } ----
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

        // If not present, insert before args' closing brace, respecting indentation a bit
        var argsInner = sb.ToString(argsOpenBrace + 1, argsCloseBrace - argsOpenBrace - 1);
        var alreadyHasOverride = Regex.IsMatch(argsInner, @"(?m)^\s*TESTCONTAINERS_HOST_OVERRIDE\s*=", RegexOptions.CultureInvariant);
        if (!alreadyHasOverride)
        {
            // Try to keep indentation roughly aligned with args entries
            var indent = "  ";

            sb.Insert(argsCloseBrace, $"{indent}TESTCONTAINERS_HOST_OVERRIDE = \"{testcontainersHostOverride}\"\n{indent}");

            // Adjust indices after insertion
            var delta = ($"{indent}TESTCONTAINERS_HOST_OVERRIDE = \"{testcontainersHostOverride}\"\n{indent}").Length;
            if (argsCloseBrace < blockEnd)
            {
                blockEnd += delta;
            }
        }

        // ---- B) EXTRA-HOSTS using a fixed snippet (SECRET_SNIPPET style) ----
        // If there's already any "extra-hosts =" attribute in this target, fail (same behavior as your secret snippet).
        if (body.IndexOf("extra-hosts =", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            throw new InvalidOperationException("Malformed HCL: extra-hosts already exists in target \"test\".");
        }

        // 5) Insert new extra hosts before the closing brace
        sb.Insert(blockEnd, EXTRA_HOSTS_SNIPPET);
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
