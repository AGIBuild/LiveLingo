using Dictation.Audio;
using Dictation.Core;
using Dictation.Models;

namespace Dictation.TestApp;

public sealed record BenchDraft(string Raw, string Cleaned);

public sealed record TakeOutcome(TakeRecord? Record, CommitResult Commit);

/// <summary>
/// One place that owns the models, the microphone and the saved take. The window only displays what it reports.
/// The cleaner is loaded once and the second-pass recognizer whenever the user selects another one; the live recognizer and VAD
/// hold per-take state and are created per take. Neither can change while a take is running.
/// </summary>
public sealed class TestBench : IDisposable
{
    private RecordingStore _store;
    private readonly MicrophoneRecorder _mic = new();

    private ISegmentRecognizer? _secondPass;
    private ITextCleaner? _cleaner;

    private ILiveRecognizer? _live;
    private IVoiceActivityDetector? _vad;
    private DictationSession? _session;
    private (string Id, WavWriter Writer)? _audio;
    private string _microphone = "";
    private IReadOnlyList<string> _glossary = [];
    private DateTime _startedAt;
    private volatile float _peak;

    public TestBench(RecordingStore store) => _store = store;

    public event Action<BenchDraft>? DraftChanged;
    public event Action<float>? Level;

    public bool IsRecording => _mic.IsRecording;

    /// <summary>Largest sample magnitude of the current take. Stays 0 when the OS delivers silence, for example without microphone permission.</summary>
    public float Peak => _peak;

    public RecordingStore Store => _store;

    public string SecondPassKey { get; private set; } = "";

    /// <summary>Where the next takes are saved. Existing takes stay where they are.</summary>
    public void UseRecordingsDirectory(string directory) => _store = new RecordingStore(directory);

    public Task LoadModelsAsync(string secondPassKey) => Task.Run(() =>
    {
        _cleaner = ModelCatalog.Cleaner("qwen35-4b");
        LoadSecondPass(secondPassKey);
    });

    public Task SelectSecondPassAsync(string key) => Task.Run(() => LoadSecondPass(key));

    private void LoadSecondPass(string key)
    {
        var next = ModelCatalog.SecondPass(key);
        _secondPass?.Dispose();
        _secondPass = next;
        SecondPassKey = key;
    }

    /// <summary>Starts a take. Audio is written to disk while it is captured when <paramref name="saveAudio"/> is set.</summary>
    public void StartTake(IReadOnlyList<string> glossary, bool saveAudio)
    {
        _glossary = glossary;
        _peak = 0;
        _startedAt = DateTime.Now;
        _audio = saveAudio ? _store.Begin(_startedAt) : null;

        _live = ModelCatalog.Live("xasr-480");
        _vad = ModelCatalog.Vad(0.25f);
        _session = new DictationSession(_live, _vad, _secondPass, _cleaner, new SessionOptions { Glossary = glossary });
        _session.Changed += OnChanged;
        _session.Start();

        _microphone = _mic.Start(OnFrame);
    }

    public async Task<TakeOutcome> StopTakeAsync()
    {
        var session = _session ?? throw new InvalidOperationException("No take in progress.");
        _mic.Stop();

        var commit = await session.CommitAsync();
        var report = session.Report();

        TakeRecord? record = null;
        if (_audio is { } audio)
        {
            record = new TakeRecord(
                audio.Id, _startedAt, (int)(audio.Writer.Samples * 1000 / AudioFormat.SampleRate), _microphone, SecondPassKey, _glossary,
                commit.LiveText, commit.RawText, commit.Text, commit.WaitMs,
                [.. report.Sentences.Select(s => new SentenceRecord(s.Raw, s.Cleaned, s.State.ToString(), s.RejectReason, s.RejectedText))],
                Reference: "");
            audio.Writer.Dispose();
            _store.Save(record);
        }

        _session.Changed -= OnChanged;
        await session.DisposeAsync();
        _live!.Dispose();
        _vad!.Dispose();
        _session = null;
        _audio = null;
        return new TakeOutcome(record, commit);
    }

    private void OnFrame(float[] frame)
    {
        var peak = 0f;
        foreach (var s in frame) peak = Math.Max(peak, Math.Abs(s));
        _peak = Math.Max(_peak, peak);

        _audio?.Writer.Append(frame);
        _session!.Push(frame);
        Level?.Invoke(peak);
    }

    private void OnChanged(DraftSnapshot snapshot)
    {
        var raw = "";
        foreach (var s in _session!.Report().Sentences) raw = SentenceSplitter.Join(raw, s.Raw);
        raw = SentenceSplitter.Join(raw, snapshot.Tail);
        raw = SentenceSplitter.Join(raw, snapshot.Unsettled);
        raw = SentenceSplitter.Join(raw, snapshot.Live);
        DraftChanged?.Invoke(new BenchDraft(raw, snapshot.FullText));
    }

    public void Dispose()
    {
        _mic.Dispose();
        (_cleaner as IDisposable)?.Dispose();
        _secondPass?.Dispose();
    }
}
