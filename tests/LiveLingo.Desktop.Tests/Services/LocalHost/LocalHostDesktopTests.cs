using System.Net;
using System.Text;
using LiveLingo.Core.Inference;
using LiveLingo.Core.LocalHost;
using LiveLingo.Core.LocalHost.Providers;
using LiveLingo.Core.Models;
using LiveLingo.Desktop.Composer;
using LiveLingo.Desktop.Platform;
using LiveLingo.Desktop.Services.Configuration;
using LiveLingo.Desktop.Services.LocalHost;
using LiveLingo.Desktop.Services.Localization;
using LiveLingo.Desktop.Services.Preferences;
using LiveLingo.Desktop.Services.Update;
using LiveLingo.Desktop.Settings;
using LiveLingo.Desktop.Web;

namespace LiveLingo.Desktop.Tests.Services.LocalHost;

public sealed class LocalHostDesktopTests
{
    private const string BionicV1Json = """
        {
          "models": [
            {
              "type": "llm",
              "publisher": "qwen",
              "key": "qwen/qwen3.8-27b",
              "display_name": "Qwen3.8 27B",
              "format": "mlx",
              "quantization": { "name": "4bit" },
              "size_bytes": 16081678492,
              "params_string": "27B",
              "max_context_length": 262144,
              "loaded_instances": [
                { "id": "qwen/qwen3.8-27b", "engine": "omlx", "config": { "context_length": 262144, "parallel": 4 } }
              ],
              "capabilities": {
                "reasoning": { "allowed_options": ["off", "xhigh", "on"], "default": "xhigh" }
              }
            }
          ]
        }
        """;

    [Fact]
    public async Task EnvironmentProbeReadsBionicConfigPortAndIgnoresInternalRpcPort()
    {
        var tempHome = Path.Combine(Path.GetTempPath(), "livelingo-bionic-probe-" + Guid.NewGuid().ToString("N"));
        var bionicInternal = Path.Combine(tempHome, ".lmstudio", "apps", "bionic", ".internal");
        Directory.CreateDirectory(bionicInternal);
        try
        {
            var ct = TestContext.Current.CancellationToken;
            await File.WriteAllTextAsync(Path.Combine(bionicInternal, "http-server-config.json"), """{"port":1235,"autoStartOnLaunch":true}""", ct);
            await File.WriteAllTextAsync(Path.Combine(bionicInternal, "http-server.json"), """{"host":"127.0.0.1","port":52173}""", ct);

            var probe = new LocalHostEnvironmentProbe(homeDir: tempHome, localAppData: tempHome);
            var targets = await probe.BuildTargetsAsync(new LocalHostSettings(), new InMemorySecretStore(), ct);

            var bionic = Assert.Single(targets, t => t.HostId == LocalHostEnvironmentProbe.BionicHostId);
            Assert.Equal(1235, bionic.BaseUri.Port);
            Assert.True(bionic.IsInstalledHint);
            Assert.DoesNotContain(targets, t => t.BaseUri.Port == 52173);

            var llamaApp = Assert.Single(targets, t => t.HostId == LocalHostEnvironmentProbe.LlamaAppHostId);
            Assert.Equal(9931, llamaApp.BaseUri.Port);
            Assert.Equal(LocalHostKind.OpenAiLocal, llamaApp.Kind);
        }
        finally
        {
            Directory.Delete(tempHome, recursive: true);
        }
    }

    [Fact]
    public async Task SettingsLocalHostRoutesResolvesLiveBionicCapabilities()
    {
        var settings = new InMemorySettingsService(new SettingsModel
        {
            LocalHost = new LocalHostSettings
            {
                Cleaner = new LocalHostStageBinding { HostId = "bionic", BaseUrl = "http://127.0.0.1:1235", ModelKey = "qwen/qwen3.8-27b" }
            }
        });
        var secrets = new InMemorySecretStore();
        using var http = new HttpClient(new StubHandler(req =>
            req.RequestUri!.AbsolutePath == "/api/v1/models"
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(BionicV1Json, Encoding.UTF8, "application/json") }
                : new HttpResponseMessage(HttpStatusCode.NotFound)));

        var registry = new LocalHostRegistry([new LmStudioFamilyProvider(http)]);
        var coordinator = new LocalHostCoordinator(registry, new FixedProbe("bionic", 1235), settings, secrets);
        var routes = new SettingsLocalHostRoutes(settings, secrets, coordinator);

