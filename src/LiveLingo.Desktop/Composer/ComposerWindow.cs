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
    private static readonly PixelPoint ParkedAt = new(-20000, -20000);

    private readonly WebSurface _web;
    private readonly IWindowMover _mover;
    private readonly ILogger _logger;
    private readonly ComposerGeometryLog _geometryLog;
    private readonly ModelUsage _modelUsage;
    private ComposerWebSession? _session;
    private ComposerFrame? _frame;
    private TargetWindowInfo? _target;
    private TaskCompletionSource? _dismissed;
    private CancellationTokenSource? _dragCts;
    private bool _placed;
    private bool _dragging;
    private readonly Queue<(int X, int Y)> _issuedPositions = new(8);
    private readonly ParkGuard _parkGuard = new(ParkedAt);
    private int? _appliedPanelLimit;
    private bool _appliedFixedHeight;

    public ComposerWindow(ILogger logger, IWindowMover mover, ModelUsage modelUsage)
    {
        _modelUsage = modelUsage;
        _logger = logger;
        _geometryLog = new ComposerGeometryLog(logger);
        _mover = mover;
        _web = new WebSurface(WebPage.ComposerEntry, logger);
        Width = ComposerFrame.WideWidth;
        Height = ComposerFrame.InitialHeight;
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
        Opened += (_, _) => Screens.Changed += (_, _) => OnDisplayChanged();
        Resized += (_, _) =>
        {
            if (_session is not null)
                _geometryLog.SystemResized(Geometry());
        };
        PositionChanged += (_, _) => OnPositionChanged();
        ScalingChanged += (_, _) => OnDisplayChanged();
    }

    /// <summary>The window's geometry as it is now, for the log.</summary>
    private ComposerGeometry Geometry()
    {
        var screen = CurrentScreen();
        return new ComposerGeometry(
            Width, Height, ClientSize, Position.X, Position.Y, MaxHeight,
            _frame?.UserWidth, _frame?.UserHeight, _frame?.ContentHeight ?? 0,
            _frame?.IsFreePlaced ?? false, _session?.Wide ?? false,
            screen?.Scaling ?? RenderScaling, screen?.WorkingArea);
    }

    public bool IsPresenting => _session is not null;

    /// <summary>Shows the window once, invisible and off screen, so its page is loaded before the first summon.</summary>
    public void Park()
    {
        _parkGuard.Reset();
        _placed = false;
        _issuedPositions.Clear();
        SetInteractive(false);
        // Show before moving: a window whose first position is far off screen comes up 0 x 0 on macOS.
        // Showing raises Screens.Changed before the window is parked; that is not drift.
        if (!IsVisible)
        {
            using (_parkGuard.BeginMove())
                Show();
        }
        // Re-apply after Show() because the native NSWindow/HWND only exists once the window has been shown.
        SetMousePassthrough(true);
        MoveToParked();
    }

    /// <summary>Puts the composer in front of the user; completes when it was dismissed (written, hidden, or toggled away).</summary>
    public Task PresentAsync(ComposerViewModel vm, ComposerTexts texts, RecentDrafts recent, TargetWindowInfo? target)
    {
        _dragCts?.Cancel();
        _dragCts?.Dispose();
        _dragCts = new CancellationTokenSource();
        _target = target;
        _dragging = false;
        _parkGuard.Reset();
        _issuedPositions.Clear();
        _appliedPanelLimit = null;
        _appliedFixedHeight = false;
        SetInteractive(false);
        _dismissed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _session = new ComposerWebSession(
            vm, texts, recent, new StorageFileDialogs(StorageProvider, texts), _web,
            SynchronizationContext.Current ?? throw new InvalidOperationException("The composer is shown from the UI thread."),
            TimeProvider.System, _logger, _modelUsage);
        _frame = new ComposerFrame(target, _session.Wide);
        _session.SizeChanged += OnSize;
        _session.WideChanged += OnWideChanged;
        _session.Dragged += OnDrag;
        _session.Resized += OnResize;
        _session.Zoomed += OnZoom;
        _session.Clipped += OnClipped;
        vm.Completed += Dismiss;
        vm.CloseRequested += Dismiss;

        // On screen but still transparent: WebKit does not run animation frames for an off-screen page, and the page
        // can only report its size from one.
        Apply();
        _geometryLog.SummonStarted(
            target is null ? "none" : $"{target.ProcessName} rect=({target.Left},{target.Top},{target.Width}x{target.Height})",
            Geometry());
        if (_web.IsReady)
            _session.Start();
        return _dismissed.Task;
    }

    /// <summary>The page is cut off by the window: the one symptom of "only half of the composer is visible", logged with everything the window knows.</summary>
    private void OnClipped(ClippedIntent clipped) => _geometryLog.Warn(
        "page-cut-off-by-the-window", Geometry(),
        $"viewport={clipped.ViewportWidth}x{clipped.ViewportHeight}css page={clipped.ContentWidth}x{clipped.ContentHeight}css");

    /// <summary>The same as the close button: the hotkey pressed again.</summary>
    public void RequestHide() => _session?.Vm.HideCommand.Execute(null);

    public void UpdateModelPlan(ComposerModelPlan plan) => _session?.Vm.UpdateModelPlan(plan);

    public void UpdatePresets(IReadOnlyList<LiveLingo.Core.Cleanup.PolishPreset> presets) => _session?.Vm.UpdatePresets(presets);

    private void OnSize(SizeIntent size)
    {
        if (_session is null || _frame is null)
            return;

        _frame.MeasureContent(size.Height);
        if (_dragging)
            return;

        Apply();
        var geometry = Geometry();
        _geometryLog.Sized(size.Width, size.Height, geometry);
        _geometryLog.CheckOnScreen(geometry);
        if (_placed)
            return;

        // The first measurement of this summon: the page has drawn the snapshot, so the window can be shown as it is.
        _placed = true;
        SetInteractive(true);
        Activate();
        _web.View.Focus();
    }

    private void OnWideChanged()
    {
        if (_session is null || _frame is null)
            return;

        _frame.SetWide(_session.Wide);
        Apply();
    }

    /// <summary>
    /// An edge or the corner is being dragged. The window keeps its top left (the page measures the pointer from it),
    /// so it is pinned where it is and the size is limited to the room left on screen instead of being pushed back.
    /// </summary>
    private void OnResize(ResizeIntent resize)
    {
        if (_session is null || _frame is null || _dragging || CurrentScreen() is not { } screen)
            return;

        _frame.ResizeEdge(
            resize.Width, resize.Height, (Width, Height), (Position.X, Position.Y),
            BoundsOf(screen.WorkingArea), screen.Scaling);
        Apply();
        _geometryLog.InfoThrottled("user-resize", Geometry(), $"asked={resize.Width?.ToString() ?? "-"}x{resize.Height?.ToString() ?? "-"}css");
    }

    /// <summary>Like zooming a window by its title bar: a large window that shows much text, and back to the size that fits the content.</summary>
    private void OnZoom()
    {
        if (_session is null || _frame is null || _dragging || CurrentScreen() is not { } screen)
            return;

        var unzooming = _frame.FixedHeight;
        _frame.ToggleZoom(BoundsOf(screen.WorkingArea), screen.Scaling);
        Apply();
        _geometryLog.Info(unzooming ? "unzoom" : "zoom", Geometry());
    }

    /// <summary>
    /// The header is the window's handle: the system moves the window (the page cannot, and its pointer coordinates
    /// are not reliable across web engines). The drag runs until the button is released; the window is then reconciled
    /// to the working area of the screen it was dropped on.
    /// </summary>
    private async void OnDrag()
    {
        if (_session is null || _frame is null || _dragging || _dragCts is null || TryGetPlatformHandle() is not { } handle)
            return;

        var session = _session;
        var token = _dragCts.Token;
        _dragging = true;
        try
        {
            await _mover.DragAsync(handle, token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            _logger.LogError("Window drag failed: {Exception}", ex.GetType().Name);
            return;
        }
        finally
        {
            if (_session == session)
                _dragging = false;
        }

        if (_session != session || _frame is null || token.IsCancellationRequested)
            return;

        _frame.MoveTo(Position.X, Position.Y);
        Apply();
        var geometry = Geometry();
        _geometryLog.Info("dragged", geometry);
        _geometryLog.CheckOnScreen(geometry);
    }

    private void OnPositionChanged()
    {
        if (_session is null)
        {
            EnforceParked("position-changed");
            return;
        }

        if (_frame is null || !_placed || _dragging)
            return;

        // Native frame changes arrive asynchronously on macOS; ignore echoes of positions Apply() itself issued, and
        // treat any other move (e.g. a window-manager shortcut on the same or another screen) as an external move.
        if (!ComposerFrame.IsExternalMove((Position.X, Position.Y), [.. _issuedPositions]))
            return;

        _frame.MoveTo(Position.X, Position.Y);
        Apply();
        var geometry = Geometry();
        _geometryLog.InfoThrottled("screen-reconciled", geometry);
        _geometryLog.CheckOnScreen(geometry);
    }

    private void OnDisplayChanged()
    {
        if (_session is null)
        {
            _parkGuard.ResetDriftRetries();
            EnforceParked("display-changed");
            return;
        }

        if (_frame is null || _dragging)
            return;

        Apply();
        if (_placed)
        {
            var geometry = Geometry();
            _geometryLog.InfoThrottled("display-changed", geometry);
            _geometryLog.CheckOnScreen(geometry);
        }
    }

    private void Dismiss()
    {
        var session = _session;
        if (session is null)
            return;

        _geometryLog.Info("dismiss", Geometry());
        _dragCts?.Cancel();
        _dragCts?.Dispose();
        _dragCts = null;
        _dragging = false;
        _session = null;
        _frame = null;
        session.Vm.Completed -= Dismiss;
        session.Vm.CloseRequested -= Dismiss;
        session.Dispose();
        Park();
        _web.Load();
        _dismissed!.SetResult();
    }

    private Screen? CurrentScreen()
    {
        if (_frame is { IsFreePlaced: true })
        {
            var (ax, ay) = ComposerPlacement.HeaderAnchor(Position.X, Position.Y, Width, DesktopScaling);
            return Screens.ScreenFromPoint(new PixelPoint(ax, ay))
                ?? Screens.ScreenFromWindow(this)
                ?? Screens.Primary;
        }

        return _target is null
            ? Screens.Primary
            : Screens.ScreenFromPoint(new PixelPoint(_target.Left + _target.Width / 2, _target.Top + _target.Height / 2)) ?? Screens.Primary;
    }

    private void Apply()
    {
        if (_session is null || _frame is null || _dragging || CurrentScreen() is not { } screen)
            return;

        var work = screen.WorkingArea;
        var plan = _frame.Plan(BoundsOf(work), screen.Scaling);
        MaxWidth = plan.MaxWidth;
        MaxHeight = plan.MaxHeight;
        Width = plan.Width;
        Height = plan.Height;
        var target = new PixelPoint(plan.X, plan.Y);
        RecordIssuedPosition(target);
        Position = target;

        if (_appliedPanelLimit != plan.MaxPanelHeight)
        {
            _appliedPanelLimit = plan.MaxPanelHeight;
            _session.LimitPanelHeight(plan.MaxPanelHeight);
        }
        if (_appliedFixedHeight != plan.FixedHeight)
        {
            _appliedFixedHeight = plan.FixedHeight;
            _session.SetFixedHeight(plan.FixedHeight);
        }
    }

    private void SetInteractive(bool interactive)
    {
        // Topmost stays on: a window the target app covers is occluded and WebKit stops running animation frames,
        // so the page would never report its size and the composer would never appear. Click-through is what
        // keeps an invisible window from swallowing clicks.
        SetMousePassthrough(!interactive);
        Opacity = interactive ? 1 : 0;
    }

    private void SetMousePassthrough(bool passthrough)
    {
        if (TryGetPlatformHandle() is { } handle)
            _mover.SetMousePassthrough(handle, passthrough);
    }

    private void MoveToParked()
    {
        using (_parkGuard.BeginMove())
        {
            RecordIssuedPosition(ParkedAt);
            Position = ParkedAt;
        }
    }

    private void EnforceParked(string reason)
    {
        if (!IsVisible)
            return;

        SetInteractive(false);
        if (!_parkGuard.ShouldRePark(Position))
            return;

        _geometryLog.Warn("parked-window-drifted", Geometry(), $"reason={reason} expected=({ParkedAt.X},{ParkedAt.Y})px");
        MoveToParked();
    }

    private void RecordIssuedPosition(PixelPoint point)
    {
        if (_issuedPositions.Count >= 8)
            _issuedPositions.Dequeue();
        _issuedPositions.Enqueue((point.X, point.Y));
    }

    private static PixelBounds BoundsOf(PixelRect rect) => new(rect.X, rect.Y, rect.Width, rect.Height);
}

/// <summary>
/// Prevents a parked off-screen window from drifting back onto a display (e.g. after macOS wake or display
/// reconfiguration) while bounding re-park attempts so OS-enforced frame constraints cannot trigger a move loop.
/// </summary>
internal sealed class ParkGuard(PixelPoint parkedAt, int maxRetries = 2)
{
    private bool _moving;
    private int _retries;

    public IDisposable BeginMove()
    {
        _moving = true;
        return new Scope(this);
    }

    public bool ShouldRePark(PixelPoint current)
    {
        if (_moving)
            return false;

        if (Math.Abs(current.X - parkedAt.X) <= 1 && Math.Abs(current.Y - parkedAt.Y) <= 1)
        {
            _retries = 0;
            return false;
        }

        if (_retries >= maxRetries)
            return false;

        _retries++;
        return true;
    }

    public void ResetDriftRetries() => _retries = 0;

    public void Reset()
    {
        _moving = false;
        _retries = 0;
    }

    private sealed class Scope(ParkGuard guard) : IDisposable
    {
        public void Dispose() => guard._moving = false;
    }
}
