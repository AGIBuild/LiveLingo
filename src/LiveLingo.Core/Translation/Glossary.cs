namespace LiveLingo.Core.Translation;

/// <summary>
/// The user's terms, optional built-in bilingual technical vocabulary, and recurring accent/speech corrections.
/// A plain term must be kept exactly as written (dictation hotwords, cleanup, the fidelity gate);
/// a pair <c>source=target</c> also fixes the translation; a correction <c>spoken=&gt;canonical</c> repairs a recurring
/// speech misrecognition while feeding only <c>canonical</c> into speech hotwords and protected terms.
/// </summary>
public sealed class Glossary
{
    /// <summary>
    /// Qwen3-ASR's ONNX decoder caps total KV (prompt + audio + output) at 512 tokens and logs an error suggestion to
    /// stderr whenever hotwords exceed 47 BPE tokens (offline-recognizer-qwen3-asr-impl.cc:866). Keeping the speech
    /// hotword subset within 24 terms / 160 chars / 36 estimated BPE tokens stays below that cliff while leaving the
    /// full glossary active for LLM cleanup, polish and the fidelity gate.
    /// </summary>
    public const int MaxSpeechHotwords = 24;
    public const int MaxSpeechHotwordChars = 160;
    public const int MaxSpeechHotwordTokens = 36;

    private static readonly char[] EntrySeparators = [',', '，', ';', '；', '\n', '\r'];
    private static readonly string[] CorrectionSeparators = ["=>", "⇒"];
    private static readonly char[] PairSeparators = ['=', ':', '：'];

    /// <summary>
    /// High-frequency English/bilingual software and AI terms commonly code-switched inside Chinese speech, ordered so
    /// the terms most prone to acoustic confusion take priority within <see cref="SpeechHotwords"/> while all entries
    /// remain active in cleanup, polish and <see cref="Dictation.FidelityGate"/>.
    /// </summary>
    public static IReadOnlyList<string> BuiltInTerms { get; } =
    [
        "Agent", "Skill", "Prompt", "Token", "API", "SDK", "CLI", "UI", "PR", "Git", "GitHub",
        "Copilot", "Claude", "Cursor", "VS Code", "LiveLingo", "MCP", "RAG", "LLM",
        "Commit", "Branch", "Merge", "Rebase", "Debug", "Bug", "Docker", "Redis", "SQLite",
        "PRD", "UX", "OpenAI", "JSON", "YAML", "Markdown", "SQL", "Kubernetes",
        "Webhook", "OAuth", "JWT", "HTTP", "HTTPS", "SSH", "GPU", "CPU",
        "macOS", "Windows", "Linux", "Avalonia"
    ];

    public static Glossary Empty { get; } = new([], [], []);

    private Glossary(IReadOnlyList<string> terms, IReadOnlyList<GlossaryTerm> pairs, IReadOnlyList<GlossaryCorrection> corrections)
    {
        Terms = terms;
        Pairs = pairs;
        Corrections = corrections;
    }

    /// <summary>Every canonical term as written, including the source side of each translation pair and canonical side of each correction.</summary>
    public IReadOnlyList<string> Terms { get; }

    public IReadOnlyList<GlossaryTerm> Pairs { get; }

    /// <summary>Recurring speech/accent misrecognitions (<c>spoken =&gt; canonical</c>) to repair during cleanup and polish.</summary>
    public IReadOnlyList<GlossaryCorrection> Corrections { get; }

    /// <summary>The pairs oriented from the source language of the text toward <paramref name="target"/>.</summary>
    public IReadOnlyList<GlossaryTerm> TermsFor(TargetLanguage target) => target switch
    {
        TargetLanguage.English => Pairs,
        TargetLanguage.Chinese => Pairs.Select(pair => new GlossaryTerm(pair.Target, pair.Source)).ToList(),
        _ => throw new ArgumentOutOfRangeException(nameof(target))
    };

    /// <summary>
    /// Bounded hotword list for the speech recognizer: user terms and correction canonicals come first, followed by
    /// built-in terms when enabled, capped so Qwen3-ASR's prompt head stays under its 47-token stderr warning limit.
    /// </summary>
    public IReadOnlyList<string> SpeechHotwords(
        int maxTerms = MaxSpeechHotwords,
        int maxChars = MaxSpeechHotwordChars,
        int maxTokens = MaxSpeechHotwordTokens) =>
        BoundHotwords(Terms, maxTerms, maxChars, maxTokens);

