using System.Collections.Concurrent;

namespace LiveLingo.Core.Models.Downloads;

/// <summary>
/// Collapses concurrent download requests for the same model into a single in-flight task,
/// so two callers that ask for the same descriptor at the same time share one download.
/// </summary>
internal sealed class InflightDownloadRegistry
{
    private readonly ConcurrentDictionary<string, Task> _inflight = new();

    public Task GetOrAdd(string modelId, Func<string, Task> factory) =>
        _inflight.GetOrAdd(modelId, factory);

    public void Release(string modelId) => _inflight.TryRemove(modelId, out _);
}
