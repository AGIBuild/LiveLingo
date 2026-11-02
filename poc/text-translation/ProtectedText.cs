using System.Text.RegularExpressions;

namespace TranslationPoc;

public sealed partial record ProtectedText(string Source, IReadOnlyDictionary<string, string> Literals)
{
    internal static IReadOnlyList<(int Start, int End)> Spans(string source) =>
        LiteralPattern().Matches(source).Select(m => (m.Index, m.Index + m.Length)).ToArray();

    public static ProtectedText Encode(string source)
    {
        var prefix = "__KEEP_";
        while (source.Contains(prefix, StringComparison.Ordinal)) prefix = "_" + prefix;
        var literals = new Dictionary<string, string>(StringComparer.Ordinal);
        var encoded = LiteralPattern().Replace(source, match =>
        {
            var token = prefix + literals.Count + "__";
            literals.Add(token, match.Value);
            return token;
        });
        return new ProtectedText(encoded, literals);
    }

    public string Restore(string translated)
    {
        foreach (var (token, original) in Literals)
        {
            var start = translated.IndexOf(token, StringComparison.Ordinal);
            if (start < 0 || translated.IndexOf(token, start + token.Length, StringComparison.Ordinal) >= 0)
                throw new InvalidDataException("Protected literal missing or duplicated: " + token);
            translated = translated.Replace(token, original, StringComparison.Ordinal);
        }
        return translated;
    }

    [GeneratedRegex(@"```[\s\S]*?```|`[^`\r\n]+`|https?://[^\s<>`，。！？；“”]+|\$\{[A-Za-z_]\w*\}|\{[A-Za-z_]\w*\}")]
    private static partial Regex LiteralPattern();
}
