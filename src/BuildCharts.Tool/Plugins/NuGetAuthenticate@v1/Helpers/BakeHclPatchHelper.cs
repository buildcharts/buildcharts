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
    /// Ensures the secrets exists inside target "_common":
    /// Modifies the supplied StringBuilder in place.
    /// </summary>
    public static void Execute(StringBuilder sb)
    {
        if (sb == null)
        {
            throw new ArgumentNullException(nameof(sb));
        }

        // Work with a string snapshot.
        var text = sb.ToString();

        // 1) Locate target
        var targetMarkerIndex = text.IndexOf(TARGET_MARKER, StringComparison.OrdinalIgnoreCase);
        if (targetMarkerIndex < 0)
        {
            throw new InvalidOperationException("Could not find target \"_common\".");
        }

        // 2) Locate the opening brace '{'
        var targetOpenBrace = text.IndexOf('{', targetMarkerIndex);
        if (targetOpenBrace < 0)
        {
            throw new InvalidOperationException("Malformed HCL: missing '{' after target \"_common\".");
        }

        // 3) Find the matching closing brace '}'
        var targetCloseBrace = FindMatchingBrace(text, targetOpenBrace);
        if (targetCloseBrace < 0)
        {
            throw new InvalidOperationException("Malformed HCL: unmatched braces in target \"_common\".");
        }

        var blockStart = targetOpenBrace + 1; // start after '{'
        var blockEnd = targetCloseBrace;

        // 4) Check for any existing secrets
        var slice = sb.ToString(blockStart, blockEnd - blockStart);

        var secretIdx = slice.IndexOf("secret =", StringComparison.OrdinalIgnoreCase);
        if (secretIdx >= 0)
        {
            // TODO: Append to secret block if already exists for future plugins.
            throw new InvalidOperationException("Malformed HCL: Secrets already exists in target \"build\".");
        }

        // 5) Insert new secrets before the closing brace
        sb.Insert(blockEnd, SECRET_SNIPPET);
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