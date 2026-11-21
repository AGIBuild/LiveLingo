using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using LiveLingo.Core.Cloud;
using LiveLingo.Core.Dictation;
using LiveLingo.Core.Drafting;
using LiveLingo.Core.Inference;
using LiveLingo.Core.Speech;
using LiveLingo.Core.Speech.Cloud;
using LiveLingo.Core.Speech.Native;
using LiveLingo.Core.Transcription;
using LiveLingo.Core.Translation;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace LiveLingo.Core.Tests.Speech;

public class QwenAudioSpeechTests
{
    [Theory]
    [InlineData("https://dashscope.aliyuncs.com/api/v1", "Qwen-Audio-3.1-ASR", "wss://dashscope.aliyuncs.com/api-ws/v1/inference", "qwen-audio-3.1-asr-flash-message", true, true)]
    [InlineData("https://ws123.cn-beijing.maas.aliyuncs.com/compatible-mode/v1", "qwen-audio-3.1-asr-flash-message", "wss://ws123.cn-beijing.maas.aliyuncs.com/api-ws/v1/inference", "qwen-audio-3.1-asr-flash-message", true, true)]
    [InlineData("https://maas.qianwenaiapi.com/compatible-mode/v1", "qwen-audio-3.1-asr-flash-message", "wss://cn-beijing.maas.qianwenaiapi.com/api-ws/v1/inference", "qwen-audio-3.1-asr-flash-message", true, true)]
    [InlineData("wss://dashscope-intl.aliyuncs.com/api-ws/v1/inference", "qwen-audio-3.1-asr-flash-streaming", "wss://dashscope-intl.aliyuncs.com/api-ws/v1/inference", "qwen-audio-3.1-asr-flash-streaming", false, false)]
    public void SpeechCloudRoute_ResolvesQwenAudioWebSocketEndpointsAndCapabilities(
        string baseUrl, string model, string expectedEndpoint, string expectedWireModel, bool nativePolish, bool contextUpdate)
    {
        var route = SpeechCloudRoute.Create(baseUrl, model, "sk-secret", "Bailian");

        Assert.Equal(SpeechCloudProtocol.QwenAudioWebSocket, route.Protocol);
        Assert.Equal(expectedEndpoint, route.Endpoint.ToString());
        Assert.Equal(expectedWireModel, route.Model);
        Assert.Equal(nativePolish, route.SupportsNativePolish);
        Assert.Equal(contextUpdate, route.SupportsContextUpdate);
        Assert.True(route.SupportsTieredHotwords);
        Assert.DoesNotContain("sk-secret", route.ToString());
    }

    [Fact]
    public void SpeechCloudRoute_ResolvesQwenAudioFlashHttpMultimodalEndpoint()
    {
        var route = SpeechCloudRoute.Create("https://dashscope.aliyuncs.com/api/v1", "qwen-audio-3.1-asr-flash", "sk-secret");

        Assert.Equal(SpeechCloudProtocol.QwenAudioHttp, route.Protocol);
        Assert.Equal("https://dashscope.aliyuncs.com/api/v1/services/aigc/multimodal-generation/generation", route.Endpoint.ToString());
        Assert.True(route.SupportsNativePolish);
        Assert.True(route.SupportsTieredHotwords);
    }

