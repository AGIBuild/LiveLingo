using LiveLingo.Desktop.Composer;
using Microsoft.Extensions.Logging;
using LiveLingo.Core.Inference;

namespace LiveLingo.Desktop.Web;

internal sealed record ToastData(string Text, int? DurationMs = null);

internal sealed record ModelsData(string Text, bool Cloud);

internal sealed record FocusData(string Target);

internal sealed record OpenMenuData(string Id);

internal sealed record LimitData(int MaxHeight);

/// <summary>Whether the user (or the zoom) fixed the window height, so the panel fills the window instead of following its content.</summary>
internal sealed record FrameData(bool FixedHeight);

/// <summary>
/// One summon of the composer: connects its view model to a page. The view model changes, the page gets a snapshot; the
/// page acts, the view model is told. The window around the page only follows <see cref="SizeChanged"/> and
/// <see cref="WideChanged"/>.
/// </summary>
internal sealed class ComposerWebSession : IDisposable
{
    private readonly WebBridge _bridge;
    private readonly ComposerIntents _intents;
    private readonly ComposerTexts _texts;
    private bool _wide;
    private int? _limit;
    private bool _fixedHeight;
    private readonly ModelUsage? _modelUsage;
    private readonly SynchronizationContext _ui;
    private readonly Dictionary<ModelStage, ModelUse> _used = [];
    private readonly Dictionary<ModelStage, ModelUse> _confirmed = [];
    private bool _disposed;
    private bool _pageReady;
    private ModelUse? _lastUse;
    private bool _cloudUsed;
    public ComposerWebSession(
        ComposerViewModel vm,
        ComposerTexts texts,
        RecentDrafts recent,
        IFileDialogs files,
        IWebHost host,
        SynchronizationContext ui,
        TimeProvider time,
        ILogger logger,
        ModelUsage? modelUsage = null)
    {
        Vm = vm;
        _texts = texts;
        _modelUsage = modelUsage;
        _ui = ui;
        if (_modelUsage is not null)
            _modelUsage.Changed += OnModelUsed;
        _wide = vm.IsWide;
        _intents = new ComposerIntents(vm, recent, files, logger);
        _bridge = new WebBridge(
            host, "composer",
            rev => ComposerViewState.From(vm, texts, recent, _intents.SourceRev) with { Rev = rev },
            () => new StringsData(texts.Locale, texts.WebStrings()),
            _intents, ui, time, logger);

        vm.PropertyChanged += OnViewModelChanged;
        vm.ToastRaised += OnToast;
        vm.RetargetRequested += OnRetarget;
        _bridge.Started += SendFocus;
        _bridge.Started += ResendLimit;
        _bridge.Started += () =>
        {
            _pageReady = true;
            SendModels();
            if (_lastUse is { } use)
                ShowModelHint(use);
        };
        _intents.Changed += _bridge.Invalidate;
        _intents.SizeChanged += size => SizeChanged?.Invoke(size);
        _intents.Dragged += () => Dragged?.Invoke();
        _intents.Resized += resize => Resized?.Invoke(resize);
        _intents.Zoomed += () => Zoomed?.Invoke();
        _intents.Clipped += clipped => Clipped?.Invoke(clipped);
        _intents.ModelInfoRequested += () => _bridge.Send("toast", new ToastData(ModelDetails(), 10000));
        _intents.DraftCleared += () => _bridge.Send("focus", new FocusData("source"));
    }

    public ComposerViewModel Vm { get; }

    /// <summary>The page measured its content.</summary>
    public event Action<SizeIntent>? SizeChanged;

    /// <summary>The user started dragging the composer by its header.</summary>
    public event Action? Dragged;

    /// <summary>The user is dragging an edge or the corner of the composer.</summary>
    public event Action<ResizeIntent>? Resized;

    /// <summary>The user asked for the large window, or for the size that follows the content again.</summary>
    public event Action? Zoomed;

