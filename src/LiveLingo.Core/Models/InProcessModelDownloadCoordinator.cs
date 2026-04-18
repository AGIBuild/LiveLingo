using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace LiveLingo.Core.Models;

/// <summary>
/// Default <see cref="IModelDownloadCoordinator"/> backed by the shared
/// <see cref="IModelManager"/>. Holds one <see cref="Session"/> per model so that
/// concurrent <see cref="StartAsync"/> calls collapse to the same running download
/// and <see cref="Cancel"/> targets a stable <see cref="CancellationTokenSource"/>
/// whose lifetime is independent of any UI window.
/// </summary>
public sealed class InProcessModelDownloadCoordinator : IModelDownloadCoordinator
{
    private readonly IModelManager _modelManager;
    private readonly ILogger<InProcessModelDownloadCoordinator>? _logger;
    private readonly ConcurrentDictionary<string, Session> _sessions = new(StringComparer.Ordinal);
    private readonly object _startLock = new();
    private readonly object _publishLock = new();

    public event Action<ModelDownloadState>? StateChanged;

    public InProcessModelDownloadCoordinator(
        IModelManager modelManager,
        ILogger<InProcessModelDownloadCoordinator>? logger = null)
    {
        _modelManager = modelManager ?? throw new ArgumentNullException(nameof(modelManager));
        _logger = logger;
    }

    public ModelDownloadState GetState(string modelId)
    {
        if (string.IsNullOrEmpty(modelId)) throw new ArgumentException("Model id required.", nameof(modelId));

        if (_sessions.TryGetValue(modelId, out var session))
            return session.State;

        var installed = _modelManager.ListInstalled().Any(m => string.Equals(m.Id, modelId, StringComparison.Ordinal));
        return installed
            ? new ModelDownloadState(modelId, ModelDownloadStatus.Installed, 100, null)
            : new ModelDownloadState(modelId, ModelDownloadStatus.Idle, 0, null);
    }

    public Task StartAsync(ModelDescriptor descriptor)
    {
        if (descriptor is null) throw new ArgumentNullException(nameof(descriptor));

        lock (_startLock)
        {
            if (_sessions.TryGetValue(descriptor.Id, out var existing) && existing.State.IsDownloading)
                return existing.Task;

            var cts = new CancellationTokenSource();
            var session = new Session(descriptor.Id, cts);
            _sessions[descriptor.Id] = session;

            Publish(session, new ModelDownloadState(descriptor.Id, ModelDownloadStatus.Downloading, 0, null));

            // Progress<T> captures the caller's SynchronizationContext so incremental
            // progress lands on the same scheduler that invoked StartAsync. Terminal
            // transitions published from RunAsync run on whichever thread the download
            // pipeline completes on; Publish serializes both paths and enforces that
            // terminal states (Installed/Failed/Cancelled) cannot be downgraded by a
            // late progress callback racing a completion.
            var progress = new Progress<ModelDownloadProgress>(p =>
                Publish(session, new ModelDownloadState(
                    descriptor.Id,
                    ModelDownloadStatus.Downloading,
                    p.Percentage,
                    null)));

            session.Task = RunAsync(descriptor, session, progress);
            return session.Task;
        }
    }

    public void Cancel(string modelId)
    {
        if (_sessions.TryGetValue(modelId, out var session) && session.State.IsDownloading)
        {
            try { session.Cts.Cancel(); }
            catch (ObjectDisposedException) { }
        }
    }

    public void NotifyDeleted(string modelId)
    {
        if (_sessions.TryRemove(modelId, out var session))
        {
            try { session.Cts.Cancel(); } catch (ObjectDisposedException) { }
        }
        StateChanged?.Invoke(new ModelDownloadState(modelId, ModelDownloadStatus.Idle, 0, null));
    }

    private async Task RunAsync(ModelDescriptor descriptor, Session session, IProgress<ModelDownloadProgress> progress)
    {
        try
        {
            await _modelManager.EnsureModelAsync(descriptor, progress, session.Cts.Token).ConfigureAwait(false);
            Publish(session, new ModelDownloadState(descriptor.Id, ModelDownloadStatus.Installed, 100, null));
        }
        catch (OperationCanceledException)
        {
            _logger?.LogInformation("Model download cancelled: {ModelId}", descriptor.Id);
            Publish(session, new ModelDownloadState(descriptor.Id, ModelDownloadStatus.Cancelled, session.State.Percentage, null));
        }
        catch (ModelDownloadAuthorizationException ex)
        {
            _logger?.LogWarning(ex, "Model download unauthorized: {ModelId}", descriptor.Id);
            Publish(session, new ModelDownloadState(
                descriptor.Id,
                ModelDownloadStatus.Failed,
                session.State.Percentage,
                ModelDownloadErrorCodes.HuggingFaceAuthorization));
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Model download failed: {ModelId}", descriptor.Id);
            Publish(session, new ModelDownloadState(
                descriptor.Id,
                ModelDownloadStatus.Failed,
                session.State.Percentage,
                ex.Message));
        }
        finally
        {
            try { session.Cts.Dispose(); } catch { /* swallow dispose races */ }
        }
    }

    private void Publish(Session session, ModelDownloadState state)
    {
        ModelDownloadState effective;
        lock (_publishLock)
        {
            // Terminal states (Installed/Failed/Cancelled) are sticky: a late
            // progress callback must not resurrect the Downloading status after
            // we've already reported a completion or failure.
            if (IsTerminal(session.State.Status) && !IsTerminal(state.Status))
                return;

            session.State = state;
            effective = state;
        }

        // Invoke outside the lock so subscribers that call back into the
        // coordinator (e.g. GetState) cannot deadlock with the publisher.
        StateChanged?.Invoke(effective);
    }

    private static bool IsTerminal(ModelDownloadStatus status) =>
        status is ModelDownloadStatus.Installed
               or ModelDownloadStatus.Failed
               or ModelDownloadStatus.Cancelled;

    private sealed class Session
    {
        public Session(string modelId, CancellationTokenSource cts)
        {
            ModelId = modelId;
            Cts = cts;
            State = new ModelDownloadState(modelId, ModelDownloadStatus.Downloading, 0, null);
        }

        public string ModelId { get; }
        public CancellationTokenSource Cts { get; }
        public ModelDownloadState State { get; set; }
        public Task Task { get; set; } = Task.CompletedTask;
    }
}

/// <summary>
/// Well-known error codes surfaced via <see cref="ModelDownloadState.ErrorMessage"/>.
/// Lets the UI map to localized copy without string matching arbitrary exception messages.
/// </summary>
public static class ModelDownloadErrorCodes
{
    public const string HuggingFaceAuthorization = "hf-auth";
}
