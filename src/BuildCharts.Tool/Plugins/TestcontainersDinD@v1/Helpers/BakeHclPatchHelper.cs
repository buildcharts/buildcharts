using System;
using System.Text;
using System.Text.RegularExpressions;

namespace BuildCharts.Tool.Plugins.TestcontainersDinD_v1.Helpers;

public static class BakeHclPatchHelper
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
    public static void Execute(StringBuilder sb, string testcontainersHostOverride)
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
            throw new InvalidOperationException($"Could not find {TARGET_MARKER}.");
        }

        // 2) Find '{' after marker
        var targetOpenBrace = text.IndexOf('{', targetMarkerIndex);
        if (targetOpenBrace < 0)
        {
            throw new InvalidOperationException($"Malformed HCL: missing '{{' after {TARGET_MARKER}.");
        }

        // 3) Find matching '}'
        var targetCloseBrace = FindMatchingBrace(text, targetOpenBrace);
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

        var argsCloseBrace = FindMatchingBrace(sb.ToString(), argsOpenBrace);
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
    /// Finds the index of the matching '}' for the '{' at openIdx; returns -1 if not found.
    /// </summary>
    private static int FindMatchingBrace(string s, int openIdx)
    {
        int depth = 0;
        for (int i = openIdx; i < s.Length; i++)
        {
            if (s[i] == '{')
            {
                depth++;
            }
            else if (s[i] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return i;
                }
            }
        }
        return -1;
    }
}