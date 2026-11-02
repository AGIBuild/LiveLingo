using System.Text.RegularExpressions;
using LiveLingo.Core.Drafting;

namespace LiveLingo.Core.Translation;

/// <summary>
/// Replaces code, URLs and placeholders with opaque tokens before translation and restores them afterwards.
/// A token that is missing or duplicated in the translation is an explicit failure.
/// </summary>
public sealed record ProtectedText(string Source, IReadOnlyDictionary<string, string> Literals)
{
    public static ProtectedText Encode(string source)
    {
        var prefix = "__KEEP_";
        while (source.Contains(prefix, StringComparison.Ordinal)) prefix = "_" + prefix;

        var literals = new Dictionary<string, string>(StringComparer.Ordinal);
        var encoded = new System.Text.StringBuilder();
        var position = 0;
        foreach (var (start, end) in TextUnits.LiteralSpans(source))
        {
            var token = prefix + literals.Count + "__";
            literals.Add(token, source[start..end]);
            encoded.Append(source, position, start - position).Append(token);
            position = end;
        }
        encoded.Append(source, position, source.Length - position);
        return new ProtectedText(encoded.ToString(), literals);
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
}
