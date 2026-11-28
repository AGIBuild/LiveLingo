using LiveLingo.Core.Cloud;
using LiveLingo.Core.Dictation;
using LiveLingo.Core.Drafting;
using LiveLingo.Core.Inference;
using LiveLingo.Core.LocalHost;
using LiveLingo.Core.Models;
using LiveLingo.Core.Speech.Cloud;
using LiveLingo.Core.Speech.Native;
using LiveLingo.Core.Transcription;
using Microsoft.Extensions.Logging;

namespace LiveLingo.Core.Speech;

/// <summary>
/// Single speech coordinator for dictation, file transcription and background warm-up.
/// Follows the user's explicit three-way stage routing: cloud speech (such as Qwen-Audio-3.1-ASR) when configured,
/// a local shared model host (loopback OpenAI-compatible ASR server) when configured, otherwise the built-in local
/// <see cref="SherpaSpeechEngine"/>. Never silently falls back across the privacy boundary.
/// </summary>
public sealed class RoutedSpeechEngine : IDictationSessionFactory, ISpeechWarmup, IAudioFileTranscriber, IAsyncDisposable
{
    private readonly ICloudRoutes _routes;
    private readonly ILocalHostRoutes? _localHostRoutes;
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
        ModelUsage? usage = null,
        ILocalHostRoutes? localHostRoutes = null)
    {
        _routes = routes;
        _localHostRoutes = localHostRoutes;
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
        if (await ResolveExternalRouteAsync(ct).ConfigureAwait(false) is not null)
            return;

        await _local.WarmAsync(glossary, ct).ConfigureAwait(false);
    }

    public Task<IDictationSession> CreateAsync(DraftSession draft, IReadOnlyList<string> glossary, CancellationToken ct) =>
        CreateAsync(draft, SpeechSessionRequest.FromHotwords(glossary), ct);

    public async Task<IDictationSession> CreateAsync(DraftSession draft, SpeechSessionRequest request, CancellationToken ct)
    {
        var route = await ResolveExternalRouteAsync(ct).ConfigureAwait(false);
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
        var route = await ResolveExternalRouteAsync(ct).ConfigureAwait(false);
        if (route is null)
            return await _local.TranscribeAsync(path, request, progress, ct).ConfigureAwait(false);

        return await QwenAudioFileTranscriber.TranscribeAsync(
            path, route, request, _wsTransport, _http, _usage, progress, ct).ConfigureAwait(false);
    }

    private async Task<SpeechCloudRoute?> ResolveExternalRouteAsync(CancellationToken ct)
    {
        var cloud = await _routes.GetSpeechAsync(ct).ConfigureAwait(false);
        if (cloud is not null)
            return cloud;

        if (_localHostRoutes is not null && await _localHostRoutes.GetSpeechAsync(ct).ConfigureAwait(false) is { } localHost)
            return SpeechCloudRoute.CreateForLocalHost(localHost);

        return null;
    }

    public ValueTask DisposeAsync() => _local.DisposeAsync();
}
