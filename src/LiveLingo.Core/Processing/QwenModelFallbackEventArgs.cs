using LiveLingo.Core.Models;

namespace LiveLingo.Core.Processing;

public sealed class QwenModelFallbackEventArgs : EventArgs
{
    public required ModelDescriptor Primary { get; init; }
    public required ModelDescriptor Fallback { get; init; }
}
