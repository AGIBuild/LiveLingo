using LiveLingo.Core.Inference;
using LiveLingo.Core.Dictation;

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
        ["cache_prompt"] = true
    };

    public async Task<TranslatedText> TranslateAsync(TranslationRequest request, CancellationToken ct)
    {
        var protectedText = ProtectedText.Encode(request.Source);
        var prompt = TranslationPrompt.Build(request with { Source = protectedText.Source });
        var maxTokens = request.FinalDocument
            ? Math.Max(MaxOutputTokens, protectedText.Source.Length * 4 + 256)
            : Math.Min(MaxOutputTokens, protectedText.Source.Length * 2 + 128);

        var completion = await client.CompleteAsync(new ChatRequest(prompt, maxTokens, 0, Sampling), ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(completion.Text))
            throw new InvalidDataException("Empty translation response.");
        var translated = protectedText.Restore(completion.Text);
        // Same-language speech editing needs the same fidelity boundary as polish, not just literal restoration.
        // Only Chinese-dominant input is checked: English -> Chinese is translation, not a lexical cleanup.
        var chinese = protectedText.Source.Count(c => c is >= '\u4e00' and <= '\u9fff');
        if (request.FinalDocument && request.Target == TargetLanguage.Chinese && chinese > 0
            && chinese * 2 >= protectedText.Source.Count(char.IsLetter))
        {
            var gate = FidelityGate.Check(request.Source, translated, request.Terms.Select(t => t.Target).ToArray(), allowParagraphs: true);
            if (!gate.Accepted)
                throw new InvalidDataException("Final same-language translation refused: " + gate.Rejection?.Reason);
        }
        return new TranslatedText(translated, completion.FirstVisibleMs, completion.TotalMs);
    }
}
