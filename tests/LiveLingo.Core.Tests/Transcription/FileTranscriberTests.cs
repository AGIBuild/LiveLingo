using LiveLingo.Core.Dictation;
using LiveLingo.Core.Tests.Support;
using LiveLingo.Core.Transcription;

namespace LiveLingo.Core.Tests.Transcription;

public class FileTranscriberTests
{
    private static float[] Audio(int seconds) => new float[seconds * AudioFormat.SampleRate];

    [Fact]
    public void EverySpeechSpanIsRecognizedWithItsPositionInTheFile()
    {
        var script = new[] { new ScriptedSegment(1000, 3000, "first"), new ScriptedSegment(5000, 8000, "second") };
        var texts = new Queue<string>(["first", "second"]);

        var result = FileTranscriber.Transcribe(
            Audio(10), new ScriptedVad(script), new ScriptedSegmentRecognizer(texts.Dequeue), null, CancellationToken.None);

        Assert.Equal(
            [
                new TimedSegment(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3), "first"),
                new TimedSegment(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(8), "second")
            ],
            result);
    }

    [Fact]
    public void ASpanThatRecognizesNothing_LeavesNoSegment_AndTextIsTrimmed()
    {
        var script = new[] { new ScriptedSegment(0, 1000, "x"), new ScriptedSegment(2000, 3000, "y") };
        var texts = new Queue<string>(["   ", "  kept  "]);

        var result = FileTranscriber.Transcribe(
            Audio(5), new ScriptedVad(script), new ScriptedSegmentRecognizer(texts.Dequeue), null, CancellationToken.None);

        Assert.Equal("kept", Assert.Single(result).Text);
    }

    [Fact]
    public void TheRecognizerReceivesExactlyTheSpeechOfEachSpan()
    {
        var script = new[] { new ScriptedSegment(1000, 3500, "x") };
        var recorder = new AudioLengthRecorder("x");

        FileTranscriber.Transcribe(Audio(6), new ScriptedVad(script), recorder, null, CancellationToken.None);

        Assert.Equal([2500.0], recorder.Milliseconds.ToArray());
    }

    [Fact]
    public void ProgressReportsEverySecondOfAudio_AndEndsAtOne()
    {
        var reports = new List<double>();

        FileTranscriber.Transcribe(
            Audio(3), new ScriptedVad([]), new ScriptedSegmentRecognizer(() => ""),
            new SyncProgress(reports), CancellationToken.None);

        Assert.Equal(3, reports.Count);
        Assert.Equal(1.0 / 3, reports[0], 6);
        Assert.Equal(2.0 / 3, reports[1], 6);
        Assert.Equal(1.0, reports[2]);
    }

    [Fact]
    public void ALongStretchOfSpeech_IsCutAtItsQuietestMomentNearTheLimit_NeverLongerThanTheLimit()
    {
        var samples = Audio(30);
        Array.Fill(samples, 0.5f);
        var quiet = 10 * AudioFormat.SampleRate; // a gap inside the last 4 s of the first 12 s window
        Array.Clear(samples, quiet, 1000);
        var vad = new ScriptedVad([new ScriptedSegment(0, 28_000, "x")]);
        var recorder = new AudioLengthRecorder("x");

        var result = FileTranscriber.Transcribe(samples, vad, recorder, null, CancellationToken.None);

        Assert.All(result, s => Assert.True((s.End - s.Start).TotalSeconds <= 12));
        Assert.Equal(28, result[^1].End.TotalSeconds, 3);
        Assert.Equal(quiet + 160, Math.Round(result[0].End.TotalSeconds * AudioFormat.SampleRate), 0);
        for (var i = 1; i < result.Count; i++)
            Assert.Equal(result[i - 1].End, result[i].Start);
    }

    [Fact]
    public void ACancelledToken_StopsTheWork()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() => FileTranscriber.Transcribe(
            Audio(3), new ScriptedVad([]), new ScriptedSegmentRecognizer(() => "x"), null, cts.Token));
    }

    [Fact]
    public void SpeechStillOpenAtTheEnd_IsFlushedAndRecognized()
    {
        var vad = new FlushingVad();

        var result = FileTranscriber.Transcribe(
            Audio(2), vad, new ScriptedSegmentRecognizer(() => "tail"), null, CancellationToken.None);

        Assert.Equal("tail", Assert.Single(result).Text);
    }

    private sealed class SyncProgress(List<double> sink) : IProgress<double>
    {
        public void Report(double value) => sink.Add(value);
    }

    private sealed class FlushingVad : IVoiceActivityDetector
    {
        private bool _flushed;
        private bool _taken;
        public bool IsSpeechActive => !_flushed;
        public void Accept(ReadOnlySpan<float> samples) { }
        public void Flush() => _flushed = true;

        public bool TryTake(out SpeechSpan span)
        {
            span = new SpeechSpan(AudioFormat.SampleRate / 2, AudioFormat.SampleRate * 2 + 1000);
            if (!_flushed || _taken)
                return false;
            _taken = true;
            return true;
        }

        public void Dispose() { }
    }
}
