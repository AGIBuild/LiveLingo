using LiveLingo.Core.Cloud;
using LiveLingo.Core.Dictation;
using LiveLingo.Core.Inference;
using LiveLingo.Core.Transcription;

namespace LiveLingo.Core.Speech.Cloud;

/// <summary>
/// Transcribes an uncompressed WAV file through a configured <see cref="SpeechCloudRoute"/> without requiring local
/// ONNX speech assets, preserving per-sentence timestamps for Composer timeline playback and SRT subtitle export.
/// </summary>
public static class QwenAudioFileTranscriber
{
    private const int WebSocketFrameSamples = 3200; // 200 ms @ 16 kHz
    private const int MaxHttpChunkSamples = 45 * AudioFormat.SampleRate;
    private const int HttpCutSearchSamples = 6 * AudioFormat.SampleRate;

    public static async Task<IReadOnlyList<TimedSegment>> TranscribeAsync(
        string path,
        SpeechCloudRoute route,
        SpeechSessionRequest request,
        IQwenWebSocketTransport wsTransport,
        HttpClient http,
        ModelUsage? usage,
        IProgress<double>? progress,
        CancellationToken ct)
    {
        var samples = await Task.Run(() => WavReader.ReadFile(path), ct).ConfigureAwait(false);
        if (samples.Length == 0)
            return [];

        usage?.Report(new ModelUse(ModelStage.Speech, route.Model, Cloud: true, Confirmed: false, route.DisplayModel, route.Name));

        IReadOnlyList<TimedSegment> segments = route.Protocol == SpeechCloudProtocol.QwenAudioWebSocket
            ? await TranscribeViaWebSocketAsync(samples, route, request, wsTransport, progress, ct).ConfigureAwait(false)
            : await TranscribeViaHttpAsync(samples, route, request, http, progress, ct).ConfigureAwait(false);

        usage?.Report(new ModelUse(ModelStage.Speech, route.Model, Cloud: true, Confirmed: true, route.DisplayModel, route.Name));
        progress?.Report(1.0);
        return segments;
    }

    private static async Task<IReadOnlyList<TimedSegment>> TranscribeViaWebSocketAsync(
        float[] samples,
        SpeechCloudRoute route,
        SpeechSessionRequest request,
        IQwenWebSocketTransport transport,
        IProgress<double>? progress,
        CancellationToken ct)
    {
        await using var socket = await transport.ConnectAsync(route.Endpoint, route.ApiKey, ct).ConfigureAwait(false);
        var taskId = Guid.NewGuid().ToString("D");

        var runTask = QwenAudioProtocol.BuildRunTaskJson(
            taskId, route, request, contextTurns: [], intermediateResults: false);
        await socket.SendTextAsync(runTask, ct).ConfigureAwait(false);

        while (true)
        {
            var msg = await socket.ReceiveTextAsync(ct).ConfigureAwait(false)
                ?? throw new CloudSpeechException("Cloud speech server closed the connection before starting file transcription.");
            var parsed = QwenAudioProtocol.ParseServerEvent(msg);
            if (parsed.Kind == QwenServerEventKind.TaskStarted)
                break;
            if (parsed.Kind == QwenServerEventKind.TaskFailed)
            {
                var detail = string.IsNullOrWhiteSpace(parsed.ErrorMessage) ? parsed.ErrorCode ?? "task-failed" : parsed.ErrorMessage;
                throw new CloudSpeechException($"Cloud speech transcription failed: {detail}");
            }
        }

        var segments = new List<TimedSegment>();
        string? partialTail = null;
        int? partialStartMs = null;

        var receiveTask = Task.Run(async () =>
        {
            while (true)
            {
                var raw = await socket.ReceiveTextAsync(ct).ConfigureAwait(false);
                if (raw is null)
                    break;
                var ev = QwenAudioProtocol.ParseServerEvent(raw);
                switch (ev.Kind)
                {
                    case QwenServerEventKind.ResultGenerated when ev.Sentence is { Heartbeat: false } s:
                    {
                        var text = TextTidy.Apply(s.Text);
                        if (!s.SentenceEnd)
                        {
                            if (text.Length > 0)
                            {
                                partialTail = text;
                                partialStartMs ??= s.BeginTimeMs ?? 0;
                            }
                            break;
                        }

                        partialTail = null;
                        partialStartMs = null;
                        if (text.Length == 0)
                            break;

                        var startMs = Math.Max(0, s.BeginTimeMs ?? (segments.Count > 0 ? (int)segments[^1].End.TotalMilliseconds : 0));
                        var endMs = Math.Max(startMs + 200, s.EndTimeMs ?? (int)Math.Round(AudioFormat.SamplesToMs(samples.Length)));
                        segments.Add(new TimedSegment(
                            TimeSpan.FromMilliseconds(startMs),
                            TimeSpan.FromMilliseconds(endMs),
                            text));
                        break;
                    }

                    case QwenServerEventKind.TaskFinished:
                        return;

                    case QwenServerEventKind.TaskFailed:
                    {
                        var detail = string.IsNullOrWhiteSpace(ev.ErrorMessage) ? ev.ErrorCode ?? "task-failed" : ev.ErrorMessage;
                        throw new CloudSpeechException($"Cloud speech transcription failed: {detail}");
                    }
                }
            }
        }, ct);

        for (var offset = 0; offset < samples.Length; offset += WebSocketFrameSamples)
        {
            ct.ThrowIfCancellationRequested();
            var count = Math.Min(WebSocketFrameSamples, samples.Length - offset);
            var pcm = QwenAudioProtocol.EncodePcm16Le(samples.AsSpan(offset, count));
            await socket.SendBinaryAsync(pcm, ct).ConfigureAwait(false);
            progress?.Report(Math.Min(0.92, (double)(offset + count) / samples.Length * 0.92));
        }

        await socket.SendTextAsync(QwenAudioProtocol.BuildFinishTaskJson(taskId), ct).ConfigureAwait(false);
        await receiveTask.ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(partialTail))
        {
            var startMs = partialStartMs ?? (segments.Count > 0 ? (int)segments[^1].End.TotalMilliseconds : 0);
            var totalMs = Math.Max(startMs + 200, (int)Math.Round(AudioFormat.SamplesToMs(samples.Length)));
            segments.Add(new TimedSegment(TimeSpan.FromMilliseconds(startMs), TimeSpan.FromMilliseconds(totalMs), partialTail));
        }

