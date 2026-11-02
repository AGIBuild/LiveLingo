using System.Diagnostics;
using System.Threading.Channels;

namespace Dictation.Core;

public sealed class SessionOptions
{
    /// <summary>How long the last sentence waits for following speech before it is cleaned anyway.</summary>
    public int CleanupHoldbackMs { get; init; } = 2000;

    /// <summary>
    /// Silence after speech that ends an utterance and therefore a sentence, and resets the live stream.
    /// Shorter pauses (a comma, a breath) never reset the stream: a fresh stream starting mid-sentence recognizes badly.
    /// </summary>
    public int HardPauseMs { get; init; } = 800;

    /// <summary>An utterance this long is cut at the next short pause instead of waiting for a hard pause.</summary>
    public int SoftMaxUtteranceMs { get; init; } = 12_000;

    /// <summary>A tail this long becomes a sentence at the next segment even without sentence punctuation.</summary>
    public int MaxTailChars { get; init; } = 90;

    public int CleanupContextSentences { get; init; } = 2;

    public int CommitTimeoutMs { get; init; } = 15000;

    /// <summary>
    /// The live text appears this long after the speech it recognizes. Audio this far before the first text is kept for the second pass.
    /// </summary>
    public int SpeechOnsetMarginMs { get; init; } = 3000;

    /// <summary>Audio kept after the last speech evidence (detector span end or last live text change) for the second pass.</summary>
    public int SpeechOffsetMarginMs { get; init; } = 300;

    /// <summary>Audio kept around a segment when it is re-decoded by the second pass.</summary>
    public int SecondPassPaddingMs { get; init; } = 200;

    /// <summary>
    /// A second-pass result with more characters per second of audio than anyone speaks is a decoder repetition loop,
    /// not speech. Such a result is dropped and the live text is kept. Pure English letters reach about 15 per second.
    /// </summary>
    public double SecondPassMaxCharsPerSecond { get; init; } = 15;

    public IReadOnlyList<string> Glossary { get; init; } = [];
}

/// <summary>
/// Turns a continuous audio stream into a draft:
/// live text while speaking, segments closed at pauses, optional second-pass settling,
/// sentence-by-sentence cleanup that runs while the user is still speaking, and a single exactly-once commit.
/// All shared state is guarded by one lock; the four workers (recognition, second pass, cleanup, commit) never block each other on model calls.
/// </summary>
public sealed class DictationSession : IAsyncDisposable
{
    private readonly ILiveRecognizer _live;
    private readonly IVoiceActivityDetector _vad;
    private readonly ISegmentRecognizer? _secondPass;
    private readonly ITextCleaner? _cleaner;
    private readonly SessionOptions _options;

    private readonly AudioBuffer _audio = new();
    private readonly Channel<float[]> _frames = Channel.CreateUnbounded<float[]>(new() { SingleReader = true });
    private readonly Channel<(Segment Segment, float[] Audio)> _secondPassJobs =
        Channel.CreateUnbounded<(Segment, float[])>(new() { SingleReader = true });
    private readonly SemaphoreSlim _wake = new(0);
    private readonly TaskCompletionSource _cleanupDrained = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource _stop = new();
    private readonly Stopwatch _clock = new();
    private readonly object _gate = new();

    private readonly List<Segment> _segments = new();
    private readonly List<Sentence> _sentences = new();
    private readonly List<BacklogSample> _backlog = new();
    private string _tail = "";
    private int _nextToSettle;
    private bool _committing;
    private bool _recognitionDone;
    private long _queuedSamples;
    private double _lastBacklogSampleMs = -1000;
    private long _streamStartSample;
    private long _openFirstTextSample = -1;
    private long _openLastTextSample = -1;
    private readonly List<SpeechSpan> _pending = new();

    private string _openText = "";
    private double _openFirstTextMs = -1;
    private int _openRevisions;
    private int _openRetractedChars;

    private Task _recognitionTask = Task.CompletedTask;
    private Task _secondPassTask = Task.CompletedTask;
    private Task _cleanupTask = Task.CompletedTask;
    private Task<CommitResult>? _commitTask;
    private CommitResult? _commit;

