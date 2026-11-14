using LiveLingo.Desktop.Platform;

namespace LiveLingo.Desktop.Composer;

public readonly record struct PixelBounds(int X, int Y, int Width, int Height);

/// <summary>The complete geometry of the composer window and its web panel on the active screen.</summary>
public readonly record struct WindowPlan(
    double Width,
    double Height,
    double MaxWidth,
    double MaxHeight,
    int X,
    int Y,
    int MaxPanelHeight,
    bool FixedHeight);

/// <summary>Where the composer window goes: centered on the target window, just above its bottom edge, fully on screen.</summary>
public static class ComposerPlacement
{
    /// <summary>Space between the composer and the bottom edge of the target window, in pixels.</summary>
    public const int BottomMargin = 24;

    /// <summary>The page keeps an 18 px margin on every side of the panel for its shadow.</summary>
    public const int ShadowMargin = 36;

    /// <summary>How much of the screen's working height the panel may take before it scrolls.</summary>
    public const double PanelShare = 0.7;

    /// <summary>Below this the footer and one line of text no longer fit.</summary>
    public const int MinPanelHeight = 160;

    /// <summary>The narrowest the panel may be dragged to (CSS pixels); with the side-by-side layout, <see cref="MinWidePanelWidth"/>.</summary>
    public const int MinPanelWidth = 520;

    public const int MinWidePanelWidth = 760;

    /// <summary>The panel width of the large window; on a smaller screen the working area limits it.</summary>
    public const int ZoomedPanelWidth = 1100;

    /// <summary>Space kept between the window and the screen edges, in DIPs.</summary>
    public const int ScreenGap = 8;

    /// <summary>The smallest window (shadow margin included) the user may drag to, in DIPs.</summary>
    public static (double Width, double Height) MinWindow(bool wide) =>
        ((wide ? MinWidePanelWidth : MinPanelWidth) + ShadowMargin, MinPanelHeight + ShadowMargin);

    /// <summary>
    /// The largest window whose top left stays at (<paramref name="x"/>, <paramref name="y"/>): it reaches the right
    /// and bottom edge of the working area, less a gap. Dragging an edge must never move the window, so it is limited
    /// to this instead of being pushed back on screen.
    /// </summary>
    public static (double Width, double Height) MaxWindowFrom(int x, int y, PixelBounds work, double scaling) =>
        ((work.X + work.Width - x) / scaling - ScreenGap, (work.Y + work.Height - y) / scaling - ScreenGap);

    /// <summary>The window size a drag asks for, kept between the smallest usable size and the room left on screen.</summary>
    public static (double Width, double Height) Resize(
        (double Width, double Height) current, int? width, int? height, bool wide, int x, int y, PixelBounds work, double scaling)
    {
        var min = MinWindow(wide);
        var max = MaxWindowFrom(x, y, work, scaling);
        return (
            width is { } w ? Math.Clamp(w, min.Width, Math.Max(min.Width, max.Width)) : current.Width,
            height is { } h ? Math.Clamp(h, min.Height, Math.Max(min.Height, max.Height)) : current.Height);
    }

    /// <summary>The large window: wide enough for two columns of text and as tall as the working area allows, centered.</summary>
    public static (double Width, double Height, int X, int Y) Zoomed(PixelBounds work, double scaling)
    {
        var width = Math.Min(ZoomedPanelWidth + ShadowMargin, work.Width / scaling - 2 * ScreenGap);
        var height = work.Height / scaling - 2 * ScreenGap;
        return (
            width,
            height,
            work.X + (int)Math.Round((work.Width - width * scaling) / 2),
            work.Y + (int)Math.Round((work.Height - height * scaling) / 2));
    }

    public static (int X, int Y) For(
        TargetWindowInfo target,
        double widthDip,
        double heightDip,
        double scaling,
        PixelBounds screen)
    {
        var width = (int)Math.Round(widthDip * scaling);
        var height = (int)Math.Round(heightDip * scaling);

        var x = target.Left + (target.Width - width) / 2;
        var y = target.Top + target.Height - height - BottomMargin;

        return Clamp(x, y, widthDip, heightDip, scaling, screen);
    }

    /// <summary>Moves a window so that all of it is inside the screen; a window larger than the screen keeps its top left visible.</summary>
    public static (int X, int Y) Clamp(int x, int y, double widthDip, double heightDip, double scaling, PixelBounds screen)
    {
        var width = (int)Math.Round(widthDip * scaling);
        var height = (int)Math.Round(heightDip * scaling);

        return (
            Math.Clamp(x, screen.X, Math.Max(screen.X, screen.X + screen.Width - width)),
            Math.Clamp(y, screen.Y, Math.Max(screen.Y, screen.Y + screen.Height - height)));
    }

    /// <summary>
    /// The tallest the panel may be on this screen, in CSS pixels: <see cref="PanelShare"/> of the working area, and never
    /// more than the working area less the shadow margin and a gap at both edges, so its buttons never end up behind the
    /// dock, the menu bar or the screen edge. The page scrolls inside it; a taller window is the user's to ask for.
    /// </summary>
    public static int MaxPanelHeight(int workHeightPx, double scaling)
    {
        var work = (int)(workHeightPx / scaling);
        var available = work - ShadowMargin - 2 * ScreenGap;
        return Math.Clamp(Math.Min(available, (int)Math.Round(work * PanelShare)), MinPanelHeight, Math.Max(MinPanelHeight, available));
    }