    public static IReadOnlyList<string> BoundHotwords(
        IReadOnlyList<string> terms,
        int maxTerms = MaxSpeechHotwords,
        int maxChars = MaxSpeechHotwordChars,
        int maxTokens = MaxSpeechHotwordTokens)
    {
        if (terms.Count == 0 || maxTerms <= 0 || maxChars <= 0 || maxTokens <= 0)
            return [];

        var selected = new List<string>(Math.Min(terms.Count, maxTerms));
        var chars = 0;
        var tokens = 0;
        foreach (var raw in terms)
        {
            var term = raw.Trim();
            if (term.Length == 0)
                continue;
            var charCost = selected.Count == 0 ? term.Length : term.Length + 2;
            var tokenCost = EstimateHotwordTokens(term);
            if (selected.Count >= maxTerms || chars + charCost > maxChars || tokens + tokenCost > maxTokens)
                break;
            selected.Add(term);
            chars += charCost;
            tokens += tokenCost;
        }
        return selected;
    }

    /// <summary>
    /// Conservative upper bound on how many Qwen BPE tokens a hotword adds to the recognizer's system prompt head
    /// (acronyms like MCP/PRD and CamelCase identifiers like LiveLingo split into 2–3 subword tokens).
    /// </summary>
    internal static int EstimateHotwordTokens(string term)
    {
        var tokens = 0;
        var i = 0;
        while (i < term.Length)
        {
            if (char.IsWhiteSpace(term[i]) || char.IsPunctuation(term[i]))
            {
                i++;
                continue;
            }
            if (IsCjk(term[i]))
            {
                var start = i;
                while (i < term.Length && IsCjk(term[i]))
                    i++;
                var cjkLen = i - start;
                tokens += cjkLen + (cjkLen + 1) / 2;
                continue;
            }

            var wordStart = i;
            var camelTransitions = 0;
            var allUpper = true;
            while (i < term.Length && !char.IsWhiteSpace(term[i]) && !char.IsPunctuation(term[i]) && !IsCjk(term[i]))
            {
                if (char.IsLower(term[i]))
                    allUpper = false;
                if (i > wordStart && char.IsUpper(term[i]) && char.IsLower(term[i - 1]))
                    camelTransitions++;
                i++;
            }

            var len = i - wordStart;
            if (allUpper && len >= 3)
                tokens += (len + 1) / 2 + 1;
            else
                tokens += Math.Max(1, (len + 4) / 5 + camelTransitions);
        }

        return Math.Max(1, tokens);
    }

