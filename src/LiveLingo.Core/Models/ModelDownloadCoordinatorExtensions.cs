namespace LiveLingo.Core.Models;

public static class ModelDownloadCoordinatorExtensions
{
    /// <summary>
    /// Downloads the models one after the other and stops at the first that failed or was cancelled. Nothing in it
    /// belongs to a window: whoever asks (the composer of this summon or of a later one) gets the same running
    /// downloads, and closing the composer neither stops nor restarts them.
    /// </summary>
    public static async Task StartAllAsync(this IModelDownloadCoordinator coordinator, IEnumerable<ModelDescriptor> models)
    {
        foreach (var model in models)
        {
            await coordinator.StartAsync(model).ConfigureAwait(false);
            if (coordinator.GetState(model.Id).Status is ModelDownloadStatus.Failed or ModelDownloadStatus.Cancelled)
                return;
        }
    }
}
