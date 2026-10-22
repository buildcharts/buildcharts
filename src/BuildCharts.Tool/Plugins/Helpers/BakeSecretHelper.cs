using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace BuildCharts.Tool.Plugins.Helpers;

public static class BakeSecretHelper
{
    public static void AddSecrets(StringBuilder builder, params string[] secrets)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var text = builder.ToString();
        var target = Regex.Match(text, "(?m)^target\\s+\"_common\"\\s*\\{");
        if (!target.Success)
        {
            throw new InvalidOperationException("Could not find target \"_common\".");
        }
        var open = target.Index + target.Length - 1;
        var close = FindClosingDelimiter(text, open, '{', '}');
        var block = text[(open + 1)..close];
        var attribute = Regex.Match(block, @"(?m)^\s*secret\s*=\s*\[");
        if (!attribute.Success)
        {
            builder.Insert(close, "  secret = [\n" + string.Join(",\n", secrets.Select(Quote)) + "\n  ]\n");
            return;
        }
        var arrayOpen = open + 1 + attribute.Index + attribute.Length - 1;
        var arrayClose = FindClosingDelimiter(text, arrayOpen, '[', ']');
        var existing = text[(arrayOpen + 1)..arrayClose];
        var additions = secrets.Where(secret => !existing.Contains(Quote(secret).Trim(), StringComparison.Ordinal)).ToArray();
        if (additions.Length == 0)
        {
            return;
        }
        var separator = string.IsNullOrWhiteSpace(existing) || existing.TrimEnd().EndsWith(',') ? string.Empty : ",";
        builder.Insert(arrayClose, separator + "\n" + string.Join(",\n", additions.Select(Quote)) + "\n  ");
    }

    private static int FindClosingDelimiter(string text, int open, char opening, char closing)
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
        throw new InvalidOperationException("Malformed HCL secret target.");
    }

    private static string Quote(string secret) => "    \"" + secret.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
}