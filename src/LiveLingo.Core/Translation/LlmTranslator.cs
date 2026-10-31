using LiveLingo.Core.Inference;

namespace LiveLingo.Core.Translation;

/// <summary>Translates with a chat model: protects literals, sends the plain-text prompt, restores literals.</summary>
public sealed class LlmTranslator(IChatCompletionClient client) : ITranslator
{
    private const int MaxOutputTokens = 2048;

    // Deterministic decoding validated in poc/text-translation; the extras are llama-server sampling fields.
    private static readonly IReadOnlyDictionary<string, object> Sampling = new Dictionary<string, object>
    {
        ["top_p"] = 0.6,
        ["top_k"] = 20,
        ["repeat_penalty"] = 1.05,
        ["seed"] = 42,
        ["cache_prompt"] = false
    };

    public async Task<TranslatedText> TranslateAsync(TranslationRequest request, CancellationToken ct)
    {
        var protectedText = ProtectedText.Encode(request.Source);
        var prompt = TranslationPrompt.Build(request with { Source = protectedText.Source });
        var maxTokens = Math.Min(MaxOutputTokens, protectedText.Source.Length * 2 + 128);

        var completion = await client.CompleteAsync(new ChatRequest(prompt, maxTokens, 0, Sampling), ct).ConfigureAwait(false);
        return new TranslatedText(protectedText.Restore(completion.Text), completion.FirstVisibleMs, completion.TotalMs);
    }
}
