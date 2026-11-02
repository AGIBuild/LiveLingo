using System.Text.RegularExpressions;

namespace Dictation.Core;

/// <summary>
/// Recognizers differ in how they space mixed Chinese/English text and some leak model tokens. This is the single place
/// that normalizes it, so every consumer sees the same text.
/// </summary>
public static partial class TextTidy
{
    private const string EndOfText = "<|endoftext|>";

    public static string Apply(string text)
    {
        // A language model that has emitted its end-of-text token and keeps going is hallucinating: observed with Qwen3-ASR on short audio.
        var end = text.IndexOf(EndOfText, StringComparison.Ordinal);
        if (end >= 0) text = text[..end];
        text = ModelToken().Replace(text, "");
        text = text.Trim();
        text = SpaceBetweenCjk().Replace(text, "");
        text = SpaceAroundFullWidthPunctuation().Replace(text, "");
        return text;
    }

    [GeneratedRegex(@"<\|[^|>]*\|>")]
    private static partial Regex ModelToken();

    [GeneratedRegex(@"(?<=\p{IsCJKUnifiedIdeographs})\s+(?=\p{IsCJKUnifiedIdeographs})")]
    private static partial Regex SpaceBetweenCjk();

    [GeneratedRegex(@"(?<=[，。！？；：、）」』”’])\s+|\s+(?=[，。！？；：、）」』”’])")]
    private static partial Regex SpaceAroundFullWidthPunctuation();
}