    [Fact]
    public async Task WebSocketSession_StreamsPcm_ReplacesPartialBySentenceId_AppliesNativePolishAndRollingContext()
    {
        var ws = new FakeWebSocket();
        var transport = new FakeTransport(ws);
        var route = SpeechCloudRoute.Create("https://dashscope.aliyuncs.com/api/v1", "qwen-audio-3.1-asr-flash-message", "sk-key", "百炼");
        var glossary = Glossary.Parse("祺元\n六零九=>LiveLingo", includeBuiltIn: true);
        var draft = new DraftSession(hasTarget: true);
        draft.Mutate(d =>
        {
            d.Output = OutputKind.Clean;
            d.AppendDictated("我们在讨论璇枢架构。", "我们在讨论璇枢架构。");
        });
        var usage = new ModelUsage();
        var reported = new List<ModelUse>();
        usage.Changed += reported.Add;

        // Server immediately answers run-task with task-started once client sends run-task.
        _ = Task.Run(async () =>
        {
            var runTask = JsonDocument.Parse(await ws.ClientTexts.Reader.ReadAsync());
            var taskId = runTask.RootElement.GetProperty("header").GetProperty("task_id").GetString()!;
            var payload = runTask.RootElement.GetProperty("payload");
            Assert.Equal("qwen-audio-3.1-asr-flash-message", payload.GetProperty("model").GetString());
            var parameters = payload.GetProperty("parameters");
            Assert.True(parameters.GetProperty("intermediate_result_enabled").GetBoolean());
            Assert.True(parameters.GetProperty("disfluency_removal_enabled").GetBoolean());
            Assert.Equal("near_meeting_16k", parameters.GetProperty("vad_model").GetString());
            Assert.Equal(50, parameters.GetProperty("vocabulary").GetProperty("祺元").GetInt32());
            Assert.Equal(50, parameters.GetProperty("vocabulary").GetProperty("LiveLingo").GetInt32());
            Assert.Equal(4, parameters.GetProperty("vocabulary").GetProperty("Agent").GetInt32());
            Assert.Contains("璇枢架构", payload.GetProperty("input").GetProperty("context")[0].GetProperty("content")[0].GetProperty("text").GetString()!);

            ws.EnqueueServerEvent("""{"header":{"task_id":"__TASK__","event":"task-started"},"payload":{}}""".Replace("__TASK__", taskId));

            // Wait for binary audio chunk from microphone.
            var binary = await ws.ClientBinaries.Reader.ReadAsync();
            Assert.True(binary.Length > 0);

            // Heartbeat packet (sentence_id=0, heartbeat=true) must be ignored.
            ws.EnqueueServerEvent("""{"header":{"task_id":"__TASK__","event":"result-generated"},"payload":{"output":{"sentence":{"sentence_id":0,"text":"IGNORE","sentence_end":false,"heartbeat":true}}}}""".Replace("__TASK__", taskId));

            // Revised intermediate results for sentence 1 replace rather than concatenate.
            ws.EnqueueServerEvent("""{"header":{"task_id":"__TASK__","event":"result-generated"},"payload":{"output":{"sentence":{"sentence_id":1,"text":"请把漆园","sentence_end":false}}}}""".Replace("__TASK__", taskId));
            await Task.Delay(25);
            ws.EnqueueServerEvent("""{"header":{"task_id":"__TASK__","event":"result-generated"},"payload":{"output":{"sentence":{"sentence_id":1,"text":"请把祺元项目的六零九打开","sentence_end":false}}}}""".Replace("__TASK__", taskId));
            await Task.Delay(25);

            // Final utterance refresh with stash for sentence 2.
            ws.EnqueueServerEvent("""{"header":{"task_id":"__TASK__","event":"result-generated"},"payload":{"output":{"sentence":{"sentence_id":1,"begin_time":120,"end_time":2400,"text":"请把祺元项目的六零九打开。","sentence_end":true,"stash":{"sentence_id":2,"text":"顺便看下"}}}}}""".Replace("__TASK__", taskId));

            // Client sends continue-task with updated context, then finish-task on stop.
            var nextText1 = JsonDocument.Parse(await ws.ClientTexts.Reader.ReadAsync());
            Assert.Equal("continue-task", nextText1.RootElement.GetProperty("header").GetProperty("action").GetString());

            var finishTask = JsonDocument.Parse(await ws.ClientTexts.Reader.ReadAsync());
            Assert.Equal("finish-task", finishTask.RootElement.GetProperty("header").GetProperty("action").GetString());

            ws.EnqueueServerEvent("""{"header":{"task_id":"__TASK__","event":"result-generated"},"payload":{"output":{"sentence":{"sentence_id":2,"begin_time":2450,"end_time":3600,"text":"顺便看下 Agent 状态。","sentence_end":true}}}}""".Replace("__TASK__", taskId));
            ws.EnqueueServerEvent("""{"header":{"task_id":"__TASK__","event":"task-finished"},"payload":{"output":{}}}""".Replace("__TASK__", taskId));
        });

        var request = new SpeechSessionRequest(
            glossary.SpeechHotwords(),
            glossary,
            EnableNativePolish: true,
            NativePolishSatisfiesClean: true);

        await using var session = await QwenWebSocketDictationSession.ConnectAndStartTaskAsync(
            transport, route, draft, request, usage, NullLogger.Instance, CancellationToken.None);
        Assert.Equal("sk-key", transport.LastApiKey);

        session.Start();
        session.Push(new float[1600]);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (draft.Read(d => d.Units.Count < 2) && sw.ElapsedMilliseconds < 5000)
            await Task.Delay(20);

        await session.StopAsync();

        var snapshot = draft.Read(d => (
            Recording: d.Recording,
            Pending: d.Pending,
            Raws: d.Units.Select(u => u.Raw).ToArray(),
            Cleans: d.Units.Select(u => u.Clean.Text ?? "").ToArray(),
            AllNative: d.Units.All(u => u.IsNativePolished)));

        Assert.False(snapshot.Recording);
        Assert.Equal("", snapshot.Pending);
        Assert.Equal(["我们在讨论璇枢架构。", "请把祺元项目的六零九打开。", "顺便看下 Agent 状态。"], snapshot.Raws);
        // Deterministic accent rule (六零九 => LiveLingo) applied on top of native ASR output while preserving Raw.
        Assert.Equal(["我们在讨论璇枢架构。", "请把祺元项目的LiveLingo打开。", "顺便看下 Agent 状态。"], snapshot.Cleans);
        Assert.True(snapshot.AllNative);
        Assert.Contains(reported, u => u is { Stage: ModelStage.Speech, Cloud: true, Confirmed: true, ShortName: "Qwen-Audio-3.1-ASR" });
        Assert.Contains(reported, u => u is { Stage: ModelStage.Cleaner, Cloud: true, Confirmed: true });
    }

