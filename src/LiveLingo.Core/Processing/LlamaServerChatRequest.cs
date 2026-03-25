using System.Text.Json.Serialization;

namespace LiveLingo.Core.Processing;

public sealed record LlamaServerChatMessage(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")] string Content);

public sealed record LlamaServerChatRequest(
    [property: JsonPropertyName("messages")] IReadOnlyList<LlamaServerChatMessage> Messages,
    [property: JsonPropertyName("max_tokens")] int MaxTokens,
    [property: JsonPropertyName("temperature")] float Temperature,
    [property: JsonPropertyName("top_p")] float TopP,
    [property: JsonPropertyName("stop")] IReadOnlyList<string> Stop,
    [property: JsonPropertyName("stream")] bool Stream)
{
    public static readonly string[] DefaultStopSequences = ["</s>", "<|im_end|>", "</think>"];

    public static LlamaServerChatRequest CreateTranslation(
        string text,
        string sourceLanguageName,
        string targetLanguageName)
    {
        var systemPrompt =
            $"You are an expert translation engine. Your task is to translate the source text from {sourceLanguageName} to {targetLanguageName}.\n\n" +
            $"Rules:\n" +
            $"1. Output ONLY the final {targetLanguageName} translation.\n" +
            $"2. Do NOT output any {sourceLanguageName} text.\n" +
            $"3. Do NOT output any explanations, conversational text, or notes.\n" +
            $"4. Do not use <think> tags or output any thought process.";
        var userPrompt = $"Translate the following {sourceLanguageName} text to {targetLanguageName}:\n\n<source>\n{text}\n</source>";

        return new LlamaServerChatRequest(
            [
                new LlamaServerChatMessage("system", systemPrompt),
                new LlamaServerChatMessage("user", userPrompt)
            ],
            MaxTokens: 512,
            Temperature: 0.1f,
            TopP: 0.95f,
            Stop: DefaultStopSequences,
            Stream: false);
    }

    public static LlamaServerChatRequest CreateTextProcessor(string systemPrompt, string text) =>
        new(
            [
                new LlamaServerChatMessage("system", $"{systemPrompt} Do not use <think> tags."),
                new LlamaServerChatMessage("user", text)
            ],
            MaxTokens: 512,
            Temperature: 0.3f,
            TopP: 0.9f,
            Stop: DefaultStopSequences,
            Stream: false);
}
