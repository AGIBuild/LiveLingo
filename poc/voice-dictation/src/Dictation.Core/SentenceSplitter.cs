using System.Text;

namespace Dictation.Core;

/// <summary>
/// Splits settled recognizer text into sentences and joins text pieces.
/// A sentence ends at sentence-final punctuation; the remainder is an unfinished tail.
/// </summary>
public static class SentenceSplitter
{
    private const string Terminators = "。！？!?；;…";
    private const string Closers = "”’」』）)\"'】";

    public static (List<string> Sentences, string Remainder) Split(string text)
    {
        var sentences = new List<string>();
        var current = new StringBuilder();

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            current.Append(c);
            if (!EndsSentenceAt(text, i)) continue;

            while (i + 1 < text.Length && (Terminators.Contains(text[i + 1]) || Closers.Contains(text[i + 1])))
                current.Append(text[++i]);

            var sentence = current.ToString().Trim();
            if (sentence.Length > 0) sentences.Add(sentence);
            current.Clear();
        }

        return (sentences, current.ToString().TrimStart());
    }

    private static bool EndsSentenceAt(string text, int i)
    {
        var c = text[i];
        if (Terminators.Contains(c)) return true;
        if (c == '\n') return true;
        if (c != '.') return false;

        // A dot ends a sentence only when followed by whitespace or the end, and not inside a number or identifier.
        var atEnd = i + 1 >= text.Length;
        var followedBySpace = !atEnd && char.IsWhiteSpace(text[i + 1]);
        var afterDigit = i > 0 && char.IsDigit(text[i - 1]);
        return (atEnd || followedBySpace) && !afterDigit;
    }

    /// <summary>Joins two text pieces, adding a space only between two ASCII word characters.</summary>
    public static string Join(string left, string right)
    {
        if (left.Length == 0) return right;
        if (right.Length == 0) return left;
        return IsAsciiWord(left[^1]) && IsAsciiWord(right[0]) ? left + " " + right : left + right;
    }

    private static bool IsAsciiWord(char c) => c < 128 && char.IsLetterOrDigit(c);
}