    /// <summary>The page is cut off by the window (a diagnostic; the window logs it).</summary>
    public event Action<ClippedIntent>? Clipped;

    /// <summary>Tells the page how tall its panel may get on the screen it is on; beyond that the page scrolls inside.</summary>
    public void LimitPanelHeight(int cssPixels)
    {
        _limit = cssPixels;
        _bridge.Send("limit", new LimitData(cssPixels));
    }

    /// <summary>Tells the page whether the window height is fixed: the panel then fills the window and shows more text.</summary>
    public void SetFixedHeight(bool fixedHeight)
    {
        _fixedHeight = fixedHeight;
        _bridge.Send("frame", new FrameData(fixedHeight));
    }

    /// <summary>The composer switched between the narrow and the wide layout, so the window changes width.</summary>
    public event Action? WideChanged;

    public bool Wide => Vm.IsWide;

    /// <summary>
    /// Starts talking to a page that is already loaded and has announced itself; a page that is still loading starts
    /// the conversation itself with its <c>ready</c> message.
    /// </summary>
    public void Start() => _bridge.Start();

    private void ResendLimit()
    {
        if (_limit is { } limit)
            _bridge.Send("limit", new LimitData(limit));
        if (_fixedHeight)
            _bridge.Send("frame", new FrameData(true));
    }

    private void SendFocus()
    {
        var empty = Vm.Lines.Count == 0 && Vm.SourceLines.Count == 0 && !Vm.HasTimeline;
        _bridge.Send("focus", new FocusData(empty ? "source" : "mic"));
    }

    public void Dispose()
    {
        _disposed = true;
        if (_modelUsage is not null)
            _modelUsage.Changed -= OnModelUsed;
        Vm.PropertyChanged -= OnViewModelChanged;
        Vm.ToastRaised -= OnToast;
        Vm.RetargetRequested -= OnRetarget;
        _bridge.Dispose();
    }

    private void OnViewModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        _bridge.Invalidate();
        if (_wide != Vm.IsWide)
        {
            _wide = Vm.IsWide;
            WideChanged?.Invoke();
        }
    }

    private void OnToast(ToastKind kind) => _bridge.Send("toast", new ToastData(_texts.Toast(kind)));

    private void OnRetarget() => _bridge.Send("openMenu", new OpenMenuData("target"));

    private string ModelDetails() => string.Join('\n', Enum.GetValues<ModelStage>().Select(stage =>
        _texts.ModelDetail(stage, _used.GetValueOrDefault(stage), _confirmed.GetValueOrDefault(stage))));

    private void SendModels() => _bridge.Send("models", new ModelsData(ModelDetails(), _cloudUsed));

    private void OnModelUsed(ModelUse use) => _ui.Post(_ =>
    {
        if (_disposed)
            return;
        var previous = _used.GetValueOrDefault(use.Stage);
        // Repeated requests for an already confirmed model must not flash "switching" between sentences.
        if (!use.Confirmed && previous is { Confirmed: true } && previous.Name == use.Name && previous.Cloud == use.Cloud)
            return;
        if (previous == use && use.Stage != ModelStage.Speech)
            return;
        _used[use.Stage] = use;
        _cloudUsed |= use.Cloud;
        _lastUse = use;
        if (use.Confirmed)
            _confirmed[use.Stage] = use;
        if (!_pageReady)
            return;
        SendModels();
        ShowModelHint(use);
    }, null);

    private void ShowModelHint(ModelUse use)
    {
        if (Vm.IsClearing)
            return;
        if (use.Stage == ModelStage.Speech ||
            use.Stage == ModelStage.Cleaner && Vm.Output == ComposerOutput.Clean ||
            use.Stage == ModelStage.Translator && Vm.Output == ComposerOutput.Translate)
            _bridge.Send("toast", new ToastData(_texts.ModelDetail(use.Stage, use, _confirmed.GetValueOrDefault(use.Stage)), 5000));
    }
}
