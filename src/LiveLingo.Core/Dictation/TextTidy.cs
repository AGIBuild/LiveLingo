using System.Text.RegularExpressions;

namespace LiveLingo.Core.Dictation;

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
        text = StripMalformedTokens(text);
        text = text.Replace("`", "", StringComparison.Ordinal);
        text = WhitespaceRun().Replace(text, " ").Trim();
        text = SpaceBetweenCjk().Replace(text, "");
        text = SpaceAroundFullWidthPunctuation().Replace(text, "");
        return text;
    }

    internal static string StripMalformedTokens(string text)
    {
        text = QwenAsrLanguagePrefix().Replace(text, "");
        text = QwenAsrJsonLangFirst().Replace(text, m => UnescapeJsonText(m.Groups[1].Value));
        text = QwenAsrJsonTextFirst().Replace(text, m => UnescapeJsonText(m.Groups[1].Value));
        text = QwenAsrJsonTruncated().Replace(text, m => UnescapeJsonText(m.Groups[1].Value));
        text = QwenAsrCodeFenceLeak().Replace(text, " ");
        text = QwenAsrInlineTemplateLeak().Replace(text, " ");
        text = ModelToken().Replace(text, "");
        text = UnclosedTokenWithBpeEcho().Replace(text, "");
        text = MalformedAngleToken().Replace(text, "");
        return text;
    }

    private static string UnescapeJsonText(string value) =>
        value.Replace("\\\"", "\"", StringComparison.Ordinal)
             .Replace("\\n", " ", StringComparison.Ordinal)
             .Replace("\\r", " ", StringComparison.Ordinal)
             .Replace("\\t", " ", StringComparison.Ordinal)
             .Replace("\\\\", "\\", StringComparison.Ordinal);

    /// <summary>Strips raw Qwen3-ASR scaffold prefixes (<c>language Chinese&lt;asr_text&gt;</c>) when served without template separation.</summary>
    [GeneratedRegex(@"^\s*language\s+[A-Za-z][A-Za-z\s-]{0,30}<asr_text>", RegexOptions.CultureInvariant)]
    private static partial Regex QwenAsrLanguagePrefix();

    /// <summary>Unwraps Qwen3-ASR JSON structured-output leaks (<c>{ "language": "Chinese", "text": "..." }</c>).</summary>
    [GeneratedRegex(@"\{\s*""language""\s*:\s*""[^""]*""\s*,\s*""text""\s*:\s*""((?:\\.|[^""\\])*)""\s*\}", RegexOptions.CultureInvariant)]
    private static partial Regex QwenAsrJsonLangFirst();

    [GeneratedRegex(@"\{\s*""text""\s*:\s*""((?:\\.|[^""\\])*)""\s*,\s*""language""\s*:\s*""[^""]*""\s*\}", RegexOptions.CultureInvariant)]
    private static partial Regex QwenAsrJsonTextFirst();

    [GeneratedRegex(@"\{\s*""language""\s*:\s*""[^""]*""\s*,\s*""text""\s*:\s*""((?:\\.|[^""\\])*)$", RegexOptions.CultureInvariant)]
    private static partial Regex QwenAsrJsonTruncated();

    /// <summary>Strips hallucinated Markdown fenced blocks (<c>```tableular data```</c>, <c>```text</c>) emitted by Qwen3-ASR during decode collapse.</summary>
    [GeneratedRegex(@"```[A-Za-z0-9_-]*\s*[\s\S]*?```|```[A-Za-z0-9_-]*", RegexOptions.CultureInvariant)]
    private static partial Regex QwenAsrCodeFenceLeak();

    /// <summary>Strips mid-utterance Qwen3-ASR scaffold leaks such as <c>language Chinese啊</c> or <c>&lt;asr_text&gt;</c>.</summary>
    [GeneratedRegex(@"(?<![A-Za-z])language\s+(?:Chinese|English|Japanese|Korean|Yue|Cantonese|None|auto)(?![A-Za-z])\s*(?:<asr_text>)?|<asr_text>", RegexOptions.CultureInvariant)]
    private static partial Regex QwenAsrInlineTemplateLeak();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRun();

    [GeneratedRegex(@"<\|[^|>]*\|>")]
    private static partial Regex ModelToken();

    /// <summary>Strips unclosed BPE/special-token glitches plus trailing 1–2 short subword echoes right before CJK (e.g. <c>&lt;thibbe Na na牛魔来</c>).</summary>
    [GeneratedRegex(@"<[A-Za-z_][A-Za-z0-9_|-]*(?:\s+[A-Za-z]{1,4}){0,2}(?=\s*\p{IsCJKUnifiedIdeographs})", RegexOptions.CultureInvariant)]
    private static partial Regex UnclosedTokenWithBpeEcho();

    /// <summary>Strips malformed or closed angle-bracket model tokens such as <c>&lt;unk&gt;</c>, <c>&lt;/s&gt;</c>, or <c>&lt;thibbe</c>.</summary>
    [GeneratedRegex(@"<(?:[|/][A-Za-z0-9_|-]*|[A-Za-z_][A-Za-z0-9_|-]*)>?", RegexOptions.CultureInvariant)]
    private static partial Regex MalformedAngleToken();

    [GeneratedRegex(@"(?<=\p{IsCJKUnifiedIdeographs})\s+(?=\p{IsCJKUnifiedIdeographs})")]
    private static partial Regex SpaceBetweenCjk();

    [GeneratedRegex(@"(?<=[，。！？；：、）」』”’])\s+|\s+(?=[，。！？；：、）」』”’])")]
    private static partial Regex SpaceAroundFullWidthPunctuation();
}
