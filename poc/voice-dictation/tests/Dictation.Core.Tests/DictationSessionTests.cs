using Dictation.Core;

namespace Dictation.Core.Tests;

public class DictationSessionTests
{
    private static readonly ScriptedSegment A = new(300, 2300, "把下载超时设成三十秒。");
    private static readonly ScriptedSegment B = new(2800, 4800, "只改下载接口，其他请求不要动。");
    private static readonly ScriptedSegment C = new(5300, 7000, "然后补一个取消操作的测试。");
    private static readonly IReadOnlyList<ScriptedSegment> Script = [A, B, C];

    private static DictationSession NewSession(
        ITextCleaner? cleaner,
        ISegmentRecognizer? secondPass = null,
        int holdbackMs = 60_000,
        IReadOnlyList<ScriptedSegment>? script = null,
        int hardPauseMs = 300,
        int softMaxUtteranceMs = 60_000,
        int maxTailChars = 90,
        ScriptedLive? live = null)
    {
        script ??= Script;
        var session = new DictationSession(
            live ?? new ScriptedLive(script), new ScriptedVad(script), secondPass, cleaner,
            new SessionOptions
            {
                CleanupHoldbackMs = holdbackMs,
                CommitTimeoutMs = 5000,
                HardPauseMs = hardPauseMs,
                SoftMaxUtteranceMs = softMaxUtteranceMs,
                MaxTailChars = maxTailChars,
            });
        session.Start();
        return session;
    }

    [Fact]
    public async Task A_hard_pause_ends_the_utterance_and_its_unpunctuated_text_becomes_a_sentence()
    {
        var script = new[] { new ScriptedSegment(300, 2300, "把下载超时设成三十秒") };
        await using var session = NewSession(RecordingCleaner.Marking(), script: script);

        Wait.PushMs(session, 2500); // silence has not reached the hard pause yet
        Assert.Empty(session.Report().Segments);
        Wait.PushMs(session, 200);
        Assert.True(await Wait.UntilAsync(() => session.Report().Sentences.Count == 1));
        Assert.Equal("把下载超时设成三十秒", session.Report().Sentences[0].Raw);
        Assert.True(session.Report().Segments[0].EndsSentence);
    }

    [Fact]
    public async Task A_short_pause_neither_resets_the_live_stream_nor_splits_the_sentence()
    {
        var script = new[]
        {
            new ScriptedSegment(300, 1800, "如果这个请求失败了"),
            new ScriptedSegment(2100, 4000, "就重试两次，不要重复提交"),
        };
        var live = new ScriptedLive(script);
        await using var session = NewSession(RecordingCleaner.Marking(), script: script, hardPauseMs: 800, live: live);

        Wait.PushMs(session, 4400);
        var commit = await session.CommitAsync();

        Assert.Equal(1, live.Closes); // a fresh stream starting mid-sentence recognized badly; only the hard end resets it
        Assert.Single(session.Report().Segments);
        Assert.Equal("如果这个请求失败了就重试两次，不要重复提交*", commit.Text);
    }

    [Fact]
    public async Task An_utterance_that_gets_too_long_is_cut_at_the_next_short_pause_without_ending_the_sentence()
    {
        var script = new[]
        {
            new ScriptedSegment(300, 1800, "先把下载接口改掉"),
            new ScriptedSegment(2100, 4000, "再补一个取消测试"),
        };
        await using var session = NewSession(RecordingCleaner.Marking(), script: script, hardPauseMs: 5000, softMaxUtteranceMs: 1000);

        Wait.PushMs(session, 4400);
        var commit = await session.CommitAsync();

        var segments = session.Report().Segments;
        Assert.Equal(2, segments.Count);
        Assert.False(segments[0].EndsSentence); // soft cut: the sentence continues into the next utterance
        Assert.Equal(1, commit.Sentences);
        Assert.Equal("先把下载接口改掉再补一个取消测试*", commit.Text);
    }

    [Fact]
    public async Task A_long_tail_becomes_a_sentence_without_waiting_for_a_pause()
    {
        var script = new[]
        {
            new ScriptedSegment(300, 1800, "先把下载接口改掉"),
            new ScriptedSegment(2100, 3600, "再补一个取消测试"),
        };
        await using var session = NewSession(null, script: script, hardPauseMs: 60_000, softMaxUtteranceMs: 1000, maxTailChars: 10);

        Wait.PushMs(session, 4000);
        Assert.True(await Wait.UntilAsync(() => session.Report().Sentences.Count >= 1));
        Assert.Equal("先把下载接口改掉再补一个取消测试", session.Report().Sentences[0].Raw);
    }

