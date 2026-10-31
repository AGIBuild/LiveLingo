using LiveLingo.Desktop.Platform;

namespace LiveLingo.Desktop.Composer;

public readonly record struct PixelBounds(int X, int Y, int Width, int Height);

/// <summary>Where the composer window goes: centered on the target window, just above its bottom edge, fully on screen.</summary>
public static class ComposerPlacement
{
    /// <summary>Space between the composer and the bottom edge of the target window, in pixels.</summary>
    public const int BottomMargin = 24;

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

        return (
            Math.Clamp(x, screen.X, Math.Max(screen.X, screen.X + screen.Width - width)),
            Math.Clamp(y, screen.Y, Math.Max(screen.Y, screen.Y + screen.Height - height)));
    }
}
