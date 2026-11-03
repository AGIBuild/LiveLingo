namespace LiveLingo.Desktop.Services.Update;

public enum UpdatePhase
{
    /// <summary>The user turned updates off (an empty update address).</summary>
    Off,

    /// <summary>This copy was not put in place by the installer, so it cannot update itself.</summary>
    Unsupported,
    Idle,
    Checking,
    Downloading,
    Ready,
    UpToDate,
    Failed
}

/// <summary>What the update check is doing now and what its last result was; the one thing every window shows.</summary>
/// <param name="Version">The version that is being downloaded or is ready to install.</param>
/// <param name="Percent">Download progress, 0-100.</param>
/// <param name="At">When the last check finished (<see cref="UpdatePhase.UpToDate"/> and <see cref="UpdatePhase.Failed"/>).</param>
/// <param name="Error">Why the last check failed.</param>
public sealed record UpdateState(
    UpdatePhase Phase, string? Version = null, int Percent = 0, DateTimeOffset? At = null, string? Error = null)
{
    public static UpdateState Off { get; } = new(UpdatePhase.Off);

    /// <summary>A check or a download is under way; another one cannot be started.</summary>
    public bool Busy => Phase is UpdatePhase.Checking or UpdatePhase.Downloading;
}
