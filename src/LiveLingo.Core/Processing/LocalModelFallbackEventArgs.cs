using LiveLingo.Core.Models;

namespace LiveLingo.Core.Processing;

public sealed class LocalModelFallbackEventArgs : EventArgs
{
    public required ModelDescriptor Primary { get; init; }
    public required ModelDescriptor Fallback { get; init; }
}

/// <summary>Backward-compat alias; prefer <see cref="LocalModelFallbackEventArgs"/>.</summary>
[Obsolete("Use LocalModelFallbackEventArgs")]
public sealed class QwenModelFallbackEventArgs : EventArgs
{
    public required ModelDescriptor Primary { get; init; }
    public required ModelDescriptor Fallback { get; init; }
}
