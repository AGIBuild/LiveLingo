namespace LiveLingo.Core.Models;

/// <summary>
/// Single aggregation point for <see cref="TranslationRouteTrace"/> records emitted
/// by <see cref="FallbackTranslationInvoker"/>. Implementations are expected to be
/// thread-safe and cheap to call on the hot translation path — the invoker records
/// exactly one trace per invocation so this is called on every translated segment.
///
/// Consumers (diagnostic panels, log sinks, metrics exporters) subscribe via
/// <see cref="RouteCompleted"/>; offline inspection can iterate <see cref="RecentTraces"/>.
/// </summary>
public interface ITranslationTelemetry
{
    /// <summary>
    /// Record a completed route trace. Must be safe to call concurrently. Listeners
    /// registered on <see cref="RouteCompleted"/> are invoked synchronously on the
    /// calling thread — implementations should not hold locks while invoking them.
    /// </summary>
    void Record(TranslationRouteTrace trace);

    /// <summary>
    /// Raised after every <see cref="Record"/> call. Subscribers must be prepared to
    /// be invoked on arbitrary threads (typically the thread that completed the
    /// translation call) and should offload any UI work to their own dispatcher.
    /// </summary>
    event Action<TranslationRouteTrace>? RouteCompleted;

    /// <summary>
    /// Snapshot of recent traces, ordered oldest-first. Intended for diagnostic
    /// inspection; the capacity is bounded so historical data will be evicted.
    /// </summary>
    IReadOnlyList<TranslationRouteTrace> RecentTraces { get; }

    /// <summary>
    /// Drop every buffered trace. Invoked by the diagnostics panel's "Clear"
    /// affordance so the user can both wipe the UI and shed retained memory.
    /// Must be safe to call concurrently with <see cref="Record"/>.
    /// </summary>
    void Clear();
}