    public DictationSession(
        ILiveRecognizer live,
        IVoiceActivityDetector vad,
        ISegmentRecognizer? secondPass,
        ITextCleaner? cleaner,
        SessionOptions? options = null)
    {
        _live = live;
        _vad = vad;
        _secondPass = secondPass;
        _cleaner = cleaner;
        _options = options ?? new SessionOptions();
    }

    public event Action<DraftSnapshot>? Changed;

    /// <summary>Milliseconds since <see cref="Start"/>; the same clock stamps every record.</summary>
    public double NowMs => _clock.Elapsed.TotalMilliseconds;

    public void Start()
    {
        _clock.Start();
        _recognitionTask = Task.Run(RecognitionLoopAsync);
        _secondPassTask = Task.Run(SecondPassLoopAsync);
        _cleanupTask = Task.Run(() => CleanupLoopAsync(_stop.Token));
    }

    /// <summary>Queues captured audio. Never blocks on recognition, so capture is never starved.</summary>
    public void Push(float[] frame)
    {
        Interlocked.Add(ref _queuedSamples, frame.Length);
        if (!_frames.Writer.TryWrite(frame))
            throw new InvalidOperationException("Audio pushed after commit.");
    }

    /// <summary>Replaces a sentence with user text. Background results can no longer change it.</summary>
    public void EditSentence(int sentenceId, string text)
    {
        lock (_gate)
        {
            var sentence = _sentences.Single(s => s.Id == sentenceId);
            sentence.UserText = text;
            sentence.State = SentenceState.UserOwned;
        }
        Notify();
    }

    public DraftSnapshot Snapshot()
    {
        lock (_gate) return BuildSnapshot();
    }

    public SessionReport Report()
    {
        lock (_gate)
            return new SessionReport([.. _segments], [.. _sentences], [.. _backlog], _commit);
    }

    /// <summary>
    /// Ends the audio, finishes the unfinished tail and returns the final text. Calling it again returns the same result,
    /// so a repeated key press can never deliver the text twice.
    /// </summary>
    public Task<CommitResult> CommitAsync()
    {
        lock (_gate) return _commitTask ??= Task.Run(CommitCoreAsync);
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        _wake.Release();
        try { await _cleanupTask; } catch (OperationCanceledException) { }
        _stop.Dispose();
    }

    // ---- recognition worker -------------------------------------------------

    private async Task RecognitionLoopAsync()
    {
        try
        {
            var reader = _frames.Reader;
            while (await reader.WaitToReadAsync())
            {
                while (reader.TryRead(out var frame))
                {
                    Interlocked.Add(ref _queuedSamples, -frame.Length);
                    _audio.Append(frame);
                    _live.Accept(frame);
                    _vad.Accept(frame);
                    OnLiveText(TextTidy.Apply(_live.CurrentText));
                    while (_vad.TryTake(out var span)) _pending.Add(span);
                    CloseUtteranceIfEnded();
                    SampleBacklog();
                }
            }

            _vad.Flush();
            while (_vad.TryTake(out var span)) _pending.Add(span);
            // Text the detector did not attribute to any speech span is closed too, so it is never lost.
            if (_pending.Count > 0 || _openText.Length > 0)
                CloseUtterance(endsSentence: true);
        }
        finally
        {
            lock (_gate) _recognitionDone = true;
        }
    }

    /// <summary>
    /// Short pauses only collect speech spans. A hard pause ends the utterance; so does a span that makes the
    /// utterance too long, because that span was reported at a short pause and is a natural place to cut.
    /// </summary>
    private void CloseUtteranceIfEnded()
    {
        if (_pending.Count == 0) return;
        var first = _pending[0];
        var last = _pending[^1];
        var silenceMs = AudioFormat.SamplesToMs(_audio.TotalSamples - last.EndSample);
        var lengthMs = AudioFormat.SamplesToMs(last.EndSample - first.StartSample);

        var hard = !_vad.IsSpeechActive && silenceMs >= _options.HardPauseMs;
        if (hard || lengthMs >= _options.SoftMaxUtteranceMs)
            CloseUtterance(endsSentence: hard);
    }

