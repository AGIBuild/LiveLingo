using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using LiveLingo.Desktop.Platform;
using LiveLingo.Desktop.Web;
using LiveLingo.Core.Inference;
using Microsoft.Extensions.Logging;

namespace LiveLingo.Desktop.Composer;

/// <summary>
/// The transparent, borderless window around the composer's page. It is created once and never hidden: between uses it
/// is parked (invisible and off screen) so the web view stays alive and the next summon only has to move it back
/// (hiding a window made the next show take up to 650 ms in WebKit, design section 8). A summon is one
/// <see cref="ComposerWebSession"/>; the window only places and sizes itself to what the page reports.
/// </summary>
internal sealed class ComposerWindow : Window
{
    /// <summary>The page keeps an 18 px margin around the panel for its shadow.</summary>
    private const double ShadowMargin = 36;
    private const double NarrowWidth = 620 + ShadowMargin;
    private const double WideWidth = 940 + ShadowMargin;
    private static readonly PixelPoint ParkedAt = new(-20000, -20000);

    private readonly WebSurface _web;
    private readonly IWindowMover _mover;
    private readonly ILogger _logger;
    private ComposerWebSession? _session;
    private TargetWindowInfo? _target;
    private TaskCompletionSource? _dismissed;
    private bool _placed;
    private readonly ModelUsage _modelUsage;

    // The user moved the window by its header or sized it: it stays where it is (kept on screen) until the next summon.
    private bool _moved;

    // The size the user chose by dragging an edge or zooming; null while the window follows the page's content (height)
    // or the layout (width). The last height the page asked for is kept so the window can go back to it.
    private double? _userWidth;
    private double? _userHeight;
    private double _contentHeight;

    public ComposerWindow(ILogger logger, IWindowMover mover, ModelUsage modelUsage)
    {
        _modelUsage = modelUsage;
        _logger = logger;
        _mover = mover;
        _web = new WebSurface(WebPage.ComposerEntry, logger);
        Width = NarrowWidth;
        Height = 260;
        CanResize = false;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        WindowDecorations = WindowDecorations.None;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        WindowStartupLocation = WindowStartupLocation.Manual;
        Title = "LiveLingo";
        Content = _web.View;
    }

    public bool IsPresenting => _session is not null;

    /// <summary>Shows the window once, invisible and off screen, so its page is loaded before the first summon.</summary>
    public void Park()
    {
        Opacity = 0;
        _placed = false;
        // Show before moving: a window whose first position is far off screen comes up 0 x 0 on macOS.
        if (!IsVisible)
            Show();
        Position = ParkedAt;
    }

    /// <summary>Puts the composer in front of the user; completes when it was dismissed (written, hidden, or toggled away).</summary>
    public Task PresentAsync(ComposerViewModel vm, ComposerTexts texts, RecentDrafts recent, TargetWindowInfo? target)
    {
        _target = target;
        _moved = false;
        _userWidth = null;
        _userHeight = null;
        _dismissed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _session = new ComposerWebSession(
            vm, texts, recent, new StorageFileDialogs(StorageProvider, texts), _web,
            SynchronizationContext.Current ?? throw new InvalidOperationException("The composer is shown from the UI thread."),
            TimeProvider.System, _logger, _modelUsage);
        _session.SizeChanged += OnSize;
        _session.WideChanged += ApplyWidth;
        _session.Dragged += OnDrag;
        _session.Resized += OnResize;
        _session.Zoomed += OnZoom;
        vm.Completed += Dismiss;
        vm.CloseRequested += Dismiss;

        // On screen but still transparent: WebKit does not run animation frames for an off-screen page, and the page
        // can only report its size from one.
        ApplyWidth();
        LimitToScreen();
        Place();
        if (_web.IsReady)
            _session.Start();
        return _dismissed.Task;
    }

    /// <summary>The same as the close button: the hotkey pressed again.</summary>
    public void RequestHide() => _session?.Vm.HideCommand.Execute(null);

    public void UpdateModelPlan(ComposerModelPlan plan) => _session?.Vm.UpdateModelPlan(plan);

    private void OnSize(SizeIntent size)
    {
        _contentHeight = size.Height;
        if (_userHeight is null)
            Height = size.Height;
        Place();
        if (_placed)
            return;

        // The first measurement of this summon: the page has drawn the snapshot, so the window can be shown as it is.
        _placed = true;
        Opacity = 1;
        Activate();
        _web.View.Focus();
    }

