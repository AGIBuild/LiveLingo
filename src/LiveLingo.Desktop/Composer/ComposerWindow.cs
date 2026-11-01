using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using LiveLingo.Desktop.Platform;
using LiveLingo.Desktop.Web;
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
    private readonly ILogger _logger;
    private ComposerWebSession? _session;
    private TargetWindowInfo? _target;
    private TaskCompletionSource? _dismissed;
    private bool _placed;

    public ComposerWindow(ILogger logger)
    {
        _logger = logger;
        _web = new WebSurface(logger);
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
        Deactivated += async (_, _) =>
        {
            if (_session is { } session)
                await session.Vm.WindowDeactivatedAsync();
        };
    }

    public bool IsPresenting => _session is not null;

    /// <summary>Shows the window once, invisible and off screen, so its page is loaded before the first summon.</summary>
    public void Park()
    {
        Opacity = 0;
        Position = ParkedAt;
        _placed = false;
        if (!IsVisible)
            Show();
    }

    /// <summary>Puts the composer in front of the user; completes when it was dismissed (written, hidden, or toggled away).</summary>
    public Task PresentAsync(ComposerViewModel vm, ComposerTexts texts, RecentDrafts recent, TargetWindowInfo? target)
    {
        _target = target;
        _dismissed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _session = new ComposerWebSession(
            vm, texts, recent, new StorageFileDialogs(StorageProvider, texts), _web,
            SynchronizationContext.Current ?? throw new InvalidOperationException("The composer is shown from the UI thread."),
            TimeProvider.System, _logger);
        _session.SizeChanged += OnSize;
        _session.WideChanged += ApplyWidth;
        vm.Completed += Dismiss;
        vm.CloseRequested += Dismiss;

        ApplyWidth();
        if (_web.IsReady)
            _session.Start();
        return _dismissed.Task;
    }

    /// <summary>The same as the close button: the hotkey pressed again.</summary>
    public void RequestHide() => _session?.Vm.HideCommand.Execute(null);

    private void OnSize(SizeIntent size)
    {
        Height = size.Height;
        if (_placed)
            return;

        // The first measurement of this summon: the page has drawn the snapshot, so the window can be shown as it is.
        _placed = true;
        Place();
        Opacity = 1;
        Activate();
        _web.View.Focus();
    }

    private void ApplyWidth()
    {
        Width = _session!.Wide ? WideWidth : NarrowWidth;
        if (_placed)
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

    private void Place()
    {
        if (_target is null)
        {
            var primary = Screens.Primary;
            if (primary is not null)
                Position = new PixelPoint(
                    primary.WorkingArea.X + (int)((primary.WorkingArea.Width - Width * primary.Scaling) / 2),
                    primary.WorkingArea.Y + (int)((primary.WorkingArea.Height - Height * primary.Scaling) / 3));
            return;
        }

        var screen = Screens.ScreenFromPoint(new PixelPoint(_target.Left + _target.Width / 2, _target.Top + _target.Height / 2))
            ?? Screens.Primary;
        if (screen is null)
            return;

        var work = screen.WorkingArea;
        var (x, y) = ComposerPlacement.For(
            _target, Width, Height, screen.Scaling,
            new PixelBounds(work.X, work.Y, work.Width, work.Height));
        Position = new PixelPoint(x, y);
    }
}
