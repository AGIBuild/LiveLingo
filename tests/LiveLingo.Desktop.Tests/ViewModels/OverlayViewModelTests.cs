using LiveLingo.Desktop.Platform;
using LiveLingo.Desktop.Services.Configuration;
using LiveLingo.Desktop.Services.Localization;
using LiveLingo.Desktop.ViewModels;
using LiveLingo.Core.Engines;
using LiveLingo.Core.Translation;
using NSubstitute;

namespace LiveLingo.Desktop.Tests.ViewModels;

public class OverlayViewModelTests
{
    private readonly TargetWindowInfo _target = new(1, 2, "slack", "Slack", 0, 0, 1920, 1080);
    private readonly ITranslationPipeline _pipeline;
    private readonly ITextInjector _injector;
    private readonly ITranslationEngine _engine;
    private readonly IClipboardService _clipboard;
    private readonly ILocalizationService _loc;

    public OverlayViewModelTests()
    {
        _pipeline = Substitute.For<ITranslationPipeline>();
        _injector = Substitute.For<ITextInjector>();
        _engine = new DeterministicTranslationEngine();
        _clipboard = Substitute.For<IClipboardService>();
        _loc = new LocalizationService();
    }

    private OverlayViewModel CreateVm() => new(_target, _pipeline, _injector, _engine, clipboard: _clipboard, localizationService: _loc);

    [Fact]
    public void Constructor_SetsDefaultValues()
    {
        var vm = CreateVm();
        Assert.Equal(string.Empty, vm.SourceText);
        Assert.Equal(string.Empty, vm.TranslatedText);
        Assert.NotEmpty(vm.ModeLabel);
        Assert.False(vm.IsTranslating);
        Assert.Equal(0, vm.SourceTextLength);
    }

    [Fact]
    public void TargetWindowHandle_ReturnsTargetHandle()
    {
        var vm = CreateVm();
        Assert.Equal((nint)1, vm.TargetWindowHandle);
    }

    [Fact]
    public void TargetInputChild_ReturnsChildHandle()
    {
        var vm = CreateVm();
        Assert.Equal((nint)2, vm.TargetInputChild);
    }

    [Fact]
    public void ToggleMode_SwitchesBetweenModes()
    {
        var vm = CreateVm();
        var initialMode = vm.Mode;
        vm.ToggleModeCommand.Execute(null);
        Assert.NotEqual(initialMode, vm.Mode);

        vm.ToggleModeCommand.Execute(null);
        Assert.Equal(initialMode, vm.Mode);
    }

    [Fact]
    public void ToggleMode_UpdatesModeLabel()
    {
        var vm = CreateVm();
        vm.ToggleModeCommand.Execute(null);
        var label1 = vm.ModeLabel;

        vm.ToggleModeCommand.Execute(null);
        var label2 = vm.ModeLabel;

        Assert.NotEqual(label1, label2);
    }

    [Fact]
    public void AutoSend_ReflectsMode()
    {
        var vm = CreateVm();

        vm.ToggleModeCommand.Execute(null);
        if (vm.Mode == InjectionMode.PasteAndSend)
            Assert.True(vm.AutoSend);
        else
            Assert.False(vm.AutoSend);
    }

    [Fact]
    public void Cancel_RaisesRequestClose()
    {
        var vm = CreateVm();
        bool closed = false;
        vm.RequestClose += () => closed = true;

        vm.CancelCommand.Execute(null);

        Assert.True(closed);
    }