    private void OnLiveText(string text)
    {
        lock (_gate)
        {
            if (text == _openText) return;
            var common = CommonPrefixLength(_openText, text);
            if (common < _openText.Length)
            {
                _openRevisions++;
                _openRetractedChars += _openText.Length - common;
            }
            if (_openFirstTextMs < 0 && text.Length > 0)
            {
                _openFirstTextMs = NowMs;
                _openFirstTextSample = _audio.TotalSamples;
            }
            _openText = text;
            _openLastTextSample = _audio.TotalSamples;
        }
        Notify();
    }

    /// <summary>
    /// Closes the live stream and queues the second pass over the same audio the stream consumed. The detector's spans only
    /// decide where to cut: they miss quiet or far-field speech, and a second pass limited to them would drop words the live
    /// stream recognized. Leading silence is trimmed to the first evidence of speech, from either the detector or the text.
    /// </summary>
    private void CloseUtterance(bool endsSentence)
    {
        var previousCut = _streamStartSample;
        var evidence = long.MaxValue;
        var lastEvidence = 0L;
        if (_pending.Count > 0)
        {
            evidence = _pending[0].StartSample;
            lastEvidence = _pending[^1].EndSample + AudioFormat.MsToSamples(_options.SpeechOffsetMarginMs);
        }
        if (_openFirstTextSample >= 0)
            evidence = Math.Min(evidence, _openFirstTextSample - AudioFormat.MsToSamples(_options.SpeechOnsetMarginMs));
        lastEvidence = Math.Max(lastEvidence, _openLastTextSample);

        // Trailing silence is trimmed: a recognizer decoding it tends to invent words. Without any evidence nothing is trimmed.
        var end = lastEvidence > 0 ? Math.Min(_audio.TotalSamples, lastEvidence) : _audio.TotalSamples;
        var audio = new SpeechSpan(Math.Max(_streamStartSample, evidence == long.MaxValue ? _streamStartSample : evidence), end);
        var speech = _pending.Count > 0 ? new SpeechSpan(_pending[0].StartSample, _pending[^1].EndSample) : audio;

        _pending.Clear();
        var finalText = TextTidy.Apply(_live.CloseSegment());
        Segment segment;
        var needsSecondPass = false;
        lock (_gate)
        {
            segment = new Segment
            {
                Id = _segments.Count,
                Speech = speech,
                Audio = audio,
                EndsSentence = endsSentence,
                LiveFinal = finalText,
                ClosedMs = NowMs,
                FirstTextMs = _openFirstTextMs,
                LiveRevisions = _openRevisions,
                LiveRetractedChars = _openRetractedChars,
            };
            _segments.Add(segment);
            _openText = "";
            _openFirstTextMs = -1;
            _openFirstTextSample = -1;
            _openLastTextSample = -1;
            _openRevisions = 0;
            _openRetractedChars = 0;
            _streamStartSample = end;

            needsSecondPass = _secondPass is not null && !string.IsNullOrWhiteSpace(finalText);
            if (!needsSecondPass)
            {
                segment.SettledText = finalText;
                segment.SettledMs = NowMs;
            }
        }

        if (needsSecondPass)
        {
            var pad = AudioFormat.MsToSamples(_options.SecondPassPaddingMs);
            var samples = _audio.Slice(Math.Max(previousCut, audio.StartSample - pad), audio.EndSample + pad);
            _secondPassJobs.Writer.TryWrite((segment, samples));
        }
        else
        {
            AdvanceSettled();
        }
        Notify();
    }

    // ---- second pass worker -------------------------------------------------

    private async Task SecondPassLoopAsync()
    {
        await foreach (var (segment, audio) in _secondPassJobs.Reader.ReadAllAsync())
        {
            var started = NowMs;
            string text;
            var failed = false;
            try
            {
                text = TextTidy.Apply(_secondPass!.Recognize(audio));
                var seconds = audio.Length / (double)AudioFormat.SampleRate;
                if (string.IsNullOrWhiteSpace(text) || text.Count(char.IsLetterOrDigit) > _options.SecondPassMaxCharsPerSecond * seconds + 10)
                {
                    text = segment.LiveFinal;
                    failed = true;
                }
            }
            catch (Exception)
            {
                text = segment.LiveFinal;
                failed = true;
            }

            lock (_gate)
            {
                segment.SettledText = text.Trim();
                segment.SettledMs = NowMs;
                segment.SecondPassDurationMs = segment.SettledMs - started;
                segment.SecondPassFailed = failed;
            }
            AdvanceSettled();
            Notify();
        }
    }

