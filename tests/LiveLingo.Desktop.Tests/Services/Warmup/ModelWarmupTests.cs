using LiveLingo.Core.Dictation;
using LiveLingo.Core.Inference;
using LiveLingo.Core.Models;
using LiveLingo.Desktop.Composer;
using LiveLingo.Desktop.Services.Configuration;
using LiveLingo.Desktop.Services.Warmup;
using LiveLingo.Desktop.Tests.Composer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace LiveLingo.Desktop.Tests.Services.Warmup;

public sealed class ModelWarmupTests : IDisposable
{
    private readonly ISpeechWarmup _speech = Substitute.For<ISpeechWarmup>();
    private readonly IInferenceWarmup _inference = Substitute.For<IInferenceWarmup>();
    private readonly IModelDownloadCoordinator _downloads = Substitute.For<IModelDownloadCoordinator>();
    private readonly FakeClock _time = new();
    private readonly SettingsModel _settings = new();
    private readonly ModelWarmup _sut;

    public ModelWarmupTests()
    {
        var settings = Substitute.For<ISettingsService>();
        settings.Current.Returns(_settings);
        _speech.WarmAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _inference.WarmAsync(Arg.Any<InferenceRole>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _sut = new ModelWarmup(_speech, _inference, settings, _downloads, NullLogger<ModelWarmup>.Instance, _time);
    }

    public void Dispose() => _sut.Dispose();

    private int SpeechWarms => _speech.ReceivedCalls().Count(call => call.GetMethodInfo().Name == nameof(ISpeechWarmup.WarmAsync));

    private IEnumerable<InferenceRole> InferenceWarms =>
        _inference.ReceivedCalls().Where(call => call.GetMethodInfo().Name == nameof(IInferenceWarmup.WarmAsync))
            .Select(call => (InferenceRole)call.GetArguments()[0]!);

    [Fact]
    public async Task AtStartup_TheSpeechModelsAreLoadedAfterTheApplicationSettled_NotBefore()
    {
        _sut.AtStartup();
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.Equal(0, SpeechWarms);

        _time.Advance(ModelWarmup.StartupDelay);

        await Wait.Until(() => SpeechWarms == 1);
        Assert.Empty(InferenceWarms);
    }

    [Fact]
    public async Task AtStartup_NothingIsLoaded_WhenThePreloadIsTurnedOff()
    {
        _settings.Composer.PreloadSpeech = false;

        _sut.AtStartup();
        _time.Advance(ModelWarmup.StartupDelay);
        await Task.Delay(50, TestContext.Current.CancellationToken);

        Assert.Equal(0, SpeechWarms);
    }

    [Fact]
    public async Task TheGlossaryOfTheSettings_IsWhatTheSpeechModelsAreLoadedWith()
    {
        _settings.Composer.Glossary = "Redis\n缓存穿透=cache penetration";

        _sut.OnSummon(ComposerOutput.Original);

        await _speech.Received(1).WarmAsync(
            Arg.Is<IReadOnlyList<string>>(terms => terms.Contains("Redis") && terms.Contains("缓存穿透")), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(ComposerOutput.Clean, InferenceRole.Cleaner)]
    [InlineData(ComposerOutput.Translate, InferenceRole.Translator)]
    public async Task OnSummon_TheSpeechModelsAndThenTheModelOfTheStartingOutputAreLoaded(ComposerOutput output, InferenceRole role)
    {
        var order = new List<string>();
        _speech.WarmAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            order.Add("speech");
            return Task.CompletedTask;
        });
        _inference.WarmAsync(Arg.Any<InferenceRole>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            order.Add("inference");
            return Task.CompletedTask;
        });

        _sut.OnSummon(output);

