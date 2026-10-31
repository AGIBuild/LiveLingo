using LiveLingo.Core;
using LiveLingo.Core.Drafting;
using LiveLingo.Core.Models;
using LiveLingo.Core.Translation;
using LiveLingo.Desktop.Composer;
using LiveLingo.Desktop.Platform;
using NSubstitute;

namespace LiveLingo.Desktop.Tests.Composer;

internal sealed class ScriptedTranslator : ITranslator
{
    public Func<TranslationRequest, CancellationToken, Task<TranslatedText>> Handler { get; set; } =
        (request, _) => Task.FromResult(new TranslatedText("T:" + request.Source, 1, 2));

    public List<TranslationRequest> Requests { get; } = [];

    public Task<TranslatedText> TranslateAsync(TranslationRequest request, CancellationToken ct)
    {
        lock (Requests)
            Requests.Add(request);
        return Handler(request, ct);
    }
}

internal sealed class Harness : IAsyncDisposable
{
    public static readonly TargetWindowInfo Target = new(1, 0, "Slack", "chat", 0, 0, 800, 600);

    public DraftSession Session { get; }
    public ScriptedTranslator Translator { get; } = new();
    public TranslationScheduler Scheduler { get; }
    public IModelManager Models { get; } = Substitute.For<IModelManager>();
    public IModelDownloadCoordinator Downloads { get; } = Substitute.For<IModelDownloadCoordinator>();
    public IPlatformServices Platform { get; } = Substitute.For<IPlatformServices>();
    public ITextInjector Injector { get; } = Substitute.For<ITextInjector>();
    public IClipboardService Clipboard { get; } = Substitute.For<IClipboardService>();
    public IWindowTracker Tracker { get; } = Substitute.For<IWindowTracker>();
    public ModelDescriptor Runtime { get; } = ModelCatalog.LlamaRuntimeFor("osx-arm64");
    public ModelDescriptor TranslatorModel { get; } = ModelCatalog.TranslatorFast;
    public ComposerViewModel Vm { get; }
    public List<string> Events { get; } = [];

    public Harness(bool modelsInstalled = true, bool hasTarget = true, SynchronizationContext? notifyOn = null)
    {
        Session = new DraftSession(hasTarget, notifyOn);
        Scheduler = new TranslationScheduler(Session, Translator, TargetLanguage.English, TimeSpan.FromHours(1));
        Models.HasAllExpectedLocalAssets(Arg.Any<ModelDescriptor>()).Returns(modelsInstalled);
        Platform.TextInjector.Returns(Injector);
        Platform.Clipboard.Returns(Clipboard);
        Platform.WindowTracker.Returns(Tracker);
        Tracker.IsAlive(Arg.Any<TargetWindowInfo>()).Returns(true);
        Vm = new ComposerViewModel(
            Session, Scheduler, [Runtime, TranslatorModel], Models, Downloads, Platform, hasTarget ? Target : null, notifyOn);
        Vm.Completed += () => Events.Add("completed");
        Vm.CloseRequested += () => Events.Add("close");
    }

    public async ValueTask DisposeAsync()
    {
        Vm.Dispose();
        await Scheduler.DisposeAsync();
    }
}

internal static class Wait
{
    public static async Task Until(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("Condition was not met in time.");
            await Task.Delay(5);
        }
    }
}
