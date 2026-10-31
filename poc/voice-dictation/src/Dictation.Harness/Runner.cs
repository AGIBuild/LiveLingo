using System.Diagnostics;
using Dictation.Audio;
using Dictation.Core;
using Dictation.Models;

namespace Dictation.Harness;

public sealed record RunConfig(
    string Live,
    string SecondPass,
    string Cleaner,
    double Pace,
    int CommitAfterMs,
    bool UseGlossary,
    float VadMinSilenceSeconds,
    float VadThreshold,
    int HoldbackMs,
    int HardPauseMs,
    int SoftMaxMs,
    int ContextSentences,
    IReadOnlyList<string> ExtraGlossary);

public sealed record SentenceOut(
    int Id, string Raw, string? Cleaned, string State, string? RejectReason, string? RejectedText,
    double FormedLagMs, double CleanedLagMs, double CleanupMs);

public sealed record SegmentOut(
    int Id, double SpeechStartMs, double SpeechEndMs, double FirstTextLagMs, double CloseLagMs, double SettleLagMs,
    double SecondPassMs, string Live, string Settled);

public sealed record ScenarioResult(
    string Id,
    string Category,
    string Voice,
    int AudioMs,
    string Spoken,
    string Live,
    string Raw,
    string Final,
    double LiveCer,
    double RawCer,
    double FinalCer,
    double FinalToRawLength,
    int MustKeepTotal, int MustKeepRaw, int MustKeepFinal,
    int ForbidRaw, int ForbidFinal,
    int TermsTotal, int TermsLive, int TermsRaw, int TermsFinal,
    int Segments,
    double FirstTextP50, double FirstTextP95,
    double CloseLagP50, double CloseLagP95,
    double SettleLagP50,
    double SecondPassMsP50,
    double CleanLagP50, double CleanLagP95, double CleanLagMax,
    double CleanupMsP50,
    int LiveRevisions, int LiveRetractedChars,
    double MaxBacklogMs,
    double CommitWaitMs, bool CommitTimedOut,
    int Cleaned, int Rejected, int RawOnly,
    int FalseGlossaryInsertions,
    List<SentenceOut> Sentences,
    List<SegmentOut> SegmentDetails);

public sealed record SerialResult(
    string Id,
    int AudioMs,
    string Raw,
    string Final,
    double RecognizeMs,
    double CleanMs,
    double WaitMs,
    double RawCer,
    int MustKeepTotal, int MustKeepRaw, int MustKeepFinal,
    int ForbidFinal);

public static class Runner
{
    public static async Task<ScenarioResult> RunAsync(
        Timeline t, string wavPath, RunConfig cfg, ISegmentRecognizer? secondPass, ITextCleaner? cleaner)
    {
        var audio = WavFile.ReadMono16k(wavPath);
        var glossary = (cfg.UseGlossary ? t.Protect : []).Concat(cfg.ExtraGlossary).Distinct().ToList();

        using var live = ModelCatalog.Live(cfg.Live);
        using var vad = ModelCatalog.Vad(cfg.VadMinSilenceSeconds, cfg.VadThreshold);
        await using var session = new DictationSession(live, vad, secondPass, cleaner,
            new SessionOptions
            {
                Glossary = glossary,
                CleanupHoldbackMs = cfg.HoldbackMs,
                HardPauseMs = cfg.HardPauseMs,
                SoftMaxUtteranceMs = cfg.SoftMaxMs,
                CleanupContextSentences = cfg.ContextSentences,
            });

        session.Start();

        const int frameSamples = 640; // 40 ms, the size a microphone callback would deliver
        var stopAt = Math.Min(audio.Length, (t.LastSpeechEndMs + cfg.CommitAfterMs) * 16);
        for (var offset = 0; offset < stopAt; offset += frameSamples)
        {
            var due = offset / 16.0 / cfg.Pace;
            while (session.NowMs < due) Thread.Sleep(1);
            session.Push(audio[offset..Math.Min(stopAt, offset + frameSamples)]);
        }

        var commit = await session.CommitAsync();
        return Summarize(t, cfg, glossary, session.Report(), commit);
    }

