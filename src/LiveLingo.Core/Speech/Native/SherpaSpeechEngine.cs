using LiveLingo.Core.Dictation;
using LiveLingo.Core.Drafting;
using LiveLingo.Core.Models;
using LiveLingo.Core.Inference;
using LiveLingo.Core.Transcription;
using LiveLingo.Core.Translation;
using Microsoft.Extensions.Logging;
using SherpaOnnx;

namespace LiveLingo.Core.Speech.Native;

/// <summary>
/// The one owner of the speech models: X-ASR 480 ms live text, Silero VAD, Qwen3-ASR second pass. The two large models
/// stay in memory between dictations (<see cref="ISpeechWarmup"/>) and are lent to a session or a file transcription;
/// the small detector is made for each use.
/// </summary>
public sealed class SherpaSpeechEngine : IDictationSessionFactory, ISpeechWarmup, IAudioFileTranscriber, IAsyncDisposable
{
    /// <summary>
    /// The detector reports a span after this much silence; tuned (~240 ms, matching the 250 ms POC baseline) so natural
    /// breath pauses during fast continuous speech register clean soft-cut points without resetting the live stream early.
    /// </summary>
    private const float LiveVadMinSilenceSeconds = 0.24f;

    /// <summary>A recording is cut at shorter pauses than live dictation so subtitles stay short.</summary>
    private const float FileVadMinSilenceSeconds = 0.5f;

    private readonly IModelManager _models;
    private readonly CoreOptions _options;
    private readonly ILogger<SherpaSpeechEngine> _logger;
    private readonly ResidentModel<OnlineRecognizer> _live;
    private readonly ResidentModel<OfflineRecognizer> _secondPass;
    private readonly ModelUsage? _usage;

    public SherpaSpeechEngine(IModelManager models, CoreOptions options, ILogger<SherpaSpeechEngine> logger, ModelUsage? usage = null)
    {
        _models = models;
        _options = options;
        _logger = logger;
        _usage = usage;
        _live = new ResidentModel<OnlineRecognizer>(
            "live speech recognizer",
            _ => SherpaLiveRecognizer.Load(models.GetModelDirectory(ModelCatalog.SpeechLive.Id)),
            options.SpeechIdleTimeout, logger: logger);
        _secondPass = new ResidentModel<OfflineRecognizer>(
            "second-pass speech recognizer",
            LoadSecondPass,
            options.SpeechIdleTimeout, logger: logger);
        _live.StateChanged += _ => Publish();
        _secondPass.StateChanged += _ => Publish();
    }

    private readonly object _publishGate = new();
    private ModelResidency _published;

    /// <summary>The state of the two models together; raised only when it differs from the one last raised.</summary>
    private void Publish()
    {
        ModelResidency state;
        lock (_publishGate)
        {
            state = State;
            if (state == _published)
                return;
            _published = state;
        }

        StateChanged?.Invoke(state);
    }

    public ModelResidency State
    {
        get
        {
            var states = new[] { _live.State, _secondPass.State };
            return states.Contains(ModelResidency.Loading) ? ModelResidency.Loading
                : states.All(state => state == ModelResidency.Ready) ? ModelResidency.Ready
                : ModelResidency.Unloaded;
        }
    }

    public event Action<ModelResidency>? StateChanged;

    public async Task WarmAsync(IReadOnlyList<string> glossary, CancellationToken ct = default)
    {
        var selected = _options.Models.Speech;
        var strategy = SpeechOptimizationStrategies.ForLocal(selected.Id);
        EnsureInstalled(ModelCatalog.SpeechFor(selected));
        var key = ResidencyKey(strategy, SpeechSessionRequest.FromHotwords(glossary));
        await Task.WhenAll(_live.WarmAsync("", ct), _secondPass.WarmAsync(key, ct)).ConfigureAwait(false);
    }

    public Task<IDictationSession> CreateAsync(DraftSession draft, IReadOnlyList<string> glossary, CancellationToken ct) =>
        CreateAsync(draft, SpeechSessionRequest.FromHotwords(glossary), ct);

