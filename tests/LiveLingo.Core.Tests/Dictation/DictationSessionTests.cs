using LiveLingo.Core.Dictation;
using LiveLingo.Core.Drafting;
using LiveLingo.Core.Tests.Support;

namespace LiveLingo.Core.Tests.Dictation;

public class DictationSessionTests
{
    private sealed class SlowLive : ILiveRecognizer
    {
        public ManualResetEventSlim Entered { get; } = new();
        public ManualResetEventSlim Release { get; } = new();
        public bool Disposed { get; private set; }
        public string CurrentText => "旧录音里的内容。";
        public void Accept(ReadOnlySpan<float> samples) { Entered.Set(); Release.Wait(5000); }
        public string CloseSegment() => CurrentText;
        public void Dispose() => Disposed = true;
    }

    [Fact]
    public async Task RecognitionThatOutlivesStopTimeout_CannotWriteIntoAResetDraft()
    {
        var draft = new DraftSession(true);
        var live = new SlowLive();
        await using var session = new DictationSession(live, new ScriptedVad([]), null, draft,
            new DictationOptions { StopTimeoutMs = 20, PassageMaxChars = 1 });
        session.Start();
        session.Push(new float[640]);
        Assert.True(await Until(() => live.Entered.IsSet));
        await session.DisposeAsync();
        draft.Mutate(d => d.Reset(true));
        live.Release.Set();
        Assert.True(await Until(() => live.Disposed));
        Assert.Empty(draft.Read(d => d.Units));
        Assert.Empty(draft.Read(d => d.Pending));
        Assert.Empty(draft.Read(d => d.SourceText()));
    }

    private static readonly ScriptedSegment A = new(300, 2300, "把下载超时设成三十秒。");
    private static readonly ScriptedSegment B = new(2800, 4800, "只改下载接口，其他请求不要动。");

    private sealed class Harness : IAsyncDisposable
    {
        public DraftSession Draft { get; } = new(hasTarget: true);
        public ScriptedLive Live { get; }
        public DictationSession Session { get; }

        public ScriptedVad Vad { get; }

        public Harness(
            IReadOnlyList<ScriptedSegment> script,
            ISegmentRecognizer? secondPass = null,
            int hardPauseMs = 300,
            int softMaxUtteranceMs = 60_000,
            int maxTailChars = 90,
            int passagePauseMs = 1500,
            int passageMaxChars = 1, // one sentence per unit, unless a test is about passages
            int firstPassageMinChars = 0,
            int stopTimeoutMs = 5000)
        {
            Live = new ScriptedLive(script);
            Vad = new ScriptedVad(script);
            Session = new DictationSession(
                Live, Vad, secondPass, Draft,
                new DictationOptions
                {
                    HardPauseMs = hardPauseMs,
                    SoftMaxUtteranceMs = softMaxUtteranceMs,
                    MaxTailChars = maxTailChars,
                    PassagePauseMs = passagePauseMs,
                    PassageMaxChars = passageMaxChars,
                    FirstPassageMinChars = firstPassageMinChars,
                    StopTimeoutMs = stopTimeoutMs
                });
            Session.Start();
        }

        public IReadOnlyList<string> Raws => Draft.Read(d => d.Units.Select(u => u.Raw).ToList());

        public string Pending => Draft.Read(d => d.Pending);

        public bool Recording => Draft.Read(d => d.Recording);

        public void Push(int ms)
        {
            for (var pushed = 0; pushed < ms; pushed += 40)
                Session.Push(new float[AudioFormat.MsToSamples(40)]);
        }

        public ValueTask DisposeAsync() => Session.DisposeAsync();
    }

