namespace TranslationPoc;

public sealed record SourceUnit(string Text, bool Complete, string Break = "", string Trail = "");

public static class Units
{
    private static readonly HashSet<string> Abbreviations = new(StringComparer.OrdinalIgnoreCase)
        { "Dr", "Mr", "Mrs", "Ms", "Prof", "e.g", "i.e", "vs" };

    public static IReadOnlyList<SourceUnit> Split(string text)
    {
        var units = new List<SourceUnit>();
        var start = 0;
        var inCode = false;
        var spans = ProtectedText.Spans(text);
        var literalIndex = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (literalIndex < spans.Count && i == spans[literalIndex].Start)
            {
                i = spans[literalIndex++].End - 1;
                continue;
            }
            if (text[i] == '`')
            {
                var ticks = 1;
                while (i + ticks < text.Length && text[i + ticks] == '`') ticks++;
                inCode = !inCode;
                i += ticks - 1;
                continue;
            }
            if (inCode) continue;

            if (text[i] == '\n')
            {
                var line = text[start..i];
                if (line.Trim().Length > 0)
                {
                    units.Add(MakeUnit(line, true, units.Count == 0, true));
                    start = i;
                }
                continue;
            }

            var boundary = text[i] is '。' or '！' or '？' or '!' or '?';
            if (text[i] == '.' && (i + 1 == text.Length || char.IsWhiteSpace(text[i + 1])))
            {
                var wordStart = i - 1;
                while (wordStart >= start && (char.IsLetter(text[wordStart]) || text[wordStart] == '.')) wordStart--;
                boundary = !Abbreviations.Contains(text[(wordStart + 1)..i]);
            }
            if (!boundary) continue;
            while (i + 1 < text.Length && text[i + 1] is '"' or '”' or '’' or '」' or '』') i++;
            if (text[start..(i + 1)].Trim().Length > 0) units.Add(MakeUnit(text[start..(i + 1)], true, units.Count == 0, false));
            start = i + 1;
        }
        var rest = text[start..];
        if (rest.Trim().Length > 0) units.Add(MakeUnit(rest, false, units.Count == 0, true));
        else if (units.Count > 0) units[^1] = units[^1] with { Trail = units[^1].Trail + rest };
        return units;
    }

    // Whitespace around a unit is layout, not content: indentation after a line break (or at the very start) and
    // whitespace before a line break (or at the very end) are kept verbatim; the translation never sees them.
    private static SourceUnit MakeUnit(string raw, bool complete, bool first, bool endsLine)
    {
        var body = raw.Trim();
        var leading = raw[..raw.IndexOf(body, StringComparison.Ordinal)];
        var trailing = raw[(leading.Length + body.Length)..];
        return new SourceUnit(body, complete, first || leading.Contains('\n') ? leading : "", endsLine ? trailing : "");
    }

    // Line breaks come from the source; same-line joins follow the target language's spacing.
    public static string Join(IEnumerable<(string Break, string Target, string Trail)> parts, string targetLanguage)
    {
        var sameLine = targetLanguage == "en" ? " " : "";
        var joined = new System.Text.StringBuilder();
        var first = true;
        foreach (var (lineBreak, target, trail) in parts)
        {
            joined.Append(lineBreak.Length > 0 ? lineBreak : first ? "" : sameLine);
            joined.Append(target).Append(trail);
            first = false;
        }
        return joined.ToString();
    }
}