    /// <summary>
    /// Where the middle of the draggable header sits in screen pixels, used to pick the screen the user dropped the
    /// window onto even when the lower half of a tall window still hangs into the previous screen.
    /// </summary>
    public static (int X, int Y) HeaderAnchor(int x, int y, double widthDip, double desktopScaling) =>
        (x + (int)Math.Round(widthDip * desktopScaling / 2), y + (int)Math.Round(ShadowMargin * desktopScaling));
}

/// <summary>
/// Pure geometry state for one summon of the composer. Distinguishes where the user dragged the window
/// (<c>_userPosition</c>, null while anchored to the target window) from a temporary size/pin override caused by
/// edge-resizing or zooming (<c>_pinnedPosition</c>, <c>_userWidth</c>, <c>_userHeight</c>), and resolves every event
/// through a single <see cref="Plan"/> calculation.
/// </summary>
public sealed class ComposerFrame(TargetWindowInfo? target, bool wide)
{
    public const double InitialHeight = 260;
    public const double NarrowWidth = 620 + ComposerPlacement.ShadowMargin;
    public const double WideWidth = 940 + ComposerPlacement.ShadowMargin;

    private bool _wide = wide;
    private double _contentHeight = InitialHeight;
    private (int X, int Y)? _userPosition;
    private (int X, int Y)? _pinnedPosition;
    private double? _userWidth;
    private double? _userHeight;

    public bool Wide => _wide;
    public double? UserWidth => _userWidth;
    public double? UserHeight => _userHeight;
    public double ContentHeight => _contentHeight;
    public bool IsFreePlaced => (_pinnedPosition ?? _userPosition) is not null;
    public bool FixedHeight => _userHeight is not null;

    /// <summary>
    /// Returns true when <paramref name="position"/> is an external move (for example, a window-manager shortcut on the
    /// same or another screen) rather than a native echo of positions this frame itself issued.
    /// </summary>
    public static bool IsExternalMove((int X, int Y) position, ReadOnlySpan<(int X, int Y)> issuedPositions)
    {
        foreach (var issued in issuedPositions)
        {
            if (Math.Abs(position.X - issued.X) <= 1 && Math.Abs(position.Y - issued.Y) <= 1)
                return false;
        }
        return true;
    }

    public void MeasureContent(int contentHeightCss)
    {
        if (_userHeight is null)
            _contentHeight = contentHeightCss;
    }

    public void SetWide(bool wide) => _wide = wide;

    public void MoveTo(int x, int y)
    {
        _userPosition = (x, y);
        if (_pinnedPosition is not null)
            _pinnedPosition = (x, y);
    }

    public void ResizeEdge(
        int? widthCss,
        int? heightCss,
        (double Width, double Height) currentSize,
        (int X, int Y) currentPosition,
        PixelBounds work,
        double scaling)
    {
        _pinnedPosition ??= currentPosition;
        var (width, height) = ComposerPlacement.Resize(
            currentSize, widthCss, heightCss, _wide, currentPosition.X, currentPosition.Y, work, scaling);
        if (widthCss is not null)
            _userWidth = width;
        if (heightCss is not null)
            _userHeight = height;
    }

    public void ToggleZoom(PixelBounds work, double scaling)
    {
        if (_userHeight is not null)
        {
            _userWidth = null;
            _userHeight = null;
            _pinnedPosition = null;
            return;
        }

        var zoomed = ComposerPlacement.Zoomed(work, scaling);
        _userWidth = zoomed.Width;
        _userHeight = zoomed.Height;
        _pinnedPosition = (zoomed.X, zoomed.Y);
    }

    public WindowPlan Plan(PixelBounds work, double scaling)
    {
        var maxWidth = work.Width / scaling;
        var maxHeight = work.Height / scaling;
        var maxPanelHeight = ComposerPlacement.MaxPanelHeight(work.Height, scaling);
        var min = ComposerPlacement.MinWindow(_wide);

        var userCapWidth = Math.Min(maxWidth, Math.Max(min.Width, maxWidth - 2 * ComposerPlacement.ScreenGap));
        var userCapHeight = Math.Min(maxHeight, Math.Max(min.Height, maxHeight - 2 * ComposerPlacement.ScreenGap));

        if (_userWidth is { } uw)
            _userWidth = Math.Clamp(uw, Math.Min(min.Width, userCapWidth), userCapWidth);
        if (_userHeight is { } uh)
            _userHeight = Math.Clamp(uh, Math.Min(min.Height, userCapHeight), userCapHeight);

        var defaultWidth = _wide ? WideWidth : NarrowWidth;
        var width = Math.Min(_userWidth ?? defaultWidth, maxWidth);
        var autoHeightCap = Math.Min(maxHeight, maxPanelHeight + ComposerPlacement.ShadowMargin);
        var height = _userHeight ?? Math.Min(_contentHeight, autoHeightCap);

        (int X, int Y) pos;
        if ((_pinnedPosition ?? _userPosition) is { } anchor)
        {
            pos = ComposerPlacement.Clamp(anchor.X, anchor.Y, width, height, scaling, work);
            if (_pinnedPosition is not null)
                _pinnedPosition = pos;
            else
                _userPosition = pos;
        }
        else if (target is null)
        {
            pos = ComposerPlacement.Clamp(
                work.X + (int)((work.Width - width * scaling) / 2),
                work.Y + (int)((work.Height - height * scaling) / 3),
                width, height, scaling, work);
        }
        else
        {
            pos = ComposerPlacement.For(target, width, height, scaling, work);
        }

        return new WindowPlan(width, height, maxWidth, maxHeight, pos.X, pos.Y, maxPanelHeight, FixedHeight);
    }
}