    [Fact]
    public async Task WebSocketSession_ThrowsCloudSpeechException_WhenHandshakeReceivesTaskFailed()
    {
        var ws = new FakeWebSocket();
        var transport = new FakeTransport(ws);
        var route = SpeechCloudRoute.Create("https://dashscope.aliyuncs.com/api/v1", "qwen-audio-3.1-asr-flash-message", "bad-key");
        var draft = new DraftSession(hasTarget: true);

        _ = Task.Run(async () =>
        {
            await ws.ClientTexts.Reader.ReadAsync();
            ws.EnqueueServerEvent("""{"header":{"task_id":"t1","event":"task-failed","error_code":"InvalidApiKey","error_message":"Invalid API-key provided."},"payload":{}}""");
        });

        var ex = await Assert.ThrowsAsync<CloudSpeechException>(() =>
            QwenWebSocketDictationSession.ConnectAndStartTaskAsync(
                transport, route, draft, SpeechSessionRequest.FromHotwords([]), null, null, CancellationToken.None));

        Assert.Contains("Invalid API-key", ex.Message);
    }

    [Fact]
    public async Task FileTranscriber_SupportsQwenAudioFlashHttpWithTimestampsAndHotwords()
    {
        var wavPath = Path.Combine(Path.GetTempPath(), $"livelingo-qwen-http-{Guid.NewGuid():N}.wav");
        try
        {
            var samples = new float[AudioFormat.SampleRate * 2];
            await File.WriteAllBytesAsync(wavPath, QwenAudioProtocol.EncodeWavPcm16Mono(samples));

            string? capturedBody = null;
            using var http = new HttpClient(new StubHttpHandler(async req =>
            {
                capturedBody = await req.Content!.ReadAsStringAsync();
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                        {
                          "output": {
                            "sentences": [
                              { "sentence_id": 1, "begin_time": 120, "end_time": 980, "text": "第一句识别结果。" },
                              { "sentence_id": 2, "begin_time": 1050, "end_time": 1920, "text": "第二句 LiveLingo 测试。" }
                            ]
                          }
                        }
                        """, Encoding.UTF8, "application/json")
                };
            }));

            var route = SpeechCloudRoute.Create("https://dashscope.aliyuncs.com/api/v1", "qwen-audio-3.1-asr-flash", "sk-key");
            var glossary = Glossary.Parse("LiveLingo");
            var segments = await QwenAudioFileTranscriber.TranscribeAsync(
                wavPath,
                route,
                new SpeechSessionRequest(["LiveLingo"], glossary),
                new FakeTransport(new FakeWebSocket()),
                http,
                usage: null,
                progress: null,
                CancellationToken.None);

            Assert.NotNull(capturedBody);
            Assert.Contains("\"LiveLingo\":50", capturedBody);
            Assert.Equal(2, segments.Count);
            Assert.Equal(TimeSpan.FromMilliseconds(120), segments[0].Start);
            Assert.Equal("第一句识别结果。", segments[0].Text);
            Assert.Equal("第二句 LiveLingo 测试。", segments[1].Text);
        }
        finally
        {
            if (File.Exists(wavPath))
                File.Delete(wavPath);
        }
    }

    [Fact]
    public async Task RoutedSpeechEngine_SkipsLocalModelWarmup_WhenSpeechIsRoutedToCloud()
    {
        var routes = Substitute.For<ICloudRoutes>();
        routes.GetSpeechAsync(Arg.Any<CancellationToken>())
            .Returns(SpeechCloudRoute.Create("https://dashscope.aliyuncs.com/api/v1", "qwen-audio-3.1-asr-flash-message", "sk-key"));
        var models = Substitute.For<LiveLingo.Core.Models.IModelManager>();
        await using var local = new SherpaSpeechEngine(models, new CoreOptions(), NullLogger<SherpaSpeechEngine>.Instance);
        using var http = new HttpClient();
        await using var routed = new RoutedSpeechEngine(
            routes, local, new FakeTransport(new FakeWebSocket()), http, NullLogger<RoutedSpeechEngine>.Instance);

        // Local models are NOT installed, yet WarmAsync succeeds as a no-op because speech is routed to cloud.
        await routed.WarmAsync(["LiveLingo"]);
        models.DidNotReceiveWithAnyArgs().HasAllExpectedLocalAssets(default!);
    }

    [Fact]
    public void SpeechOptimizationStrategies_IsolateModelSpecificTuning_WithZeroNegativeOptimizations()
    {
        var qwen17B = SpeechOptimizationStrategies.ForLocal(LiveLingo.Core.Models.ModelCatalog.SpeechQuality.Id);
        var qwen06B = SpeechOptimizationStrategies.ForLocal(LiveLingo.Core.Models.ModelCatalog.SpeechSecondPass.Id);
        var xAsr = SpeechOptimizationStrategies.ForLocal(LiveLingo.Core.Models.ModelCatalog.SpeechFast.Id);
        var cloud31 = SpeechOptimizationStrategies.ForCloud(SpeechCloudProtocol.QwenAudioWebSocket, "qwen-audio-3.1-asr-flash-message");
        var vllm17B = SpeechCloudRoute.Create("http://localhost:8000/v1", "Qwen/Qwen3-ASR-1.7B", "local-key");

        // 1. Qwen3-ASR 1.7B: verbatim ASR (keeps LLM cleanup active!), wider clause window (6.5s/11s/15s), bounded 36-token hotwords, up to 8 CPU threads.
        Assert.False(qwen17B.Profile.SupportsNativePolish);
        Assert.Equal(SpeechHotwordMode.BoundedPromptHead, qwen17B.Profile.HotwordMode);
        Assert.True(qwen17B.Profile.Dictation.SoftMaxUtteranceMs > qwen06B.Profile.Dictation.SoftMaxUtteranceMs);
        Assert.True(qwen17B.Profile.Dictation.HardMaxUtteranceMs > qwen06B.Profile.Dictation.HardMaxUtteranceMs);
        Assert.True(qwen17B.Profile.Dictation.StopTimeoutMs > qwen06B.Profile.Dictation.StopTimeoutMs);
        Assert.Equal(15, qwen17B.Profile.MaxSegmentSeconds);
        Assert.Equal(8, qwen17B.ResolveThreads(configuredThreads: 0, logicalCores: 16));

        // 2. X-ASR Offline (non-autoregressive transducer): freed from 0.6B's 13s KV slicing, chunk period stripping, hotwords, and LLM loop rate guard.
        Assert.Equal(SpeechHotwordMode.None, xAsr.Profile.HotwordMode);
        Assert.Empty(xAsr.SelectPromptHotwords(Glossary.Parse("Redis, LiveLingo", includeBuiltIn: true), ["Redis"]));
        Assert.Equal(30, xAsr.Profile.MaxSegmentSeconds);
        Assert.False(xAsr.Profile.StripChunkBoundaryPeriod);
        Assert.Null(xAsr.Profile.MaxCharsPerSecondGuard);
        Assert.Equal(0, xAsr.Profile.Dictation.SecondPassMaxCharsPerSecond);

        var sb = new StringBuilder("This is first.");
        SherpaQwen3Recognizer.AppendChunk(sb, "second part", stripBoundaryPeriod: xAsr.Profile.StripChunkBoundaryPeriod);
        Assert.Equal("This is first. second part", sb.ToString());

        // 3. Cloud Qwen-Audio-3.1-ASR-Message: native polish active, zero client slicing, tiered P0/P1 vocabulary, no dangling-clause period hold.
        Assert.True(cloud31.Profile.SupportsNativePolish);
        Assert.True(cloud31.Profile.SupportsRollingContext);
        Assert.Equal(0, cloud31.Profile.MaxSegmentSeconds);
        Assert.False(cloud31.Profile.HoldDanglingClausePeriod);

        // 4. Local/vLLM OpenAI-compatible Qwen/Qwen3-ASR-1.7B: recognized as OpenAiChatAudio with verbatim ASR (LLM cleanup NOT bypassed).
        Assert.Equal(SpeechCloudProtocol.OpenAiChatAudio, vllm17B.Protocol);
        Assert.Equal("Qwen3-ASR 1.7B", vllm17B.DisplayModel);
        Assert.False(vllm17B.SupportsNativePolish);
        Assert.False(vllm17B.Strategy.Profile.SupportsNativePolish);
    }

    private sealed class FakeTransport(FakeWebSocket socket) : IQwenWebSocketTransport
    {
        public string? LastApiKey { get; private set; }

        public Task<IQwenWebSocketConnection> ConnectAsync(Uri endpoint, string apiKey, CancellationToken ct)
        {
            LastApiKey = apiKey;
            return Task.FromResult<IQwenWebSocketConnection>(socket);
        }
    }

    private sealed class FakeWebSocket : IQwenWebSocketConnection
    {
        public Channel<string> ClientTexts { get; } = Channel.CreateUnbounded<string>();
        public Channel<byte[]> ClientBinaries { get; } = Channel.CreateUnbounded<byte[]>();
        private readonly Channel<string?> _serverMessages = Channel.CreateUnbounded<string?>();

        public void EnqueueServerEvent(string json) => _serverMessages.Writer.TryWrite(json);

        public Task SendTextAsync(string json, CancellationToken ct)
        {
            ClientTexts.Writer.TryWrite(json);
            return Task.CompletedTask;
        }

        public Task SendBinaryAsync(ReadOnlyMemory<byte> data, CancellationToken ct)
        {
            ClientBinaries.Writer.TryWrite(data.ToArray());
            return Task.CompletedTask;
        }

        public async Task<string?> ReceiveTextAsync(CancellationToken ct) =>
            await _serverMessages.Reader.ReadAsync(ct);

        public Task CloseOutputAsync(CancellationToken ct)
        {
            _serverMessages.Writer.TryWrite(null);
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            _serverMessages.Writer.TryComplete();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class StubHttpHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            handler(request);
    }
}
