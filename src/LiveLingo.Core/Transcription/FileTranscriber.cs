using LiveLingo.Core.Dictation;

namespace LiveLingo.Core.Transcription;

/// <summary>Cuts a recording at its pauses and recognizes every stretch of speech, keeping where it was said.</summary>
public static class FileTranscriber
{
    /// <summary>Audio is given to the detector in pieces of this length so cancellation and progress stay responsive.</summary>
    private const int ChunkSamples = AudioFormat.SampleRate;

    /// <summary>
    /// The second-pass model decodes at most about this much audio reliably (it truncates longer input), so a longer
    /// stretch of speech is cut at its quietest moment in the last <see cref="CutSearchSamples"/>.
    /// </summary>
    public const int MaxSpanSamples = 12 * AudioFormat.SampleRate;

    private const int CutSearchSamples = 4 * AudioFormat.SampleRate;
    private const int FrameSamples = AudioFormat.SampleRate / 50;

    public static IReadOnlyList<TimedSegment> Transcribe(
        float[] samples,
        IVoiceActivityDetector vad,
        ISegmentRecognizer recognizer,
        IProgress<double>? progress,
        CancellationToken ct)
    {
        var segments = new List<TimedSegment>();

        void Recognize(int start, int end)
        {
            ct.ThrowIfCancellationRequested();
            var text = recognizer.Recognize(samples.AsSpan(start, end - start)).Trim();
            if (text.Length == 0)
                return;

            segments.Add(new TimedSegment(
                TimeSpan.FromMilliseconds(AudioFormat.SamplesToMs(start)),
                TimeSpan.FromMilliseconds(AudioFormat.SamplesToMs(end)),
                text));
        }

        void Drain()
        {
            while (vad.TryTake(out var span))
            {
                var start = (int)Math.Clamp(span.StartSample, 0, samples.Length);
                var end = (int)Math.Clamp(span.EndSample, start, samples.Length);
                while (end - start > MaxSpanSamples)
                {
                    var cut = QuietestPoint(samples, start + MaxSpanSamples - CutSearchSamples, start + MaxSpanSamples);
                    Recognize(start, cut);
                    start = cut;
                }
                Recognize(start, end);
            }
        }

        for (var offset = 0; offset < samples.Length; offset += ChunkSamples)
        {
            ct.ThrowIfCancellationRequested();
            vad.Accept(samples.AsSpan(offset, Math.Min(ChunkSamples, samples.Length - offset)));
            Drain();
            progress?.Report(Math.Min(1.0, (double)(offset + ChunkSamples) / samples.Length));
        }

        vad.Flush();
        Drain();
        return segments;
    }

    /// <summary>The middle of the quietest 20 ms frame in [from, to): where a long stretch can be cut with the least damage.</summary>
    private static int QuietestPoint(float[] samples, int from, int to)
    {
        var best = from;
        var bestEnergy = double.MaxValue;
        for (var frame = from; frame + FrameSamples <= to; frame += FrameSamples)
        {
            var energy = 0.0;
            for (var i = frame; i < frame + FrameSamples; i++)
                energy += samples[i] * samples[i];
            if (energy < bestEnergy)
            {
                bestEnergy = energy;
                best = frame;
            }
        }
        return best + FrameSamples / 2;
    }
}