    private void ApplyWidth()
    {
        Width = _userWidth ?? (_session!.Wide ? WideWidth : NarrowWidth);
        if (_placed)
            Place();
    }

    /// <summary>
    /// An edge or the corner is being dragged. The window keeps its top left (the page measures the pointer from it),
    /// so it is pinned where it is and the size is limited to the room left on screen instead of being pushed back.
    /// </summary>
    private void OnResize(ResizeIntent resize)
    {
        if (_session is null || CurrentScreen() is not { } screen)
            return;

        _moved = true;
        var work = screen.WorkingArea;
        var (width, height) = ComposerPlacement.Resize(
            (Width, Height), resize.Width, resize.Height, _session.Wide, Position.X, Position.Y,
            new PixelBounds(work.X, work.Y, work.Width, work.Height), screen.Scaling);
        if (resize.Width is not null)
        {
            _userWidth = width;
            Width = width;
        }
        if (resize.Height is not null)
        {
            _userHeight = height;
            Height = height;
            _session.SetFixedHeight(true);
        }
    }

    /// <summary>Like zooming a window by its title bar: a large window that shows much text, and back to the size that fits the content.</summary>
    private void OnZoom()
    {
        if (_session is null || CurrentScreen() is not { } screen)
            return;

        if (_userHeight is not null)
        {
            _userWidth = null;
            _userHeight = null;
            _moved = false;
            _session.SetFixedHeight(false);
            ApplyWidth();
            Height = _contentHeight;
            Place();
            return;
        }

        var work = screen.WorkingArea;
        var zoomed = ComposerPlacement.Zoomed(new PixelBounds(work.X, work.Y, work.Width, work.Height), screen.Scaling);
        _moved = true;
        _userWidth = zoomed.Width;
        _userHeight = zoomed.Height;
        Width = zoomed.Width;
        Height = zoomed.Height;
        Position = new PixelPoint(zoomed.X, zoomed.Y);
        _session.SetFixedHeight(true);
    }

    /// <summary>
    /// The header is the window's handle: the system moves the window (the page cannot, and its pointer coordinates
    /// are not reliable across web engines). The drag runs until the button is released; the window is then brought
    /// back on screen, with the page limited to the screen it was dropped on.
    /// </summary>
    private void OnDrag()
    {
        if (_session is null || TryGetPlatformHandle() is not { } handle)
            return;

        _mover.BeginDrag(handle);
        _moved = true;
        LimitToScreen();
        Place();
    }

    private void Dismiss()
    {
        var session = _session;
        if (session is null)
            return;

        _session = null;
        session.Vm.Completed -= Dismiss;
        session.Vm.CloseRequested -= Dismiss;
        session.Dispose();
        Park();
        _web.Load();
        _dismissed!.SetResult();
    }

    private Screen? CurrentScreen()
    {
        if (_moved)
            return Screens.ScreenFromWindow(this) ?? Screens.Primary;

        return _target is null
            ? Screens.Primary
            : Screens.ScreenFromPoint(new PixelPoint(_target.Left + _target.Width / 2, _target.Top + _target.Height / 2)) ?? Screens.Primary;
    }

    /// <summary>
    /// The page never grows beyond the working area of the screen it is on (no dock, menu bar or screen edge hides its
    /// buttons): the page scrolls inside its panel, and the window is capped as well.
    /// </summary>
    private void LimitToScreen()
    {
        if (CurrentScreen() is not { } screen)
            return;

        MaxHeight = screen.WorkingArea.Height / screen.Scaling;
        _session?.LimitPanelHeight(ComposerPlacement.MaxPanelHeight(screen.WorkingArea.Height, screen.Scaling));
    }

    private void Place()
    {
        if (CurrentScreen() is not { } screen)
            return;

        var work = screen.WorkingArea;
        var bounds = new PixelBounds(work.X, work.Y, work.Width, work.Height);
        (int X, int Y) at;
        if (_moved)
        {
            at = ComposerPlacement.Clamp(Position.X, Position.Y, Width, Height, screen.Scaling, bounds);
        }
        else if (_target is null)
        {
            at = ComposerPlacement.Clamp(
                work.X + (int)((work.Width - Width * screen.Scaling) / 2),
                work.Y + (int)((work.Height - Height * screen.Scaling) / 3),
                Width, Height, screen.Scaling, bounds);
        }
        else
        {
            at = ComposerPlacement.For(_target, Width, Height, screen.Scaling, bounds);
        }

        Position = new PixelPoint(at.X, at.Y);
    }
}