    public async Task<IDictationSession> CreateAsync(DraftSession draft, SpeechSessionRequest request, CancellationToken ct)
    {
        var selected = _options.Models.Speech;
        var strategy = SpeechOptimizationStrategies.ForLocal(selected.Id);
        var names = $"{ModelCatalog.SpeechLive.DisplayName} + {selected.DisplayName}";
        var shortNames = $"{ModelCatalog.SpeechLive.ShortName} + {selected.ShortName}";
        _usage?.Report(new ModelUse(ModelStage.Speech, names, Cloud: false, Confirmed: false, shortNames));
        EnsureInstalled(ModelCatalog.SpeechFor(selected));
        var key = ResidencyKey(strategy, request);
        await Task.WhenAll(_live.WarmAsync("", ct), _secondPass.WarmAsync(key, ct)).ConfigureAwait(false);
        var live = await _live.AcquireAsync("", ct).ConfigureAwait(false);
        ResidentModel<OfflineRecognizer>.Lease secondPass;
        try
        {
            secondPass = await _secondPass.AcquireAsync(key, ct).ConfigureAwait(false);
        }
        catch
        {
            live.Dispose();
            throw;
        }

        var liveRecognizer = new SherpaLiveRecognizer(live);
        var secondPassRecognizer = new SherpaQwen3Recognizer(secondPass, strategy.Profile);
        try
        {
            var session = new DictationSession(
                liveRecognizer,
                NewVad(LiveVadMinSilenceSeconds),
                secondPassRecognizer,
                draft,
                strategy.Profile.Dictation,
                _logger);
            _usage?.Report(new ModelUse(ModelStage.Speech, names, Cloud: false, Confirmed: true, shortNames));
            return session;
        }
        catch
        {
            liveRecognizer.Dispose();
            secondPassRecognizer.Dispose();
            throw;
        }
    }

    public Task<IReadOnlyList<TimedSegment>> TranscribeAsync(
        string path, IReadOnlyList<string> glossary, IProgress<double>? progress, CancellationToken ct) =>
        TranscribeAsync(path, SpeechSessionRequest.FromHotwords(glossary), progress, ct);

    public async Task<IReadOnlyList<TimedSegment>> TranscribeAsync(
        string path, SpeechSessionRequest request, IProgress<double>? progress, CancellationToken ct)
    {
        var selected = _options.Models.Speech;
        var strategy = SpeechOptimizationStrategies.ForLocal(selected.Id);
        EnsureInstalled(ModelCatalog.TranscriptionFor(selected));
        var samples = await Task.Run(() => WavReader.ReadFile(path), ct).ConfigureAwait(false);
        var key = ResidencyKey(strategy, request);
        using var recognizer = new SherpaQwen3Recognizer(
            await _secondPass.AcquireAsync(key, ct).ConfigureAwait(false),
            strategy.Profile);
        var maxSpanSamples = Math.Max(AudioFormat.SampleRate, (strategy.Profile.MaxSegmentSeconds - 1) * AudioFormat.SampleRate);
        return await Task.Run(() =>
        {
            using var vad = NewVad(FileVadMinSilenceSeconds);
            return FileTranscriber.Transcribe(samples, vad, recognizer, progress, ct, maxSpanSamples);
        }, ct).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await _live.DisposeAsync().ConfigureAwait(false);
        await _secondPass.DisposeAsync().ConfigureAwait(false);
    }

    private OfflineRecognizer LoadSecondPass(string residencyKey)
    {
        var parts = residencyKey.Split('\n', 3);
        var modelId = parts[0];
        var threads = parts.Length >= 2 && int.TryParse(parts[1], out var parsedThreads) ? parsedThreads : 4;
        var hotwords = parts.Length >= 3 ? parts[2] : "";
        return SpeechOptimizationStrategies.ForLocal(modelId).Load(_models.GetModelDirectory(modelId), hotwords, threads);
    }

    private string ResidencyKey(ILocalSecondPassStrategy strategy, SpeechSessionRequest request)
    {
        var threads = strategy.ResolveThreads(_options.InferenceThreads, Environment.ProcessorCount);
        if (!strategy.Profile.SupportsHotwords)
            return $"{strategy.Id}\n{threads}";

        var hotwords = string.Join(", ", strategy.SelectPromptHotwords(request.Glossary, request.Hotwords));
        return $"{strategy.Id}\n{threads}\n{hotwords}";
    }

    private SherpaSileroVad NewVad(float minSilenceSeconds) =>
        new(Path.Combine(_models.GetModelDirectory(ModelCatalog.SpeechVad.Id), "silero_vad.onnx"), minSilenceSeconds);

    private void EnsureInstalled(IEnumerable<ModelDescriptor> required)
    {
        foreach (var model in required)
        {
            if (!_models.HasAllExpectedLocalAssets(model))
                throw new ModelNotReadyException(model.Type, model.Id, $"'{model.DisplayName}' is not installed.", "Download it first.");
        }
    }
}