    // ---- settling and sentence building ------------------------------------

    /// <summary>Segments settle strictly in order, so sentences are append-only.</summary>
    private void AdvanceSettled()
    {
        lock (_gate)
        {
            while (_nextToSettle < _segments.Count && _segments[_nextToSettle].SettledText is { } text)
            {
                var segment = _segments[_nextToSettle++];
                var (sentences, remainder) = SentenceSplitter.Split(SentenceSplitter.Join(_tail, text));
                foreach (var raw in sentences) AddSentence(raw, segment.Id);
                _tail = remainder;
                if (segment.EndsSentence || _tail.Length >= _options.MaxTailChars) FlushTail(segment.Id);
            }
        }
        _wake.Release();
    }

    private void AddSentence(string raw, int endSegmentId)
    {
        _sentences.Add(new Sentence
        {
            Id = _sentences.Count,
            Raw = raw,
            EndSegmentId = endSegmentId,
            FormedMs = NowMs,
            State = _cleaner is null ? SentenceState.Raw : SentenceState.Pending,
        });
    }

    private void FlushTail(int endSegmentId)
    {
        if (_tail.Length == 0) return;
        AddSentence(_tail, endSegmentId);
        _tail = "";
    }

    // ---- cleanup worker -----------------------------------------------------

    private async Task CleanupLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            Sentence? target = null;
            CleanupRequest? request = null;
            lock (_gate)
            {
                target = PickEligible();
                if (target is not null)
                {
                    target.State = SentenceState.InFlight;
                    request = BuildRequest(target);
                }
                else if (_committing && _recognitionDone && !HasUnfinishedCleanup())
                {
                    _cleanupDrained.TrySetResult();
                }
            }

            if (target is null || request is null)
            {
                try { await _wake.WaitAsync(100, ct); } catch (OperationCanceledException) { return; }
                continue;
            }

