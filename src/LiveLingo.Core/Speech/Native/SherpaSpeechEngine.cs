using LiveLingo.Core.Dictation;
using LiveLingo.Core.Drafting;
using LiveLingo.Core.Models;
using LiveLingo.Core.Inference;
using LiveLingo.Core.Transcription;
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
    /// <summary>The detector reports a span after this much silence; shorter than the hard pause so pauses are seen early.</summary>
    private const float LiveVadMinSilenceSeconds = 0.3f;

    /// <summary>A recording is cut at shorter pauses than live dictation so subtitles stay short.</summary>
    private const float FileVadMinSilenceSeconds = 0.5f;

    private readonly IModelManager _models;
    private readonly ResidentModel<OnlineRecognizer> _live;
    private readonly ResidentModel<OfflineRecognizer> _secondPass;
    private readonly ModelUsage? _usage;

    public SherpaSpeechEngine(IModelManager models, CoreOptions options, ILogger<SherpaSpeechEngine> logger, ModelUsage? usage = null)
    {
        _models = models;
        _usage = usage;
        _live = new ResidentModel<OnlineRecognizer>(
            "live speech recognizer",
            _ => SherpaLiveRecognizer.Load(models.GetModelDirectory(ModelCatalog.SpeechLive.Id)),
            options.SpeechIdleTimeout, logger: logger);
        _secondPass = new ResidentModel<OfflineRecognizer>(
            "second-pass speech recognizer",
            hotwords => SherpaQwen3Recognizer.Load(models.GetModelDirectory(ModelCatalog.SpeechSecondPass.Id), hotwords),
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
        EnsureInstalled(ModelCatalog.Speech);
        await Task.WhenAll(_live.WarmAsync("", ct), _secondPass.WarmAsync(Hotwords(glossary), ct)).ConfigureAwait(false);
    }

    public async Task<DictationSession> CreateAsync(DraftSession draft, IReadOnlyList<string> glossary, CancellationToken ct)
    {
        var names = $"{ModelCatalog.SpeechLive.DisplayName} + {ModelCatalog.SpeechSecondPass.DisplayName}";
        var shortNames = $"{ModelCatalog.SpeechLive.ShortName} + {ModelCatalog.SpeechSecondPass.ShortName}";
        _usage?.Report(new ModelUse(ModelStage.Speech, names, Cloud: false, Confirmed: false, shortNames));
        await WarmAsync(glossary, ct).ConfigureAwait(false);
        var live = await _live.AcquireAsync("", ct).ConfigureAwait(false);
        ResidentModel<OfflineRecognizer>.Lease secondPass;
        try
        {
            secondPass = await _secondPass.AcquireAsync(Hotwords(glossary), ct).ConfigureAwait(false);
        }
        catch
        {
            live.Dispose();
            throw;
        }

        var liveRecognizer = new SherpaLiveRecognizer(live);
        var secondPassRecognizer = new SherpaQwen3Recognizer(secondPass);
        try
        {
            var session = new DictationSession(liveRecognizer, NewVad(LiveVadMinSilenceSeconds), secondPassRecognizer, draft);
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

    public async Task<IReadOnlyList<TimedSegment>> TranscribeAsync(
        string path, IReadOnlyList<string> glossary, IProgress<double>? progress, CancellationToken ct)
    {
        EnsureInstalled(ModelCatalog.Transcription);
        var samples = await Task.Run(() => WavReader.ReadFile(path), ct).ConfigureAwait(false);
        using var recognizer = new SherpaQwen3Recognizer(await _secondPass.AcquireAsync(Hotwords(glossary), ct).ConfigureAwait(false));
        return await Task.Run(() =>
        {
            using var vad = NewVad(FileVadMinSilenceSeconds);
            return FileTranscriber.Transcribe(samples, vad, recognizer, progress, ct);
        }, ct).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await _live.DisposeAsync().ConfigureAwait(false);
        await _secondPass.DisposeAsync().ConfigureAwait(false);
    }

    private SherpaSileroVad NewVad(float minSilenceSeconds) =>
        new(Path.Combine(_models.GetModelDirectory(ModelCatalog.SpeechVad.Id), "silero_vad.onnx"), minSilenceSeconds);

    private static string Hotwords(IReadOnlyList<string> glossary) => string.Join("\n", glossary);

    private void EnsureInstalled(IEnumerable<ModelDescriptor> required)
    {
        foreach (var model in required)
        {
            if (!_models.HasAllExpectedLocalAssets(model))
                throw new ModelNotReadyException(model.Type, model.Id, $"'{model.DisplayName}' is not installed.", "Download it first.");
        }
    }
}
