namespace LiveLingo.Core.Models;

/// <summary>
/// Explicit null-object implementation used when a coordinator is genuinely
/// not needed (e.g. unit tests that only exercise pure ViewModel wiring, or
/// legacy constructors that never owned model downloads). Keeps call sites
/// from sprinkling <c>if (coordinator is null)</c> guards.
/// </summary>
public sealed class NullModelDownloadCoordinator : IModelDownloadCoordinator
{
    public static readonly NullModelDownloadCoordinator Instance = new();

    private NullModelDownloadCoordinator() { }

    public event Action<ModelDownloadState>? StateChanged
    {
        add { /* no-op */ }
        remove { /* no-op */ }
    }

    public ModelDownloadState GetState(string modelId)
        => new(modelId, ModelDownloadStatus.Idle, 0, null);

    public Task StartAsync(ModelDescriptor descriptor) => Task.CompletedTask;

    public void Cancel(string modelId) { }

    public void NotifyDeleted(string modelId) { }
}
