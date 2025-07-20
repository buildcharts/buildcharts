using System;
using System.Text;

namespace BuildCharts.Tool.Plugins.NuGetAuthenticate_v1.Helpers;

public static class BakeHclPatchHelper
{
    private const string TARGET_MARKER = "target \"_common\"";
    private const string SECRET_SNIPPET =
        "  secret = [\n" +
        "    \"type=file,id=VSS_NUGET_EXTERNAL_FEED_ENDPOINTS,src=.buildcharts/secrets/VSS_NUGET_EXTERNAL_FEED_ENDPOINTS\",\n" +
        "    \"type=file,id=VSS_NUGET_ACCESSTOKEN,src=.buildcharts/secrets/VSS_NUGET_ACCESSTOKEN\"\n" +
        "  ]\n";

    /// <summary>
    /// Ensures that the secret stanza exists in the <c>target "build"</c> block.
    /// Modifies the supplied <see cref="StringBuilder"/> in place.
    /// </summary>
    public static void AddSecretsToBuildTarget(StringBuilder sb)
    {
        if (sb == null)
        {
            throw new ArgumentNullException(nameof(sb));
        }

        // 1) Locate target "build"
        var start = sb.ToString().IndexOf(TARGET_MARKER, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            throw new InvalidOperationException("Could not find target \"common\".");
        }

        // 2) Locate the opening brace '{'
        var openBrace = sb.ToString().IndexOf('{', start);
        if (openBrace < 0)
        {
            throw new InvalidOperationException("Malformed HCL: missing '{' after target \"build\".");
        }

        // 3) Find the matching closing brace '}'
        var depth = 0;
        var pos = openBrace;

        for (; pos < sb.Length; pos++)
        {
            if (sb[pos] == '{')
            {
                depth++;
            }
            if (sb[pos] == '}')
            {
                depth--;
            }
            if (depth == 0)
            {
                break; // Reached end of build block
            }
        }
        if (depth != 0)
        {
            throw new InvalidOperationException("Malformed HCL: unmatched braces in target \"build\".");
        }

        var blockStart = openBrace + 1; // text after '{'
        var blockEnd = pos; // position of '}'

        // 4) Remove any existing secret stanza
        var slice = sb.ToString(blockStart, blockEnd - blockStart);

        var secretIdx = slice.IndexOf("secret =", StringComparison.OrdinalIgnoreCase);
        if (secretIdx >= 0)
        {
            throw new InvalidOperationException("Malformed HCL: Secrets already exists in target \"build\".");
        }

        // 5) Insert new secret stanza before the closing brace
        sb.Insert(blockEnd, SECRET_SNIPPET);
    }
}