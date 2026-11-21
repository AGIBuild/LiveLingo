using LiveLingo.Core.Cloud;
using LiveLingo.Core.Dictation;
using LiveLingo.Core.Drafting;
using LiveLingo.Core.Inference;
using LiveLingo.Core.Models;
using LiveLingo.Core.Speech.Cloud;
using LiveLingo.Core.Speech.Native;
using LiveLingo.Core.Transcription;
using Microsoft.Extensions.Logging;

namespace LiveLingo.Core.Speech;

/// <summary>
/// Single speech coordinator for dictation, file transcription and background warm-up.
/// Follows the user's explicit stage routing: cloud speech (such as Qwen-Audio-3.1-ASR) when configured,
/// otherwise the local <see cref="SherpaSpeechEngine"/>. Never silently falls back across the privacy boundary.
/// </summary>
public sealed class RoutedSpeechEngine : IDictationSessionFactory, ISpeechWarmup, IAudioFileTranscriber, IAsyncDisposable
{
    private readonly ICloudRoutes _routes;
    private readonly SherpaSpeechEngine _local;
    private readonly IQwenWebSocketTransport _wsTransport;
    private readonly HttpClient _http;
    private readonly ModelUsage? _usage;
    private readonly ILogger<RoutedSpeechEngine> _logger;

    public RoutedSpeechEngine(
        ICloudRoutes routes,
        SherpaSpeechEngine local,
        IQwenWebSocketTransport wsTransport,
        HttpClient http,
        ILogger<RoutedSpeechEngine> logger,
        ModelUsage? usage = null)
    {
        _routes = routes;
        _local = local;
        _wsTransport = wsTransport;
        _http = http;
        _logger = logger;
        _usage = usage;
    }

    public ModelResidency State => _local.State;

    public event Action<ModelResidency>? StateChanged
    {
        add => _local.StateChanged += value;
        remove => _local.StateChanged -= value;
    }

    public async Task WarmAsync(IReadOnlyList<string> glossary, CancellationToken ct = default)
    {
        if (await _routes.GetSpeechAsync(ct).ConfigureAwait(false) is not null)
            return;

        await _local.WarmAsync(glossary, ct).ConfigureAwait(false);
    }

    public Task<IDictationSession> CreateAsync(DraftSession draft, IReadOnlyList<string> glossary, CancellationToken ct) =>
        CreateAsync(draft, SpeechSessionRequest.FromHotwords(glossary), ct);

    public async Task<IDictationSession> CreateAsync(DraftSession draft, SpeechSessionRequest request, CancellationToken ct)
    {
        var route = await _routes.GetSpeechAsync(ct).ConfigureAwait(false);
        if (route is null)
            return await _local.CreateAsync(draft, request, ct).ConfigureAwait(false);

        if (route.Protocol == SpeechCloudProtocol.QwenAudioWebSocket)
        {
            return await QwenWebSocketDictationSession.ConnectAndStartTaskAsync(
                _wsTransport, route, draft, request, _usage, _logger, ct).ConfigureAwait(false);
        }

        return new CloudHttpDictationSession(_http, route, request, draft, _usage, _logger);
    }

    public Task<IReadOnlyList<TimedSegment>> TranscribeAsync(
        string path, IReadOnlyList<string> glossary, IProgress<double>? progress, CancellationToken ct) =>
        TranscribeAsync(path, SpeechSessionRequest.FromHotwords(glossary), progress, ct);

    public async Task<IReadOnlyList<TimedSegment>> TranscribeAsync(
        string path, SpeechSessionRequest request, IProgress<double>? progress, CancellationToken ct)
    {
        var route = await _routes.GetSpeechAsync(ct).ConfigureAwait(false);
        if (route is null)
            return await _local.TranscribeAsync(path, request, progress, ct).ConfigureAwait(false);

        return await QwenAudioFileTranscriber.TranscribeAsync(
            path, route, request, _wsTransport, _http, _usage, progress, ct).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync() => _local.DisposeAsync();
}
