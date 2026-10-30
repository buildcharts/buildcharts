namespace BuildCharts.Tool.Plugins.Helpers;

public static class BakeHclHelper
{
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
}
