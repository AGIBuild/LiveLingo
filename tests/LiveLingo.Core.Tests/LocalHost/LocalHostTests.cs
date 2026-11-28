using System.Net;
using System.Text;
using System.Text.Json;
using LiveLingo.Core.Cloud;
using LiveLingo.Core.Inference;
using LiveLingo.Core.LocalHost;
using LiveLingo.Core.LocalHost.Providers;
using LiveLingo.Core.Models;

namespace LiveLingo.Core.Tests.LocalHost;

public sealed class LocalHostTests
{
    private const string RealBionicV1ModelsJson = """
        {
          "models": [
            {
              "type": "llm",
              "publisher": "mradermacher",
              "key": "inspiremusic-1.5b-long",
              "display_name": "InspireMusic 1.5B Long",
              "architecture": "qwen2",
              "quantization": { "name": "Q8_0", "bits_per_weight": 8 },
              "size_bytes": 1646573088,
              "params_string": "1.5B",
              "loaded_instances": [],
              "max_context_length": 131072,
              "format": "gguf"
            },
            {
              "type": "llm",
              "publisher": "google",
              "key": "google/gemma-4-26b-a4b-qat",
              "display_name": "Gemma 4 26B A4B QAT",
              "architecture": "gemma4",
              "quantization": { "name": "4bit", "bits_per_weight": 4 },
              "size_bytes": 15641333028,
              "params_string": "26B",
              "loaded_instances": [],
              "max_context_length": 262144,
              "format": "mlx",
              "capabilities": {
                "reasoning": { "allowed_options": ["off", "on"], "default": "on" }
              }
            },
            {
              "type": "llm",
              "publisher": "qwen",
              "key": "qwen/qwen3.8-27b",
              "display_name": "Qwen3.8 27B",
              "architecture": "qwen3_5",
              "quantization": { "name": "4bit", "bits_per_weight": 4 },
              "size_bytes": 16081678492,
              "params_string": "27B",
              "loaded_instances": [
                {
                  "id": "qwen/qwen3.8-27b",
                  "engine": "omlx",
                  "config": { "context_length": 262144, "parallel": 4 }
                }
              ],
              "max_context_length": 262144,
              "format": "mlx",
              "capabilities": {
                "reasoning": { "allowed_options": ["off", "low", "medium", "xhigh", "on"], "default": "xhigh" }
              }
            },
            {
              "type": "embedding",
              "publisher": "nomic-ai",
              "key": "text-embedding-nomic-embed-text-v1.5",
              "display_name": "Nomic Embed Text v1.5",
              "size_bytes": 84106624,
              "max_context_length": 2048,
              "format": "gguf"
            }
          ]
        }
        """;

    [Theory]
    [InlineData("http://127.0.0.1:1235", true)]
    [InlineData("http://localhost:1234", true)]
    [InlineData("http://[::1]:11434", true)]
    [InlineData("http://192.168.1.20:1234", false)]
    [InlineData("https://127.0.0.1:1234", false)]
    [InlineData("https://api.openai.com/v1", false)]
    [InlineData("http://user:pass@127.0.0.1:1234", false)]
    public void RouteEnforcesStrictLoopbackPrivacy(string url, bool expectedValid)
    {
        Assert.Equal(expectedValid, LocalHostRoute.TryParseLoopbackUri(url, out _));
        if (!expectedValid)
        {
            Assert.Throws<ArgumentException>(() =>
                LocalHostRoute.Create("bionic", "Bionic", LocalHostKind.Bionic, url, "qwen/qwen3.8-27b"));
        }
    }

    [Fact]
    public void MatcherFiltersNonTextModelsAndRanksHotBionicModelFirst()
    {
        Assert.True(LmStudioFamilyProvider.TryParseV1Models(RealBionicV1ModelsJson, out var parsed));
        // Embedding model is excluded by v1 parser.
        Assert.DoesNotContain(parsed, m => m.Key.Contains("embed", StringComparison.Ordinal));

        var evaluated = LocalHostModelMatcher.Evaluate(ModelStage.Cleaner, parsed);
        // Music model is filtered out by matcher non-text modal filter.
        Assert.DoesNotContain(evaluated, e => e.Model.Key == "inspiremusic-1.5b-long");
        Assert.Equal(2, evaluated.Count);

        // Hot omlx Qwen3.8 27B ranks #1 with FlagshipBilingual suitability.
        var top = evaluated[0];
        Assert.Equal("qwen/qwen3.8-27b", top.Model.Key);
        Assert.Equal(HostModelSuitability.FlagshipBilingual, top.Suitability);
        Assert.True(top.Model.IsLoaded);
        Assert.Equal("omlx", top.Model.LoadedEngine);
        Assert.Equal(4, top.Model.ParallelSlots);
        Assert.True(top.Model.Reasoning?.SupportsOff);
    }