    private static ScenarioResult Summarize(Timeline t, RunConfig cfg, IReadOnlyList<string> glossary, SessionReport report, CommitResult commit)
    {
        double Wall(double audioMs) => audioMs / cfg.Pace;
        double SpeechEnd(Segment s) => Wall(AudioFormat.SamplesToMs(s.Speech.EndSample));

        var withText = report.Segments.Where(s => s.LiveFinal.Length > 0).ToList();
        var firstText = withText.Where(s => s.FirstTextMs >= 0)
            .Select(s => s.FirstTextMs - Wall(AudioFormat.SamplesToMs(s.Speech.StartSample))).ToList();
        var closeLag = withText.Select(s => s.ClosedMs - SpeechEnd(s)).ToList();
        var settleLag = withText.Where(s => s.SettledMs >= 0).Select(s => s.SettledMs - SpeechEnd(s)).ToList();
        var secondPassMs = withText.Where(s => s.SecondPassDurationMs >= 0).Select(s => s.SecondPassDurationMs).ToList();

        var cleanLag = new List<double>();
        var cleanMs = new List<double>();
        var sentences = new List<SentenceOut>();
        foreach (var s in report.Sentences)
        {
            var end = SpeechEnd(report.Segments[Math.Min(s.EndSegmentId, report.Segments.Count - 1)]);
            var lag = s.CleanedMs >= 0 ? s.CleanedMs - end : double.NaN;
            if (!double.IsNaN(lag)) cleanLag.Add(lag);
            if (s.CleanupDurationMs >= 0) cleanMs.Add(s.CleanupDurationMs);
            sentences.Add(new SentenceOut(s.Id, s.Raw, s.Cleaned, s.State.ToString(), s.RejectReason, s.RejectedText,
                s.FormedMs - end, lag, s.CleanupDurationMs));
        }

        var spoken = t.SpokenText;
        var spokenNorm = Strings.Normalize(spoken);
        // A glossary term that appears in a cleaned sentence although it was neither recognized nor spoken is an invention.
        var falseInsertions = report.Sentences.Where(s => s.Cleaned is not null).Sum(s =>
            glossary.Select(Strings.Normalize).Count(g =>
                g.Length > 0
                && Strings.Normalize(s.Cleaned!).Contains(g, StringComparison.Ordinal)
                && !Strings.Normalize(s.Raw).Contains(g, StringComparison.Ordinal)
                && !spokenNorm.Contains(g, StringComparison.Ordinal)));
        var (termsLive, termsTotal) = Scoring.Terms(commit.LiveText, t.Protect);
        var (termsRaw, _) = Scoring.Terms(commit.RawText, t.Protect);
        var (termsFinal, _) = Scoring.Terms(commit.Text, t.Protect);
        var rawLen = Strings.Normalize(commit.RawText).Length;

        return new ScenarioResult(
            t.Id, t.Category, t.Voice, t.DurationMs, spoken, commit.LiveText, commit.RawText, commit.Text,
            Scoring.Cer(spoken, commit.LiveText), Scoring.Cer(spoken, commit.RawText), Scoring.Cer(spoken, commit.Text),
            rawLen == 0 ? 1 : (double)Strings.Normalize(commit.Text).Length / rawLen,
            t.MustKeep.Count, Scoring.Hits(commit.RawText, t.MustKeep), Scoring.Hits(commit.Text, t.MustKeep),
            Scoring.ForbiddenHits(commit.RawText, t.Forbid), Scoring.ForbiddenHits(commit.Text, t.Forbid),
            termsTotal, termsLive, termsRaw, termsFinal,
            report.Segments.Count,
            Scoring.Percentile(firstText, 0.5), Scoring.Percentile(firstText, 0.95),
            Scoring.Percentile(closeLag, 0.5), Scoring.Percentile(closeLag, 0.95),
            Scoring.Percentile(settleLag, 0.5),
            Scoring.Percentile(secondPassMs, 0.5),
            Scoring.Percentile(cleanLag, 0.5), Scoring.Percentile(cleanLag, 0.95),
            cleanLag.Count == 0 ? double.NaN : cleanLag.Max(),
            Scoring.Percentile(cleanMs, 0.5),
            report.Segments.Sum(s => s.LiveRevisions), report.Segments.Sum(s => s.LiveRetractedChars),
            report.Backlog.Count == 0 ? 0 : report.Backlog.Max(b => b.AudioBacklogMs),
            commit.WaitMs, commit.TimedOut,
            commit.Cleaned, commit.Rejected, commit.RawOnly,
            falseInsertions,
            sentences,
            report.Segments.Select(s => new SegmentOut(
                s.Id, Wall(AudioFormat.SamplesToMs(s.Speech.StartSample)), SpeechEnd(s),
                s.FirstTextMs >= 0 ? s.FirstTextMs - Wall(AudioFormat.SamplesToMs(s.Speech.StartSample)) : double.NaN,
                s.ClosedMs - SpeechEnd(s), s.SettledMs >= 0 ? s.SettledMs - SpeechEnd(s) : double.NaN,
                s.SecondPassDurationMs, s.LiveFinal, s.SettledText ?? "")).ToList());
    }

