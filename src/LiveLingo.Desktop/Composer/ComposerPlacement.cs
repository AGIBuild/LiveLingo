using LiveLingo.Desktop.Platform;

namespace LiveLingo.Desktop.Composer;

public readonly record struct PixelBounds(int X, int Y, int Width, int Height);

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
    private const int ScreenGap = 8;

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
}
