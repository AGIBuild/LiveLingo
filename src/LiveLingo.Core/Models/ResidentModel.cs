using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LiveLingo.Core.Models;

public enum ModelResidency
{
    Unloaded,
    Loading,
    Ready
}

/// <summary>
/// A model kept in memory between uses, so that the next use does not wait for it to load. It is loaded once (however
/// many callers ask at the same time), lent out as a <see cref="Lease"/>, and unloaded after it has been idle for the
/// idle timeout. The key identifies what was loaded with (for example the hotwords of a recognizer); asking for another
/// key reloads the model, which is only possible while nobody holds it.
/// </summary>
public sealed class ResidentModel<T> : IAsyncDisposable where T : class, IDisposable
{
    /// <summary>The loaded model, lent to one user. Disposing it gives the model back; it does not unload it.</summary>
    public sealed class Lease : IDisposable
    {
        private readonly ResidentModel<T> _owner;
        private int _released;

        internal Lease(ResidentModel<T> owner, T value)
        {
            _owner = owner;
            Value = value;
        }

        public T Value { get; }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
                _owner.Release();
        }
    }

    private readonly string _name;
    private readonly Func<string, T> _load;
    private readonly TimeSpan _idleTimeout;
    private readonly ILogger _logger;
    private readonly ITimer _idleTimer;
    private readonly SemaphoreSlim _loading = new(1, 1);
    private readonly object _sync = new();
    private T? _value;
    private string _key = "";
    private int _leases;
    private bool _disposed;
    private ModelResidency _state;

    /// <param name="name">What the model is, for the log.</param>
    /// <param name="load">Loads the model for a key; called on a thread-pool thread, one at a time.</param>
    public ResidentModel(string name, Func<string, T> load, TimeSpan idleTimeout, TimeProvider? time = null, ILogger? logger = null)
    {
        _name = name;
        _load = load;
        _idleTimeout = idleTimeout;
        _logger = logger ?? NullLogger.Instance;
        _idleTimer = (time ?? TimeProvider.System).CreateTimer(_ => UnloadIfIdle(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public ModelResidency State
    {
        get
        {
            lock (_sync)
                return _state;
        }
    }

    /// <summary>Raised when <see cref="State"/> changed, on whichever thread caused it.</summary>
    public event Action<ModelResidency>? StateChanged;

    /// <summary>Makes sure the model is in memory and restarts its idle countdown.</summary>
    public async Task WarmAsync(string key, CancellationToken ct = default)
    {
        await EnsureLoadedAsync(key, ct).ConfigureAwait(false);
        lock (_sync)
        {
            if (_leases == 0 && _value is not null)
                _idleTimer.Change(_idleTimeout, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>Lends the model, loading it first when it is not in memory.</summary>
    public async Task<Lease> AcquireAsync(string key, CancellationToken ct = default)
    {
        while (true)
        {
            await EnsureLoadedAsync(key, ct).ConfigureAwait(false);
            lock (_sync)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                // It may have been unloaded for idleness since it was loaded; then it is loaded again.
                if (_value is not null && _key == key)
                {
                    _leases++;
                    _idleTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                    return new Lease(this, _value);
                }
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        T? unload = null;
        lock (_sync)
        {
            if (_disposed)
                return ValueTask.CompletedTask;
            _disposed = true;
            if (_leases == 0)
            {
                unload = _value;
                _value = null;
            }
        }

        _idleTimer.Dispose();
        unload?.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task EnsureLoadedAsync(string key, CancellationToken ct)
    {
        await _loading.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            T? replaced;
            lock (_sync)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_value is not null && _key == key)
                    return;
                if (_value is not null && _leases > 0)
                    throw new InvalidOperationException($"'{_name}' is in use and cannot be reloaded for another key.");

                replaced = _value;
                _value = null;
            }

            replaced?.Dispose();
            SetState(ModelResidency.Loading);

            var clock = Stopwatch.StartNew();
            T loaded;
            try
            {
                loaded = await Task.Run(() => _load(key), CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                SetState(ModelResidency.Unloaded);
                throw;
            }

            lock (_sync)
            {
                if (_disposed)
                {
                    loaded.Dispose();
                    throw new ObjectDisposedException(GetType().Name);
                }

                _value = loaded;
                _key = key;
            }

            _logger.LogInformation("Loaded {Model} in {Milliseconds} ms", _name, clock.ElapsedMilliseconds);
            SetState(ModelResidency.Ready);
        }
        finally
        {
            _loading.Release();
        }
    }

    private void Release()
    {
        T? unload = null;
        lock (_sync)
        {
            if (--_leases != 0)
                return;

            if (_disposed)
            {
                unload = _value;
                _value = null;
            }
            else
            {
                _idleTimer.Change(_idleTimeout, Timeout.InfiniteTimeSpan);
            }
        }

        unload?.Dispose();
    }

    private void UnloadIfIdle()
    {
        T unload;
        lock (_sync)
        {
            if (_leases > 0 || _value is null)
                return;

            unload = _value;
            _value = null;
        }

        unload.Dispose();
        _logger.LogInformation("Unloaded {Model} after {Minutes} idle minutes", _name, _idleTimeout.TotalMinutes);
        SetState(ModelResidency.Unloaded);
    }

    private void SetState(ModelResidency state)
    {
        lock (_sync)
        {
            if (_state == state)
                return;
            _state = state;
        }

        StateChanged?.Invoke(state);
    }
}
