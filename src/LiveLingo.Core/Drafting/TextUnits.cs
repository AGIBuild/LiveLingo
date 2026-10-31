using System.Text;
using System.Text.RegularExpressions;

namespace LiveLingo.Core.Drafting;

/// <summary>Splitting, routing and joining of draft text. The only place that knows sentence and layout rules.</summary>
public static partial class TextUnits
{
    /// <summary>Longer text is treated as a finished document rather than live speech.</summary>
    public const int WholeTextLength = 400;

    private const int StructuredLineCount = 3;

    private static readonly HashSet<string> Abbreviations = new(StringComparer.OrdinalIgnoreCase)
        { "Dr", "Mr", "Mrs", "Ms", "Prof", "e.g", "i.e", "vs" };

    /// <summary>Spans of text that must neither be split into sentences nor translated (code, URLs, placeholders).</summary>
    public static IReadOnlyList<(int Start, int End)> LiteralSpans(string source) =>
        LiteralPattern().Matches(source).Select(m => (m.Index, m.Index + m.Length)).ToArray();

    /// <summary>
    /// Splits text into sentence and line units. Whitespace around a unit is layout, not content:
    /// indentation after a line break (or at the very start) and whitespace before a line break (or at the very end)
    /// is kept verbatim in <see cref="SourceUnit.Lead"/> / <see cref="SourceUnit.Trail"/>; other spacing is dropped.
    /// </summary>
    public static IReadOnlyList<SourceUnit> Split(string text)
    {
        var units = new List<SourceUnit>();
        var start = 0;
        var inCode = false;
        var spans = LiteralSpans(text);
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

            if (inCode)
                continue;

            if (text[i] == '\n')
            {
                var line = text[start..i];
                if (line.Trim().Length > 0)
                {
                    units.Add(MakeUnit(line, complete: true, first: units.Count == 0, endsLine: true));
                    start = i;
                }
                continue;
            }

            if (!EndsSentenceAt(text, i, start))
                continue;

            while (i + 1 < text.Length && text[i + 1] is '"' or '”' or '’' or '」' or '』') i++;
            var sentence = text[start..(i + 1)];
            if (sentence.Trim().Length > 0)
                units.Add(MakeUnit(sentence, complete: true, first: units.Count == 0, endsLine: false));
            start = i + 1;
        }

        var rest = text[start..];
        if (rest.Trim().Length > 0)
            units.Add(MakeUnit(rest, complete: false, first: units.Count == 0, endsLine: true));
        else if (units.Count > 0)
            units[^1] = units[^1] with { Trail = units[^1].Trail + rest };
        return units;
    }

    /// <summary>Code, structured (headings, lists, braces) or long text is processed as one document.</summary>
    public static DraftRoute RouteFor(string text)
    {
        if (text.Contains("```", StringComparison.Ordinal) || text.Length > WholeTextLength)
            return DraftRoute.Whole;

        var structured = text.Split('\n').Count(line => StructuredLine().IsMatch(line));
        return structured >= StructuredLineCount ? DraftRoute.Whole : DraftRoute.Units;
    }

    /// <summary>The whole text as a single unit, with only the outer whitespace kept as layout.</summary>
    public static IReadOnlyList<SourceUnit> Whole(string text)
    {
        var body = text.Trim();
        if (body.Length == 0)
            return [];

        var lead = text[..text.IndexOf(body, StringComparison.Ordinal)];
        return [new SourceUnit(body, lead, text[(lead.Length + body.Length)..])];
    }

    /// <summary>
    /// Joins unit bodies. Layout whitespace is kept verbatim; two adjacent same-line units get a space only
    /// when the seam is between two ASCII characters (English sentences), never next to CJK text.
    /// </summary>
    public static string Join(IEnumerable<(string Lead, string Body, string Trail)> parts)
    {
        var joined = new StringBuilder();
        var previousTrail = "";
        var previousBody = "";
        var first = true;
        foreach (var (lead, body, trail) in parts)
        {
            if (!first && lead.Length == 0 && previousTrail.Length == 0 && NeedsSpace(previousBody, body))
                joined.Append(' ');
            joined.Append(lead).Append(body).Append(trail);
            previousTrail = trail;
            previousBody = body;
            first = false;
        }
        return joined.ToString();
    }

    private static bool EndsSentenceAt(string text, int i, int sentenceStart)
    {
        if (text[i] is '。' or '！' or '？' or '!' or '?')
            return true;
        if (text[i] != '.' || (i + 1 != text.Length && !char.IsWhiteSpace(text[i + 1])))
            return false;

        var wordStart = i - 1;
        while (wordStart >= sentenceStart && (char.IsLetter(text[wordStart]) || text[wordStart] == '.')) wordStart--;
        return !Abbreviations.Contains(text[(wordStart + 1)..i]);
    }

    private static SourceUnit MakeUnit(string raw, bool complete, bool first, bool endsLine)
    {
        var body = raw.Trim();
        var leading = raw[..raw.IndexOf(body, StringComparison.Ordinal)];
        var trailing = raw[(leading.Length + body.Length)..];
        return new SourceUnit(body, first || leading.Contains('\n') ? leading : "", endsLine ? trailing : "", complete);
    }

    private static bool NeedsSpace(string left, string right) =>
        left.Length > 0 && right.Length > 0 && IsAsciiGlyph(left[^1]) && IsAsciiGlyph(right[0]);

    private static bool IsAsciiGlyph(char c) => c is > ' ' and < (char)127;

    [GeneratedRegex(@"```[\s\S]*?```|`[^`\r\n]+`|https?://[^\s<>`，。！？；“”]+|\$\{[A-Za-z_]\w*\}|\{[A-Za-z_]\w*\}")]
    private static partial Regex LiteralPattern();

    [GeneratedRegex(@"^\s*(#{1,6}\s|[-*]\s|\d+\.\s|[{}\[\]<]|```)")]
    private static partial Regex StructuredLine();
}