    [Fact]
    public async Task InjectAsync_SkipsWhenTranslatedTextEmpty()
    {
        var vm = CreateVm();
        vm.TranslatedText = string.Empty;

        await vm.InjectAsync();

        await _injector.DidNotReceive()
            .InjectAsync(Arg.Any<TargetWindowInfo>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InjectAsync_DelegatesToInjector()
    {
        var vm = CreateVm();
        vm.TranslatedText = "Hello";

        await vm.InjectAsync();

        await _injector.Received(1)
            .InjectAsync(_target, "Hello", vm.AutoSend, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OnSourceTextChanged_TriggersPipeline_AfterDebounce()
    {
        _pipeline.ProcessAsync(Arg.Any<TranslationRequest>(), Arg.Any<CancellationToken>())
            .Returns(new TranslationResult("Result", "zh", "Result", TimeSpan.FromMilliseconds(10), null));

        var vm = CreateVm();
        vm.SourceText = "你好";

        await Task.Delay(600);

        await _pipeline.Received()
            .ProcessAsync(Arg.Any<TranslationRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void OnSourceTextChanged_ClearsTranslated_WhenEmpty()
    {
        var vm = CreateVm();
        vm.SourceText = "something";
        vm.TranslatedText = "translated";
        vm.SourceText = "";
        Assert.Equal(string.Empty, vm.TranslatedText);
    }

    [Fact]
    public void PasteAndSend_ModeLabel()
    {
        var vm = CreateVm();
        while (vm.Mode != InjectionMode.PasteAndSend)
            vm.ToggleModeCommand.Execute(null);

        Assert.Equal("Paste & Send", vm.ModeLabel);
    }

    [Fact]
    public void PasteOnly_ModeLabel()
    {
        var vm = CreateVm();
        while (vm.Mode != InjectionMode.PasteOnly)
            vm.ToggleModeCommand.Execute(null);

        Assert.Equal("Paste Only", vm.ModeLabel);
    }

    [Fact]
    public void PropertyChanged_IsRaised()
    {
        var vm = CreateVm();
        var raised = new List<string>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        vm.TranslatedText = "test";

        Assert.Contains("TranslatedText", raised);
    }

    [Fact]
    public void TargetLanguage_SetFromConstructor()
    {
        var vm = new OverlayViewModel(_target, _pipeline, _injector, _engine, "zh", localizationService: _loc);
        Assert.Equal("zh", vm.TargetLanguage);
    }

    [Fact]
    public void TargetLanguage_DefaultsToEn()
    {
        var vm = CreateVm();
        Assert.Equal("en", vm.TargetLanguage);
        Assert.NotNull(vm.SelectedTargetLanguage);
        Assert.Equal("en", vm.SelectedTargetLanguage!.Code);
    }

    [Fact]
    public async Task RunPipelineAsync_SetsTranslatedTextAndStatus()
    {
        _pipeline.ProcessAsync(Arg.Any<TranslationRequest>(), Arg.Any<CancellationToken>())
            .Returns(new TranslationResult("Translated", "zh", "Translated", TimeSpan.FromMilliseconds(42), null));

        var vm = CreateVm();
        vm.SourceText = "你好";
        await Task.Delay(600);

        Assert.Equal("Translated", vm.TranslatedText);
        Assert.Contains("42", vm.StatusText);
    }

    [Fact]
    public async Task RunPipelineAsync_CancelsPrevious_WhenSourceTextChangesRapidly()
    {
        var slowTcs = new TaskCompletionSource<TranslationResult>();
        _pipeline.ProcessAsync(Arg.Any<TranslationRequest>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var ct = callInfo.ArgAt<CancellationToken>(1);
                return Task.Run(async () =>
                {
                    await Task.Delay(2000, ct);
                    return new TranslationResult("Old", "zh", "Old", TimeSpan.Zero, null);
                }, ct);
            });

        var vm = CreateVm();
        vm.SourceText = "first";
        await Task.Delay(50);

        _pipeline.ProcessAsync(Arg.Any<TranslationRequest>(), Arg.Any<CancellationToken>())
            .Returns(new TranslationResult("Latest", "zh", "Latest", TimeSpan.Zero, null));

        vm.SourceText = "second";
        await Task.Delay(600);

        Assert.Equal("Latest", vm.TranslatedText);
    }

    [Fact]
    public async Task InjectAsync_SkipsWhenTranslatedTextIsWhitespace()
    {
        var vm = CreateVm();
        vm.TranslatedText = "   ";

        await vm.InjectAsync();

        await _injector.DidNotReceive()
            .InjectAsync(Arg.Any<TargetWindowInfo>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InjectAsync_PassesCorrectAutoSend_PasteAndSend()
    {
        var vm = CreateVm();
        while (vm.Mode != InjectionMode.PasteAndSend)
            vm.ToggleModeCommand.Execute(null);
        vm.TranslatedText = "text";

        await vm.InjectAsync();

        await _injector.Received(1)
            .InjectAsync(_target, "text", true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InjectAsync_PassesCorrectAutoSend_PasteOnly()
    {
        var vm = CreateVm();
        while (vm.Mode != InjectionMode.PasteOnly)
            vm.ToggleModeCommand.Execute(null);
        vm.TranslatedText = "text";

        await vm.InjectAsync();

        await _injector.Received(1)
            .InjectAsync(_target, "text", false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunPipelineAsync_ShowsTranslatingStatus_BeforeComplete()
    {
        var tcs = new TaskCompletionSource<TranslationResult>();
        _pipeline.ProcessAsync(Arg.Any<TranslationRequest>(), Arg.Any<CancellationToken>())
            .Returns(tcs.Task);

        var vm = CreateVm();
        vm.SourceText = "test";
        await Task.Delay(700);

        Assert.Equal("Translating...", vm.StatusText);
        Assert.True(vm.IsTranslating);
        tcs.SetResult(new TranslationResult("Done", "zh", "Done", TimeSpan.Zero, null));
    }

    [Fact]
    public void OnSourceTextChanged_WhitespaceOnly_ClearsTranslated()
    {
        var vm = CreateVm();
        vm.TranslatedText = "something";
        vm.SourceText = "   ";
        Assert.Equal(string.Empty, vm.TranslatedText);
    }

    [Fact]
    public async Task RunPipelineAsync_ShowsErrorStatus_WhenModelNotFound()
    {
        _pipeline.ProcessAsync(Arg.Any<TranslationRequest>(), Arg.Any<CancellationToken>())
            .Returns<TranslationResult>(_ => throw new FileNotFoundException("Model not found"));

        var vm = CreateVm();
        vm.SourceText = "test";
        await Task.Delay(600);

        Assert.Contains("Model not downloaded", vm.StatusText);
    }

    [Fact]
    public async Task RunPipelineAsync_ShowsErrorStatus_WhenGenericException()
    {
        _pipeline.ProcessAsync(Arg.Any<TranslationRequest>(), Arg.Any<CancellationToken>())
            .Returns<TranslationResult>(_ => throw new InvalidOperationException("Something broke"));

        var vm = CreateVm();
        vm.SourceText = "test";
        await Task.Delay(600);

        Assert.Contains("Something broke", vm.StatusText);
        Assert.StartsWith("Error:", vm.StatusText);
    }

    [Fact]
    public async Task RunPipelineAsync_SilentOnCancellation()
    {
        _pipeline.ProcessAsync(Arg.Any<TranslationRequest>(), Arg.Any<CancellationToken>())
            .Returns<TranslationResult>(_ => throw new OperationCanceledException());

        var vm = CreateVm();
        vm.SourceText = "test";
        await Task.Delay(600);

        Assert.DoesNotContain("Error", vm.StatusText);
    }

    [Fact]
    public async Task EndToEnd_RealPipeline_WithDeterministicEngine()
    {
        var engine = new DeterministicTranslationEngine();
        var detector = new LiveLingo.Core.LanguageDetection.ScriptBasedDetector();
        var pipeline = new LiveLingo.Core.Translation.TranslationPipeline(
            detector, engine, [],
            NSubstitute.Substitute.For<Microsoft.Extensions.Logging.ILogger<LiveLingo.Core.Translation.TranslationPipeline>>());

        var settings = new UserSettings
        {
            Translation = new TranslationSettings
            {
                DefaultSourceLanguage = "zh",
                DefaultTargetLanguage = "en"
            }
        };
        var vm = new OverlayViewModel(_target, pipeline, _injector, engine, settings, localizationService: _loc);

        vm.SourceText = "你好世界";
        await Task.Delay(700);

        Assert.Equal("[zh→en] 你好世界", vm.TranslatedText);
        Assert.Contains("Translated", vm.StatusText);
        Assert.DoesNotContain("Error", vm.StatusText);
    }

    [Fact]
    public async Task EndToEnd_RealPipeline_SameLanguageSkipsTranslation()
    {
        var engine = new DeterministicTranslationEngine();
        var detector = NSubstitute.Substitute.For<LiveLingo.Core.LanguageDetection.ILanguageDetector>();
        detector.DetectAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new LiveLingo.Core.LanguageDetection.DetectionResult("en", 0.99f));

        var pipeline = new LiveLingo.Core.Translation.TranslationPipeline(
            detector, engine, [],
            NSubstitute.Substitute.For<Microsoft.Extensions.Logging.ILogger<LiveLingo.Core.Translation.TranslationPipeline>>());

        var vm = new OverlayViewModel(_target, pipeline, _injector, engine, "en", localizationService: _loc);

        vm.SourceText = "Hello";
        await Task.Delay(700);

        Assert.Equal("Hello", vm.TranslatedText);
    }

    [Fact]
    public async Task EndToEnd_RealPipeline_WithPostProcessing()
    {
        var engine = new DeterministicTranslationEngine();
        var detector = new LiveLingo.Core.LanguageDetection.ScriptBasedDetector();
        var processor = NSubstitute.Substitute.For<LiveLingo.Core.Processing.ITextProcessor>();
        processor.Name.Returns("summarize");
        processor.ProcessAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => $"SUMMARY({callInfo.ArgAt<string>(0)})");

        var pipeline = new LiveLingo.Core.Translation.TranslationPipeline(
            detector, engine, [processor],
            NSubstitute.Substitute.For<Microsoft.Extensions.Logging.ILogger<LiveLingo.Core.Translation.TranslationPipeline>>());

        var settings = new UserSettings
        {
            Translation = new TranslationSettings { DefaultSourceLanguage = "zh", DefaultTargetLanguage = "en" },
            Processing = new ProcessingSettings { DefaultMode = "Summarize" }
        };
        var vm = new OverlayViewModel(_target, pipeline, _injector, engine, settings, localizationService: _loc);

        vm.SourceText = "你好世界";
        await Task.Delay(700);

        Assert.Equal("SUMMARY([zh→en] 你好世界)", vm.TranslatedText);
        Assert.Contains("post", vm.StatusText);
    }

    private sealed class DeterministicTranslationEngine : LiveLingo.Core.Engines.ITranslationEngine
    {
        public IReadOnlyList<LiveLingo.Core.Engines.LanguageInfo> SupportedLanguages { get; } =
        [
            new("en", "English"), new("zh", "中文"), new("ja", "日本語"),
            new("ko", "한국어"), new("fr", "Français"), new("de", "Deutsch"),
            new("es", "Español"), new("ru", "Русский"), new("ar", "العربية"),
            new("pt", "Português"),
        ];

        public Task<string> TranslateAsync(string text, string src, string tgt, CancellationToken ct)
            => Task.FromResult($"[{src}→{tgt}] {text}");

        public bool SupportsLanguagePair(string src, string tgt) => true;
        public void Dispose() { }
    }

    [Fact]
    public void Constructor_WithSettings_UsesTargetLanguage()
    {
        var settings = new UserSettings
        {
            Translation = new TranslationSettings { DefaultTargetLanguage = "ja" }
        };
        var vm = new OverlayViewModel(_target, _pipeline, _injector, _engine, settings, localizationService: _loc);
        Assert.Equal("ja", vm.TargetLanguage);
    }

    [Fact]
    public void Constructor_WithSettings_UsesPasteOnlyMode()
    {
        var settings = new UserSettings
        {
            UI = new UISettings { DefaultInjectionMode = "PasteOnly" }
        };
        var vm = new OverlayViewModel(_target, _pipeline, _injector, _engine, settings, localizationService: _loc);
        Assert.Equal(InjectionMode.PasteOnly, vm.Mode);
    }

    [Fact]
    public void Constructor_WithSettings_UsesPasteAndSendMode()
    {
        var settings = new UserSettings
        {
            UI = new UISettings { DefaultInjectionMode = "PasteAndSend" }
        };
        var vm = new OverlayViewModel(_target, _pipeline, _injector, _engine, settings, localizationService: _loc);
        Assert.Equal(InjectionMode.PasteAndSend, vm.Mode);
    }

    [Fact]
    public async Task Constructor_WithSettings_AppliesPostProcessing()
    {
        var settings = new UserSettings
        {
            Processing = new ProcessingSettings { DefaultMode = "Summarize" }
        };

        _pipeline.ProcessAsync(Arg.Any<TranslationRequest>(), Arg.Any<CancellationToken>())
            .Returns(new TranslationResult("Result", "zh", "Result", TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(5)));

        var vm = new OverlayViewModel(_target, _pipeline, _injector, _engine, settings, localizationService: _loc);
        vm.SourceText = "你好世界";
        await Task.Delay(600);

        await _pipeline.Received().ProcessAsync(
            Arg.Is<TranslationRequest>(r => r.PostProcessing != null && r.PostProcessing.Summarize),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Constructor_WithSettings_UsesSourceLanguage()
    {
        var settings = new UserSettings
        {
            Translation = new TranslationSettings { DefaultSourceLanguage = "ja" }
        };

        _pipeline.ProcessAsync(Arg.Any<TranslationRequest>(), Arg.Any<CancellationToken>())
            .Returns(new TranslationResult("Result", "ja", "Result", TimeSpan.FromMilliseconds(10), null));

        var vm = new OverlayViewModel(_target, _pipeline, _injector, _engine, settings, localizationService: _loc);
        vm.SourceText = "こんにちは";
        await Task.Delay(600);

        await _pipeline.Received().ProcessAsync(
            Arg.Is<TranslationRequest>(r => r.SourceLanguage == "ja"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void CycleLanguage_CyclesThroughEngineLanguages()
    {
        var settings = new UserSettings
        {
            Translation = new TranslationSettings { DefaultTargetLanguage = "en" }
        };
        var vm = new OverlayViewModel(_target, _pipeline, _injector, _engine, settings, localizationService: _loc);
        Assert.Equal("en", vm.TargetLanguage);

        vm.CycleLanguageCommand.Execute(null);
        Assert.Equal(_engine.SupportedLanguages[1].Code, vm.TargetLanguage);
    }

    [Fact]
    public void CycleLanguage_WrapsAroundToFirst()
    {
        var lastLang = _engine.SupportedLanguages[^1].Code;
        var vm = new OverlayViewModel(_target, _pipeline, _injector, _engine, lastLang, localizationService: _loc);

        vm.CycleLanguageCommand.Execute(null);
        Assert.Equal(_engine.SupportedLanguages[0].Code, vm.TargetLanguage);
    }

    [Fact]
    public void CycleLanguage_StartsFromSettingsDefault()
    {
        var settings = new UserSettings
        {
            Translation = new TranslationSettings { DefaultTargetLanguage = "ja" }
        };
        var vm = new OverlayViewModel(_target, _pipeline, _injector, _engine, settings, localizationService: _loc);
        Assert.Equal("ja", vm.TargetLanguage);

        vm.CycleLanguageCommand.Execute(null);
        var jaIndex = _engine.SupportedLanguages.ToList().FindIndex(l => l.Code == "ja");
        Assert.Equal(_engine.SupportedLanguages[(jaIndex + 1) % _engine.SupportedLanguages.Count].Code, vm.TargetLanguage);
    }

    [Fact]
    public void AvailableLanguages_ComesFromEngine()
    {
        Assert.True(_engine.SupportedLanguages.Count >= 5);
        Assert.Contains(_engine.SupportedLanguages, l => l.Code == "en");
        Assert.Contains(_engine.SupportedLanguages, l => l.Code == "zh");
        Assert.Contains(_engine.SupportedLanguages, l => l.Code == "ja");
    }

    [Fact]
    public void SelectedTargetLanguage_UpdatesTargetLanguage()
    {
        var vm = CreateVm();
        var zh = _engine.SupportedLanguages.First(l => l.Code == "zh");

        vm.SelectedTargetLanguage = zh;

        Assert.Equal("zh", vm.TargetLanguage);
    }

    [Fact]
    public async Task SelectedTargetLanguage_RetranslatesWhenSourceExists()
    {
        _pipeline.ProcessAsync(Arg.Any<TranslationRequest>(), Arg.Any<CancellationToken>())
            .Returns(new TranslationResult("Result", "zh", "Result", TimeSpan.FromMilliseconds(10), null));
        var vm = CreateVm();
        vm.SourceText = "hello";
        await Task.Delay(600);

        vm.SelectedTargetLanguage = _engine.SupportedLanguages.First(l => l.Code == "zh");
        await Task.Delay(600);

        await _pipeline.Received().ProcessAsync(
            Arg.Is<TranslationRequest>(r => r.TargetLanguage == "zh"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Constructor_WithUnsupportedTarget_FallsBackToFirstSupportedLanguage()
    {
        var settings = new UserSettings
        {
            Translation = new TranslationSettings { DefaultTargetLanguage = "xx" }
        };
        var vm = new OverlayViewModel(_target, _pipeline, _injector, _engine, settings, localizationService: _loc);

        Assert.Equal(_engine.SupportedLanguages[0].Code, vm.TargetLanguage);
        Assert.NotNull(vm.SelectedTargetLanguage);
        Assert.Equal(_engine.SupportedLanguages[0].Code, vm.SelectedTargetLanguage!.Code);
    }

    [Fact]
    public void Constructor_WithEmptySupportedLanguages_LeavesSelectedTargetNull()
    {
        var engine = new EmptyLanguageEngine();
        var vm = new OverlayViewModel(_target, _pipeline, _injector, engine, "en", localizationService: _loc);

        Assert.Equal("en", vm.TargetLanguage);
        Assert.Null(vm.SelectedTargetLanguage);
        Assert.Empty(vm.AvailableTargetLanguages);
    }

    [Fact]
    public void Constructor_SettingsWithEmptyLanguages_UsesDefaultEnglishTarget()
    {
        var engine = new EmptyLanguageEngine();
        var settings = new UserSettings
        {
            Translation = new TranslationSettings
            {
                DefaultSourceLanguage = "",
                DefaultTargetLanguage = ""
            }
        };

        var vm = new OverlayViewModel(_target, _pipeline, _injector, engine, settings, localizationService: _loc);

        Assert.Equal("en", vm.TargetLanguage);
        Assert.Null(vm.SelectedTargetLanguage);
    }

    [Fact]
    public void CycleLanguage_DoesNothing_WhenNoLanguages()
    {
        var engine = new EmptyLanguageEngine();
        var vm = new OverlayViewModel(_target, _pipeline, _injector, engine, "en", localizationService: _loc);

        vm.CycleLanguageCommand.Execute(null);

        Assert.Equal("en", vm.TargetLanguage);
    }

    [Fact]
    public async Task RunPipelineAsync_ShowsError_WhenTargetLanguageNotConfigured()
    {
        var vm = CreateVm();
        vm.TargetLanguage = "";
        vm.SourceText = "hello";
        await Task.Delay(600);

        Assert.Contains("Target language is not configured", vm.StatusText);
        await _pipeline.DidNotReceive()
            .ProcessAsync(Arg.Any<TranslationRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunPipelineAsync_ShowsError_WhenLanguagePairUnsupported()
    {
        var engine = new PairRejectingEngine();
        var settings = new UserSettings
        {
            Translation = new TranslationSettings
            {
                DefaultSourceLanguage = "ja",
                DefaultTargetLanguage = "en"
            }
        };
        var vm = new OverlayViewModel(_target, _pipeline, _injector, engine, settings, localizationService: _loc);
        vm.SourceText = "test";
        await Task.Delay(600);

        Assert.Contains("Unsupported language pair", vm.StatusText);
        await _pipeline.DidNotReceive()
            .ProcessAsync(Arg.Any<TranslationRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void SelectedTargetLanguage_Null_DoesNotChangeTargetLanguage()
    {
        var vm = CreateVm();
        var current = vm.TargetLanguage;

        vm.SelectedTargetLanguage = null;

        Assert.Equal(current, vm.TargetLanguage);
    }

    [Fact]
    public void CancelCommand_WithoutSubscriber_DoesNotThrow()
    {
        var vm = CreateVm();
        vm.CancelCommand.Execute(null);
    }

    // --- A1: Debounce tests ---

    [Fact]
    public async Task Debounce_RapidInput_OnlyTriggersOnce()
    {
        _pipeline.ProcessAsync(Arg.Any<TranslationRequest>(), Arg.Any<CancellationToken>())
            .Returns(new TranslationResult("R", "zh", "R", TimeSpan.Zero, null));

        var vm = CreateVm();
        vm.SourceText = "a";
        await Task.Delay(50);
        vm.SourceText = "ab";
        await Task.Delay(50);
        vm.SourceText = "abc";

        await Task.Delay(600);

        await _pipeline.Received(1)
            .ProcessAsync(
                Arg.Is<TranslationRequest>(r => r.SourceText == "abc"),
                Arg.Any<CancellationToken>());
    }

    // --- A3: Copy tests ---

    [Fact]
    public async Task CopyTranslationCommand_CopiesToClipboard()
    {
        var vm = CreateVm();
        vm.TranslatedText = "Hello World";

        await vm.CopyTranslationCommand.ExecuteAsync(null);

        await _clipboard.Received(1).SetTextAsync("Hello World", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CopyTranslationCommand_ShowsFeedbackThenResets()
    {
        var vm = CreateVm();
        vm.TranslatedText = "Hello";

        var task = vm.CopyTranslationCommand.ExecuteAsync(null);

        Assert.True(vm.ShowCopiedFeedback);

        await task;

        Assert.False(vm.ShowCopiedFeedback);
    }

    [Fact]
    public async Task CopyTranslationCommand_NoOp_WhenClipboardNull()
    {
        var vm = new OverlayViewModel(_target, _pipeline, _injector, _engine, localizationService: _loc);
        vm.TranslatedText = "Hello";

        await vm.CopyTranslationCommand.ExecuteAsync(null);

        Assert.False(vm.ShowCopiedFeedback);
    }

    [Fact]
    public async Task CopyTranslationCommand_NoOp_WhenTextEmpty()
    {
        var vm = CreateVm();
        vm.TranslatedText = "";

        await vm.CopyTranslationCommand.ExecuteAsync(null);

        await _clipboard.DidNotReceive().SetTextAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // --- A4: IsTranslating tests ---

    [Fact]
    public async Task IsTranslating_TrueDuringPipeline_FalseAfter()
    {
        var tcs = new TaskCompletionSource<TranslationResult>();
        _pipeline.ProcessAsync(Arg.Any<TranslationRequest>(), Arg.Any<CancellationToken>())
            .Returns(tcs.Task);

        var vm = CreateVm();
        vm.SourceText = "test";
        await Task.Delay(550);

        Assert.True(vm.IsTranslating);

        tcs.SetResult(new TranslationResult("Done", "zh", "Done", TimeSpan.Zero, null));
        await Task.Delay(50);

        Assert.False(vm.IsTranslating);
    }

    [Fact]
    public async Task IsTranslating_FalseAfterError()
    {
        _pipeline.ProcessAsync(Arg.Any<TranslationRequest>(), Arg.Any<CancellationToken>())
            .Returns<TranslationResult>(_ => throw new InvalidOperationException("fail"));

        var vm = CreateVm();
        vm.SourceText = "test";
        await Task.Delay(600);

        Assert.False(vm.IsTranslating);
    }

    [Fact]
    public void IsTranslating_FalseWhenCleared()
    {
        var vm = CreateVm();
        vm.SourceText = "test";
        vm.SourceText = "";

        Assert.False(vm.IsTranslating);
    }

    // --- C1: Swap languages tests ---

    [Fact]
    public void SwapLanguages_SwapsSourceAndTarget()
    {
        var settings = new UserSettings
        {
            Translation = new TranslationSettings
            {
                DefaultSourceLanguage = "zh",
                DefaultTargetLanguage = "en"
            }
        };
        var vm = new OverlayViewModel(_target, _pipeline, _injector, _engine, settings, _clipboard, localizationService: _loc);

        vm.SwapLanguagesCommand.Execute(null);

        Assert.Equal("zh", vm.TargetLanguage);
        Assert.NotNull(vm.SelectedSourceLanguage);
        Assert.Equal("en", vm.SelectedSourceLanguage!.Code);
    }

    [Fact]
    public void SwapLanguages_DoesNothing_WhenNoSelectedTarget()
    {
        var engine = new EmptyLanguageEngine();
        var vm = new OverlayViewModel(_target, _pipeline, _injector, engine, "en", localizationService: _loc);

        vm.SwapLanguagesCommand.Execute(null);

        Assert.Equal("en", vm.TargetLanguage);
    }

    // --- C3: Character count tests ---

    [Fact]
    public void SourceTextLength_UpdatesOnSourceTextChanged()
    {
        var vm = CreateVm();

        vm.SourceText = "Hello";
        Assert.Equal(5, vm.SourceTextLength);

        vm.SourceText = "";
        Assert.Equal(0, vm.SourceTextLength);
    }

    [Fact]
    public void SourceTextLength_TracksMultibyteCharacters()
    {
        var vm = CreateVm();
        vm.SourceText = "你好世界";
        Assert.Equal(4, vm.SourceTextLength);
    }

    // --- Settings constructor with source language ---

    [Fact]
    public void Constructor_WithSettings_SetsSelectedSourceLanguage()
    {
        var settings = new UserSettings
        {
            Translation = new TranslationSettings
            {
                DefaultSourceLanguage = "zh",
                DefaultTargetLanguage = "en"
            }
        };
        var vm = new OverlayViewModel(_target, _pipeline, _injector, _engine, settings, localizationService: _loc);

        Assert.NotNull(vm.SelectedSourceLanguage);
        Assert.Equal("zh", vm.SelectedSourceLanguage!.Code);
    }

    [Fact]
    public void Constructor_WithSettings_NullSourceLanguage_SelectedSourceNull()
    {
        var settings = new UserSettings
        {
            Translation = new TranslationSettings { DefaultSourceLanguage = "" }
        };
        var vm = new OverlayViewModel(_target, _pipeline, _injector, _engine, settings, localizationService: _loc);

        Assert.Null(vm.SelectedSourceLanguage);
    }

    // --- D1: Persistence tests ---

    [Fact]
    public void PersistIfChanged_TargetLanguageChanged_CallsUpdate()
    {
        var settingsService = Substitute.For<ISettingsService>();
        settingsService.Current.Returns(new UserSettings());
        var settings = new UserSettings
        {
            Translation = new TranslationSettings { DefaultTargetLanguage = "en" }
        };
        var vm = new OverlayViewModel(_target, _pipeline, _injector, _engine, settings,
            localizationService: _loc, settingsService: settingsService);

        vm.SelectedTargetLanguage = _engine.SupportedLanguages.First(l => l.Code == "zh");
        vm.PersistIfChanged();

        settingsService.Received(1).Update(Arg.Any<Func<UserSettings, UserSettings>>());
    }

    [Fact]
    public void PersistIfChanged_NoChange_DoesNotCallUpdate()
    {
        var settingsService = Substitute.For<ISettingsService>();
        settingsService.Current.Returns(new UserSettings());
        var settings = new UserSettings
        {
            Translation = new TranslationSettings { DefaultTargetLanguage = "en" }
        };
        var vm = new OverlayViewModel(_target, _pipeline, _injector, _engine, settings,
            localizationService: _loc, settingsService: settingsService);

        vm.PersistIfChanged();

        settingsService.DidNotReceive().Update(Arg.Any<Func<UserSettings, UserSettings>>());
    }

    [Fact]
    public void PersistIfChanged_ModeChanged_CallsUpdate()
    {
        var settingsService = Substitute.For<ISettingsService>();
        settingsService.Current.Returns(new UserSettings());
        var settings = new UserSettings
        {
            UI = new UISettings { DefaultInjectionMode = "PasteAndSend" }
        };
        var vm = new OverlayViewModel(_target, _pipeline, _injector, _engine, settings,
            localizationService: _loc, settingsService: settingsService);

        vm.ToggleModeCommand.Execute(null);
        vm.PersistIfChanged();

        settingsService.Received(1).Update(Arg.Any<Func<UserSettings, UserSettings>>());
    }

    [Fact]
    public void Cancel_DoesNotPersistDirectly_DeferredToCloseEvent()
    {
        var settingsService = Substitute.For<ISettingsService>();
        settingsService.Current.Returns(new UserSettings());
        var settings = new UserSettings
        {
            Translation = new TranslationSettings { DefaultTargetLanguage = "en" }
        };
        var vm = new OverlayViewModel(_target, _pipeline, _injector, _engine, settings,
            localizationService: _loc, settingsService: settingsService);

        vm.SelectedTargetLanguage = _engine.SupportedLanguages.First(l => l.Code == "ja");
        vm.CancelCommand.Execute(null);

        settingsService.DidNotReceive().Update(Arg.Any<Func<UserSettings, UserSettings>>());

        vm.PersistIfChanged();
        settingsService.Received(1).Update(Arg.Any<Func<UserSettings, UserSettings>>());
    }

    [Fact]
    public void PersistIfChanged_NullSettingsService_DoesNotThrow()
    {
        var settings = new UserSettings
        {
            Translation = new TranslationSettings { DefaultTargetLanguage = "en" }
        };
        var vm = new OverlayViewModel(_target, _pipeline, _injector, _engine, settings,
            localizationService: _loc);

        vm.SelectedTargetLanguage = _engine.SupportedLanguages.First(l => l.Code == "zh");
        vm.PersistIfChanged();
    }

    // --- D2: Settings entry tests ---

    [Fact]
    public void OpenSettingsCommand_RaisesRequestOpenSettings()
    {
        var vm = CreateVm();
        bool raised = false;
        vm.RequestOpenSettings += () => raised = true;

        vm.OpenSettingsCommand.Execute(null);

        Assert.True(raised);
    }

    [Fact]
    public void OpenSettingsCommand_WithoutSubscriber_DoesNotThrow()
    {
        var vm = CreateVm();
        vm.OpenSettingsCommand.Execute(null);
    }

    // --- D3: Language picker tests ---

    [Fact]
    public void ToggleLanguagePicker_TogglesState()
    {
        var vm = CreateVm();
        Assert.False(vm.IsLanguagePickerOpen);

        vm.ToggleLanguagePickerCommand.Execute(null);
        Assert.True(vm.IsLanguagePickerOpen);

        vm.ToggleLanguagePickerCommand.Execute(null);
        Assert.False(vm.IsLanguagePickerOpen);
    }

    [Fact]
    public void SelectLanguage_SetsTargetAndClosesPicker()
    {
        var vm = CreateVm();
        vm.IsLanguagePickerOpen = true;
        var ja = _engine.SupportedLanguages.First(l => l.Code == "ja");

        vm.SelectLanguageCommand.Execute(ja);

        Assert.Equal("ja", vm.TargetLanguage);
        Assert.False(vm.IsLanguagePickerOpen);
    }

    private sealed class EmptyLanguageEngine : LiveLingo.Core.Engines.ITranslationEngine
    {
        public IReadOnlyList<LiveLingo.Core.Engines.LanguageInfo> SupportedLanguages { get; } = [];
        public Task<string> TranslateAsync(string text, string src, string tgt, CancellationToken ct) => Task.FromResult(text);
        public bool SupportsLanguagePair(string src, string tgt) => false;
        public void Dispose() { }
    }

    private sealed class PairRejectingEngine : LiveLingo.Core.Engines.ITranslationEngine
    {
        public IReadOnlyList<LiveLingo.Core.Engines.LanguageInfo> SupportedLanguages { get; } =
            [new("ja", "日本語"), new("en", "English")];
        public Task<string> TranslateAsync(string text, string src, string tgt, CancellationToken ct) => Task.FromResult(text);
        public bool SupportsLanguagePair(string src, string tgt) => false;
        public void Dispose() { }
    }
}