    [Fact]
    public async Task A_sentence_is_cleaned_while_the_next_one_is_still_being_spoken()
    {
        var cleaner = RecordingCleaner.Marking();
        await using var session = NewSession(cleaner);

        Wait.PushMs(session, 2600); // A is over and its pause has been seen; nothing follows it yet
        await Wait.UntilAsync(() => session.Report().Sentences.Count == 1);
        await Task.Delay(300);
        Assert.Empty(cleaner.Requests); // held back: a pause might still be mid-sentence

        Wait.PushMs(session, 600); // B has started
        Assert.True(await Wait.UntilAsync(() => cleaner.Requests.Count == 1));
        Assert.True(cleaner.Requests.TryPeek(out var request));
        Assert.Equal(A.Text, request!.Target);
        Assert.True(request.Continues); // the cleaner is told the speaker went on after the pause

        Wait.PushMs(session, 4400);
        var commit = await session.CommitAsync();
        Assert.Equal("把下载超时设成三十秒。*只改下载接口，其他请求不要动。*然后补一个取消操作的测试。*", commit.Text);
        Assert.Equal(3, commit.Cleaned);
    }

    [Fact]
    public async Task The_last_sentence_is_cleaned_after_the_holdback_when_nothing_follows()
    {
        var cleaner = RecordingCleaner.Marking();
        await using var session = NewSession(cleaner, holdbackMs: 400);

        Wait.PushMs(session, 2600);
        await Task.Delay(150);
        Assert.Empty(cleaner.Requests);

        Assert.True(await Wait.UntilAsync(() => cleaner.Requests.Count == 1));
        Assert.True(cleaner.Requests.TryPeek(out var request));
        Assert.False(request!.Continues);
    }

    [Fact]
    public async Task Commit_returns_the_same_result_when_called_twice_and_cleans_each_sentence_once()
    {
        var cleaner = RecordingCleaner.Marking();
        await using var session = NewSession(cleaner);
        Wait.PushMs(session, 7400);

        var first = session.CommitAsync();
        var second = session.CommitAsync();
        Assert.Same(await first, await second);
        Assert.Equal(3, cleaner.Requests.Count);
        Assert.Throws<InvalidOperationException>(() => session.Push(new float[640]));
    }

    [Fact]
    public async Task A_user_edit_wins_over_a_cleanup_result_that_arrives_later()
    {
        var release = new TaskCompletionSource();
        var cleaner = new RecordingCleaner(async (r, _) =>
        {
            await release.Task;
            return r.Target + "*";
        });
        await using var session = NewSession(cleaner);

        Wait.PushMs(session, 3200);
        Assert.True(await Wait.UntilAsync(() => cleaner.Requests.Count == 1)); // sentence 0 is in flight
        session.EditSentence(0, "把下载超时设成 30 秒。");
        release.SetResult();
        Wait.PushMs(session, 4200);

        var commit = await session.CommitAsync();
        Assert.StartsWith("把下载超时设成 30 秒。", commit.Text);
        Assert.Equal(SentenceState.UserOwned, session.Report().Sentences[0].State);
    }

    [Fact]
    public async Task A_cleanup_that_fails_the_fidelity_gate_keeps_the_raw_sentence()
    {
        var cleaner = new RecordingCleaner((r, _) =>
            Task.FromResult(r.Target + "并且顺便重构整个下载模块，同时升级所有依赖包。"));
        await using var session = NewSession(cleaner, script: [A]);
        Wait.PushMs(session, 2700);

        var commit = await session.CommitAsync();
        Assert.Equal(A.Text, commit.Text);
        Assert.Equal(1, commit.Rejected);
        Assert.Equal(0, commit.Cleaned);
    }

    [Fact]
    public async Task Second_pass_text_replaces_the_live_text_before_sentences_are_formed()
    {
        var secondPass = new ScriptedSegmentRecognizer(() => "把下载超时设成 30 秒。");
        await using var session = NewSession(RecordingCleaner.Marking(), secondPass, script: [A]);
        Wait.PushMs(session, 2700);

        var commit = await session.CommitAsync();
        Assert.Equal("把下载超时设成 30 秒。*", commit.Text);
        Assert.Equal(A.Text, commit.LiveText); // the live result stays available for comparison
        Assert.True(session.Report().Segments[0].SecondPassDurationMs >= 0);
    }

    [Fact]
    public async Task The_second_pass_decodes_the_audio_the_live_stream_consumed_even_where_the_detector_heard_no_speech()
    {
        // Observed on real far-field meeting recordings: the detector reported 0.5 s of an 8 s sentence,
        // the second pass decoded only that slice and replaced the correct live text with "落实好消防。".
        var spoken = new ScriptedSegment(300, 7300, "主要是对各种突发类事件深入进行调查处理落实好消防措施");
        var detected = new ScriptedSegment(6800, 7300, "x");
        var secondPass = new AudioLengthRecorder("主要是对各种突发类事件深入进行调查处理，落实好消防措施。");
        await using var session = new DictationSession(
            new ScriptedLive([spoken]), new ScriptedVad([detected]), secondPass, RecordingCleaner.Marking(),
            new SessionOptions { HardPauseMs = 300, CleanupHoldbackMs = 60_000, CommitTimeoutMs = 5000 });
        session.Start();

        Wait.PushMs(session, 8000);
        Assert.True(await Wait.UntilAsync(() => !secondPass.Milliseconds.IsEmpty));
        Assert.True(secondPass.Milliseconds.First() >= 6500, $"second pass received {secondPass.Milliseconds.First():F0} ms");
    }

