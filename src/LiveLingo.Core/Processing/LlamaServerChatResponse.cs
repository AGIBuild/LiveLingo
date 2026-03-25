using System.Text.Json;

namespace LiveLingo.Core.Processing;

/// <summary>
/// Parses non-streaming <c>/v1/chat/completions</c> responses from llama-server.
/// Qwen3 and similar templates may split output into <c>reasoning_content</c> vs <c>content</c>
/// depending on <c>--reasoning-format</c>; this helper reads both.
/// </summary>
public static class LlamaServerChatResponse
{
    // Qwen-style think markers (avoid mixing up open vs close in source).
    private const string ClosingThinkTag = "\u003c/think\u003e";
    private const string OpeningThinkTag = "\u003cthink\u003e";

    /// <summary>
    /// Returns assistant text from the first choice's message, preferring <c>content</c> and
    /// falling back to <c>reasoning_content</c> when empty.
    /// </summary>
    public static string GetAssistantText(JsonElement root)
    {
        var message = root.GetProperty("choices")[0].GetProperty("message");
        return GetAssistantTextFromMessage(message);
    }

    public static string GetAssistantTextFromMessage(JsonElement message)
    {
        var content = message.TryGetProperty("content", out var c)
            ? ExtractTextFromContentElement(c)
            : string.Empty;

        if (!string.IsNullOrWhiteSpace(content))
            return content;

        if (message.TryGetProperty("reasoning_content", out var r))
            return ExtractTextFromContentElement(r);

        return string.Empty;
    }

    /// <summary>
    /// OpenAI-compatible APIs may return <c>content</c> as a string or as a JSON array of parts
    /// (e.g. <c>{"type":"text","text":"..."}</c>).
    /// </summary>
    public static string ExtractTextFromContentElement(JsonElement content)
    {
        switch (content.ValueKind)
        {
            case JsonValueKind.String:
                return content.GetString()?.Trim() ?? string.Empty;
            case JsonValueKind.Array:
                var parts = new List<string>();
                foreach (var part in content.EnumerateArray())
                {
                    if (part.ValueKind == JsonValueKind.String)
                    {
                        var s = part.GetString();
                        if (!string.IsNullOrEmpty(s))
                            parts.Add(s);
                    }
                    else if (part.TryGetProperty("text", out var textEl) &&
                             textEl.ValueKind == JsonValueKind.String)
                    {
                        var t = textEl.GetString();
                        if (!string.IsNullOrEmpty(t))
                            parts.Add(t);
                    }
                }

                return string.Join("", parts).Trim();
            case JsonValueKind.Null:
                return string.Empty;
            default:
                return string.Empty;
        }
    }

    /// <summary>
    /// Short diagnostic line for logs when assistant text is empty (no full response body).
    /// </summary>
    public static string DescribeFirstChoiceForLog(JsonElement root)
    {
        try
        {
            var choice = root.GetProperty("choices")[0];
            var finish = choice.TryGetProperty("finish_reason", out var fr)
                ? (fr.ValueKind == JsonValueKind.String ? fr.GetString() ?? "?" : fr.ToString())
                : "?";
            var msg = choice.GetProperty("message");
            var contentDesc = msg.TryGetProperty("content", out var c) ? SummarizeJsonValue(c) : "missing";
            var reasoningDesc = msg.TryGetProperty("reasoning_content", out var r) ? SummarizeJsonValue(r) : "missing";
            return $"finish_reason={finish}, content={contentDesc}, reasoning_content={reasoningDesc}";
        }
        catch (Exception ex)
        {
            return $"parse error: {ex.Message}";
        }
    }

    private static string SummarizeJsonValue(JsonElement e) =>
        e.ValueKind switch
        {
            JsonValueKind.String => $"string(len={e.GetString()?.Length ?? 0})",
            JsonValueKind.Array => $"array(len={e.GetArrayLength()})",
            JsonValueKind.Null => "null",
            _ => e.ValueKind.ToString()
        };

    /// <summary>
    /// Keeps text after the last closing think tag when present. If there is an opening
    /// <c>think</c> tag but no closing tag, strips the opening prefix only (translation may follow).
    /// </summary>
    public static string StripQwenThinkTags(string result)
    {
        if (result.Contains(ClosingThinkTag))
            return result.Split(ClosingThinkTag).Last().Trim();

        if (result.StartsWith(OpeningThinkTag, StringComparison.Ordinal))
            return result[OpeningThinkTag.Length..].TrimStart();

        return result;
    }
}
