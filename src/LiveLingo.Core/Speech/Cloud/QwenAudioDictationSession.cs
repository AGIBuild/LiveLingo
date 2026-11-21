using System.Diagnostics;
using System.Threading.Channels;
using LiveLingo.Core.Cloud;
using LiveLingo.Core.Dictation;
using LiveLingo.Core.Drafting;
using LiveLingo.Core.Inference;
using LiveLingo.Core.Transcription;
using LiveLingo.Core.Translation;
using Microsoft.Extensions.Logging;

namespace LiveLingo.Core.Speech.Cloud;

/// <summary>
/// Full-duplex real-time dictation session backed by Alibaba Bailian / DashScope WebSocket ASR
/// (<c>qwen-audio-3.1-asr-flash-message</c> and <c>qwen-audio-3.1-asr-flash-streaming</c>).
/// Streams 16 kHz mono PCM16 frames without client-side sentence slicing, displays incremental <c>sentence_end=false</c>
/// hypotheses in <see cref="Draft.Pending"/>, settles refreshed <c>sentence_end=true</c> utterances directly into
/// <see cref="Draft.Units"/>, and feeds settled utterances back via <c>continue-task</c> context updates.
/// </summary>
public sealed class QwenWebSocketDictationSession : IDictationSession
{
    private const int PcmBatchSamples = 1280; // 80 ms @ 16 kHz
    private static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan FinishTimeout = TimeSpan.FromSeconds(10);

    private readonly IQwenWebSocketConnection _socket;
    private readonly string _taskId;
    private readonly SpeechCloudRoute _route;
    private readonly SpeechSessionRequest _request;
    private readonly DraftSession _draft;
    private readonly int _draftEpoch;
    private readonly ModelUsage? _usage;
    private readonly ILogger? _logger;

    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly Channel<float[]> _frames = Channel.CreateUnbounded<float[]>(new() { SingleReader = true });
    private readonly CancellationTokenSource _sessionCts = new();
    private readonly object _stateGate = new();
    private readonly SortedDictionary<int, string> _openSentences = [];
    private readonly List<string> _historyTurns = [];
    private readonly TaskCompletionSource _taskFinishedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private string _publishedPending = "";
    private bool _finishing;
    private Task _sendTask = Task.CompletedTask;
    private Task _receiveTask = Task.CompletedTask;
    private Task? _stopTask;

    private QwenWebSocketDictationSession(
        IQwenWebSocketConnection socket,
        string taskId,
        SpeechCloudRoute route,
        SpeechSessionRequest request,
        DraftSession draft,
        List<string> initialHistory,
        ModelUsage? usage,
        ILogger? logger)
    {
        _socket = socket;
        _taskId = taskId;
        _route = route;
        _request = request;
        _draft = draft;
        _draftEpoch = draft.Read(d => d.Epoch);
        _historyTurns.AddRange(initialHistory);
        _usage = usage;
        _logger = logger;
    }

