using LiveLingo.Core.Models;
using Microsoft.Extensions.Logging;

namespace LiveLingo.Core.Speech;

/// <summary>
/// Default implementation: maps each routing mode to a canonical descriptor in
/// <see cref="ModelRegistry.SpeechToTextModels"/> and the matching engine implementation.
/// Re-reads <see cref="CoreOptions.SpeechRoutingMode"/> on every call so that toggling the routing
/// mode in settings takes effect on the next transcription request without restart.
/// </summary>
internal sealed class DefaultSpeechEngineSelector : ISpeechEngineSelector
{
    private readonly CoreOptions _options;
    private readonly IReadOnlyDictionary<string, ISpeechToTextEngine> _enginesByModelId;
    private readonly ISpeechToTextEngine _stub;
    private readonly ILogger<DefaultSpeechEngineSelector>? _logger;

    public DefaultSpeechEngineSelector(
        CoreOptions options,
        IEnumerable<ISpeechToTextEngine> engines,
        ILogger<DefaultSpeechEngineSelector>? logger = null)
    {
        _options = options;
        _logger = logger;

        var lookup = new Dictionary<string, ISpeechToTextEngine>(StringComparer.OrdinalIgnoreCase);
        ISpeechToTextEngine? stub = null;

        foreach (var engine in engines)
        {
            if (engine is StubSpeechToTextEngine)
            {
                stub = engine;
                continue;
            }

            foreach (var modelId in engine.SupportedModelIds)
                lookup[modelId] = engine;
        }

        _enginesByModelId = lookup;
        _stub = stub ?? new StubSpeechToTextEngine();
    }

    public SttRoutingMode CurrentMode => _options.SpeechRoutingMode;

    public ModelDescriptor GetActiveModel()
    {
        var overrideId = _options.ActiveSttModelId;
        if (!string.IsNullOrWhiteSpace(overrideId))
        {
            var match = ModelRegistry.SpeechToTextModels
                .FirstOrDefault(m => string.Equals(m.Id, overrideId, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
                return match;

            _logger?.LogWarning(
                "Configured ActiveSttModelId='{Id}' not found in SpeechToTextModels; falling back to routing-mode default.",
                overrideId);
        }

        return ResolveDefaultModel(CurrentMode);
    }

    public ISpeechToTextEngine GetEngine()
    {
        var model = GetActiveModel();
        if (_enginesByModelId.TryGetValue(model.Id, out var engine))
            return engine;

        _logger?.LogWarning(
            "No engine registered for STT model '{Id}'. Returning stub engine; transcription will fail until a matching engine is registered.",
            model.Id);
        return _stub;
    }

    private static ModelDescriptor ResolveDefaultModel(SttRoutingMode mode) => mode switch
    {
        // For now every routing mode resolves to Cohere Transcribe; phase 2 will plug in
        // streaming Zipformer / multilingual Parakeet without changing this contract.
        SttRoutingMode.AccuracyFirst => ModelRegistry.SherpaCohereTranscribe14LangInt8,
        SttRoutingMode.StreamingFirst => ModelRegistry.SherpaCohereTranscribe14LangInt8,
        SttRoutingMode.MultilingualFirst => ModelRegistry.SherpaCohereTranscribe14LangInt8,
        _ => ModelRegistry.SherpaCohereTranscribe14LangInt8
    };
}