    [Fact]
    public void MatcherFlagsForcedReasoningAndSmallContextAsSuboptimal()
    {
        var forcedR1 = new LocalHostModelInfo(
            "deepseek/deepseek-r1", "DeepSeek R1",
            ParamsString: "32B", MaxContextLength: 32768,
            Reasoning: new LocalHostReasoningCapability(["on"], "on"));
        var tinyCtx = new LocalHostModelInfo(
            "tiny/chat-7b", "Tiny Ctx 7B",
            ParamsString: "7B", MaxContextLength: 2048);

        var evaluated = LocalHostModelMatcher.Evaluate(ModelStage.Cleaner, [forcedR1, tinyCtx]);
        Assert.All(evaluated, e => Assert.Equal(HostModelSuitability.SuboptimalWarning, e.Suitability));
        Assert.False(LocalHostModelMatcher.HasSuitableModel(ModelStage.Cleaner, [forcedR1, tinyCtx]));
    }

    [Fact]
    public async Task LmStudioProviderRejectsInternalRpcPortThatOnlyAnswersGreeting()
    {
        using var http = new HttpClient(new StubHandler(req => req.RequestUri!.AbsolutePath switch
        {
            "/lmstudio-greeting" => JsonResponse("""{"lmstudio":true}"""),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        }));
        var provider = new LmStudioFamilyProvider(http);
        var target = new LocalHostProbeTarget(
            "bionic", LocalHostKind.Bionic, "Bionic", new Uri("http://127.0.0.1:52173"), IsInstalledHint: true);

        var result = await provider.ProbeAsync(target, CancellationToken.None);

        Assert.Equal(LocalHostStatus.InstalledNotServing, result.Status);
        Assert.Empty(result.Models);
    }

