using System.Collections.Concurrent;
using Dictation.Core;

namespace Dictation.Core.Tests;

/// <summary>One scripted utterance: speech occupies [StartMs, EndMs) and the recognizer finally returns Text.</summary>
public sealed record ScriptedSegment(int StartMs, int EndMs, string Text);

/// <summary>
/// Behaves like one continuous stream: text of every scripted segment is revealed as audio passes,
/// and closing hands over everything spoken since the previous close.
/// </summary>
public sealed class ScriptedLive : ILiveRecognizer
{
    private readonly IReadOnlyList<ScriptedSegment> _script;
    private long _total;
    private int _consumed;

    public ScriptedLive(IReadOnlyList<ScriptedSegment> script) => _script = script;

    /// <summary>How many times the stream was reset. A short pause must not reset it.</summary>
    public int Closes { get; private set; }

    public string CurrentText
    {
        get
        {
            var ms = AudioFormat.SamplesToMs(_total);
            var text = "";
            for (var i = _consumed; i < _script.Count && ms > _script[i].StartMs; i++)
            {
                var s = _script[i];
                var progress = Math.Min(1.0, (ms - s.StartMs) / (s.EndMs - s.StartMs));
                text += s.Text[..(int)(s.Text.Length * progress)];
            }
            return text;
        }
    }

    public void Accept(ReadOnlySpan<float> samples) => _total += samples.Length;

    public string CloseSegment()
    {
        Closes++;
        var ms = AudioFormat.SamplesToMs(_total);
        var text = "";
        while (_consumed < _script.Count && _script[_consumed].EndMs <= ms)
            text += _script[_consumed++].Text;
        return text;
    }

    public void Dispose() { }
}

public sealed class ScriptedVad : IVoiceActivityDetector
{
    private const int MinSilenceMs = 250;
    private readonly IReadOnlyList<ScriptedSegment> _script;
    private long _total;
    private int _next;

    public ScriptedVad(IReadOnlyList<ScriptedSegment> script) => _script = script;

    public void Accept(ReadOnlySpan<float> samples) => _total += samples.Length;

    public bool IsSpeechActive
    {
        get
        {
            var ms = AudioFormat.SamplesToMs(_total);
            return _script.Any(s => ms >= s.StartMs && ms < s.EndMs + MinSilenceMs);
        }
    }

    public bool TryTake(out SpeechSpan span)
    {
        if (_next < _script.Count && AudioFormat.SamplesToMs(_total) >= _script[_next].EndMs + MinSilenceMs)
        {
            var s = _script[_next++];
            span = new SpeechSpan(AudioFormat.MsToSamples(s.StartMs), AudioFormat.MsToSamples(s.EndMs));
            return true;
        }
        span = default;
        return false;
    }

    public void Flush()
    {
        // The script never leaves speech unreported once its silence has been pushed.
    }

    public void Dispose() { }
}

public sealed class ScriptedSegmentRecognizer : ISegmentRecognizer
{
    private readonly Func<string> _next;
    public ScriptedSegmentRecognizer(Func<string> next) => _next = next;
    public string Recognize(ReadOnlySpan<float> samples) => _next();
    public void Dispose() { }
}

/// <summary>Returns fixed text and remembers how much audio each call received.</summary>
public sealed class AudioLengthRecorder : ISegmentRecognizer
{
    private readonly string _text;
    public ConcurrentQueue<double> Milliseconds { get; } = new();

    public AudioLengthRecorder(string text) => _text = text;

    public string Recognize(ReadOnlySpan<float> samples)
    {
        Milliseconds.Enqueue(AudioFormat.SamplesToMs(samples.Length));
        return _text;
    }

    public void Dispose() { }
}

public sealed class RecordingCleaner : ITextCleaner
{
    private readonly Func<CleanupRequest, CancellationToken, Task<string>> _clean;
    public ConcurrentQueue<CleanupRequest> Requests { get; } = new();

    public RecordingCleaner(Func<CleanupRequest, CancellationToken, Task<string>> clean) => _clean = clean;

    public async Task<string> CleanAsync(CleanupRequest request, CancellationToken ct)
    {
        Requests.Enqueue(request);
        return await _clean(request, ct);
    }

    public static RecordingCleaner Marking(int delayMs = 0) => new(async (r, ct) =>
    {
        if (delayMs > 0) await Task.Delay(delayMs, ct);
        return r.Target + "*";
    });
}

public static class Wait
{
    public static async Task<bool> UntilAsync(Func<bool> condition, int timeoutMs = 3000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if (condition()) return true;
            await Task.Delay(10);
        }
        return condition();
    }

    /// <summary>Pushes constant audio in 40 ms frames; the content is irrelevant to the scripted fakes.</summary>
    public static void PushMs(DictationSession session, int ms)
    {
        for (var pushed = 0; pushed < ms; pushed += 40)
            session.Push(new float[AudioFormat.MsToSamples(40)]);
    }
}
