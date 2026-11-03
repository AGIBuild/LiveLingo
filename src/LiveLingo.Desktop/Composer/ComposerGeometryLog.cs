using Avalonia;
using Microsoft.Extensions.Logging;

namespace LiveLingo.Desktop.Composer;

/// <summary>Where the composer window is and how big, as the window itself sees it. Numbers only: nothing the user wrote.</summary>
/// <param name="Width">Requested width and height in device-independent pixels.</param>
/// <param name="Client">What the system gave the window; differs from the request when the system constrained it.</param>
/// <param name="X">Screen position in physical pixels.</param>
/// <param name="MaxHeight">The cap set for the screen the window is on, in device-independent pixels.</param>
/// <param name="UserWidth">Set while the user's size (edge drag or zoom) overrides the page's; null while the window follows the page.</param>
/// <param name="ContentHeight">The last height the page asked for, in CSS pixels.</param>
/// <param name="Work">Working area of the screen the window is placed on, in physical pixels.</param>
internal readonly record struct ComposerGeometry(
    double Width, double Height, Size Client, int X, int Y, double MaxHeight, double? UserWidth, double? UserHeight,
    double ContentHeight, bool Moved, bool Wide, double Scaling, PixelRect? Work)
{
    public override string ToString() =>
        $"window={Width:0.#}x{Height:0.#}dip client={Client.Width:0.#}x{Client.Height:0.#} pos=({X},{Y})px " +
        $"max={MaxHeight:0.#} user={Format(UserWidth)}x{Format(UserHeight)} content={ContentHeight:0.#}css " +
        $"moved={Moved} wide={Wide} scale={Scaling:0.##} work={(Work is { } w ? $"({w.X},{w.Y},{w.Width}x{w.Height})px" : "unknown")}";

    private static string Format(double? value) => value is { } v ? v.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) : "-";
}

/// <summary>
/// The one place that writes what the composer window does with its size and position. A report that "the window shows
/// only half" is answered from the log: every line carries the number of the summon and the whole geometry, so one
/// summon can be read from its start (what was asked) to the last size the window took, and the two symptoms that can
/// hide the page — the window smaller than the page, and a window that went off screen — are logged as warnings.
/// </summary>
internal sealed class ComposerGeometryLog(ILogger logger)
{
    private int _summon;
    private long _lastThrottled;
    private readonly Dictionary<string, long> _lastWarned = [];

    public int Summon => _summon;

    public void SummonStarted(string target, ComposerGeometry geometry) => Info("summon", geometry, $"target={target}", newSummon: true);

    public void Info(string what, ComposerGeometry geometry, string detail = "", bool newSummon = false)
    {
        if (newSummon)
            _summon++;
        logger.LogInformation("Composer window summon={Summon} event={Event} {Detail} {Geometry}", _summon, what, detail, geometry);
    }

    /// <summary>Problems repeat while the cause lasts (every size change of a drag): each kind is logged at most once a second.</summary>
    public void Warn(string what, ComposerGeometry geometry, string detail)
    {
        var now = Environment.TickCount64;
        if (_lastWarned.TryGetValue(what, out var last) && now - last < 1000)
            return;

        _lastWarned[what] = now;
        logger.LogWarning("Composer window summon={Summon} PROBLEM={Event} {Detail} {Geometry}", _summon, what, detail, geometry);
    }

    /// <summary>
    /// For events that come in bursts (a drag reports the size on every pointer move): the first of a burst is logged,
    /// the rest only when a second has passed. Whatever happens next logs the full geometry again, so the final state is
    /// never lost.
    /// </summary>
    public void InfoThrottled(string what, ComposerGeometry geometry, string detail = "")
    {
        var now = Environment.TickCount64;
        if (now - _lastThrottled < 1000)
            return;

        _lastThrottled = now;
        Info(what, geometry, detail);
    }

    /// <summary>The page asked for a size; flags a window that cannot show it (capped or constrained by the system).</summary>
    public void Sized(int contentWidth, int contentHeight, ComposerGeometry geometry)
    {
        var detail = $"page={contentWidth}x{contentHeight}css";
        if (geometry.UserHeight is null)
            Info("size", geometry, detail);
        else
            InfoThrottled("size-while-user-sized", geometry, detail);

        if (geometry.UserHeight is null && contentHeight > geometry.MaxHeight + 1)
            Warn("content-taller-than-window-cap", geometry, $"{detail} cap={geometry.MaxHeight:0.#}dip");
    }

    /// <summary>The system sized the window differently from what was asked.</summary>
    public void SystemResized(ComposerGeometry geometry)
    {
        if (Math.Abs(geometry.Client.Height - geometry.Height) > 1 || Math.Abs(geometry.Client.Width - geometry.Width) > 1)
            Warn("system-changed-the-size", geometry, "the window got a different size than it asked for");
        else
            InfoThrottled("system-resized", geometry);
    }

    /// <summary>The window is not entirely inside the working area of its screen (nothing should leave it so).</summary>
    public void CheckOnScreen(ComposerGeometry geometry)
    {
        if (geometry.Work is not { } work)
            return;

        var right = geometry.X + geometry.Width * geometry.Scaling;
        var bottom = geometry.Y + geometry.Height * geometry.Scaling;
        if (geometry.X < work.X - 1 || geometry.Y < work.Y - 1 || right > work.Right + 1 || bottom > work.Bottom + 1)
            Warn("window-outside-the-working-area", geometry, $"right={right:0} bottom={bottom:0}");
    }
}
