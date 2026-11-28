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
        1. 把被停顿切碎的半句话接成通顺完整的句子（删去打断主谓宾或动补关系的错误句号，修正“的/地/得”及多字漏字病句，例如“打的还不错，的可以点点”改为“打得还不错，可以点点”），按语义重新断句；句首用于反驳前文的“不是”后面补上逗号，避免误读成反义。
        2. 彻底删去所有口头语、语气赘词（呃、嗯、啊、呀、呢、吧、嘛、那个、这个、就是、然后呢、对吧、你看）、单字结巴（如“我我”→“我”）、重复句以及说到一半卡壳重来的残片（同一个意思或提问重复多遍时，只保留最通顺完整的那一遍；自我改口时只保留纠正后的内容），并剔除背景音拟声词连读（如“噔噔噔噔…”）与明显的识别乱码残片。
        3. 根据全文主题和上下文修正明显的同音/近音误识别（如“在/再”、专有名词或高频词汇误听）并补全快读漏字；词表里的写法优先；确实无法从上下文判断原意的词保留原样，不要凭空编造。
        4. 保持说话人自然流畅的真实口吻，写成干净利索、可直接发送的文字，不要改成生硬公文腔，也不要添加标题或列表符号。

        示例 1：
        原文：呃，我觉得这个，就是那个接口的超时。设成十秒。不对，设成三十秒吧。嗯，对，就这样。
        润色：我觉得这个接口的超时设成三十秒吧。

        示例 2：
        原文：噔噔噔噔噔。打的还不错，的可以点点关注呀，点关注呀！我我刚才看了一下，哥，能不能给打给给龙拿了？哥，能不能把龙拿了？没有别的意外了，快打！快打！
        润色：打得还不错，可以点点关注！我刚才看了一下，哥，能不能把龙拿了？没有别的意外了，快打！
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

    public static string NormalizeText(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n').Trim();

    private static string Normalize(string text) => NormalizeText(text);
}
