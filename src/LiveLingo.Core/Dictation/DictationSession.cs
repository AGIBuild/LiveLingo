using System.Diagnostics;
using System.Threading.Channels;
using LiveLingo.Core.Drafting;
using LiveLingo.Core.Transcription;
using Microsoft.Extensions.Logging;

namespace LiveLingo.Core.Dictation;

/// <summary>
/// Turns a continuous audio stream into the units of a <see cref="DraftSession"/>: live text while speaking
/// (<see cref="Draft.Pending"/>), segments closed at pauses, optional second-pass settling, and passages (a few
/// sentences said together) appended in speech order. Cleanup and translation are separate schedulers that observe the same draft.
/// Capture is never blocked by recognition: <see cref="Push"/> only queues the frame.
/// </summary>
public sealed class DictationSession : IDictationSession
{
    private const int MidSpeechCutSearchMs = 1200;
    private const int MidSpeechCutTailGuardMs = 80;
    private const int MinMidSpeechSegmentMs = 500;

    private sealed class Segment
    {
        public int Id { get; init; }
        public SpeechSpan Speech { get; init; }
        public SpeechSpan Audio { get; init; }
        public bool EndsSentence { get; init; }
        /// <summary>Set when the utterance hit <see cref="DictationOptions.HardMaxUtteranceMs"/> without a silence pause and was sliced at an energy valley.</summary>
        public bool CutMidSpeech { get; init; }
        /// <summary>Set when the speaker stayed silent after this segment long enough to end the passage.</summary>
        public bool EndsPassage { get; set; }
        public string LiveFinal { get; init; } = "";
        public string? SettledText { get; set; }
        public bool SecondPassFailed { get; set; }
    }

