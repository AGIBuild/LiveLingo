namespace LiveLingo.Core.Cleanup;

/// <summary>
/// The part of the polish prompt the user may change: what a polished text should be like (the "polish requirements").
/// The rest of the prompt (what the input is, and the rules that keep the user's meaning) is fixed in
/// <see cref="LlmDictationPolisher"/>, so a requirement text that is silly cannot turn the polish into something that
/// answers questions or invents facts; the fidelity gate checks the result either way.
/// This is the one place that decides which text is used: whatever the settings hold, <see cref="Resolve"/> returns
/// something usable.
/// </summary>
public static class PolishStyle
{
    public const int MinLength = 10;
    public const int MaxLength = 1500;

    public const string Default = """
        润色要求：
        1. 把被停顿切碎的半句话接成完整的句子，按意思重新断句，理顺前后的衔接。
        2. 必须删去所有口头语和填充词（呃、嗯、啊、那个、这个、就是、然后呢、对吧、你看），以及犹豫、重复和说到一半就放弃的半句；说话人把同一个意思又完整地说了一遍时，只保留完整的那一遍；自我纠正时，只保留纠正后的内容。
        3. 根据全文主题和上下文改正同音错字和技术名词的误识别；词表里的写法优先；拿不准的词保留原样，不要删除。
        4. 保持说话人自己的口吻，写成通顺、可以直接发送的文字，不要改成公文腔。

        示例：
        原文：呃，我觉得这个，就是那个接口的超时。设成十秒。不对，设成三十秒吧。嗯，对，就这样。
        润色：我觉得这个接口的超时设成三十秒吧。
        """;

    /// <summary>
    /// A requirement text is usable when it says something (not blank, not a few characters), is not longer than the
    /// model can sensibly read next to the dictation, and holds no control characters (a damaged settings file).
    /// </summary>
    public static bool IsUsable(string? text)
    {
        if (text is null)
            return false;

        var trimmed = text.Trim();
        return trimmed.Length is >= MinLength and <= MaxLength
            && !trimmed.Any(c => char.IsControl(c) && c is not ('\n' or '\r' or '\t'));
    }

    /// <summary>Settings store "" for "the default"; a stored text that is not usable is treated the same way.</summary>
    public static string Resolve(string? custom) =>
        IsUsable(custom) ? Normalize(custom!) : Default;

    /// <summary>
    /// What to store for a text the user saved: nothing when it is the default (so the default can improve later) or when
    /// it is blank; null when it is not usable and must be refused.
    /// </summary>
    public static string? ToStored(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";
        if (!IsUsable(text))
            return null;

        var normalized = Normalize(text);
        return normalized == Normalize(Default) ? "" : normalized;
    }

    private static string Normalize(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
}