        var cleanerRoute = await routes.GetAsync(InferenceRole.Cleaner, CancellationToken.None);
        var translatorRoute = await routes.GetAsync(InferenceRole.Translator, CancellationToken.None);

        Assert.NotNull(cleanerRoute);
        Assert.Equal("qwen/qwen3.8-27b", cleanerRoute!.ModelKey);
        Assert.True(cleanerRoute.ReasoningOffSupported);
        Assert.True(cleanerRoute.IsLoaded);
        Assert.Equal(4, cleanerRoute.ParallelSlots);
        Assert.Null(translatorRoute);
    }

    [Fact]
    public async Task SettingsViewModelEnforcesThreeWayMutualExclusionAndExposesHotBanner()
    {
        var settings = new InMemorySettingsService(new SettingsModel());
        var secrets = new InMemorySecretStore();
        using var http = new HttpClient(new StubHandler(req =>
            req.RequestUri!.AbsolutePath == "/api/v1/models"
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(BionicV1Json, Encoding.UTF8, "application/json") }
                : new HttpResponseMessage(HttpStatusCode.NotFound)));

        var registry = new LocalHostRegistry([new LmStudioFamilyProvider(http)]);
        var hostCoordinator = new LocalHostCoordinator(registry, new FixedProbe("bionic", 1235), settings, secrets);
        await hostCoordinator.RefreshAsync(force: true, TestContext.Current.CancellationToken);

        using var vm = new SettingsViewModel(
            settings,
            secrets,
            new StubModelManager(),
            new RecentDrafts(),
            new StubMicrophone(),
            new ComposerPreferences(new InMemoryPreferencesStore(), settings),
            localHostCoordinator: hostCoordinator);

        var texts = new SettingsTexts(new LocalizationService());
        var before = SettingsViewState.From(vm, texts, "models", new AppInfo("2.5.0", "macOS"), UpdateViewState.From(UpdateState.Off, false, texts, "2.5.0"), "cleaner");
        Assert.NotNull(before.Models.HotHost);
        Assert.Equal("bionic", before.Models.HotHost!.HostId);
        Assert.Equal("qwen/qwen3.8-27b", before.Models.HotHost.ModelKey);

        // One-click reuse binds both stages to Bionic's hot model and hides the hot banner.
        Assert.True(vm.ApplyHotLocalHostModel("bionic", "qwen/qwen3.8-27b"));
        Assert.True(vm.LocalHostClean);
        Assert.True(vm.LocalHostTranslate);
        Assert.False(vm.CloudClean);
        Assert.False(vm.CloudTranslate);

        var after = SettingsViewState.From(vm, texts, "models", new AppInfo("2.5.0", "macOS"), UpdateViewState.From(UpdateState.Off, false, texts, "2.5.0"), "cleaner");
        Assert.Null(after.Models.HotHost);
        Assert.False(after.Models.Hardware.AnyCloud);

        // Composer model plan requires zero built-in LLM downloads when routed to Bionic.
        var plan = ComposerModelPlan.Create(
            ModelSelection.Baseline,
            cleanInCloud: false,
            translateInCloud: false,
            cleanOnLocalHost: vm.LocalHostClean,
            translateOnLocalHost: vm.LocalHostTranslate);
        Assert.Empty(plan.Clean);
        Assert.Empty(plan.Translate);

        // Switching Cleaner back to a built-in model clears its LocalHost binding.
        Assert.True(vm.SelectModel(ModelCatalog.Cleaner.Id));
        Assert.False(vm.LocalHostClean);
        Assert.True(vm.LocalHostTranslate);
    }

    [Fact]
    public void WebIntentValidatesLocalHostIntentsAndRejectsNonLoopbackUrls()
    {
        var validSelect = WebIntent.Parse("""{"v":1,"name":"selectLocalHostModel","stage":"Cleaner","hostId":"bionic","modelKey":"qwen/qwen3.8-27b"}""", out _);
        Assert.IsType<SelectLocalHostModelIntent>(validSelect);

        var validCustom = WebIntent.Parse("""{"v":1,"name":"addCustomLocalHost","label":"Local MLX","baseUrl":"http://127.0.0.1:8080"}""", out _);
        Assert.IsType<AddCustomLocalHostIntent>(validCustom);

        var remoteCustom = WebIntent.Parse("""{"v":1,"name":"addCustomLocalHost","label":"Remote","baseUrl":"http://192.168.1.50:8080"}""", out var reason);
        Assert.Null(remoteCustom);
        Assert.Equal("invalid", reason);

        var configIntent = WebIntent.Parse("""{"v":1,"name":"saveLocalHostConfig","hostId":"bionic","baseUrl":"http://127.0.0.1:1235","apiToken":"secret-token","clearToken":false}""", out _);
        Assert.IsType<SaveLocalHostConfigIntent>(configIntent);
        Assert.DoesNotContain("secret-token", configIntent!.ToString()!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SpeechStageExcludesBionicAndFlagsStaleBindingWhileAllowingOpenAiAsrServer()
    {
        const string bionicWithAsrJson = """
            {
              "models": [
                {
                  "type": "llm",
                  "publisher": "ggml-org",
                  "key": "qwen3-asr-1.7b",
                  "display_name": "Qwen3 ASR 1.7B",
                  "format": "gguf",
                  "size_bytes": 2806809248,
                  "params_string": "2.0B",
                  "loaded_instances": [
                    { "id": "qwen3-asr-1.7b", "engine": "llama.cpp", "config": { "context_length": 65536, "parallel": 4 } }
                  ]
                }
              ]
            }
            """;
        const string openAiAsrJson = """{"data":[{"id":"mlx-community/Qwen3-ASR-1.7B-8bit"}]}""";

        var settings = new InMemorySettingsService(new SettingsModel());
        var secrets = new InMemorySecretStore();
        using var http = new HttpClient(new StubHandler(req => req.RequestUri!.Port switch
        {
            1235 when req.RequestUri.AbsolutePath == "/api/v1/models" =>
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(bionicWithAsrJson, Encoding.UTF8, "application/json") },
            8000 when req.RequestUri.AbsolutePath == "/v1/models" =>
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(openAiAsrJson, Encoding.UTF8, "application/json") },
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        }));

        var registry = new LocalHostRegistry([new LmStudioFamilyProvider(http), new GenericOpenAiLocalProvider(http)]);
        var probe = new MultiProbe(
        [
            new LocalHostProbeTarget("bionic", LocalHostKind.Bionic, "Bionic", new Uri("http://127.0.0.1:1235"), IsInstalledHint: true),
            new LocalHostProbeTarget("custom-asr", LocalHostKind.OpenAiLocal, "mlx-audio", new Uri("http://127.0.0.1:8000"))
        ]);
        var hostCoordinator = new LocalHostCoordinator(registry, probe, settings, secrets);
        await hostCoordinator.RefreshAsync(force: true, TestContext.Current.CancellationToken);

        using var vm = new SettingsViewModel(
            settings,
            secrets,
            new StubModelManager(),
            new RecentDrafts(),
            new StubMicrophone(),
            new ComposerPreferences(new InMemoryPreferencesStore(), settings),
            localHostCoordinator: hostCoordinator);
        var texts = new SettingsTexts(new LocalizationService());

        // 1. Bionic is hidden on Speech even when it lists qwen3-asr-1.7b; OpenAiLocal ASR host is shown and selectable.
        var state = SettingsViewState.From(vm, texts, "models", new AppInfo("2.5.0", "macOS"), UpdateViewState.From(UpdateState.Off, false, texts, "2.5.0"), "speech");
        var speechStage = Assert.Single(state.Models.Stages, s => s.Stage == "speech");
        Assert.DoesNotContain(speechStage.LocalHosts!, h => h.HostId == "bionic");
        var asrHost = Assert.Single(speechStage.LocalHosts!, h => h.HostId == "custom-asr");
        Assert.True(Assert.Single(asrHost.Models).CanSelect);

        // 2. Selecting Bionic for Speech is rejected; selecting OpenAiLocal succeeds and resolves a Speech route.
        Assert.False(vm.SelectLocalHostModel(ModelStage.Speech, "bionic", "qwen3-asr-1.7b"));
        Assert.False(vm.LocalHostSpeech);
        Assert.True(vm.SelectLocalHostModel(ModelStage.Speech, "custom-asr", "mlx-community/Qwen3-ASR-1.7B-8bit"));
        var routes = new SettingsLocalHostRoutes(settings, secrets, hostCoordinator);
        var speechRoute = await routes.GetSpeechAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(speechRoute);
        Assert.Equal("custom-asr", speechRoute!.HostId);

        // 3. A stale Bionic speech binding from an older settings.json is flagged with an unsupported warning and fails fast at runtime.
        var stale = settings.CloneCurrent();
        stale.LocalHost.Speech = new LocalHostStageBinding { HostId = "bionic", BaseUrl = "http://127.0.0.1:1235/", ModelKey = "qwen3-asr-1.7b" };
        settings.Replace(stale);

        var staleState = SettingsViewState.From(vm, texts, "models", new AppInfo("2.5.0", "macOS"), UpdateViewState.From(UpdateState.Off, false, texts, "2.5.0"), "speech");
        var staleSpeechStage = Assert.Single(staleState.Models.Stages, s => s.Stage == "speech");
        var staleBionic = Assert.Single(staleSpeechStage.LocalHosts!, h => h.HostId == "bionic");
        var staleModel = Assert.Single(staleBionic.Models);
        Assert.True(staleModel.Active);
        Assert.False(staleModel.CanSelect);
        Assert.Equal("suboptimalwarning", staleModel.Suitability);
        Assert.False(string.IsNullOrWhiteSpace(staleModel.WarningText));

        var ex = await Assert.ThrowsAsync<LocalHostUnavailableException>(() => routes.GetSpeechAsync(TestContext.Current.CancellationToken));
        Assert.Equal(LocalHostFailureKind.HostError, ex.Kind);
        Assert.Contains("Bionic", ex.Message, StringComparison.Ordinal);
    }

    private sealed class MultiProbe(IReadOnlyList<LocalHostProbeTarget> targets) : ILocalHostEnvironmentProbe
    {
        public Task<IReadOnlyList<LocalHostProbeTarget>> BuildTargetsAsync(LocalHostSettings settings, ISecretStore secrets, CancellationToken ct) =>
            Task.FromResult(targets);

        public bool TryOpenHostApp(LocalHostInstance host) => true;
        public Task<bool> TryStartLocalServerAsync(LocalHostInstance host, CancellationToken ct) => Task.FromResult(true);
    }

    private sealed class FixedProbe(string hostId, int port) : ILocalHostEnvironmentProbe
    {
        public Task<IReadOnlyList<LocalHostProbeTarget>> BuildTargetsAsync(LocalHostSettings settings, ISecretStore secrets, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<LocalHostProbeTarget>>(
                [new LocalHostProbeTarget(hostId, LocalHostKind.Bionic, "Bionic", new Uri($"http://127.0.0.1:{port}"), IsInstalledHint: true)]);

        public bool TryOpenHostApp(LocalHostInstance host) => true;
        public Task<bool> TryStartLocalServerAsync(LocalHostInstance host, CancellationToken ct) => Task.FromResult(true);
    }

    private sealed class InMemorySettingsService(SettingsModel current) : ISettingsService
    {
        public SettingsModel Current { get; private set; } = current;
        public event Action? SettingsChanged;
        public bool SettingsFileExists() => true;
        public Task LoadAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task SaveAsync(CancellationToken ct = default) => Task.CompletedTask;
        public SettingsModel CloneCurrent() => Current.DeepClone();
        public void Replace(SettingsModel model)
        {
            Current = model.DeepClone();
            SettingsChanged?.Invoke();
        }
    }

    private sealed class InMemoryPreferencesStore : IPreferencesStore
    {
        private PreferencesData _data = new();
        public PreferencesData Load() => _data;
        public void Save(PreferencesData data) => _data = data;
    }

    private sealed class StubMicrophone : IMicrophone
    {
        public string PreferredDevice { get; set; } = "";
        public bool IsRecording => false;
        public IReadOnlyList<string> InputDevices() => [];
        public string Start(Action<float[]> onSamples) => "";
        public void Stop() { }
        public void Dispose() { }
    }

    private sealed class StubModelManager : IModelManager
    {
        public string GetModelDirectory(string modelId) => Path.Combine(Path.GetTempPath(), "stub-models", modelId);
        public bool HasAllExpectedLocalAssets(ModelDescriptor descriptor) => descriptor.Id == ModelCatalog.Cleaner.Id;
        public IReadOnlyList<InstalledModel> ListInstalled() => [];
        public Task EnsureModelAsync(ModelDescriptor descriptor, IProgress<ModelDownloadProgress>? progress = null, CancellationToken ct = default) => Task.CompletedTask;
        public Task<long> CleanObsoleteModelsAsync(IEnumerable<string> modelIds, CancellationToken ct = default) => Task.FromResult(0L);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(handler(request));
    }
}
