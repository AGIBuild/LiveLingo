namespace LiveLingo.Core.Translation;

/// <summary>
/// The user's terms. A plain term must be kept exactly as written (dictation hotwords, cleanup, the fidelity gate);
/// a pair <c>source=target</c> also fixes the translation (Chinese source, English target).
/// </summary>
public sealed class Glossary
{
    private static readonly char[] EntrySeparators = [',', '，', ';', '；', '\n', '\r'];
    private static readonly char[] PairSeparators = ['=', ':', '：'];

    public static Glossary Empty { get; } = new([], []);

    private Glossary(IReadOnlyList<string> terms, IReadOnlyList<GlossaryTerm> pairs)
    {
        Terms = terms;
        Pairs = pairs;
    }

    /// <summary>Every term as written, including the source side of each pair.</summary>
    public IReadOnlyList<string> Terms { get; }

    public IReadOnlyList<GlossaryTerm> Pairs { get; }

    /// <summary>The pairs oriented from the source language of the text toward <paramref name="target"/>.</summary>
    public IReadOnlyList<GlossaryTerm> TermsFor(TargetLanguage target) => target switch
    {
        TargetLanguage.English => Pairs,
        TargetLanguage.Chinese => Pairs.Select(pair => new GlossaryTerm(pair.Target, pair.Source)).ToList(),
        _ => throw new ArgumentOutOfRangeException(nameof(target))
    };

    /// <summary>One entry per line or separated by commas or semicolons; <c>#</c> starts a comment line.</summary>
    public static Glossary Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Empty;

        var terms = new List<string>();
        var pairs = new List<GlossaryTerm>();
        foreach (var line in text.Split('\n'))
        {
            if (line.TrimStart().StartsWith('#'))
                continue;

            foreach (var entry in line.Split(EntrySeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                Add(entry, terms, pairs);
        }

        return terms.Count == 0 ? Empty : new Glossary(terms, pairs);
    }

    private static void Add(string entry, List<string> terms, List<GlossaryTerm> pairs)
    {
        var arrow = entry.IndexOf("->", StringComparison.Ordinal);
        var separatorLength = 2;
        if (arrow < 0)
        {
            arrow = entry.IndexOfAny(PairSeparators);
            separatorLength = 1;
        }

        if (arrow < 0)
        {
            AddTerm(entry, terms);
            return;
        }

        var source = entry[..arrow].Trim();
        var target = entry[(arrow + separatorLength)..].Trim();
        if (source.Length == 0)
            return;

        AddTerm(source, terms);
        if (target.Length > 0)
            pairs.Add(new GlossaryTerm(source, target));
    }

    private static void AddTerm(string term, List<string> terms)
    {
        if (!terms.Contains(term, StringComparer.OrdinalIgnoreCase))
            terms.Add(term);
    }
}