    [Fact]
    public async Task LmStudioProviderReportsAuthRequiredOn401()
    {
        using var http = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)));
        var provider = new LmStudioFamilyProvider(http);
        var target = new LocalHostProbeTarget("bionic", LocalHostKind.Bionic, "Bionic", new Uri("http://127.0.0.1:1235"));

        var result = await provider.ProbeAsync(target, CancellationToken.None);

        Assert.Equal(LocalHostStatus.AuthRequired, result.Status);
    }

    [Fact]
    public async Task LmStudioWarmLoadDoesNotEvictAnotherLoadedModel()
    {
        var loadCalled = false;
        using var http = new HttpClient(new StubHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath == "/api/v1/models")
                return JsonResponse(RealBionicV1ModelsJson);
            if (req.RequestUri.AbsolutePath == "/api/v1/models/load")
            {
                loadCalled = true;
                return JsonResponse("""{"status":"loaded"}""");
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }));

        var provider = new LmStudioFamilyProvider(http);
        // Gemma 4 is unloaded, while qwen/qwen3.8-27b is currently loaded by Bionic.
        var route = LocalHostRoute.Create("bionic", "Bionic", LocalHostKind.Bionic, "http://127.0.0.1:1235", "google/gemma-4-26b-a4b-qat");

        await provider.TryWarmLoadAsync(route, CancellationToken.None);

        Assert.False(loadCalled);
    }

    [Fact]
    public async Task LmStudioChatClientSendsReasoningOffAndIsolatesReasoningDeltas()
    {
        string? capturedBody = null;
        var sse = string.Join("\n",
            "event: chat.start",
            """data: {"type":"chat.start","model_instance_id":"qwen/qwen3.8-27b"}""",
            "",
            "event: reasoning.delta",
            """data: {"type":"reasoning.delta","content":"internal thought should be hidden"}""",
            "",
            "event: message.delta",
            """data: {"type":"message.delta","content":"Clean "}""",
            "",
            "event: message.delta",
            """data: {"type":"message.delta","content":"output."}""",
            "",
            "event: chat.end",
            """data: {"type":"chat.end","result":{"model_instance_id":"qwen/qwen3.8-27b","stats":{"total_output_tokens":4}}}""",
            "");

        using var http = new HttpClient(new StubHandler(req =>
        {
            capturedBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(sse, Encoding.UTF8, "text/event-stream")
            };
        }));

        var route = LocalHostRoute.Create(
            "bionic", "Bionic", LocalHostKind.Bionic, "http://127.0.0.1:1235", "qwen/qwen3.8-27b",
            LocalHostApiTier.NativeV1, reasoningOffSupported: true, isLoaded: true, parallelSlots: 4);
        var client = new LmStudioChatClient(http, route);

        var deltas = new List<string>();
        var completion = await client.CompleteAsync(
            new ChatRequest("raw speech", 256, 0, new Dictionary<string, object> { ["top_k"] = 20 }, "sys", deltas.Add),
            CancellationToken.None);

        Assert.Equal("Clean output.", completion.Text);
        Assert.Equal(["Clean ", "output."], deltas);
        Assert.Equal(4, completion.OutputTokens);
        Assert.Equal("qwen/qwen3.8-27b", completion.Model);

        using var payload = JsonDocument.Parse(capturedBody!);
        Assert.Equal("off", payload.RootElement.GetProperty("reasoning").GetString());
        Assert.False(payload.RootElement.GetProperty("store").GetBoolean());
        Assert.Equal(20, payload.RootElement.GetProperty("top_k").GetInt32());
    }

    [Fact]
    public async Task LmStudioChatClientDoesNotSendConnectionCloseHeader()
    {
        bool? sawConnectionClose = null;
        using var http = new HttpClient(new StubHandler(req =>
        {
            sawConnectionClose = req.Headers.ConnectionClose;
            var sse = "event: message.delta\ndata: {\"type\":\"message.delta\",\"content\":\"ok\"}\n\nevent: chat.end\ndata: {\"type\":\"chat.end\"}\n\n";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(sse, Encoding.UTF8, "text/event-stream") };
        }));

        var route = LocalHostRoute.Create("bionic", "Bionic", LocalHostKind.Bionic, "http://127.0.0.1:1235", "qwen/qwen3.8-27b");
        await new LmStudioChatClient(http, route).CompleteAsync(
            new ChatRequest("hi", 32, 0, new Dictionary<string, object>()), CancellationToken.None);

        Assert.NotEqual(true, sawConnectionClose);
    }

    [Fact]
    public async Task LmStudioChatClientOmitsReasoningWhenUnsupportedAndSkipsWarmPromptOnSingleSlot()
    {
        var requests = new List<string>();
        using var http = new HttpClient(new StubHandler(req =>
        {
            requests.Add(req.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            var sse = "event: message.delta\ndata: {\"type\":\"message.delta\",\"content\":\"ok\"}\n\nevent: chat.end\ndata: {\"type\":\"chat.end\"}\n\n";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(sse, Encoding.UTF8, "text/event-stream") };
        }));

        var singleSlotRoute = LocalHostRoute.Create(
            "bionic", "Bionic", LocalHostKind.Bionic, "http://127.0.0.1:1235", "plain-llm",
            LocalHostApiTier.NativeV1, reasoningOffSupported: false, isLoaded: true, parallelSlots: 1);
        var client = new LmStudioChatClient(http, singleSlotRoute);

        await client.WarmPromptAsync("sys");
        Assert.Empty(requests);

        await client.CompleteAsync(new ChatRequest("hi", 64, 0, new Dictionary<string, object>()), CancellationToken.None);
        Assert.Single(requests);
        using var doc = JsonDocument.Parse(requests[0]);
        Assert.False(doc.RootElement.TryGetProperty("reasoning", out _));
    }

    [Fact]
    public async Task LmStudioChatClientReportsModelLoadCrashAsHostErrorAndExtractsHttp400Message()
    {
        var crashedSse = string.Join("\n",
            "event: chat.start",
            """data: {"type":"chat.start","model_instance_id":"qwen3.8-flash-next"}""",
            "",
            "event: model_load.start",
            """data: {"type":"model_load.start","model_instance_id":"qwen3.8-flash-next"}""",
            "",
            "event: model_load.progress",
            """data: {"type":"model_load.progress","model_instance_id":"qwen3.8-flash-next","progress":0.13}""",
            "");

        using var crashHttp = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(crashedSse, Encoding.UTF8, "text/event-stream")
        }));
        var route = LocalHostRoute.Create("bionic", "Bionic", LocalHostKind.Bionic, "http://127.0.0.1:1235", "qwen3.8-flash-next");
        var client = new LmStudioChatClient(crashHttp, route);

        var loadEx = await Assert.ThrowsAsync<LocalHostUnavailableException>(() =>
            client.CompleteAsync(new ChatRequest("hi", 64, 0, new Dictionary<string, object>()), CancellationToken.None));
        Assert.Equal(LocalHostFailureKind.HostError, loadEx.Kind);
        Assert.Contains("failed to load", loadEx.Message);
        Assert.Contains("13%", loadEx.Message);

        using var http400 = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(
                """{"error":{"message":"Failed to load model: signal=SIGSEGV","type":"invalid_request"}}""",
                Encoding.UTF8,
                "application/json")
        }));
        var client400 = new LmStudioChatClient(http400, route);
        var ex400 = await Assert.ThrowsAsync<LocalHostUnavailableException>(() =>
            client400.CompleteAsync(new ChatRequest("hi", 64, 0, new Dictionary<string, object>()), CancellationToken.None));
        Assert.Contains("signal=SIGSEGV", ex400.Message);
    }

    [Fact]
    public void MatcherFlagsExperimentalGgufArchitectureAsSuboptimal()
    {
        var exp = new LocalHostModelInfo(
            "qwen3.8-flash-next", "Mtp Qwen3.8 Flash Next",
            Architecture: "qwen4exp", Format: "gguf", ParamsString: "512x1.4B", MaxContextLength: 262144);
        var stable = new LocalHostModelInfo(
            "qwen/qwen3.8-27b", "Qwen3.8 27B",
            Architecture: "qwen3_5", Format: "mlx", ParamsString: "27B", MaxContextLength: 262144);

        var evaluated = LocalHostModelMatcher.Evaluate(ModelStage.Cleaner, [exp, stable]);
        Assert.Equal("qwen/qwen3.8-27b", evaluated[0].Model.Key);
        Assert.Equal(HostModelSuitability.FlagshipBilingual, evaluated[0].Suitability);
        Assert.Equal("qwen3.8-flash-next", evaluated[1].Model.Key);
        Assert.Equal(HostModelSuitability.SuboptimalWarning, evaluated[1].Suitability);
        Assert.Equal("experimentalArch", evaluated[1].WarningReason);
    }

    [Fact]
    public void MatcherFiltersAndRanksSpeechAsrModelsForSpeechStageAndMapsLocalHostSpeechRoute()
    {
        var whisper = new LocalHostModelInfo("whisper-large-v3-turbo", "Whisper Large v3 Turbo", IsLoaded: true);
        var qwenAsr = new LocalHostModelInfo("qwen3-asr-1.7b", "Qwen3 ASR 1.7B", IsLoaded: false);
        var textLlm = new LocalHostModelInfo("qwen/qwen3.8-27b", "Qwen3.8 27B", IsLoaded: true);
        var tts = new LocalHostModelInfo("kokoro-82m-tts", "Kokoro TTS", IsLoaded: true);

        var speechEval = LocalHostModelMatcher.Evaluate(ModelStage.Speech, [textLlm, tts, qwenAsr, whisper]);
        Assert.Equal(2, speechEval.Count);
        Assert.Equal("whisper-large-v3-turbo", speechEval[0].Model.Key);
        Assert.Equal(HostModelSuitability.FlagshipBilingual, speechEval[0].Suitability);
        Assert.Equal("qwen3-asr-1.7b", speechEval[1].Model.Key);

        var whisperRoute = SpeechCloudRoute.CreateForLocalHost(
            LocalHostRoute.Create("custom-asr", "Local Whisper", LocalHostKind.OpenAiLocal, "http://127.0.0.1:8000", "whisper-large-v3-turbo", LocalHostApiTier.OpenAiCompat));
        Assert.True(whisperRoute.IsLocalHost);
        Assert.Equal(SpeechCloudProtocol.OpenAiTranscriptions, whisperRoute.Protocol);
        Assert.Equal("http://127.0.0.1:8000/v1/audio/transcriptions", whisperRoute.Endpoint.ToString());

        var qwenRoute = SpeechCloudRoute.CreateForLocalHost(
            LocalHostRoute.Create("custom-qwen", "Local Qwen ASR", LocalHostKind.OpenAiLocal, "http://127.0.0.1:8001", "qwen3-asr-1.7b", LocalHostApiTier.OpenAiCompat));
        Assert.True(qwenRoute.IsLocalHost);
        Assert.Equal(SpeechCloudProtocol.OpenAiChatAudio, qwenRoute.Protocol);
        Assert.Equal("http://127.0.0.1:8001/v1/chat/completions", qwenRoute.Endpoint.ToString());
    }

    [Fact]
    public async Task GenericOpenAiProviderParsesLlamaAppRouterModeModelsAndMetadata()
    {
        const string llamaAppModelsJson = """
            {
              "data": [
                {
                  "id": "ggml-org/Qwen3-ASR-1.7B-GGUF:Q8_0",
                  "status": { "value": "loaded" },
                  "context_length": 65536,
                  "meta": { "size": 2159087616, "ftype": "Q8_0" }
                },
                {
                  "id": "ggml-org/Qwen3-ASR-0.6B-GGUF:Q8_0",
                  "status": { "value": "unloaded" },
                  "context_length": 32768
                }
              ],
              "object": "list"
            }
            """;

        using var http = new HttpClient(new StubHandler(req => req.RequestUri!.AbsolutePath switch
        {
            "/v1/models" => JsonResponse(llamaAppModelsJson),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        }));
        var provider = new GenericOpenAiLocalProvider(http);
        var instance = await provider.ProbeAsync(
            new LocalHostProbeTarget("llama-app-9931", LocalHostKind.OpenAiLocal, "Llama.app (:9931)", new Uri("http://127.0.0.1:9931")),
            CancellationToken.None);

        Assert.Equal(LocalHostStatus.Running, instance.Status);
        Assert.True(instance.SupportsSpeech);
        Assert.Equal(2, instance.Models.Count);

        var loaded = Assert.Single(instance.Models, m => m.Key == "ggml-org/Qwen3-ASR-1.7B-GGUF:Q8_0");
        Assert.True(loaded.IsLoaded);
        Assert.Equal(65536, loaded.MaxContextLength);
        Assert.Equal(2159087616L, loaded.SizeBytes);
        Assert.Equal("Q8_0", loaded.Quantization);

        var unloaded = Assert.Single(instance.Models, m => m.Key == "ggml-org/Qwen3-ASR-0.6B-GGUF:Q8_0");
        Assert.False(unloaded.IsLoaded);
        Assert.Equal(32768, unloaded.MaxContextLength);
    }

    [Fact]
    public async Task OllamaProviderMergesRunningPsStateAndChatClientDisablesThinking()
    {
        string? chatPayload = null;
        using var http = new HttpClient(new StubHandler(req => req.RequestUri!.AbsolutePath switch
        {
            "/api/tags" => JsonResponse("""{"models":[{"name":"qwen3:8b","size":5200000000,"details":{"family":"qwen3","parameter_size":"8B","quantization_level":"Q4_K_M","format":"gguf"}}]}"""),
            "/api/ps" => JsonResponse("""{"models":[{"name":"qwen3:8b"}]}"""),
            "/api/chat" => CaptureAndReturnOllamaStream(req, body => chatPayload = body),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        }));

        var provider = new OllamaHostProvider(http);
        var instance = await provider.ProbeAsync(
            new LocalHostProbeTarget("ollama", LocalHostKind.Ollama, "Ollama", new Uri("http://127.0.0.1:11434")),
            CancellationToken.None);

        Assert.Equal(LocalHostStatus.Running, instance.Status);
        var model = Assert.Single(instance.Models);
        Assert.True(model.IsLoaded);
        Assert.Equal("8B", model.ParamsString);

        var route = LocalHostRoute.Create("ollama", "Ollama", LocalHostKind.Ollama, "http://127.0.0.1:11434", "qwen3:8b", LocalHostApiTier.OllamaNative, isLoaded: true);
        var completion = await provider.CreateChatClient(route, http).CompleteAsync(
            new ChatRequest("hello", 128, 0, new Dictionary<string, object>(), "sys"),
            CancellationToken.None);

        Assert.Equal("final answer", completion.Text);
        using var doc = JsonDocument.Parse(chatPayload!);
        Assert.False(doc.RootElement.GetProperty("think").GetBoolean());
        Assert.Equal("10m", doc.RootElement.GetProperty("keep_alive").GetString());
    }

    [Fact]
    public async Task RegistryDeduplicatesSharedPortAndPrefersBionicOverGeneric()
    {
        using var http = new HttpClient(new StubHandler(req => req.RequestUri!.AbsolutePath switch
        {
            "/api/v1/models" => JsonResponse(RealBionicV1ModelsJson),
            "/v1/models" => JsonResponse("""{"data":[{"id":"qwen/qwen3.8-27b"}]}"""),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        }));
        var registry = new LocalHostRegistry([new LmStudioFamilyProvider(http), new GenericOpenAiLocalProvider(http)]);

        var hosts = await registry.DiscoverAsync(
        [
            new LocalHostProbeTarget("custom-1235", LocalHostKind.OpenAiLocal, "Custom", new Uri("http://127.0.0.1:1235")),
            new LocalHostProbeTarget("bionic", LocalHostKind.Bionic, "Bionic", new Uri("http://127.0.0.1:1235"))
        ]);

        var winner = Assert.Single(hosts);
        Assert.Equal(LocalHostKind.Bionic, winner.Kind);
        Assert.Equal(LocalHostApiTier.NativeV1, winner.ApiTier);
    }

    [Fact]
    public async Task RoutedChatClientUsesLocalHostWithoutSilentFallbackOnFailure()
    {
        var builtInCalled = false;
        var builtIn = new FakeChatClient(() =>
        {
            builtInCalled = true;
            return new ChatCompletion("built-in", 1, 2, 1);
        });
        using var failingHttp = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)));
        var registry = new LocalHostRegistry([new LmStudioFamilyProvider(failingHttp)]);
        var route = LocalHostRoute.Create("bionic", "Bionic", LocalHostKind.Bionic, "http://127.0.0.1:1235", "missing-model");
        var usage = new ModelUsage();
        ModelUse? lastUse = null;
        usage.Changed += u => lastUse = u;

        var routed = new RoutedChatClient(
            InferenceRole.Cleaner,
            new LocalOnlyRoutes(),
            builtIn,
            failingHttp,
            usage,
            new FixedLocalHostRoutes(route),
            registry);

        var ex = await Assert.ThrowsAsync<LocalHostUnavailableException>(() =>
            routed.CompleteAsync(new ChatRequest("hi", 64, 0, new Dictionary<string, object>()), CancellationToken.None));

        Assert.Equal(LocalHostFailureKind.ModelNotFound, ex.Kind);
        Assert.True(ServiceFailure.IsUnavailable(ex));
        Assert.False(builtInCalled);
        Assert.NotNull(lastUse);
        Assert.Equal(ModelOrigin.LocalHost, lastUse!.Origin);
        Assert.False(lastUse.Cloud);
    }

    [Fact]
    public void ModelAdvisorAccountsZeroSelfManagedLlmMemoryWhenHostedLocally()
    {
        var assessment = ModelAdvisor.AssessPlan(
            ModelSelection.Baseline,
            HardwareProfile.Baseline32GbAppleMetal,
            cleanOnLocalHost: true,
            translateOnLocalHost: true);

        Assert.True(assessment.UsesSharedLocalHost);
        Assert.Equal(ModelSelection.Baseline.Speech.Profile!.ResidentBytes, assessment.ActiveCoResidentBytes);
        Assert.False(assessment.ExceedsSafeBudget);
    }

    private static HttpResponseMessage CaptureAndReturnOllamaStream(HttpRequestMessage req, Action<string> onBody)
    {
        onBody(req.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
        var ndjson = string.Join("\n",
            """{"model":"qwen3:8b","message":{"role":"assistant","thinking":"hidden","content":""},"done":false}""",
            """{"model":"qwen3:8b","message":{"role":"assistant","content":"final "},"done":false}""",
            """{"model":"qwen3:8b","message":{"role":"assistant","content":"answer"},"done":true,"eval_count":2}""");
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(ndjson, Encoding.UTF8, "application/x-ndjson") };
    }

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(handler(request));
    }

    private sealed class FakeChatClient(Func<ChatCompletion> complete) : IChatCompletionClient
    {
        public Task<ChatCompletion> CompleteAsync(ChatRequest request, CancellationToken ct) => Task.FromResult(complete());
    }

    private sealed class FixedLocalHostRoutes(LocalHostRoute route) : ILocalHostRoutes
    {
        public Task<LocalHostRoute?> GetAsync(InferenceRole role, CancellationToken ct) => Task.FromResult<LocalHostRoute?>(route);
    }
}