    /// <summary>One entry per line or separated by commas or semicolons; <c>#</c> starts a comment line.</summary>
    public static Glossary Parse(string? text, bool includeBuiltIn = false)
    {
        var terms = new List<string>();
        var pairs = new List<GlossaryTerm>();
        var corrections = new List<GlossaryCorrection>();

        if (!string.IsNullOrWhiteSpace(text))
        {
            foreach (var line in text.Split('\n'))
            {
                if (line.TrimStart().StartsWith('#'))
                    continue;

                foreach (var entry in line.Split(EntrySeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    Add(entry, terms, pairs, corrections);
            }
        }

        if (includeBuiltIn)
        {
            foreach (var builtIn in BuiltInTerms)
                AddTerm(builtIn, terms);
        }

        return terms.Count == 0 && pairs.Count == 0 && corrections.Count == 0
            ? Empty
            : new Glossary(terms, pairs, corrections);
    }

    /// <summary>
    /// Detects a concise single-span correction (<c>spoken =&gt; canonical</c>) when the user edits a recognized sentence,
    /// expanding mid-word cuts to full ASCII words or two-character Chinese words. Returns null for broad rewrites,
    /// pure deletions/insertions, punctuation-only edits, or corrections already stored.
    /// </summary>
    public GlossaryCorrection? SuggestCorrection(string original, string edited)
    {
        var before = original.Trim();
        var after = edited.Trim();
        if (before.Length == 0 || after.Length == 0 || before == after || before.Contains('\n') || after.Contains('\n'))
            return null;

        var prefix = 0;
        var limit = Math.Min(before.Length, after.Length);
        while (prefix < limit && before[prefix] == after[prefix])
            prefix++;

        var suffix = 0;
        while (suffix < before.Length - prefix && suffix < after.Length - prefix
               && before[before.Length - 1 - suffix] == after[after.Length - 1 - suffix])
            suffix++;

        // Never slice through the middle of an ASCII identifier ("Scale" -> "Skill" must not become "cal" -> "kil").
        while (prefix > 0 && (IsAsciiWordChar(before[prefix - 1]) && (prefix < before.Length && IsAsciiWordChar(before[prefix]) || prefix < after.Length && IsAsciiWordChar(after[prefix]))))
            prefix--;
        while (suffix > 0 && (IsAsciiWordChar(before[before.Length - suffix]) && (before.Length - suffix - 1 >= prefix && IsAsciiWordChar(before[before.Length - suffix - 1]) || after.Length - suffix - 1 >= prefix && IsAsciiWordChar(after[after.Length - suffix - 1]))))
            suffix--;

        var spoken = before.Substring(prefix, before.Length - prefix - suffix);
        var canonical = after.Substring(prefix, after.Length - prefix - suffix);

        // A one-character CJK homophone fix ("用" -> "润" inside "用色" -> "润色") needs its adjacent character for context.
        if (spoken.Length == 1 && canonical.Length == 1 && IsCjk(spoken[0]) && IsCjk(canonical[0]))
        {
            if (suffix > 0 && IsCjk(before[before.Length - suffix]))
                suffix--;
            else if (prefix > 0 && IsCjk(before[prefix - 1]))
                prefix--;

            spoken = before.Substring(prefix, before.Length - prefix - suffix);
            canonical = after.Substring(prefix, after.Length - prefix - suffix);
        }

        spoken = TrimPunctuationAndSpace(spoken);
        canonical = TrimPunctuationAndSpace(canonical);
        if (spoken.Length is 0 or > 16 || canonical.Length is 0 or > 24)
            return null;
        if (string.Equals(spoken, canonical, StringComparison.OrdinalIgnoreCase))
            return null;
        if (!spoken.Any(char.IsLetterOrDigit) || !canonical.Any(char.IsLetterOrDigit))
            return null;
        if (Corrections.Any(c => string.Equals(c.Spoken, spoken, StringComparison.OrdinalIgnoreCase)))
            return null;

        return new GlossaryCorrection(spoken, canonical);
    }

    /// <summary>
    /// Applies explicit accent/misrecognition rules (<c>spoken =&gt; canonical</c>) longest-first before cleanup/polish,
    /// enforcing whole-word boundaries on ASCII terms so substrings inside longer identifiers are never altered.
    /// Raw draft text stays untouched; only the text entering cleanup/polish is pre-normalized.
    /// </summary>
    public static string ApplyCorrections(string text, IReadOnlyList<GlossaryCorrection>? corrections)
    {
        if (string.IsNullOrEmpty(text) || corrections is null || corrections.Count == 0)
            return text;

        var result = text;
        foreach (var corr in corrections
                     .Where(c => c.Spoken.Length > 0 && c.Canonical.Length > 0)
                     .OrderByDescending(c => c.Spoken.Length))
        {
            var pattern = System.Text.RegularExpressions.Regex.Escape(corr.Spoken);
            if (IsAsciiWordChar(corr.Spoken[0]))
                pattern = @"(?<![A-Za-z0-9_-])" + pattern;
            if (IsAsciiWordChar(corr.Spoken[^1]))
                pattern += @"(?![A-Za-z0-9_-])";
            result = System.Text.RegularExpressions.Regex.Replace(
                result,
                pattern,
                _ => corr.Canonical,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }
        return result;
    }

    private static void Add(string entry, List<string> terms, List<GlossaryTerm> pairs, List<GlossaryCorrection> corrections)
    {
        foreach (var sep in CorrectionSeparators)
        {
            var corrIndex = entry.IndexOf(sep, StringComparison.Ordinal);
            if (corrIndex < 0)
                continue;

            var spoken = entry[..corrIndex].Trim();
            var canonical = entry[(corrIndex + sep.Length)..].Trim();
            if (spoken.Length == 0 || canonical.Length == 0 || string.Equals(spoken, canonical, StringComparison.Ordinal))
                return;

            AddTerm(canonical, terms);
            var existing = corrections.FindIndex(c => string.Equals(c.Spoken, spoken, StringComparison.OrdinalIgnoreCase));
            if (existing >= 0)
                corrections[existing] = new GlossaryCorrection(spoken, canonical);
            else
                corrections.Add(new GlossaryCorrection(spoken, canonical));
            return;
        }

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

    private static bool IsAsciiWordChar(char c) =>
        c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_' or '-';

    private static bool IsCjk(char c) => c is >= '\u4e00' and <= '\u9fff';

    private static string TrimPunctuationAndSpace(string value)
    {
        var start = 0;
        var end = value.Length;
        while (start < end && (char.IsWhiteSpace(value[start]) || char.IsPunctuation(value[start])))
            start++;
        while (end > start && (char.IsWhiteSpace(value[end - 1]) || char.IsPunctuation(value[end - 1])))
            end--;
        return value[start..end];
    }
}