    private readonly ILiveRecognizer _live;
    private readonly IVoiceActivityDetector _vad;
    private readonly ISegmentRecognizer? _secondPass;
    private readonly DraftSession _draft;
    private readonly int _draftEpoch;
    private readonly DictationOptions _options;
    private readonly ILogger? _logger;

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
    private readonly List<string> _passage = [];
    private int _emittedPassages;
    private string _tail = "";
    private char? _tailHeldTerminator;
    private string _openText = "";
    private string _publishedPending = "";
    private int _nextToSettle;
    private long _streamStartSample;
    private bool _continuesSpeech;
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
        DictationOptions? options = null,
        ILogger? logger = null)
    {
        _live = live;
        _vad = vad;
        _secondPass = secondPass;
        _draft = draft;
        _draftEpoch = draft.Read(d => d.Epoch);
        _options = options ?? new DictationOptions();
        _logger = logger;
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
        _draft.Mutate(draft =>
        {
            if (draft.Epoch == _draftEpoch && draft.Snapshot is null)
                draft.Recording = true;
        });
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
        var workers = Task.WhenAll(_recognitionTask, _secondPassTask);
        if (workers.IsCompleted)
            ReleaseRecognizers();
        else
            _ = workers.ContinueWith(_ => ReleaseRecognizers(), TaskScheduler.Default);
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
                    AddPendingSpan(span);
                CloseUtteranceIfEnded();
                EndPassageIfSilent();
            }
        }

        _vad.Flush();
        while (_vad.TryTake(out var span))
            AddPendingSpan(span);
        // Text the detector did not attribute to any speech span (or trailing speech right after a mid-speech cut
        // whose streaming lookahead has not ticked yet) is closed too, so it is never lost.
        if (_pending.Count > 0 || _openText.Length > 0 || (_continuesSpeech && _audio.TotalSamples > _streamStartSample))
            CloseUtterance(endsSentence: true);
    }

    private void AddPendingSpan(SpeechSpan span)
    {
        if (span.EndSample <= _streamStartSample)
            return;
        _pending.Add(span.StartSample < _streamStartSample
            ? new SpeechSpan(_streamStartSample, span.EndSample)
            : span);
    }

    /// <summary>
    /// Short pauses only collect speech spans. A hard pause ends the utterance; so does a span that makes the
    /// utterance too long once the speaker actually enters a pause (<c>!_vad.IsSpeechActive</c>). When speech
    /// continues without any detected pause beyond <see cref="DictationOptions.HardMaxUtteranceMs"/>, the segment is
    /// sliced at the quietest 20 ms frame near the end so the second-pass 512-token KV window never overflows and no
    /// spoken frames are dropped or double-decoded between segments.
    /// </summary>
    private void CloseUtteranceIfEnded()
    {
        var hardMaxMs = Math.Max(_options.HardMaxUtteranceMs, _options.SoftMaxUtteranceMs);
        if (_pending.Count == 0)
        {
            if (!_vad.IsSpeechActive && _openText.Length == 0)
                _continuesSpeech = false;

            var activeSince = _continuesSpeech && _vad.IsSpeechActive
                ? _streamStartSample
                : _openFirstTextSample >= 0
                    ? Math.Max(_streamStartSample, _openFirstTextSample - AudioFormat.MsToSamples(_options.SpeechOnsetMarginMs))
                    : -1;
            if (activeSince >= 0
                && AudioFormat.SamplesToMs(_audio.TotalSamples - activeSince) >= hardMaxMs)
            {
                CloseUtterance(endsSentence: false, cutMidSpeech: true);
            }
            return;
        }

        var first = _pending[0];
        var last = _pending[^1];
        var silenceMs = AudioFormat.SamplesToMs(_audio.TotalSamples - last.EndSample);
        var lengthMs = AudioFormat.SamplesToMs(last.EndSample - first.StartSample);
        var openElapsedMs = AudioFormat.SamplesToMs(_audio.TotalSamples - first.StartSample);

        var hard = !_vad.IsSpeechActive && silenceMs >= _options.HardPauseMs;
        var softPause = !_vad.IsSpeechActive && lengthMs >= _options.SoftMaxUtteranceMs;
        var forcedMax = openElapsedMs >= hardMaxMs;
        if (hard || softPause || forcedMax)
            CloseUtterance(endsSentence: hard, cutMidSpeech: !hard && !softPause);
    }

    /// <summary>
    /// A long silence after the last segment ends the passage, so what was said so far is cleaned while the speaker thinks.
    /// The break is recorded on the segment, so it applies in order even when that segment is still being decoded.
    /// </summary>
    private void EndPassageIfSilent()
    {
        if (_pending.Count > 0 || _vad.IsSpeechActive)
            return;

        lock (_gate)
        {
            if (_openText.Length > 0 || _segments.Count == 0)
                return;

            var last = _segments[^1];
            var silenceMs = AudioFormat.SamplesToMs(_audio.TotalSamples - last.Speech.EndSample);
            if (last.EndsPassage || silenceMs < _options.PassagePauseMs)
                return;

            last.EndsPassage = true;
        }

        Settle();
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
    private void CloseUtterance(bool endsSentence, bool cutMidSpeech = false)
    {
        var totalNow = _audio.TotalSamples;
        var previousCut = _streamStartSample;
        var wasContinuing = _continuesSpeech;

        long start;
        if (wasContinuing)
        {
            start = previousCut;
        }
        else
        {
            var evidence = long.MaxValue;
            if (_pending.Count > 0)
                evidence = _pending[0].StartSample;
            if (_openFirstTextSample >= 0)
                evidence = Math.Min(evidence, _openFirstTextSample - AudioFormat.MsToSamples(_options.SpeechOnsetMarginMs));
            start = Math.Max(previousCut, evidence == long.MaxValue ? previousCut : evidence);
        }

        long end;
        if (cutMidSpeech)
        {
            var searchFrom = Math.Max(start + AudioFormat.MsToSamples(MinMidSpeechSegmentMs), totalNow - AudioFormat.MsToSamples(MidSpeechCutSearchMs));
            var searchTo = totalNow - AudioFormat.MsToSamples(MidSpeechCutTailGuardMs);
            if (searchTo - searchFrom >= AudioFormat.SampleRate / 50)
            {
                var window = _audio.Slice(searchFrom, searchTo);
                end = searchFrom + FileTranscriber.QuietestPoint(window, 0, window.Length);
            }
            else
            {
                end = totalNow;
            }
        }
        else
        {
            // Trailing silence is trimmed once the detector observed speech end: a recognizer decoding long silence
            // tends to invent words. When the user stops before any pause was detected (_pending is empty), nothing is
            // trimmed because _openLastTextSample lags live speech by the streaming transducer's lookahead.
            var lastEvidence = _pending.Count > 0
                ? Math.Max(_pending[^1].EndSample + AudioFormat.MsToSamples(_options.SpeechOffsetMarginMs), _openLastTextSample)
                : 0L;
            end = lastEvidence > 0 ? Math.Min(totalNow, lastEvidence) : totalNow;
            end = Math.Max(start, end);
        }

        var audio = new SpeechSpan(start, end);
        var speech = !cutMidSpeech && _pending.Count > 0
            ? new SpeechSpan(_pending[0].StartSample, Math.Min(end, _pending[^1].EndSample))
            : audio;

        _pending.Clear();
        var finalText = TextTidy.Apply(_live.CloseSegment());
        var carryText = "";
        if (cutMidSpeech && end < totalNow)
        {
            var carryover = _audio.Slice(end, totalNow);
            if (carryover.Length > 0)
            {
                _live.Accept(carryover);
                carryText = TextTidy.Apply(_live.CurrentText);
            }
        }

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
                CutMidSpeech = cutMidSpeech,
                LiveFinal = finalText,
                SettledText = needsSecondPass ? null : finalText
            };
            _segments.Add(segment);
            _openText = carryText;
            _openFirstTextSample = carryText.Length > 0 ? end : -1;
            _openLastTextSample = carryText.Length > 0 ? totalNow : -1;
            _streamStartSample = end;
            _continuesSpeech = cutMidSpeech;
        }

        if (needsSecondPass)
        {
            var pad = AudioFormat.MsToSamples(_options.SecondPassPaddingMs);
            var sliceStart = wasContinuing ? audio.StartSample : Math.Max(previousCut, audio.StartSample - pad);
            var sliceEnd = cutMidSpeech ? audio.EndSample : audio.EndSample + pad;
            var samples = _audio.Slice(sliceStart, sliceEnd);
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
                var rateExceeded = _options.SecondPassMaxCharsPerSecond > 0
                    && text.Count(char.IsLetterOrDigit) > _options.SecondPassMaxCharsPerSecond * seconds + 10;
                if (string.IsNullOrWhiteSpace(text) || rateExceeded)
                {
                    text = segment.LiveFinal;
                    failed = true;
                }
            }
            catch (Exception ex)
            {
                // The live text is a complete answer; a failing decoder must not cost the user their words.
                _logger?.LogWarning(ex, "Second-pass speech recognizer failed on segment {SegmentId}; keeping live text.", segment.Id);
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

    // ---- settling, sentence and passage building ----------------------------

    /// <summary>
    /// Segments settle strictly in order, so units are append-only. Settled sentences wait in the open passage;
    /// a passage becomes one unit when the speaker paused long enough, when it is long enough, or when stopping.
    /// </summary>
    private void Settle(bool endPassage = false)
    {
        lock (_outputGate)
        {
            List<string> passages = [];
            string pending;
            lock (_gate)
            {
                while (_nextToSettle < _segments.Count && _segments[_nextToSettle].SettledText is { } text)
                {
                    var segment = _segments[_nextToSettle++];
                    var passageEnding = endPassage || segment.EndsPassage;
                    if (text.Length == 0)
                    {
                        if (segment.EndsSentence && (passageEnding || !IsDanglingClause(_tail)))
                            FlushTail(passages);
                        if (segment.EndsPassage)
                            EndPassage(passages);
                        continue;
                    }

                    if (!passageEnding && HoldPrematurePeriod(text, segment.CutMidSpeech, _options.HoldDanglingClausePeriod, out var held, out var heldTerminator))
                    {
                        text = held;
                        _tailHeldTerminator = heldTerminator;
                    }
                    else
                    {
                        _tailHeldTerminator = null;
                    }

                    var combined = JoinText(_tail, text);
                    _tail = "";
                    var units = TextUnits.Split(combined);
                    var remainder = "";
                    foreach (var unit in units)
                    {
                        if (unit.IsComplete)
                        {
                            AddSentence(passages, unit.Body);
                        }
                        else
                        {
                            remainder = unit.Body;
                        }
                    }
                    if (remainder.Length == 0)
                        _tailHeldTerminator = null;
                    _tail = remainder;
                    if ((segment.EndsSentence && (passageEnding || !IsDanglingClause(_tail))) || _tail.Length >= _options.MaxTailChars)
                        FlushTail(passages);
                    if (segment.EndsPassage)
                        EndPassage(passages);
                }

                // The break may be recorded after its segment settled.
                if (endPassage || (_nextToSettle > 0 && _segments[_nextToSettle - 1].EndsPassage))
                    EndPassage(passages);
                pending = PendingText();
            }

            Write(passages, pending);
        }
    }

    private void AddSentence(List<string> passages, string sentence)
    {
        _passage.Add(sentence);
        var length = PassageText().Length;
        var fastFirst = _emittedPassages == 0 && _options.FirstPassageMinChars > 0 && length >= _options.FirstPassageMinChars;
        if (fastFirst || length >= _options.PassageMaxChars)
            EndPassage(passages);
    }

    private void FlushTail(List<string> passages)
    {
        if (_tail.Length == 0)
            return;
        var tail = _tail;
        if (_tailHeldTerminator is { } terminator)
        {
            if (tail.EndsWith('，'))
                tail = tail[..^1] + terminator;
            else if (!tail.EndsWith(terminator))
                tail += terminator;
        }
        _tail = "";
        _tailHeldTerminator = null;
        AddSentence(passages, tail);
    }

    private static readonly string[] DanglingClauseEndings =
    [
        "如果说", "如果", "因为", "所以", "但是", "不过", "而且", "并且", "或者说", "或者",
        "比如说", "比如", "例如", "特别是", "尤其是", "对于", "关于", "按照", "通过", "根据",
        "应该以", "以及", "要求的是", "意思是", "问题是", "最好的办法", "一个办法",
        "把", "被", "将", "向", "往", "从", "和", "跟", "与",
        "他就", "我就", "你就", "她就", "它就", "我们就", "他们就", "咱们就",
        "他还", "我还", "他都", "我都", "他才", "的这种", "的那种"
    ];

    private static readonly string[] DanglingStandaloneClauses =
    [
        "这种", "那种", "有这种", "有那种", "是这种", "像这种", "这类", "那类", "他每天", "我每天", "你每天"
    ];

    private static readonly string[] SeamlessMidPhraseEndings =
    [
        "的", "地", "得", "在", "从", "向", "往", "把", "被", "将", "对", "给", "用", "按", "比", "和", "跟", "与",
        "这种", "那种", "这个", "那个", "这些", "那些", "这类", "那类", "他就", "我就", "你就"
    ];

    private static readonly string[] CompleteDeReplies =
    [
        "好的", "对的", "是的", "行的", "可以的", "没问题的", "真的", "有的", "会的", "不会的", "不是的"
    ];

    private static readonly string[] IncompleteClauseOpeners =
    [
        "如果", "因为", "虽然", "尽管", "当", "在", "把", "对", "从", "按"
    ];

    /// <summary>
    /// Isolated second-pass decoding appends a period either when an utterance was sliced mid-speech at
    /// <see cref="DictationOptions.HardMaxUtteranceMs"/>, or after a hesitation on a syntactically incomplete
    /// function word ("如果说你还需要更强的。", "他就。", "有这种。"). Holding that premature period while speech
    /// continues lets the next segment complete the sentence; stopping restores the period.
    /// </summary>
    private static bool HoldPrematurePeriod(
        string text,
        bool cutMidSpeech,
        bool holdDanglingClause,
        out string held,
        out char? terminator)
    {
        held = text;
        terminator = null;

        if (cutMidSpeech)
        {
            if (text.EndsWith('。'))
            {
                var midCore = text[..^1].TrimEnd();
                if (midCore.Length == 0)
                    return false;
                held = EndsSeamlessMidPhrase(midCore) ? midCore : midCore + "，";
                terminator = '。';
                return true;
            }

            if (text.EndsWith('.') && text.Length >= 2 && IsAsciiLetter(text[^2]))
            {
                held = text[..^1].TrimEnd();
                terminator = '.';
                return true;
            }
        }

        if (!holdDanglingClause || !text.EndsWith('。'))
            return false;

        var core = text[..^1].TrimEnd();
        if (!IsDanglingCore(core))
            return false;

        held = core + "，";
        terminator = '。';
        return true;
    }

    internal static bool EndsSeamlessMidPhrase(string core)
    {
        if (core.Length == 0)
            return false;
        var clause = LastClause(core);
        if (CompleteDeReplies.Any(reply => clause.Equals(reply, StringComparison.Ordinal)))
            return false;
        foreach (var ending in SeamlessMidPhraseEndings)
        {
            if (clause.EndsWith(ending, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private static bool IsAsciiLetter(char c) => c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z');

    private static bool IsDanglingClause(string tail)
    {
        var core = tail.TrimEnd('，', ',', '、', ' ');
        return IsDanglingCore(core);
    }

    private static bool IsDanglingCore(string core)
    {
        if (core.Length == 0)
            return false;

        var clause = LastClause(core);
        foreach (var standalone in DanglingStandaloneClauses)
        {
            if (clause.Equals(standalone, StringComparison.Ordinal))
                return true;
        }

        foreach (var ending in DanglingClauseEndings)
        {
            if (clause.EndsWith(ending, StringComparison.Ordinal))
                return true;
        }

        if (clause.EndsWith('的') && !CompleteDeReplies.Any(reply => clause.Equals(reply, StringComparison.Ordinal) || clause.EndsWith("，" + reply, StringComparison.Ordinal)))
        {
            if (clause.Length <= 14 || IncompleteClauseOpeners.Any(opener => clause.StartsWith(opener, StringComparison.Ordinal)))
                return true;
        }

        return false;
    }

    private static string LastClause(string text)
    {
        var cut = text.LastIndexOfAny(['，', ',', '。', '！', '？', '；']);
        return cut >= 0 ? text[(cut + 1)..].Trim() : text.Trim();
    }

    private void EndPassage(List<string> passages)
    {
        FlushTail(passages);
        if (_passage.Count == 0)
            return;
        passages.Add(PassageText());
        _emittedPassages++;
        _passage.Clear();
    }

    private string PassageText() => TextUnits.Join(_passage.Select(sentence => ("", sentence, "")));

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

    private void Write(List<string> passages, string pending)
    {
        if (passages.Count == 0 && pending == _publishedPending)
            return;

        _publishedPending = pending;
        _draft.Mutate(draft =>
        {
            if (draft.Epoch != _draftEpoch || draft.Snapshot is not null)
                return;
            foreach (var passage in passages)
                draft.Append(passage);
            draft.SetPending(pending);
        });
    }

    /// <summary>Recognized but not yet written as a unit, in time order: the open passage, the unfinished tail, unsettled segments, the open segment.</summary>
    private string PendingText()
    {
        var text = JoinText(PassageText(), _tail);
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

        Settle(endPassage: true);
        _draft.Mutate(draft =>
        {
            if (draft.Epoch == _draftEpoch && draft.Snapshot is null)
                draft.Recording = false;
        });
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