    /// <summary>
    /// Baseline A: wait for the end of speech, then recognize every segment and clean the whole text in one call.
    /// Only the time after the speech ends matters, so audio is not replayed in real time.
    /// </summary>
    public static async Task<SerialResult> RunSerialAsync(
        Timeline t, string wavPath, ISegmentRecognizer recognizer, ITextCleaner? cleaner, float vadMinSilence, bool useGlossary)
    {
        var audio = WavFile.ReadMono16k(wavPath);
        var stopAt = Math.Min(audio.Length, (t.LastSpeechEndMs + 200) * 16);
        var recorded = audio[..stopAt];

        var clock = Stopwatch.StartNew();
        var spans = new List<SpeechSpan>();
        using (var vad = ModelCatalog.Vad(vadMinSilence))
        {
            const int frame = 640; // 40 ms at 16 kHz, as a live recorder delivers it
            for (var offset = 0; offset < recorded.Length; offset += frame)
            {
                vad.Accept(recorded[offset..Math.Min(recorded.Length, offset + frame)]);
                while (vad.TryTake(out var span)) spans.Add(span);
            }
            vad.Flush();
            while (vad.TryTake(out var span)) spans.Add(span);
        }

        var raw = "";
        clock.Restart(); // VAD runs during recording; only work after the user stops counts as wait
        foreach (var span in spans)
        {
            var pad = AudioFormat.MsToSamples(200);
            var piece = recognizer.Recognize(recorded[(int)Math.Max(0, span.StartSample - pad)..(int)Math.Min(recorded.Length, span.EndSample + pad)]);
            raw = SentenceSplitter.Join(raw, piece);
        }
        var recognizeMs = clock.Elapsed.TotalMilliseconds;

        var final = raw;
        if (cleaner is not null)
            final = await cleaner.CleanAsync(new CleanupRequest(raw, "", false, useGlossary ? t.Protect : []), CancellationToken.None);
        var waitMs = clock.Elapsed.TotalMilliseconds;

        return new SerialResult(
            t.Id, t.DurationMs, raw, final, recognizeMs, waitMs - recognizeMs, waitMs,
            Scoring.Cer(t.SpokenText, raw),
            t.MustKeep.Count, Scoring.Hits(raw, t.MustKeep), Scoring.Hits(final, t.MustKeep),
            Scoring.ForbiddenHits(final, t.Forbid));
    }
}
