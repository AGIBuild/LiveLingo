namespace LiveLingo.Core.Translation.Segmentation;

/// <summary>
/// Splits the raw input at any newline sequence and reports how many blank
/// lines follow each non-empty line. The returned list contains only
/// non-blank entries; pure-whitespace lines are folded into the trailing
/// blank-line count of the preceding non-blank line.
/// </summary>
internal static class LineSplitter
{
    /// <summary>One non-blank line plus the count of blank lines that follow it.</summary>
    public readonly record struct Line(string Text, int FollowingBlankLines);

    public static IReadOnlyList<Line> Split(string text)
    {
        // Normalise CRLF and bare CR so downstream logic only deals with '\n'.
        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var raw = normalized.Split('\n');

        var result = new List<Line>();
        var i = 0;
        while (i < raw.Length)
        {
            var content = raw[i].Trim();
            if (content.Length == 0) { i++; continue; }

            var blanks = 0;
            var j = i + 1;
            while (j < raw.Length && raw[j].Trim().Length == 0)
            {
                blanks++;
                j++;
            }
            result.Add(new Line(content, blanks));
            i = j;
        }

        return result;
    }
}
