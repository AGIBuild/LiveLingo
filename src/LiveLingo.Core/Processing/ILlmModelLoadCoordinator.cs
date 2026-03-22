namespace LiveLingo.Core.Processing;

/// <summary>
/// Allows the shell to reset the in-process LLM so the next load prefers the primary translation model again.
/// </summary>
public interface ILlmModelLoadCoordinator
{
    /// <summary>
    /// Unloads any loaded weights and sets the active descriptor back to the primary translation model.
    /// </summary>
    Task RequestRetryPrimaryTranslationModelAsync(CancellationToken cancellationToken = default);
}