        await socket.CloseOutputAsync(ct).ConfigureAwait(false);
        return segments;
    }

    private static async Task<IReadOnlyList<TimedSegment>> TranscribeViaHttpAsync(
        float[] samples,
        SpeechCloudRoute route,
        SpeechSessionRequest request,
        HttpClient http,
        IProgress<double>? progress,
        CancellationToken ct)
    {
        var segments = new List<TimedSegment>();
        var history = new List<string>();
        var start = 0;

        while (start < samples.Length)
        {
            ct.ThrowIfCancellationRequested();
            int end;
            if (samples.Length - start <= MaxHttpChunkSamples)
            {
                end = samples.Length;
            }
            else
            {
                var searchFrom = Math.Max(start + AudioFormat.SampleRate, start + MaxHttpChunkSamples - HttpCutSearchSamples);
                var searchTo = start + MaxHttpChunkSamples;
                end = FileTranscriber.QuietestPoint(samples.AsSpan(), searchFrom, searchTo);
            }

            var chunkOffsetMs = (int)Math.Round(AudioFormat.SamplesToMs(start));
            var chunkResults = await QwenAudioProtocol.TranscribeChunkHttpAsync(
                http,
                route,
                samples.AsMemory(start, end - start),
                request,
                history,
                ct).ConfigureAwait(false);

            foreach (var item in chunkResults)
            {
                if (string.IsNullOrWhiteSpace(item.Text))
                    continue;
                var segStartMs = chunkOffsetMs + Math.Max(0, item.BeginTimeMs);
                var segEndMs = chunkOffsetMs + Math.Max(item.BeginTimeMs + 200, item.EndTimeMs);
                segments.Add(new TimedSegment(
                    TimeSpan.FromMilliseconds(segStartMs),
                    TimeSpan.FromMilliseconds(segEndMs),
                    item.Text));

                history.Add(item.Text);
                if (history.Count > QwenAudioProtocol.MaxContextTurns)
                    history.RemoveAt(0);
            }

            start = end;
            progress?.Report(Math.Min(0.98, (double)start / samples.Length));
        }

        return segments;
    }
}
