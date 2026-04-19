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
        var resolved = SpeechModelRouting.Resolve(CurrentMode, overrideId);

        if (!string.IsNullOrWhiteSpace(overrideId) &&
            !string.Equals(resolved.Id, overrideId, StringComparison.OrdinalIgnoreCase))
        {
            _logger?.LogWarning(
                "Configured ActiveSttModelId='{Id}' not found in SpeechToTextModels; falling back to routing-mode default.",
                overrideId);
        }

        return resolved;
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
}
