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
    /// Starts the drag from the mouse button that is held down right now. Returns when the button is released (the
    /// system runs the drag in its own loop).
    /// </summary>
    void BeginDrag(IPlatformHandle window);
}
