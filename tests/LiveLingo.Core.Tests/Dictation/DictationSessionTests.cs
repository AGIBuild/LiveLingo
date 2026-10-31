using LiveLingo.Core.Dictation;
using LiveLingo.Core.Drafting;
using LiveLingo.Core.Tests.Support;

namespace LiveLingo.Core.Tests.Dictation;

public class DictationSessionTests
{
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
}