        await Wait.Until(() => order.Count == 2);
        Assert.Equal(["speech", "inference"], order);
        Assert.Equal([role], InferenceWarms);
    }

    [Fact]
    public void OnSummon_TheSpeechModelsStartLoadingBeforeTheCallReturns_SoTheComposerCanSayThatTheyAreComing()
    {
        var gate = new TaskCompletionSource();
        _speech.WarmAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>()).Returns(gate.Task);

        _sut.OnSummon(ComposerOutput.Original);

        Assert.Equal(1, SpeechWarms);
        gate.SetResult();
    }

    [Fact]
    public async Task OnSummon_TheOriginalWording_NeedsNoModelBesidesTheSpeechOnes()
    {
        _sut.OnSummon(ComposerOutput.Original);

        await Wait.Until(() => SpeechWarms == 1);
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.Empty(InferenceWarms);
    }

    [Fact]
    public async Task WithCleanupTurnedOff_TheCleanerIsNotLoaded()
    {
        _settings.Composer.CleanupEnabled = false;

        _sut.OnSummon(ComposerOutput.Clean);

        await Wait.Until(() => SpeechWarms == 1);
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.Empty(InferenceWarms);
    }

    [Fact]
    public async Task ChangingTheOutput_LoadsTheModelOfTheNewOutput_WithoutTouchingTheSpeechModels()
    {
        _sut.OnOutputChanged(ComposerOutput.Translate);

        await Wait.Until(() => InferenceWarms.Count() == 1);
        Assert.Equal([InferenceRole.Translator], InferenceWarms);
        Assert.Equal(0, SpeechWarms);
    }

    [Fact]
    public async Task AModelThatIsNotInstalled_IsSkippedQuietly_AndTheOutputModelIsStillTried()
    {
        _speech.WarmAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new ModelNotReadyException(ModelType.SpeechToText, "m", "not installed", "download")));

        _sut.OnSummon(ComposerOutput.Translate);

        await Wait.Until(() => InferenceWarms.Count() == 1);
    }

    [Fact]
    public async Task AFailedLoad_NeverReachesTheCaller_BecauseWhoeverNeedsTheModelAsksAgain()
    {
        _speech.WarmAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new IOException("disk")));

        _sut.OnSummon(ComposerOutput.Translate);

        await Wait.Until(() => InferenceWarms.Count() == 1);
    }

    [Fact]
    public async Task AFinishedSpeechDownload_LoadsTheModelsAtOnce_AnythingElseDoesNot()
    {
        _downloads.StateChanged += Raise.Event<Action<ModelDownloadState>>(
            new ModelDownloadState(ModelCatalog.Cleaner.Id, ModelDownloadStatus.Installed, 100, null));
        _downloads.StateChanged += Raise.Event<Action<ModelDownloadState>>(
            new ModelDownloadState(ModelCatalog.SpeechLive.Id, ModelDownloadStatus.Downloading, 50, null));
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.Equal(0, SpeechWarms);

        _downloads.StateChanged += Raise.Event<Action<ModelDownloadState>>(
            new ModelDownloadState(ModelCatalog.SpeechLive.Id, ModelDownloadStatus.Installed, 100, null));

        await Wait.Until(() => SpeechWarms == 1);
    }

    [Fact]
    public async Task AFinishedSpeechDownload_LoadsNothing_WhenThePreloadIsTurnedOff()
    {
        _settings.Composer.PreloadSpeech = false;

        _downloads.StateChanged += Raise.Event<Action<ModelDownloadState>>(
            new ModelDownloadState(ModelCatalog.SpeechLive.Id, ModelDownloadStatus.Installed, 100, null));
        await Task.Delay(50, TestContext.Current.CancellationToken);

        Assert.Equal(0, SpeechWarms);
    }

    [Fact]
    public void TheContainer_CanBuildIt_WithoutATimeProvider()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddSingleton(_speech);
        services.AddSingleton(_inference);
        services.AddSingleton(_downloads);
        services.AddSingleton(Substitute.For<ISettingsService>());
        services.AddSingleton<Microsoft.Extensions.Logging.ILogger<ModelWarmup>>(NullLogger<ModelWarmup>.Instance);
        services.AddSingleton<ModelWarmup>();
        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<ModelWarmup>());
    }

    [Fact]
    public async Task AfterDispose_DownloadsAreNoLongerListenedTo()
    {
        _sut.Dispose();

        _downloads.StateChanged += Raise.Event<Action<ModelDownloadState>>(
            new ModelDownloadState(ModelCatalog.SpeechLive.Id, ModelDownloadStatus.Installed, 100, null));
        await Task.Delay(50, TestContext.Current.CancellationToken);

        Assert.Equal(0, SpeechWarms);
    }
}
