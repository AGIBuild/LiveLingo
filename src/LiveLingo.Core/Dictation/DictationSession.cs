using System.Diagnostics;
using System.Threading.Channels;
using LiveLingo.Core.Drafting;

namespace LiveLingo.Core.Dictation;

/// <summary>
/// Turns a continuous audio stream into the units of a <see cref="DraftSession"/>: live text while speaking
/// (<see cref="Draft.Pending"/>), segments closed at pauses, optional second-pass settling, and sentences appended in
/// speech order. Cleanup and translation are separate schedulers that observe the same draft.
/// Capture is never blocked by recognition: <see cref="Push"/> only queues the frame.
/// </summary>
public sealed class DictationSession : IAsyncDisposable
{
    private sealed class Segment
    {
        public int Id { get; init; }
        public SpeechSpan Speech { get; init; }
        public SpeechSpan Audio { get; init; }
        public bool EndsSentence { get; init; }
        public string LiveFinal { get; init; } = "";
        public string? SettledText { get; set; }
        public bool SecondPassFailed { get; set; }
    }

    private readonly ILiveRecognizer _live;
    private readonly IVoiceActivityDetector _vad;
    private readonly ISegmentRecognizer? _secondPass;
    private readonly DraftSession _draft;
    private readonly DictationOptions _options;

    private readonly AudioBuffer _audio = new();
    private readonly Channel<float[]> _frames = Channel.CreateUnbounded<float[]>(new() { SingleReader = true });
    private readonly Channel<(Segment Segment, float[] Audio)> _secondPassJobs =
        Channel.CreateUnbounded<(Segment, float[])>(new() { SingleReader = true });
    private readonly Stopwatch _clock = new();

    // _gate guards the recognition state below; _outputGate serializes everything written to the draft so units
    // are appended in order and the pending text never goes back to an older value.
    private readonly object _gate = new();
    private readonly object _outputGate = new();

    private readonly List<Segment> _segments = [];
    private readonly List<SpeechSpan> _pending = [];
    private string _tail = "";
    private string _openText = "";
    private string _publishedPending = "";
    private int _nextToSettle;
    private long _streamStartSample;
    private long _openFirstTextSample = -1;
    private long _openLastTextSample = -1;

    private Task _recognitionTask = Task.CompletedTask;
    private Task _secondPassTask = Task.CompletedTask;
    private Task? _stopTask;

    public DictationSession(
        ILiveRecognizer live,
        IVoiceActivityDetector vad,
        ISegmentRecognizer? secondPass,
        DraftSession draft,
        DictationOptions? options = null)
    {
        _live = live;
        _vad = vad;
        _secondPass = secondPass;
        _draft = draft;
        _options = options ?? new DictationOptions();
    }

    public IReadOnlyList<SegmentRecord> Segments
    {
        get
        {
            lock (_gate)
                return _segments
                    .Select(s => new SegmentRecord(s.Id, s.Speech, s.Audio, s.EndsSentence, s.LiveFinal, s.SettledText, s.SecondPassFailed))
                    .ToList();
        }
    }

    public void Start()
    {
        _clock.Start();
        _draft.Mutate(draft => draft.Recording = true);
        _recognitionTask = Task.Run(RecognitionLoopAsync);
        _secondPassTask = Task.Run(SecondPassLoopAsync);
    }

    /// <summary>Queues captured audio. Never blocks on recognition, so capture is never starved.</summary>
    public void Push(float[] frame)
    {
        if (!_frames.Writer.TryWrite(frame))
            throw new InvalidOperationException("Audio pushed after the session was stopped.");
    }

    /// <summary>
    /// Ends the audio, settles what is still in flight and turns the unfinished tail into a unit. Calling it again
    /// returns the same task. Segments the second pass has not finished within the timeout keep their live text.
    /// </summary>
    public Task StopAsync()
    {
        lock (_gate)
            return _stopTask ??= Task.Run(StopCoreAsync);
    }

