using System.Runtime.CompilerServices;
using LiveLingo.Core.Models;
using Microsoft.Extensions.Logging;

namespace LiveLingo.Core.Engines;

/// <summary>
/// Top-level translation engine that routes requests between a dedicated ONNX
/// fast-path (Marian) for specific language pairs and the general chat pipeline
/// (Llama/Gemma) for everything else.
///
/// Routing decision (capability check, not defensive fallback):
/// <list type="bullet">
///   <item>Fast path supports the language pair <b>and</b> its on-disk assets are complete → Marian ONNX</item>
///   <item>Otherwise → chat-based engine (Gemma/Qwen via MEA IChatClient)</item>
/// </list>
///
/// The chat-based engine owns its own cloud/local model selection through
/// <see cref="IModelSelector"/>; this router only decides <i>engine family</i>.
/// </summary>
public sealed class HybridTranslationEngine : ITranslationEngine
{
    private readonly IFastPathTranslationEngine _fastPath;
    private readonly IChatPathTranslationEngine _chatPath;
    private readonly IModelCatalog _catalog;
    private readonly IModelManager _modelManager;
    private readonly ILogger<HybridTranslationEngine> _logger;

    public IReadOnlyList<LanguageInfo> SupportedLanguages { get; }

    public HybridTranslationEngine(
        IFastPathTranslationEngine fastPath,
        IChatPathTranslationEngine chatPath,
        IModelCatalog catalog,
        IModelManager modelManager,
        ILogger<HybridTranslationEngine> logger)
    {
        _fastPath = fastPath;
        _chatPath = chatPath;
        _catalog = catalog;
        _modelManager = modelManager;
        _logger = logger;

        SupportedLanguages = fastPath.SupportedLanguages
            .Concat(chatPath.SupportedLanguages)
            .DistinctBy(l => l.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<string> TranslateAsync(
        string text, string sourceLanguage, string targetLanguage, CancellationToken ct)
    {
        if (TrySelectFastPath(sourceLanguage, targetLanguage))
        {
            _logger.LogDebug(
                "Routing {Src}→{Tgt} ({Len} chars) to Marian ONNX fast path.",
                sourceLanguage, targetLanguage, text.Length);
            return await _fastPath.TranslateAsync(text, sourceLanguage, targetLanguage, ct)
                .ConfigureAwait(false);
        }

        _logger.LogDebug(
            "Routing {Src}→{Tgt} ({Len} chars) to chat engine.",
            sourceLanguage, targetLanguage, text.Length);
        return await _chatPath.TranslateAsync(text, sourceLanguage, targetLanguage, ct)
            .ConfigureAwait(false);
    }

    public async IAsyncEnumerable<TranslationDelta> TranslateStreamingAsync(
        string text,
        string sourceLanguage,
        string targetLanguage,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (TrySelectFastPath(sourceLanguage, targetLanguage))
        {
            _logger.LogDebug(
                "Streaming {Src}→{Tgt} via Marian ONNX (single-delta emission).",
                sourceLanguage, targetLanguage);
            await foreach (var delta in _fastPath
                               .TranslateStreamingAsync(text, sourceLanguage, targetLanguage, ct)
                               .ConfigureAwait(false))
            {
                yield return delta;
            }
            yield break;
        }

        _logger.LogDebug(
            "Streaming {Src}→{Tgt} via chat engine (token-level).",
            sourceLanguage, targetLanguage);
        await foreach (var delta in _chatPath
                           .TranslateStreamingAsync(text, sourceLanguage, targetLanguage, ct)
                           .ConfigureAwait(false))
        {
            yield return delta;
        }
    }

    public bool SupportsLanguagePair(string sourceLanguage, string targetLanguage) =>
        _fastPath.SupportsLanguagePair(sourceLanguage, targetLanguage) ||
        _chatPath.SupportsLanguagePair(sourceLanguage, targetLanguage);

    /// <summary>
    /// Fast path is selected iff the Marian catalog has a matching descriptor
    /// AND all required ONNX assets exist locally. No cross-engine fallback
    /// occurs at runtime — either the fast path is ready or it isn't.
    /// </summary>
    private bool TrySelectFastPath(string sourceLanguage, string targetLanguage)
    {
        if (!_fastPath.SupportsLanguagePair(sourceLanguage, targetLanguage))
            return false;

        var descriptor = _catalog.GetProfiles(ModelTaskType.Translation)
            .Where(p => p.ExecutionKind == ModelExecutionKind.OnnxTranslation)
            .Where(p => p.Languages.Count >= 2)
            .FirstOrDefault(p =>
                string.Equals(p.Languages[0], sourceLanguage, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(p.Languages[1], targetLanguage, StringComparison.OrdinalIgnoreCase))
            ?.Descriptor;

        return descriptor is not null && _modelManager.HasAllExpectedLocalAssets(descriptor);
    }

    public void Dispose()
    {
        _fastPath.Dispose();
        _chatPath.Dispose();
    }
}