    public static async Task<QwenWebSocketDictationSession> ConnectAndStartTaskAsync(
        IQwenWebSocketTransport transport,
        SpeechCloudRoute route,
        DraftSession draft,
        SpeechSessionRequest request,
        ModelUsage? usage,
        ILogger? logger,
        CancellationToken ct)
    {
        usage?.Report(new ModelUse(ModelStage.Speech, route.Model, Cloud: true, Confirmed: false, route.DisplayModel, route.Name));

        var initialHistory = draft.Read(d => d.Units
            .Select(u => u.Clean.Text ?? u.Raw)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .TakeLast(QwenAudioProtocol.MaxContextTurns)
            .ToList());

        using var handshakeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        handshakeCts.CancelAfter(HandshakeTimeout);

        var socket = await transport.ConnectAsync(route.Endpoint, route.ApiKey, handshakeCts.Token).ConfigureAwait(false);
        var taskId = Guid.NewGuid().ToString("D");
        try
        {
            var runTaskJson = QwenAudioProtocol.BuildRunTaskJson(
                taskId, route, request, initialHistory, intermediateResults: true);
            await socket.SendTextAsync(runTaskJson, handshakeCts.Token).ConfigureAwait(false);

            while (true)
            {
                var message = await socket.ReceiveTextAsync(handshakeCts.Token).ConfigureAwait(false)
                    ?? throw new CloudSpeechException("Cloud speech server closed the connection before starting the task.");

                var parsed = QwenAudioProtocol.ParseServerEvent(message);
                if (parsed.Kind == QwenServerEventKind.TaskStarted)
                    break;
                if (parsed.Kind == QwenServerEventKind.TaskFailed)
                {
                    var detail = string.IsNullOrWhiteSpace(parsed.ErrorMessage) ? parsed.ErrorCode ?? "task-failed" : parsed.ErrorMessage;
                    throw new CloudSpeechException($"Cloud speech task failed: {detail}");
                }
            }

            usage?.Report(new ModelUse(ModelStage.Speech, route.Model, Cloud: true, Confirmed: true, route.DisplayModel, route.Name));
            if (request.NativePolishSatisfiesClean && route.SupportsNativePolish)
            {
                usage?.Report(new ModelUse(ModelStage.Cleaner, route.Model, Cloud: true, Confirmed: true, route.DisplayModel, route.Name));
            }

            return new QwenWebSocketDictationSession(socket, taskId, route, request, draft, initialHistory, usage, logger);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            await socket.DisposeAsync().ConfigureAwait(false);
            throw new CloudSpeechException("Timed out waiting for cloud speech recognition task to start.");
        }
        catch
        {
            await socket.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public void Start()
    {
        _draft.Mutate(d =>
        {
            if (d.Epoch == _draftEpoch && d.Snapshot is null)
                d.Recording = true;
        });
        _receiveTask = Task.Run(ReceiveLoopAsync);
        _sendTask = Task.Run(SendLoopAsync);
    }

    public void Push(float[] frame)
    {
        if (!_frames.Writer.TryWrite(frame))
            throw new InvalidOperationException("Audio pushed after the session was stopped.");
    }

    public Task StopAsync()
    {
        lock (_stateGate)
            return _stopTask ??= Task.Run(StopCoreAsync);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        await _sessionCts.CancelAsync().ConfigureAwait(false);
        try
        {
            await Task.WhenAll(_sendTask, _receiveTask).ConfigureAwait(false);
        }
        catch
        {
            // Worker exceptions are already handled inside the loops.
        }
        await _socket.DisposeAsync().ConfigureAwait(false);
        _sendLock.Dispose();
        _sessionCts.Dispose();
    }

    private async Task SendLoopAsync()
    {
        var batch = new List<float>(PcmBatchSamples * 2);
        try
        {
            var reader = _frames.Reader;
            while (await reader.WaitToReadAsync(_sessionCts.Token).ConfigureAwait(false))
            {
                while (reader.TryRead(out var frame))
                {
                    batch.AddRange(frame);
                    if (batch.Count >= PcmBatchSamples)
                    {
                        await SendPcmBatchAsync(batch, _sessionCts.Token).ConfigureAwait(false);
                        batch.Clear();
                    }
                }
            }

            if (batch.Count > 0)
            {
                await SendPcmBatchAsync(batch, _sessionCts.Token).ConfigureAwait(false);
                batch.Clear();
            }

            lock (_stateGate)
                _finishing = true;

            var finishJson = QwenAudioProtocol.BuildFinishTaskJson(_taskId);
            await SendTextSafeAsync(finishJson, _sessionCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Cloud speech audio sender stopped early");
            _taskFinishedTcs.TrySetResult();
        }
    }

    private async Task ReceiveLoopAsync()
    {
        try
        {
            while (!_sessionCts.IsCancellationRequested)
            {
                var raw = await _socket.ReceiveTextAsync(_sessionCts.Token).ConfigureAwait(false);
                if (raw is null)
                    break;

                var ev = QwenAudioProtocol.ParseServerEvent(raw);
                switch (ev.Kind)
                {
                    case QwenServerEventKind.ResultGenerated when ev.Sentence is { Heartbeat: false } sentence:
                        await HandleSentenceAsync(sentence).ConfigureAwait(false);
                        break;

                    case QwenServerEventKind.TaskFinished:
                        _taskFinishedTcs.TrySetResult();
                        return;

                    case QwenServerEventKind.TaskFailed:
                        _logger?.LogWarning(
                            "Cloud speech task failed mid-session ({Code}): {Message}",
                            ev.ErrorCode,
                            ev.ErrorMessage);
                        _taskFinishedTcs.TrySetResult();
                        return;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Cloud speech receiver terminated unexpectedly");
        }
        finally
        {
            _taskFinishedTcs.TrySetResult();
        }
    }

    private async Task HandleSentenceAsync(QwenSentenceUpdate sentence)
    {
        string? settledRaw = null;
        string? settledClean = null;
        string pending;
        List<string>? contextSnapshot = null;

        lock (_stateGate)
        {
            var tidy = TextTidy.Apply(sentence.Text);
            if (!sentence.SentenceEnd)
            {
                if (tidy.Length > 0)
                    _openSentences[sentence.SentenceId] = tidy;
                else
                    _openSentences.Remove(sentence.SentenceId);
            }
            else
            {
                _openSentences.Remove(sentence.SentenceId);
                if (sentence.StashSentenceId is { } stashId && stashId > sentence.SentenceId)
                {
                    var stashTidy = TextTidy.Apply(sentence.StashText ?? "");
                    if (stashTidy.Length > 0)
                        _openSentences[stashId] = stashTidy;
                }

                if (tidy.Length > 0)
                {
                    settledRaw = tidy;
                    var normalized = Glossary.ApplyCorrections(tidy, _request.Glossary?.Corrections);
                    if (_request.NativePolishSatisfiesClean && _route.SupportsNativePolish)
                        settledClean = normalized;

                    _historyTurns.Add(normalized);
                    if (_historyTurns.Count > QwenAudioProtocol.MaxContextTurns * 2)
                        _historyTurns.RemoveRange(0, _historyTurns.Count - QwenAudioProtocol.MaxContextTurns);

                    if (_route.SupportsContextUpdate && !_finishing)
                        contextSnapshot = [.. _historyTurns.TakeLast(QwenAudioProtocol.MaxContextTurns)];
                }
            }

            pending = JoinOpenSentences();
        }

        PublishToDraft(settledRaw is null ? [] : [(settledRaw, settledClean)], pending);

        if (contextSnapshot is { Count: > 0 })
        {
            try
            {
                var continueJson = QwenAudioProtocol.BuildContinueTaskJson(_taskId, contextSnapshot);
                await SendTextSafeAsync(continueJson, _sessionCts.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger?.LogDebug(ex, "Ignoring non-fatal continue-task context update error");
            }
        }
    }

    private async Task StopCoreAsync()
    {
        _frames.Writer.TryComplete();
        var deadline = Stopwatch.GetTimestamp() + (long)(Stopwatch.Frequency * FinishTimeout.TotalSeconds);

        await WaitWithDeadlineAsync(_sendTask, deadline).ConfigureAwait(false);
        await WaitWithDeadlineAsync(_taskFinishedTcs.Task, deadline).ConfigureAwait(false);

        List<(string Raw, string? Clean)> trailing = [];
        lock (_stateGate)
        {
            if (_openSentences.Count > 0)
            {
                var joined = JoinOpenSentences();
                _openSentences.Clear();
                if (joined.Length > 0)
                {
                    var normalized = Glossary.ApplyCorrections(joined, _request.Glossary?.Corrections);
                    var clean = _request.NativePolishSatisfiesClean && _route.SupportsNativePolish ? normalized : null;
                    trailing.Add((joined, clean));
                }
            }
        }

        _draft.Mutate(d =>
        {
            if (d.Epoch != _draftEpoch || d.Snapshot is not null)
                return;
            foreach (var (raw, clean) in trailing)
                d.AppendDictated(raw, clean);
            d.SetPending("");
            d.Recording = false;
        });

        try
        {
            using var closeCts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            await _socket.CloseOutputAsync(closeCts.Token).ConfigureAwait(false);
        }
        catch
        {
        }
    }

    private void PublishToDraft(IReadOnlyList<(string Raw, string? Clean)> settled, string pending)
    {
        lock (_stateGate)
        {
            if (settled.Count == 0 && pending == _publishedPending)
                return;
            _publishedPending = pending;
        }

        _draft.Mutate(d =>
        {
            if (d.Epoch != _draftEpoch || d.Snapshot is not null)
                return;
            foreach (var (raw, clean) in settled)
                d.AppendDictated(raw, clean);
            d.SetPending(pending);
        });
    }

    private string JoinOpenSentences() =>
        _openSentences.Count == 0
            ? ""
            : TextUnits.Join(_openSentences.Values.Where(v => v.Length > 0).Select(v => ("", v, "")));

    private async Task SendPcmBatchAsync(List<float> samples, CancellationToken ct)
    {
        var pcm = QwenAudioProtocol.EncodePcm16Le(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(samples));
        await _sendLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await _socket.SendBinaryAsync(pcm, ct).ConfigureAwait(false);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private async Task SendTextSafeAsync(string json, CancellationToken ct)
    {
        await _sendLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await _socket.SendTextAsync(json, ct).ConfigureAwait(false);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private static async Task WaitWithDeadlineAsync(Task task, long deadlineTimestamp)
    {
        var remainingSeconds = Math.Max(0, (deadlineTimestamp - Stopwatch.GetTimestamp()) / (double)Stopwatch.Frequency);
        if (remainingSeconds <= 0)
            return;
        await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(remainingSeconds))).ConfigureAwait(false);
    }
}

/// <summary>
/// Energy-endpointed cloud dictation session for non-streaming HTTP ASR endpoints
/// (such as <c>qwen-audio-3.1-asr-flash</c>, <c>qwen3-asr-flash</c> and OpenAI <c>/audio/transcriptions</c>),
/// requiring zero local ONNX downloads.
/// </summary>
public sealed class CloudHttpDictationSession : IDictationSession
{
    private const float SpeechRmsThreshold = 0.012f;
    private const int SilencePauseMs = 850;
    private const int MaxSegmentMs = 25_000;
    private const int MinSpeechMs = 160;
    private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(15);

    private readonly HttpClient _http;
    private readonly SpeechCloudRoute _route;
    private readonly SpeechSessionRequest _request;
    private readonly DraftSession _draft;
    private readonly int _draftEpoch;
    private readonly ModelUsage? _usage;
    private readonly ILogger? _logger;

    private readonly Channel<float[]> _frames = Channel.CreateUnbounded<float[]>(new() { SingleReader = true });
    private readonly Channel<float[]> _segments = Channel.CreateUnbounded<float[]>(new() { SingleReader = true });
    private readonly List<string> _historyTurns = [];
    private readonly object _stopGate = new();

    private Task _endpointTask = Task.CompletedTask;
    private Task _recognizeTask = Task.CompletedTask;
    private Task? _stopTask;

    public CloudHttpDictationSession(
        HttpClient http,
        SpeechCloudRoute route,
        SpeechSessionRequest request,
        DraftSession draft,
        ModelUsage? usage = null,
        ILogger? logger = null)
    {
        _http = http;
        _route = route;
        _request = request;
        _draft = draft;
        _draftEpoch = draft.Read(d => d.Epoch);
        _usage = usage;
        _logger = logger;
        _historyTurns.AddRange(draft.Read(d => d.Units
            .Select(u => u.Clean.Text ?? u.Raw)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .TakeLast(QwenAudioProtocol.MaxContextTurns)));
    }

    public void Start()
    {
        _usage?.Report(new ModelUse(ModelStage.Speech, _route.Model, Cloud: true, Confirmed: false, _route.DisplayModel, _route.Name));
        _draft.Mutate(d =>
        {
            if (d.Epoch == _draftEpoch && d.Snapshot is null)
                d.Recording = true;
        });
        _endpointTask = Task.Run(EndpointLoopAsync);
        _recognizeTask = Task.Run(RecognizeLoopAsync);
    }

    public void Push(float[] frame)
    {
        if (!_frames.Writer.TryWrite(frame))
            throw new InvalidOperationException("Audio pushed after the session was stopped.");
    }

    public Task StopAsync()
    {
        lock (_stopGate)
            return _stopTask ??= Task.Run(StopCoreAsync);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
    }

    private async Task EndpointLoopAsync()
    {
        var buffer = new List<float>();
        var preRoll = new Queue<float[]>();
        var inSpeech = false;
        var silenceSamples = 0;
        var speechSamples = 0;
        var maxSamples = (int)AudioFormat.MsToSamples(MaxSegmentMs);
        var pauseSamples = (int)AudioFormat.MsToSamples(SilencePauseMs);
        var minSpeechSamples = (int)AudioFormat.MsToSamples(MinSpeechMs);

        var reader = _frames.Reader;
        while (await reader.WaitToReadAsync().ConfigureAwait(false))
        {
            while (reader.TryRead(out var frame))
            {
                var active = FrameRms(frame) >= SpeechRmsThreshold;
                if (!inSpeech)
                {
                    if (!active)
                    {
                        preRoll.Enqueue(frame);
                        while (preRoll.Count > 6)
                            preRoll.Dequeue();
                        continue;
                    }

                    inSpeech = true;
                    silenceSamples = 0;
                    speechSamples = frame.Length;
                    while (preRoll.Count > 0)
                        buffer.AddRange(preRoll.Dequeue());
                    buffer.AddRange(frame);
                    continue;
                }

                buffer.AddRange(frame);
                if (active)
                {
                    silenceSamples = 0;
                    speechSamples += frame.Length;
                }
                else
                {
                    silenceSamples += frame.Length;
                }

                if (silenceSamples >= pauseSamples || buffer.Count >= maxSamples)
                {
                    if (speechSamples >= minSpeechSamples)
                        _segments.Writer.TryWrite(buffer.ToArray());
                    buffer.Clear();
                    inSpeech = false;
                    silenceSamples = 0;
                    speechSamples = 0;
                }
            }
        }

        if (inSpeech && speechSamples >= minSpeechSamples && buffer.Count > 0)
            _segments.Writer.TryWrite(buffer.ToArray());

        _segments.Writer.TryComplete();
    }

    private async Task RecognizeLoopAsync()
    {
        await foreach (var audio in _segments.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            try
            {
                var results = await QwenAudioProtocol.TranscribeChunkHttpAsync(
                    _http, _route, audio, _request, _historyTurns, CancellationToken.None).ConfigureAwait(false);
                _usage?.Report(new ModelUse(ModelStage.Speech, _route.Model, Cloud: true, Confirmed: true, _route.DisplayModel, _route.Name));
                if (_request.NativePolishSatisfiesClean && _route.SupportsNativePolish)
                {
                    _usage?.Report(new ModelUse(ModelStage.Cleaner, _route.Model, Cloud: true, Confirmed: true, _route.DisplayModel, _route.Name));
                }

                foreach (var item in results)
                {
                    if (string.IsNullOrWhiteSpace(item.Text))
                        continue;
                    var normalized = Glossary.ApplyCorrections(item.Text, _request.Glossary?.Corrections);
                    var clean = _request.NativePolishSatisfiesClean && _route.SupportsNativePolish ? normalized : null;
                    _historyTurns.Add(normalized);
                    if (_historyTurns.Count > QwenAudioProtocol.MaxContextTurns)
                        _historyTurns.RemoveAt(0);

                    _draft.Mutate(d =>
                    {
                        if (d.Epoch == _draftEpoch && d.Snapshot is null)
                            d.AppendDictated(item.Text, clean);
                    });
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Cloud HTTP speech recognition failed for an utterance");
            }
        }
    }

    private async Task StopCoreAsync()
    {
        _frames.Writer.TryComplete();
        await Task.WhenAny(Task.WhenAll(_endpointTask, _recognizeTask), Task.Delay(DrainTimeout)).ConfigureAwait(false);
        _draft.Mutate(d =>
        {
            if (d.Epoch == _draftEpoch && d.Snapshot is null)
            {
                d.SetPending("");
                d.Recording = false;
            }
        });
    }

    private static float FrameRms(ReadOnlySpan<float> samples)
    {
        if (samples.Length == 0)
            return 0f;
        double sum = 0;
        for (var i = 0; i < samples.Length; i++)
            sum += samples[i] * samples[i];
        return (float)Math.Sqrt(sum / samples.Length);
    }
}
