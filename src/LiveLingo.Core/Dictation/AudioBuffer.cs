namespace LiveLingo.Core.Dictation;

/// <summary>Append-only 16 kHz audio kept so closed segments can be re-decoded.</summary>
public sealed class AudioBuffer
{
    private readonly object _gate = new();
    private readonly List<(long Start, float[] Data)> _chunks = new();
    private long _total;

    public long TotalSamples
    {
        get { lock (_gate) return _total; }
    }

    public void Append(ReadOnlySpan<float> samples)
    {
        var copy = samples.ToArray();
        lock (_gate)
        {
            _chunks.Add((_total, copy));
            _total += copy.Length;
        }
    }

    /// <summary>Copies [start, end) clamped to the audio that is still retained.</summary>
    public float[] Slice(long start, long end)
    {
        lock (_gate)
        {
            start = Math.Max(0, start);
            end = Math.Min(_total, end);
            if (end <= start) return [];

            var result = new float[end - start];
            foreach (var (chunkStart, data) in _chunks)
            {
                var chunkEnd = chunkStart + data.Length;
                if (chunkEnd <= start) continue;
                if (chunkStart >= end) break;
                var from = Math.Max(start, chunkStart);
                var to = Math.Min(end, chunkEnd);
                Array.Copy(data, from - chunkStart, result, from - start, to - from);
            }
            return result;
        }
    }

    public void TrimBefore(long sample)
    {
        lock (_gate)
            _chunks.RemoveAll(c => c.Start + c.Data.Length <= sample);
    }
}