    private static async Task<bool> Until(Func<bool> condition, int timeoutMs = 3000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if (condition())
                return true;
            await Task.Delay(10);
        }
        return condition();
    }

    [Fact]
    public async Task Start_MarksTheDraftAsRecording_AndStopReleasesIt()
    {
        await using var h = new Harness([A]);
        Assert.True(h.Recording);

        await h.Session.StopAsync();

        Assert.False(h.Recording);
    }

    [Fact]
    public async Task AHardPause_EndsTheUtterance_AndItsUnpunctuatedTextBecomesAUnit()
    {
        var script = new[] { new ScriptedSegment(300, 2300, "把下载超时设成三十秒") };
        await using var h = new Harness(script);

        h.Push(2500); // silence has not reached the hard pause yet
        Assert.Empty(h.Session.Segments);
        h.Push(200);

        Assert.True(await Until(() => h.Raws.Count == 1));
        Assert.Equal("把下载超时设成三十秒", h.Raws[0]);
        Assert.True(h.Session.Segments[0].EndsSentence);
    }

    [Fact]
    public async Task LiveText_IsPendingWhileSpeaking_AndMovesIntoUnitsWhenSettled()
    {
        var script = new[] { A };
        await using var h = new Harness(script);

        h.Push(1500);
        Assert.True(await Until(() => h.Pending.Length > 0));
        Assert.Empty(h.Raws);

        h.Push(1300);
        await h.Session.StopAsync();

        Assert.Equal(["把下载超时设成三十秒。"], h.Raws);
        Assert.Equal("", h.Pending);
    }

    [Fact]
    public async Task AShortPause_NeitherResetsTheLiveStream_NorSplitsTheSentence()
    {
        var script = new[]
        {
            new ScriptedSegment(300, 1800, "如果这个请求失败了"),
            new ScriptedSegment(2100, 4000, "就重试两次，不要重复提交"),
        };
        await using var h = new Harness(script, hardPauseMs: 800);

        h.Push(4400);
        await h.Session.StopAsync();

        Assert.Equal(1, h.Live.Closes); // a fresh stream starting mid-sentence recognized badly
        Assert.Single(h.Session.Segments);
        Assert.Equal(["如果这个请求失败了就重试两次，不要重复提交"], h.Raws);
    }

    [Fact]
    public async Task AnUtteranceThatGetsTooLong_IsCutAtTheNextShortPause_WithoutEndingTheSentence()
    {
        var script = new[]
        {
            new ScriptedSegment(300, 1800, "先把下载接口改掉"),
            new ScriptedSegment(2100, 4000, "再补一个取消测试"),
        };
        await using var h = new Harness(script, hardPauseMs: 5000, softMaxUtteranceMs: 1000);

        h.Push(4400);
        await h.Session.StopAsync();

        Assert.Equal(2, h.Session.Segments.Count);
        Assert.False(h.Session.Segments[0].EndsSentence);
        Assert.Equal(["先把下载接口改掉再补一个取消测试"], h.Raws);
    }

    [Fact]
    public async Task ALongTail_BecomesAUnitWithoutWaitingForAPause()
    {
        var script = new[]
        {
            new ScriptedSegment(300, 1800, "先把下载接口改掉"),
            new ScriptedSegment(2100, 3600, "再补一个取消测试"),
        };
        await using var h = new Harness(script, hardPauseMs: 60_000, softMaxUtteranceMs: 1000, maxTailChars: 10);

        h.Push(4000);

        Assert.True(await Until(() => h.Raws.Count >= 1));
        Assert.Equal("先把下载接口改掉再补一个取消测试", h.Raws[0]);
    }

    [Fact]
    public async Task SecondPassText_ReplacesTheLiveText_BeforeUnitsAreFormed()
    {
        var script = new[] { A };
        await using var h = new Harness(script, secondPass: new ScriptedSegmentRecognizer(() => "把下载超时设成 30 秒。"));

        h.Push(2800);
        await h.Session.StopAsync();

        Assert.Equal(["把下载超时设成 30 秒。"], h.Raws);
        Assert.False(h.Session.Segments[0].SecondPassFailed);
    }

    [Fact]
    public async Task TheSecondPass_DecodesTheAudioTheLiveStreamConsumed_EvenWhereTheDetectorHeardNoSpeech()
    {
        var script = new[] { new ScriptedSegment(1000, 2000, "很轻的声音。") };
        var recorder = new AudioLengthRecorder("很轻的声音。");
        await using var h = new Harness(script, secondPass: recorder);

        h.Push(2600);
        await h.Session.StopAsync();

        var decoded = Assert.Single(recorder.Milliseconds);
        Assert.InRange(decoded, 900, 2700);
    }

    [Fact]
    public async Task TheSecondPass_DoesNotDecodeTheSilenceOfTheHardPause()
    {
        var script = new[] { new ScriptedSegment(300, 1300, "短句。") };
        var recorder = new AudioLengthRecorder("短句。");
        await using var h = new Harness(script, secondPass: recorder, hardPauseMs: 1500);

        h.Push(3500);
        await h.Session.StopAsync();

        var decoded = Assert.Single(recorder.Milliseconds);
        Assert.True(decoded < 2000, $"decoded {decoded} ms, silence was included");
    }

    [Fact]
    public async Task SecondPassAudio_NeverReachesBackBeforeThePreviousCut()
    {
        var script = new[]
        {
            new ScriptedSegment(300, 1300, "第一句。"),
            new ScriptedSegment(2600, 3600, "第二句。"),
        };
        var recorder = new AudioLengthRecorder("句子。");
        await using var h = new Harness(script, secondPass: recorder, hardPauseMs: 600);

        h.Push(4600);
        await h.Session.StopAsync();

        Assert.Equal(2, recorder.Milliseconds.Count);
        Assert.All(recorder.Milliseconds, ms => Assert.True(ms < 2600, $"decoded {ms} ms"));
    }

    [Fact]
    public async Task ASecondPassRepetitionLoop_IsDropped_AndTheLiveTextIsKept()
    {
        var script = new[] { A };
        var loop = new string('的', 400);
        await using var h = new Harness(script, secondPass: new ScriptedSegmentRecognizer(() => loop));

        h.Push(2800);
        await h.Session.StopAsync();

        Assert.Equal(["把下载超时设成三十秒。"], h.Raws);
        Assert.True(h.Session.Segments[0].SecondPassFailed);
    }

    [Fact]
    public async Task AFailingSecondPass_KeepsTheLiveText_AndIsReported()
    {
        var script = new[] { A };
        await using var h = new Harness(script, secondPass: new ScriptedSegmentRecognizer(
            () => throw new InvalidOperationException("decoder crashed")));

        h.Push(2800);
        await h.Session.StopAsync();

        Assert.Equal(["把下载超时设成三十秒。"], h.Raws);
        Assert.True(h.Session.Segments[0].SecondPassFailed);
    }

    [Fact]
    public async Task Stop_SettlesWhatASlowSecondPassHasNotFinished_FromTheLiveText()
    {
        var script = new[] { A };
        var release = new ManualResetEventSlim();
        await using var h = new Harness(script, stopTimeoutMs: 300,
            secondPass: new ScriptedSegmentRecognizer(() => { release.Wait(5000); return "迟到的结果。"; }));

        h.Push(2800);
        await h.Session.StopAsync();
        release.Set();

        Assert.Equal(["把下载超时设成三十秒。"], h.Raws);
        Assert.True(h.Session.Segments[0].SecondPassFailed);
        Assert.False(h.Recording);
    }

    [Fact]
    public async Task Stop_FinishesTheUnfinishedTail_AndIsIdempotent()
    {
        var script = new[] { new ScriptedSegment(300, 2300, "最后一句没有标点") };
        await using var h = new Harness(script, hardPauseMs: 60_000);

        h.Push(2500);
        var first = h.Session.StopAsync();
        var second = h.Session.StopAsync();
        await Task.WhenAll(first, second);

        Assert.Same(first, second);
        Assert.Equal(["最后一句没有标点"], h.Raws);
    }

    [Fact]
    public async Task PushAfterStop_IsAnError()
    {
        await using var h = new Harness([A]);
        await h.Session.StopAsync();

        Assert.Throws<InvalidOperationException>(() => h.Session.Push(new float[640]));
    }

    [Fact]
    public async Task SentencesAreAppendedInSpeechOrder_EvenWhenSecondPassesFinishOutOfStep()
    {
        var script = new[] { A, B };
        var calls = 0;
        await using var h = new Harness(script, hardPauseMs: 300, secondPass: new ScriptedSegmentRecognizer(() =>
            Interlocked.Increment(ref calls) == 1 ? "第一句。" : "第二句。"));

        h.Push(5400);
        await h.Session.StopAsync();

        Assert.Equal(["第一句。", "第二句。"], h.Raws);
    }

    [Fact]
    public async Task UnitsStayAppendOnly_WhileTheUserEditsAnEarlierOne()
    {
        var script = new[] { A, B };
        await using var h = new Harness(script, hardPauseMs: 300);

        h.Push(2800);
        Assert.True(await Until(() => h.Raws.Count == 1));
        h.Draft.Mutate(d => d.Edit(0, UnitKind.Clean, "我改过的。"));
        h.Push(2600);
        await h.Session.StopAsync();

        Assert.Equal(2, h.Raws.Count);
        Assert.Equal("我改过的。", h.Draft.Read(d => d.Units[0].Clean.Text));
    }

    [Fact]
    public async Task SentencesSaidTogether_BecomeOnePassage_AndWaitInThePendingTextMeanwhile()
    {
        var script = new[] { A, B };
        await using var h = new Harness(script, hardPauseMs: 300, passageMaxChars: 120);

        h.Push(3000);
        Assert.True(await Until(() => h.Pending.Contains("三十秒")));
        Assert.Empty(h.Raws);

        h.Push(2400);
        await h.Session.StopAsync();

        Assert.Equal(["把下载超时设成三十秒。只改下载接口，其他请求不要动。"], h.Raws);
        Assert.Equal("", h.Pending);
    }

    [Fact]
    public async Task ALongSilence_EndsThePassage_WhileStillRecording()
    {
        var script = new[] { A, new ScriptedSegment(6000, 7500, "然后补测试。") };
        await using var h = new Harness(script, hardPauseMs: 300, passagePauseMs: 1500, passageMaxChars: 120);

        h.Push(3600); // 1.3 s after A ended: still the same passage
        Assert.True(await Until(() => h.Session.Segments.Count == 1));
        Assert.Empty(h.Raws);

        h.Push(400);

        Assert.True(await Until(() => h.Raws.Count == 1));
        Assert.Equal("把下载超时设成三十秒。", h.Raws[0]);
        Assert.True(h.Recording);

        h.Push(4000);
        await h.Session.StopAsync();
        Assert.Equal(["把下载超时设成三十秒。", "然后补测试。"], h.Raws);
    }

    [Fact]
    public async Task APassage_EndsAtTheSentenceThatMakesItLongEnough()
    {
        var script = new[] { A, B, new ScriptedSegment(5300, 7300, "第三句。") };
        await using var h = new Harness(script, hardPauseMs: 300, passageMaxChars: 15);

        h.Push(7800);
        await h.Session.StopAsync();

        Assert.Equal(["把下载超时设成三十秒。只改下载接口，其他请求不要动。", "第三句。"], h.Raws);
    }

    [Fact]
    public async Task APassage_KeepsItsBreakWhenTheEndingSegmentIsStillBeingDecoded()
    {
        var release = new ManualResetEventSlim();
        var calls = 0;
        var script = new[] { A, new ScriptedSegment(6000, 7500, "然后补测试。") };
        await using var h = new Harness(script, hardPauseMs: 300, passagePauseMs: 1500, passageMaxChars: 120,
            secondPass: new ScriptedSegmentRecognizer(() =>
            {
                if (Interlocked.Increment(ref calls) == 1)
                    release.Wait(5000);
                return calls == 1 ? "把下载超时设成三十秒。" : "然后补测试。";
            }));

        h.Push(4200); // the silence outlasts the pause while the second pass has not answered
        Assert.True(await Until(() => h.Session.Segments.Count == 1));
        await Task.Delay(100);
        Assert.Empty(h.Raws);

        release.Set();

        Assert.True(await Until(() => h.Raws.Count == 1));
        Assert.Equal("把下载超时设成三十秒。", h.Raws[0]);
    }

    [Fact]
    public async Task DanglingClauseAtHardPause_HoldsItsPeriodAsACommaUntilTheClauseCompletes()
    {
        var script = new[]
        {
            new ScriptedSegment(200, 1400, "如果说你还需要更强的。"),
            new ScriptedSegment(2000, 3600, "Agent 润色描述，也可以澄清出来。"),
        };
        var calls = 0;
        await using var h = new Harness(script, hardPauseMs: 400, passagePauseMs: 4000, passageMaxChars: 1,
            secondPass: new ScriptedSegmentRecognizer(() =>
                Interlocked.Increment(ref calls) == 1 ? "如果说你还需要更强的。" : "Agent 润色描述，也可以澄清出来。"));

        h.Push(1900); // past hard pause after segment 1, before segment 2
        Assert.True(await Until(() => h.Session.Segments.Count == 1));
        Assert.Empty(h.Raws); // dangling clause held in tail instead of breaking mid-sentence

        h.Push(2200);
        await h.Session.StopAsync();

        Assert.Equal(["如果说你还需要更强的，Agent 润色描述，也可以澄清出来。"], h.Raws);
    }

    [Fact]
    public async Task DanglingClauseFollowedByStop_RestoresItsFinalPeriod()
    {
        var script = new[] { new ScriptedSegment(200, 1400, "如果说你还需要更强的。") };
        await using var h = new Harness(script, hardPauseMs: 400, passagePauseMs: 4000, passageMaxChars: 1,
            secondPass: new ScriptedSegmentRecognizer(() => "如果说你还需要更强的。"));

        h.Push(1900);
        await h.Session.StopAsync();

        Assert.Equal(["如果说你还需要更强的。"], h.Raws);
    }

    [Fact]
    public async Task CompleteShortAffirmationEndingWithDe_SettlesImmediatelyWithoutHolding()
    {
        var script = new[] { new ScriptedSegment(200, 1000, "好的。") };
        await using var h = new Harness(script, hardPauseMs: 300, passagePauseMs: 4000, passageMaxChars: 1,
            secondPass: new ScriptedSegmentRecognizer(() => "好的。"));

        h.Push(1500);

        Assert.True(await Until(() => h.Raws.Count == 1));
        Assert.Equal("好的。", h.Raws[0]);
    }

    [Fact]
    public async Task UninterruptedLongSpeech_CutsSoftlyAtHardMax_SoSecondPassSlicesNeverExceedQwen3Window()
    {
        // Reproduces 26.4 s of continuous speech with no VAD pause (formerly 660 audio tokens > 512 KV limit).
        var draft = new DraftSession(hasTarget: true);
        var script = new[] { new ScriptedSegment(200, 26_600, new string('字', 120) + "。") };
        var recorder = new AudioLengthRecorder("一段连续口述");
        await using var session = new DictationSession(
            new ScriptedLive(script),
            new ScriptedVad(script),
            recorder,
            draft,
            new DictationOptions
            {
                HardPauseMs = 1000,
                SoftMaxUtteranceMs = 8_000,
                HardMaxUtteranceMs = 12_000,
                PassageMaxChars = 500,
                FirstPassageMinChars = 0
            });
        session.Start();

        for (var pushed = 0; pushed < 27_000; pushed += 40)
            session.Push(new float[AudioFormat.MsToSamples(40)]);
        await session.StopAsync();

        Assert.True(recorder.Milliseconds.Count >= 2);
        Assert.All(recorder.Milliseconds, ms => Assert.True(ms <= 16_000, $"slice was {ms} ms"));
    }

    private sealed class LaggedLiveRecognizer(int lagMs) : ILiveRecognizer
    {
        private long _acceptedSamples;
        private long _emittedChunks;
        public List<int> ClosedSampleCounts { get; } = [];
        public string CurrentText => _emittedChunks > 0 ? $"流式{_emittedChunks}" : "";

        public void Accept(ReadOnlySpan<float> samples)
        {
            _acceptedSamples += samples.Length;
            var lagSamples = AudioFormat.MsToSamples(lagMs);
            if (_acceptedSamples > lagSamples)
                _emittedChunks = (_acceptedSamples - lagSamples) / AudioFormat.MsToSamples(480);
        }

        public string CloseSegment()
        {
            ClosedSampleCounts.Add((int)_acceptedSamples);
            var text = _acceptedSamples > 0 ? "流式片段" : "";
            _acceptedSamples = 0;
            _emittedChunks = 0;
            return text;
        }

        public void Dispose() { }
    }

    private sealed class SliceVerifyingRecognizer(Func<int, ReadOnlySpan<float>, string> decode) : ISegmentRecognizer
    {
        private int _call;
        public List<float[]> Slices { get; } = [];

        public string Recognize(ReadOnlySpan<float> samples)
        {
            var copy = samples.ToArray();
            Slices.Add(copy);
            return decode(_call++, samples);
        }

        public void Dispose() { }
    }

    [Fact]
    public async Task MidSpeechForcedCut_ConservesEverySampleAtQuietestValley_EvenWhenLiveStreamLagsBy600Ms()
    {
        // Continuous speech from 0 ms to 13,200 ms with a 600 ms transducer output lag and a single quiet 20 ms valley
        // at [11,360 ms, 11,400 ms). Verifies zero dropped samples, zero double-decoded samples, and seamless phrase join.
        var draft = new DraftSession(hasTarget: true);
        var script = new[] { new ScriptedSegment(0, 13_200, "持续说话无停顿") };
        var live = new LaggedLiveRecognizer(lagMs: 600);
        var secondPass = new SliceVerifyingRecognizer((idx, _) =>
            idx == 0 ? "慢慢慢慢的语料库在。" : "我脑中大量积累。");

        await using var session = new DictationSession(
            live,
            new ScriptedVad(script),
            secondPass,
            draft,
            new DictationOptions
            {
                HardPauseMs = 1000,
                SoftMaxUtteranceMs = 8_000,
                HardMaxUtteranceMs = 12_000,
                PassageMaxChars = 500,
                FirstPassageMinChars = 0
            });
        session.Start();

        var totalSamples = (int)AudioFormat.MsToSamples(13_200);
        var valleyStart = (int)AudioFormat.MsToSamples(11_360);
        var valleyEnd = (int)AudioFormat.MsToSamples(11_400);
        var fullWaveform = new float[totalSamples];
        for (var i = 0; i < totalSamples; i++)
        {
            var amplitude = i >= valleyStart && i < valleyEnd ? 0.001f : 0.25f;
            // Monotonic sample tag inside amplitude variation so exact sample continuity across slices can be proven.
            fullWaveform[i] = amplitude + (i % 997) * 1e-6f;
        }

        var frameSamples = (int)AudioFormat.MsToSamples(40);
        for (var offset = 0; offset < totalSamples; offset += frameSamples)
            session.Push(fullWaveform[offset..Math.Min(totalSamples, offset + frameSamples)]);
        await session.StopAsync();

        Assert.Equal(2, secondPass.Slices.Count);
        var firstSlice = secondPass.Slices[0];
        var secondSlice = secondPass.Slices[1];

        // Cut landed inside the quiet valley rather than mid-syllable at 12,000 ms or lagging at 11,400 - 600 ms.
        Assert.InRange(firstSlice.Length, valleyStart, valleyEnd);
        Assert.Equal(totalSamples, firstSlice.Length + secondSlice.Length);

        // Exact sample-by-sample partition: slice 0 + slice 1 reconstructs the entire spoken waveform with 0 gap and 0 overlap.
        var reconstructed = firstSlice.Concat(secondSlice).ToArray();
        Assert.Equal(fullWaveform, reconstructed);

        // Isolated period on mid-phrase preposition "在。" was stripped seamlessly without breaking or inserting a comma.
        Assert.Equal(["慢慢慢慢的语料库在我脑中大量积累。"], draft.Read(d => d.Units.Select(u => u.Raw).ToList()));
    }

    [Fact]
    public async Task MidSpeechForcedCutOnEnglish_StripsSyntheticPeriodAndKeepsWordSpace()
    {
        var draft = new DraftSession(hasTarget: true);
        var script = new[] { new ScriptedSegment(0, 13_000, "continuous english dictation") };
        var live = new LaggedLiveRecognizer(lagMs: 480);
        var secondPass = new SliceVerifyingRecognizer((idx, _) =>
            idx == 0 ? "We are testing the speech pipeline." : "inside a noisy coffee shop.");

        await using var session = new DictationSession(
            live,
            new ScriptedVad(script),
            secondPass,
            draft,
            new DictationOptions { HardMaxUtteranceMs = 12_000, SoftMaxUtteranceMs = 8_000, PassageMaxChars = 500, FirstPassageMinChars = 0 });
        session.Start();

        for (var pushed = 0; pushed < 13_000; pushed += 40)
            session.Push(new float[AudioFormat.MsToSamples(40)]);
        await session.StopAsync();

        Assert.Equal(
            ["We are testing the speech pipeline inside a noisy coffee shop."],
            draft.Read(d => d.Units.Select(u => u.Raw).ToList()));
    }

    [Fact]
    public async Task SoftMaxUtterance_WaitsForActualSilencePause_InsteadOfCuttingActiveSpeech()
    {
        var script = new[]
        {
            new ScriptedSegment(200, 1400, "第一小句"),
            new ScriptedSegment(1700, 5200, "第二小句正在连续发音不能从中间劈开"),
        };
        await using var h = new Harness(script, hardPauseMs: 5000, softMaxUtteranceMs: 2500, passageMaxChars: 200);

        // At 3600 ms, elapsed since first.StartSample is 3400 ms (> softMax 2500 ms), but speaker is mid-sentence in segment 2.
        h.Push(3600);
        await Task.Delay(50);
        Assert.Empty(h.Session.Segments);

        // Once segment 2 finishes and enters its short pause at 5450+ ms, soft cut closes cleanly in silence.
        h.Push(2000);
        Assert.True(await Until(() => h.Session.Segments.Count == 1));
        Assert.False(h.Session.Segments[0].EndsSentence);
        await h.Session.StopAsync();
        Assert.Equal(["第一小句第二小句正在连续发音不能从中间劈开"], h.Raws);
    }

    [Fact]
    public async Task HesitationDanglingClauses_HoldPeriods_WhileShortPredicateRepliesKeepPeriods()
    {
        var script = new[]
        {
            new ScriptedSegment(200, 1200, "另外一群孩子怎么来，他就。"),
            new ScriptedSegment(1800, 2800, "按照学校节奏来。"),
            new ScriptedSegment(3400, 4200, "我不喜欢这种。"),
            new ScriptedSegment(4800, 5600, "差很多。"),
        };
        var idx = 0;
        string[] decoded = ["另外一群孩子怎么来，他就。", "按照学校节奏来。", "我不喜欢这种。", "差很多。"];
        await using var h = new Harness(
            script,
            hardPauseMs: 400,
            passagePauseMs: 4000,
            passageMaxChars: 1,
            secondPass: new ScriptedSegmentRecognizer(() => decoded[idx++]));

        h.Push(6200);
        await h.Session.StopAsync();

        Assert.Equal(
            ["另外一群孩子怎么来，他就，按照学校节奏来。", "我不喜欢这种。", "差很多。"],
            h.Raws);
    }

    [Fact]
    public async Task Dispose_ReleasesTheRecognizersItWasGiven()
    {
        var secondPass = new ScriptedSegmentRecognizer(() => "好的。");
        var h = new Harness([A], secondPass);
        h.Push(2800);

        await h.DisposeAsync();

        Assert.True(h.Live.Disposed);
        Assert.True(h.Vad.Disposed);
        Assert.True(secondPass.Disposed);
    }

    [Fact]
    public async Task Dispose_WaitsForASlowSecondPassBeforeReleasingTheNativeRecognizers()
    {
        var release = new ManualResetEventSlim();
        var secondPass = new ScriptedSegmentRecognizer(() => { release.Wait(5000); return "好的。"; });
        var h = new Harness([A], secondPass, stopTimeoutMs: 200);
        h.Push(2800);

        await h.DisposeAsync();
        Assert.False(secondPass.Disposed);

        release.Set();
        Assert.True(await Until(() => secondPass.Disposed));
        Assert.True(h.Live.Disposed);
    }

    [Fact]
    public async Task DefaultOptions_EmitFirstCompleteSentenceImmediately_AndStreamSubsequentMicroPassagesWithoutWaitingForSilence()
    {
        // Speaker talks continuously with short 400 ms / 850 ms breath pauses — never reaching a 1.2 s passage silence.
        // Default options must emit the opening sentence right away so live cleanup starts within ~1 s of the sentence end,
        // while subsequent short sentences group up to PassageMaxChars (55 chars).
        var draft = new DraftSession(hasTarget: true);
        var script = new[]
        {
            new ScriptedSegment(200, 1800, "把下载接口的超时时间改成三十秒。"),
            new ScriptedSegment(2700, 4200, "顺便检查一下重试策略。"),
            new ScriptedSegment(4600, 6200, "不要改动鉴权逻辑。"),
        };
        await using var session = new DictationSession(
            new ScriptedLive(script), new ScriptedVad(script), null, draft, new DictationOptions());
        session.Start();

        // Push up to 2650 ms (850 ms silence after first sentence >= HardPauseMs 800 ms, < PassagePauseMs 1200 ms).
        for (var pushed = 0; pushed < 2680; pushed += 40)
            session.Push(new float[AudioFormat.MsToSamples(40)]);

        Assert.True(await Until(() => draft.Read(d => d.Units.Count) == 1));
        Assert.Equal("把下载接口的超时时间改成三十秒。", draft.Read(d => d.Units[0].Raw));

        // Tiny filler opening ("嗯。") below FirstPassageMinChars waits to group with the real sentence.
        var draftWithFiller = new DraftSession(hasTarget: true);
        var fillerScript = new[]
        {
            new ScriptedSegment(200, 600, "嗯。"),
            new ScriptedSegment(1500, 3100, "把下载接口的超时时间改成三十秒。"),
        };
        await using var fillerSession = new DictationSession(
            new ScriptedLive(fillerScript), new ScriptedVad(fillerScript), null, draftWithFiller, new DictationOptions());
        fillerSession.Start();
        for (var pushed = 0; pushed < 1480; pushed += 40)
            fillerSession.Push(new float[AudioFormat.MsToSamples(40)]);
        await Task.Delay(50);
        Assert.Empty(draftWithFiller.Read(d => d.Units));

        for (var pushed = 1480; pushed < 4000; pushed += 40)
            fillerSession.Push(new float[AudioFormat.MsToSamples(40)]);
        Assert.True(await Until(() => draftWithFiller.Read(d => d.Units.Count) == 1));
        Assert.Equal("嗯。把下载接口的超时时间改成三十秒。", draftWithFiller.Read(d => d.Units[0].Raw));

        // Multi-sentence mid-speech slice emits completed earlier sentences immediately and joins the held clause seamlessly.
        var midCutDraft = new DraftSession(hasTarget: true);
        var midCutScript = new[] { new ScriptedSegment(0, 9_000, "连续口述不喘气") };
        var midCalls = 0;
        await using var midCutSession = new DictationSession(
            new ScriptedLive(midCutScript),
            new ScriptedVad(midCutScript),
            new ScriptedSegmentRecognizer(() => Interlocked.Increment(ref midCalls) == 1 ? "第一句已经说完了。第二句切在" : "半路也接上了。"),
            midCutDraft,
            new DictationOptions());
        midCutSession.Start();
        for (var pushed = 0; pushed < 8_200; pushed += 40)
            midCutSession.Push(new float[AudioFormat.MsToSamples(40)]);

        Assert.True(await Until(() => midCutDraft.Read(d => d.Units.Count) == 1));
        Assert.Equal("第一句已经说完了。", midCutDraft.Read(d => d.Units[0].Raw));

        await midCutSession.StopAsync();
        Assert.Equal(["第一句已经说完了。", "第二句切在半路也接上了。"], midCutDraft.Read(d => d.Units.Select(u => u.Raw).ToList()));
    }
}
