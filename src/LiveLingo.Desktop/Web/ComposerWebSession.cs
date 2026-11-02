using LiveLingo.Desktop.Composer;
using Microsoft.Extensions.Logging;

namespace LiveLingo.Desktop.Web;

internal sealed record ToastData(string Text);

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
    public ComposerWebSession(
        ComposerViewModel vm,
        ComposerTexts texts,
        RecentDrafts recent,
        IFileDialogs files,
        IWebHost host,
        SynchronizationContext ui,
        TimeProvider time,
        ILogger logger)
    {
        Vm = vm;
        _texts = texts;
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
        _intents.Changed += _bridge.Invalidate;
        _intents.SizeChanged += size => SizeChanged?.Invoke(size);
        _intents.Dragged += () => Dragged?.Invoke();
        _intents.Resized += resize => Resized?.Invoke(resize);
        _intents.Zoomed += () => Zoomed?.Invoke();
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
}
