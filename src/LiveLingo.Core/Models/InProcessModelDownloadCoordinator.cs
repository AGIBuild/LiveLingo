using System.Collections.Concurrent;
using System.Diagnostics;
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

        var resolvedId = ModelCatalog.FindSelectable(modelId)?.ArtifactId ?? modelId;
        if (_sessions.TryGetValue(resolvedId, out var session))
            return session.State with { ModelId = modelId };

        var installed = _modelManager.ListInstalled().FirstOrDefault(m => string.Equals(m.Id, resolvedId, StringComparison.Ordinal));
        return installed is not null
            ? new ModelDownloadState(modelId, ModelDownloadStatus.Installed, 100, null, installed.SizeBytes, installed.SizeBytes)
            : new ModelDownloadState(modelId, ModelDownloadStatus.Idle, 0, null);
    }

    public Task StartAsync(ModelDescriptor descriptor)
    {
        if (descriptor is null) throw new ArgumentNullException(nameof(descriptor));
        descriptor = descriptor.ResolveArtifact();

        lock (_startLock)
        {
            if (_sessions.TryGetValue(descriptor.Id, out var existing) && existing.State.IsDownloading)
                return existing.Task;

            var cts = new CancellationTokenSource();
            var session = new Session(descriptor.Id, descriptor.SizeBytes, cts);
            _sessions[descriptor.Id] = session;

            Publish(session, new ModelDownloadState(descriptor.Id, ModelDownloadStatus.Downloading, 0, null, 0, descriptor.SizeBytes));
            _logger?.LogInformation("Model download started: {ModelId} ({SizeBytes} bytes)", descriptor.Id, descriptor.SizeBytes);

            // Subscribers marshal StateChanged onto their own UI scheduler; reporting synchronously with lightweight
            // throttling avoids double async scheduler hops and prevents UI dispatcher flooding on fast links.
            var progress = new ThrottledProgress(descriptor.SizeBytes, state => Publish(session, state), descriptor.Id);

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
        var started = Stopwatch.GetTimestamp();
        try
        {
            await _modelManager.EnsureModelAsync(descriptor, progress, session.Cts.Token).ConfigureAwait(false);
            Publish(session, new ModelDownloadState(
                descriptor.Id, ModelDownloadStatus.Installed, 100, null, descriptor.SizeBytes, descriptor.SizeBytes));
            _logger?.LogInformation("Model download finished: {ModelId} in {Seconds:0.0} s", descriptor.Id, Stopwatch.GetElapsedTime(started).TotalSeconds);
        }
        catch (OperationCanceledException)
        {
            _logger?.LogInformation("Model download cancelled: {ModelId}", descriptor.Id);
            Publish(session, new ModelDownloadState(
                descriptor.Id, ModelDownloadStatus.Cancelled, session.State.Percentage, null,
                session.State.BytesDownloaded, session.State.TotalBytes));
        }
        catch (ModelDownloadAuthorizationException ex)
        {
            _logger?.LogWarning(ex, "Model download unauthorized: {ModelId}", descriptor.Id);
            Publish(session, new ModelDownloadState(
                descriptor.Id,
                ModelDownloadStatus.Failed,
                session.State.Percentage,
                ModelDownloadErrorCodes.HuggingFaceAuthorization,
                session.State.BytesDownloaded,
                session.State.TotalBytes));
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Model download failed: {ModelId}", descriptor.Id);
            Publish(session, new ModelDownloadState(
                descriptor.Id,
                ModelDownloadStatus.Failed,
                session.State.Percentage,
                ex.Message,
                session.State.BytesDownloaded,
                session.State.TotalBytes));
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
        public Session(string modelId, long totalBytes, CancellationTokenSource cts)
        {
            ModelId = modelId;
            Cts = cts;
            State = new ModelDownloadState(modelId, ModelDownloadStatus.Downloading, 0, null, 0, totalBytes);
        }

        public string ModelId { get; }
        public CancellationTokenSource Cts { get; }
        public ModelDownloadState State { get; set; }
        public Task Task { get; set; } = Task.CompletedTask;
    }

    private sealed class ThrottledProgress(
        long fallbackTotalBytes,
        Action<ModelDownloadState> publish,
        string modelId) : IProgress<ModelDownloadProgress>
    {
        private readonly object _gate = new();
        private long _lastTick;
        private long _lastBytes = -1;
        private double _lastPercentage = -1;

        public void Report(ModelDownloadProgress p)
        {
            var total = p.TotalBytes > 0 ? p.TotalBytes : fallbackTotalBytes;
            var downloaded = Math.Clamp(p.BytesDownloaded, 0, Math.Max(total, p.BytesDownloaded));
            var percentage = total > 0 ? Math.Clamp((double)downloaded / total * 100d, 0d, 100d) : p.Percentage;
            var now = Stopwatch.GetTimestamp();

            lock (_gate)
            {
                var firstChunk = _lastBytes <= 0 && downloaded > 0;
                var completedBytes = total > 0 && downloaded >= total && _lastBytes < total;
                var movedEnough = Math.Abs(percentage - _lastPercentage) >= 0.25d
                               || downloaded - _lastBytes >= 2 * 1024 * 1024;
                var timeElapsed = _lastTick == 0 || Stopwatch.GetElapsedTime(_lastTick, now) >= TimeSpan.FromMilliseconds(80);

                if (!firstChunk && !completedBytes && !(movedEnough && timeElapsed) && _lastBytes >= 0)
                    return;

                _lastTick = now;
                _lastBytes = downloaded;
                _lastPercentage = percentage;
            }

            publish(new ModelDownloadState(modelId, ModelDownloadStatus.Downloading, percentage, null, downloaded, total));
        }
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