    /// <summary>
    /// Stops the session and releases the recognizers it was given. If a worker is still decoding after the stop
    /// timeout, they are released as soon as it finishes: native recognizers must not be freed while in use.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _ = Task.WhenAll(_recognitionTask, _secondPassTask).ContinueWith(
            _ => ReleaseRecognizers(), TaskScheduler.Default);
    }

    private void ReleaseRecognizers()
    {
        _live.Dispose();
        _vad.Dispose();
        _secondPass?.Dispose();
    }

    // ---- recognition worker -------------------------------------------------

    private async Task RecognitionLoopAsync()
    {
        var reader = _frames.Reader;
        while (await reader.WaitToReadAsync().ConfigureAwait(false))
        {
            while (reader.TryRead(out var frame))
            {
                _audio.Append(frame);
                _live.Accept(frame);
                _vad.Accept(frame);
                OnLiveText(TextTidy.Apply(_live.CurrentText));
                while (_vad.TryTake(out var span))
                    _pending.Add(span);
                CloseUtteranceIfEnded();
            }
        }

        _vad.Flush();
        while (_vad.TryTake(out var span))
            _pending.Add(span);
        // Text the detector did not attribute to any speech span is closed too, so it is never lost.
        if (_pending.Count > 0 || _openText.Length > 0)
            CloseUtterance(endsSentence: true);
    }

    /// <summary>
    /// Short pauses only collect speech spans. A hard pause ends the utterance; so does a span that makes the
    /// utterance too long, because that span was reported at a short pause and is a natural place to cut.
    /// </summary>
    private void CloseUtteranceIfEnded()
    {
        if (_pending.Count == 0)
            return;

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
            if (text == _openText)
                return;
            if (_openText.Length == 0 && text.Length > 0)
                _openFirstTextSample = _audio.TotalSamples;
            _openText = text;
            _openLastTextSample = _audio.TotalSamples;
        }
        Publish();
    }

    /// <summary>
    /// Closes the live stream and queues the second pass over the same audio the stream consumed. The detector's spans
    /// only decide where to cut: they miss quiet or far-field speech, and a second pass limited to them would drop
    /// words the live stream recognized. Leading silence is trimmed to the first evidence of speech, from either the
    /// detector or the text.
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
        var needsSecondPass = _secondPass is not null && !string.IsNullOrWhiteSpace(finalText);
        lock (_gate)
        {
            segment = new Segment
            {
                Id = _segments.Count,
                Speech = speech,
                Audio = audio,
                EndsSentence = endsSentence,
                LiveFinal = finalText,
                SettledText = needsSecondPass ? null : finalText
            };
            _segments.Add(segment);
            _openText = "";
            _openFirstTextSample = -1;
            _openLastTextSample = -1;
            _streamStartSample = end;
        }

        if (needsSecondPass)
        {
            var pad = AudioFormat.MsToSamples(_options.SecondPassPaddingMs);
            var samples = _audio.Slice(Math.Max(previousCut, audio.StartSample - pad), audio.EndSample + pad);
            _secondPassJobs.Writer.TryWrite((segment, samples));
        }

        Settle();
    }

    // ---- second pass worker -------------------------------------------------

    private async Task SecondPassLoopAsync()
    {
        await foreach (var (segment, audio) in _secondPassJobs.Reader.ReadAllAsync().ConfigureAwait(false))
        {
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
                // The live text is a complete answer; a failing decoder must not cost the user their words.
                text = segment.LiveFinal;
                failed = true;
            }

            lock (_gate)
            {
                // A result that arrives after stopping settled the segment from its live text is too late.
                if (segment.SettledText is not null)
                    continue;
                segment.SettledText = text.Trim();
                segment.SecondPassFailed = failed;
            }
            Settle();
        }
    }

    // ---- settling and sentence building ------------------------------------

    /// <summary>Segments settle strictly in order, so units are append-only.</summary>
    private void Settle(bool flushTail = false)
    {
        lock (_outputGate)
        {
            List<string> sentences = [];
            string pending;
            lock (_gate)
            {
                while (_nextToSettle < _segments.Count && _segments[_nextToSettle].SettledText is { } text)
                {
                    var segment = _segments[_nextToSettle++];
                    var units = TextUnits.Split(JoinText(_tail, text));
                    var remainder = "";
                    foreach (var unit in units)
                    {
                        if (unit.IsComplete)
                            sentences.Add(unit.Body);
                        else
                            remainder = unit.Body;
                    }
                    _tail = remainder;
                    if (segment.EndsSentence || _tail.Length >= _options.MaxTailChars)
                        FlushTail(sentences);
                }
                if (flushTail)
                    FlushTail(sentences);
                pending = PendingText();
            }

            Write(sentences, pending);
        }
    }

    private void FlushTail(List<string> sentences)
    {
        if (_tail.Length == 0)
            return;
        sentences.Add(_tail);
        _tail = "";
    }

    private void Publish()
    {
        lock (_outputGate)
        {
            string pending;
            lock (_gate)
                pending = PendingText();
            if (pending != _publishedPending)
                Write([], pending);
        }
    }

    private void Write(List<string> sentences, string pending)
    {
        if (sentences.Count == 0 && pending == _publishedPending)
            return;

        _publishedPending = pending;
        _draft.Mutate(draft =>
        {
            foreach (var sentence in sentences)
                draft.Append(sentence);
            draft.SetPending(pending);
        });
    }

    /// <summary>Recognized but not yet settled speech, in time order: the unfinished tail, unsettled segments, the open segment.</summary>
    private string PendingText()
    {
        var text = _tail;
        for (var i = _nextToSettle; i < _segments.Count; i++)
            text = JoinText(text, _segments[i].SettledText ?? _segments[i].LiveFinal);
        return JoinText(text, _openText);
    }

    // ---- stop ---------------------------------------------------------------

    private async Task StopCoreAsync()
    {
        var deadline = Stopwatch.GetTimestamp() + Stopwatch.Frequency * _options.StopTimeoutMs / 1000;

        _frames.Writer.Complete();
        await WaitAsync(_recognitionTask, deadline).ConfigureAwait(false);
        _secondPassJobs.Writer.Complete();
        await WaitAsync(_secondPassTask, deadline).ConfigureAwait(false);

        lock (_gate)
        {
            // Anything a timed-out second pass left unsettled falls back to its live text.
            foreach (var segment in _segments.Where(s => s.SettledText is null))
            {
                segment.SettledText = segment.LiveFinal;
                segment.SecondPassFailed = true;
            }
        }

        Settle(flushTail: true);
        _draft.Mutate(draft => draft.Recording = false);
    }

    private static async Task WaitAsync(Task task, long deadline)
    {
        var remaining = TimeSpan.FromSeconds(Math.Max(0, (deadline - Stopwatch.GetTimestamp()) / (double)Stopwatch.Frequency));
        if (await Task.WhenAny(task, Task.Delay(remaining)).ConfigureAwait(false) == task)
            await task.ConfigureAwait(false); // surfaces a worker failure instead of waiting out the timeout
    }

    private static string JoinText(string left, string right) =>
        TextUnits.Join([("", left, ""), ("", right, "")]);
}