            var started = NowMs;
            string? cleaned = null;
            string? error = null;
            try { cleaned = await _cleaner!.CleanAsync(request, ct); }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) { error = ex.Message; }

            lock (_gate)
            {
                target.CleanupDurationMs = NowMs - started;
                if (target.UserText is null)
                {
                    var gate = error is null ? FidelityGate.Check(target.Raw, cleaned!, _options.Glossary) : new GateResult(false, error);
                    target.CleanedMs = NowMs;
                    if (gate.Accepted)
                    {
                        target.Cleaned = cleaned!.Trim();
                        target.State = SentenceState.Cleaned;
                    }
                    else
                    {
                        target.RejectReason = gate.Reason;
                        target.RejectedText = cleaned;
                        target.State = SentenceState.Rejected;
                    }
                }
            }
            Notify();
        }
    }

    private Sentence? PickEligible()
    {
        foreach (var sentence in _sentences)
        {
            if (sentence.State != SentenceState.Pending) continue;
            // Order is preserved: if the oldest pending sentence is held back, nothing behind it can be eligible.
            return IsEligible(sentence) ? sentence : null;
        }
        return null;
    }

    /// <summary>
    /// A sentence is cleaned once something follows it, because a pause may split one sentence in two.
    /// The last sentence waits at most the hold-back time.
    /// </summary>
    private bool IsEligible(Sentence sentence) =>
        _committing || HasFollowing(sentence) || NowMs - sentence.FormedMs >= _options.CleanupHoldbackMs;

    /// <summary>A sentence waits for what follows it, because a long thinking pause can split one sentence in two.</summary>
    private bool HasFollowing(Sentence sentence)
    {
        if (sentence.Id < _sentences.Count - 1) return true;
        if (_tail.Length > 0 || _openText.Length > 0) return true;
        for (var i = _nextToSettle; i < _segments.Count; i++)
            if (_segments[i].LiveFinal.Length > 0) return true;
        return false;
    }

    private bool HasUnfinishedCleanup() =>
        _sentences.Any(s => s.State is SentenceState.Pending or SentenceState.InFlight);

    private CleanupRequest BuildRequest(Sentence target)
    {
        var preceding = "";
        var from = Math.Max(0, target.Id - _options.CleanupContextSentences);
        for (var i = from; i < target.Id; i++)
            preceding = SentenceSplitter.Join(preceding, _sentences[i].Best);

        return new CleanupRequest(target.Raw, preceding, HasFollowing(target), _options.Glossary);
    }

    // ---- commit -------------------------------------------------------------

    private async Task<CommitResult> CommitCoreAsync()
    {
        var started = NowMs;
        var timedOut = false;
        var deadline = started + _options.CommitTimeoutMs;

        _frames.Writer.Complete();
        timedOut |= !await WaitAsync(_recognitionTask, deadline);
        _secondPassJobs.Writer.Complete();
        timedOut |= !await WaitAsync(_secondPassTask, deadline);

        lock (_gate)
        {
            // Anything a timed-out second pass left unsettled falls back to its live text.
            foreach (var segment in _segments.Where(s => s.SettledText is null))
            {
                segment.SettledText = segment.LiveFinal;
                segment.SettledMs = NowMs;
                segment.SecondPassFailed = true;
            }
            _committing = true;
        }
        AdvanceSettled();
        lock (_gate) FlushTail(_segments.Count - 1);
        _wake.Release();

        timedOut |= !await WaitAsync(_cleanupDrained.Task, deadline);

        CommitResult result;
        lock (_gate)
        {
            foreach (var s in _sentences.Where(s => s.State is SentenceState.Pending or SentenceState.InFlight))
                s.State = SentenceState.Raw;

            var text = "";
            var raw = "";
            foreach (var s in _sentences)
            {
                text = SentenceSplitter.Join(text, s.Best);
                raw = SentenceSplitter.Join(raw, s.Raw);
            }
            var live = "";
            foreach (var segment in _segments) live = SentenceSplitter.Join(live, segment.LiveFinal);

            result = new CommitResult(
                text, raw, live, _sentences.Count,
                _sentences.Count(s => s.State == SentenceState.Cleaned),
                _sentences.Count(s => s.State == SentenceState.Rejected),
                _sentences.Count(s => s.State is SentenceState.Raw),
                NowMs - started, timedOut);
            _commit = result;
        }
        _stop.Cancel();
        Notify();
        return result;
    }

    private async Task<bool> WaitAsync(Task task, double deadlineMs)
    {
        var remaining = Math.Max(0, deadlineMs - NowMs);
        if (await Task.WhenAny(task, Task.Delay(TimeSpan.FromMilliseconds(remaining))) != task)
            return false;
        await task; // surfaces a worker failure instead of waiting out the timeout
        return true;
    }

    // ---- helpers ------------------------------------------------------------

    private void SampleBacklog()
    {
        var now = NowMs;
        if (now - _lastBacklogSampleMs < 1000) return;
        _lastBacklogSampleMs = now;
        var backlogMs = AudioFormat.SamplesToMs(Interlocked.Read(ref _queuedSamples));
        lock (_gate) _backlog.Add(new BacklogSample(now, backlogMs));
    }

    private DraftSnapshot BuildSnapshot()
    {
        var unsettled = "";
        for (var i = _nextToSettle; i < _segments.Count; i++)
            unsettled = SentenceSplitter.Join(unsettled, _segments[i].SettledText ?? _segments[i].LiveFinal);

        return new DraftSnapshot(
            _sentences.Select(s => new SentenceView(s.Id, s.Best, s.State)).ToList(),
            _tail, unsettled, _openText);
    }

    private void Notify()
    {
        var handler = Changed;
        if (handler is null) return;
        handler(Snapshot());
    }

    private static int CommonPrefixLength(string a, string b)
    {
        var n = Math.Min(a.Length, b.Length);
        var i = 0;
        while (i < n && a[i] == b[i]) i++;
        return i;
    }
}