    [Fact]
    public async Task The_second_pass_does_not_decode_the_silence_of_the_hard_pause()
    {
        // Observed: Qwen3-ASR invents words ("然后。") on trailing silence, which raised the error rate on clean clips.
        var secondPass = new AudioLengthRecorder("文本。");
        await using var session = NewSession(RecordingCleaner.Marking(), secondPass, script: [A], hardPauseMs: 800);

        Wait.PushMs(session, 4000);
        Assert.True(await Wait.UntilAsync(() => !secondPass.Milliseconds.IsEmpty));
        // Speech ends at 2300 ms, the pause lasts 800 ms; the decoded audio ends 300 ms plus the padding after the speech.
        Assert.True(secondPass.Milliseconds.First() <= 2900, $"decoded {secondPass.Milliseconds.First():F0} ms");
    }

    [Fact]
    public async Task Second_pass_audio_never_reaches_back_before_the_previous_cut()
    {
        // Padding that crosses the cut makes the word at the boundary appear in both segments ("首先。首先，").
        var secondPass = new AudioLengthRecorder("文本。");
        await using var session = NewSession(RecordingCleaner.Marking(), secondPass, script: [A, B], hardPauseMs: 300);

        Wait.PushMs(session, 6000);
        Assert.True(await Wait.UntilAsync(() => secondPass.Milliseconds.Count == 2));

        // The second utterance closes at 5120 ms. Without overlap the two segments decode exactly the 5120 ms that were heard.
        var lengths = secondPass.Milliseconds.ToArray();
        Assert.True(lengths.Sum() <= 5200, $"segments decoded {lengths.Sum():F0} ms for 5120 ms of audio: {string.Join(", ", lengths)}");
    }

    [Fact]
    public async Task A_second_pass_repetition_loop_is_dropped_and_the_live_text_is_kept()
    {
        // Observed: Fun-ASR-Nano returned 334 characters for 3 seconds of audio.
        var secondPass = new ScriptedSegmentRecognizer(() => "升级到" + string.Concat(Enumerable.Repeat("点四", 200)));
        await using var session = NewSession(RecordingCleaner.Marking(), secondPass, script: [A]);
        Wait.PushMs(session, 2700);

        var commit = await session.CommitAsync();
        Assert.Equal(A.Text + "*", commit.Text);
        Assert.True(session.Report().Segments[0].SecondPassFailed);
    }

    [Fact]
    public async Task Without_a_cleaner_the_raw_text_is_committed()
    {
        await using var session = NewSession(cleaner: null);
        Wait.PushMs(session, 7400);

        var commit = await session.CommitAsync();
        Assert.Equal(string.Concat(Script.Select(s => s.Text)), commit.Text);
        Assert.Equal(3, commit.RawOnly);
    }

    [Fact]
    public async Task Commit_only_waits_for_the_unfinished_tail_when_earlier_sentences_were_cleaned_during_speech()
    {
        var cleaner = RecordingCleaner.Marking(delayMs: 400);
        await using var session = NewSession(cleaner);

        Wait.PushMs(session, 5600); // A and B are over, C has started
        Assert.True(await Wait.UntilAsync(
            () => session.Report().Sentences.Count(s => s.State == SentenceState.Cleaned) == 2, 4000));

        Wait.PushMs(session, 1800);
        var commit = await session.CommitAsync();

        Assert.Equal(3, commit.Cleaned);
        Assert.InRange(commit.WaitMs, 0, 900); // one 400 ms cleanup plus scheduling; cleaning all three at commit would take 1200+
    }

    [Fact]
    public async Task A_pause_that_splits_one_sentence_is_stitched_by_the_cleaner_context()
    {
        var split = new[]
        {
            new ScriptedSegment(300, 1800, "如果这个请求失败了。"),
            new ScriptedSegment(3300, 5300, "就重试两次，但是不要重复提交订单。"),
        };
        var cleaner = new RecordingCleaner((r, _) => Task.FromResult(
            r.Continues ? r.Target.TrimEnd('。') + "，" : r.Target));
        await using var session = NewSession(cleaner, script: split);

        Wait.PushMs(session, 2100);
        await Task.Delay(200);
        Assert.Empty(cleaner.Requests); // 1.5 s pause: still waiting for what follows

        Wait.PushMs(session, 3600);
        var commit = await session.CommitAsync();
        Assert.Equal("如果这个请求失败了，就重试两次，但是不要重复提交订单。", commit.Text);
    }
}
