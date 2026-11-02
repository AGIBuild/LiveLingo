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

    /// <summary>The tallest the panel gets on a large screen (CSS pixels); the page scrolls inside it.</summary>
    public const int MaxPanelHeightLimit = 470;

    /// <summary>Below this the footer and one line of text no longer fit.</summary>
    public const int MinPanelHeight = 160;

    /// <summary>Space kept between the window and the screen edges, in DIPs.</summary>
    private const int ScreenGap = 8;

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
    /// The tallest the panel may be on this screen, in CSS pixels: the working area less the shadow margin and a gap at
    /// both edges, so its buttons never end up behind the dock, the menu bar or the screen edge.
    /// </summary>
    public static int MaxPanelHeight(int workHeightPx, double scaling)
    {
        var available = (int)(workHeightPx / scaling) - ShadowMargin - 2 * ScreenGap;
        return Math.Clamp(available, MinPanelHeight, MaxPanelHeightLimit);
    }
}
