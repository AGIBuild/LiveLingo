using LLama;
using LLama.Common;
using LLama.Sampling;
using LiveLingo.Core.Processing;
using Microsoft.Extensions.Logging;

namespace LiveLingo.Core.Engines;

public sealed class LlamaTranslationEngine : ITranslationEngine
{
    private readonly QwenModelHost _host;
    private readonly ILogger<LlamaTranslationEngine> _logger;

    private static readonly Dictionary<string, (string EnglishName, string DisplayName)> Languages =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["zh"] = ("Chinese", "中文"),
            ["en"] = ("English", "English"),
            ["ja"] = ("Japanese", "日本語"),
            ["ko"] = ("Korean", "한국어"),
            ["fr"] = ("French", "Français"),
            ["de"] = ("German", "Deutsch"),
            ["es"] = ("Spanish", "Español"),
            ["ru"] = ("Russian", "Русский"),
            ["ar"] = ("Arabic", "العربية"),
            ["pt"] = ("Portuguese", "Português"),
        };

    public IReadOnlyList<LanguageInfo> SupportedLanguages { get; } =
        Languages.Select(kv => new LanguageInfo(kv.Key, kv.Value.DisplayName)).ToList();

    internal static readonly string[] StopSequences = ["</s>", "<|im_end|>"];

    public LlamaTranslationEngine(QwenModelHost host, ILogger<LlamaTranslationEngine> logger)
    {
        _host = host;
        _logger = logger;
    }

    public async Task<string> TranslateAsync(
        string text, string sourceLanguage, string targetLanguage, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var srcName = GetLanguageName(sourceLanguage);
        var tgtName = GetLanguageName(targetLanguage);

        var weights = await _host.GetWeightsAsync(ct);
        var modelParams = new ModelParams(_host.ModelPath) { ContextSize = 2048 };
        var executor = new StatelessExecutor(weights, modelParams);

        var inferenceParams = new InferenceParams
        {
            MaxTokens = 512,
            AntiPrompts = StopSequences,
            SamplingPipeline = new DefaultSamplingPipeline
            {
                Temperature = 0.1f,
                TopP = 0.95f,
            }
        };

        // Enforce non-thinking mode for reasoning models
        var systemPrompt = $"You are a professional translator. Translate the user's text from {srcName} to {tgtName}. Output ONLY the translated text, nothing else. Do not output any thought process or explanation.";
        var prompt = $"<|im_start|>system\n{systemPrompt}<|im_end|>\n<|im_start|>user\n{text}<|im_end|>\n<|im_start|>assistant\n";

        _logger.LogDebug("Translation prompt for {Src}→{Tgt}: {Prompt}", sourceLanguage, targetLanguage, prompt);

        var output = new List<string>();
        await foreach (var token in executor.InferAsync(prompt, inferenceParams, ct))
        {
            output.Add(token);
            if (string.Concat(output).Length > text.Length * 5)
                break;
        }

        var result = string.Concat(output).Trim();

        if (string.IsNullOrWhiteSpace(result))
        {
            _logger.LogWarning("Translation returned empty output for {Src}→{Tgt}", sourceLanguage, targetLanguage);
            return text;
        }

        _logger.LogDebug("Translated {Src}→{Tgt}: {In} → {Out}", sourceLanguage, targetLanguage, text, result);
        return result;
    }

    public bool SupportsLanguagePair(string sourceLanguage, string targetLanguage) =>
        Languages.ContainsKey(sourceLanguage) && Languages.ContainsKey(targetLanguage);

    public void Dispose() { }

    internal static string BuildPrompt(string text, string sourceLanguage, string targetLanguage)
    {
        var srcName = GetLanguageName(sourceLanguage);
        var tgtName = GetLanguageName(targetLanguage);
        var systemPrompt = $"You are a professional translator. Translate the user's text from {srcName} to {tgtName}. Output ONLY the translated text, nothing else. Do not output any thought process or explanation.";
        return $"<|im_start|>system\n{systemPrompt}<|im_end|>\n<|im_start|>user\n{text}<|im_end|>\n<|im_start|>assistant\n";
    }

    private static string GetLanguageName(string code) =>
        Languages.TryGetValue(code, out var lang) ? lang.EnglishName : code;
}
