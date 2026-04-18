namespace LiveLingo.Core.Models;

/// <summary>
/// Singleton orchestrator for model downloads. Keeps the download lifecycle
/// decoupled from UI windows so a Settings window being closed and reopened
/// never cancels or double-starts an in-flight download.
///
/// Listeners subscribe to <see cref="StateChanged"/> and marshal updates onto
/// their own UI scheduler; coordinator raises events on whichever thread the
/// underlying download pipeline publishes on.
/// </summary>
public interface IModelDownloadCoordinator
{
    /// <summary>
    /// Fired every time a tracked model transitions status or its progress advances.
    /// The payload is a fresh <see cref="ModelDownloadState"/> snapshot.
    /// </summary>
    event Action<ModelDownloadState>? StateChanged;

    /// <summary>
    /// Returns the most recent known state for <paramref name="modelId"/>. When no
    /// session exists, the status is derived from <see cref="IModelManager.ListInstalled"/>
    /// so a fresh UI opens with an accurate initial picture.
    /// </summary>
    ModelDownloadState GetState(string modelId);

    /// <summary>
    /// Starts or attaches to an in-flight download for <paramref name="descriptor"/>.
    /// Calling it for an already-downloading model is a no-op that returns the
    /// existing task. The returned task never throws on cancellation or download
    /// failure; terminal outcomes are delivered via <see cref="StateChanged"/>.
    /// </summary>
    Task StartAsync(ModelDescriptor descriptor);

    /// <summary>
    /// Requests cancellation of a running download. Safe to call for unknown
    /// models; the coordinator simply ignores it.
    /// </summary>
    void Cancel(string modelId);

    /// <summary>
    /// Notifies the coordinator that a model was removed from disk (e.g. via the
    /// Models tab Delete action). Resets any cached state so subsequent UI opens
    /// reflect the uninstalled state without stale "Installed" flags.
    /// </summary>
    void NotifyDeleted(string modelId);
}
