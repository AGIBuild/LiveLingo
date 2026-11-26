using Avalonia.Platform;

namespace LiveLingo.Desktop.Platform;

/// <summary>
/// Moves a window with the system's own window drag. The composer's page cannot move its native window, and the
/// pointer's screen coordinates differ between the web engines (WebKit reports them upside down in some embeddings),
/// so the system tracks the pointer itself and the page only says "the user started dragging the header".
/// </summary>
public interface IWindowMover
{
    /// <summary>
    /// Starts the drag from the mouse button that is held down right now and completes once the button is released.
    /// </summary>
    Task DragAsync(IPlatformHandle window, CancellationToken cancellationToken = default);

    /// <summary>
    /// Toggles native click-through on the window so a parked or not-yet-visible transparent window can never swallow
    /// pointer events for apps underneath even if the OS pulls it back onto a display.
    /// </summary>
    void SetMousePassthrough(IPlatformHandle window, bool passthrough);
}
