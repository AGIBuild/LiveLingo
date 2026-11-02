using System.Diagnostics;
using LiveLingo.Core.Models;
using Microsoft.Extensions.Logging;

namespace LiveLingo.Core.Inference;

public sealed record LlamaServerHostOptions(
    TimeSpan IdleTimeout,
    TimeSpan StartTimeout,
    TimeSpan PollInterval,
    int ContextSize = 8192)
{
    public static LlamaServerHostOptions Default { get; } =
        new(TimeSpan.FromMinutes(10), TimeSpan.FromSeconds(90), TimeSpan.FromMilliseconds(150));
}

/// <summary>
/// A running server that is not unloaded for idleness while a lease is held. Replacing the model of a role or
/// disposing the host stops the server immediately; requests in flight then fail with an explicit error.
/// </summary>
public sealed class ServerLease : IDisposable
{
    private readonly Action _release;
    private int _released;

    internal ServerLease(IChatCompletionClient client, Action release)
    {
        Client = client;
        _release = release;
    }

    public IChatCompletionClient Client { get; }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _released, 1) == 0)
            _release();
    }
}

/// <summary>
/// Runs the pinned llama-server, one process per <see cref="InferenceRole"/>, bound to a random loopback port.
/// A server is started on first use, reused while the same model is requested, restarted on the next request
/// after it exits, and unloaded after <see cref="LlamaServerHostOptions.IdleTimeout"/> without leases.
/// A failed start is an explicit error; nothing is retried behind the caller's back.
/// </summary>
public sealed class LlamaServerHost : IAsyncDisposable
{
    private sealed class Instance(
        ModelDescriptor model,
        ILlamaProcess process,
        HttpClient http,
        IChatCompletionClient client,
        ITimer idleTimer)
    {
        public ModelDescriptor Model { get; } = model;
        public ILlamaProcess Process { get; } = process;
        public HttpClient Http { get; } = http;
        public IChatCompletionClient Client { get; } = client;
        public ITimer IdleTimer { get; } = idleTimer;
        public int Leases { get; set; }
        public bool Stopped { get; set; }
    }

    private readonly IModelManager _models;
    private readonly ModelDescriptor _runtime;
    private readonly ILlamaProcessLauncher _launcher;
    private readonly IServerHealthProbe _probe;
    private readonly IPortAllocator _ports;
    private readonly CoreOptions _core;
    private readonly LlamaServerHostOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<LlamaServerHost> _logger;
    private readonly Dictionary<InferenceRole, Instance> _instances = [];
    private readonly Dictionary<InferenceRole, SemaphoreSlim> _locks = [];
    private readonly object _gate = new();
    private bool _disposed;

    public LlamaServerHost(
        IModelManager models,
        ModelDescriptor runtime,
        ILlamaProcessLauncher launcher,
        IServerHealthProbe probe,
        IPortAllocator ports,
        CoreOptions core,
        LlamaServerHostOptions options,
        TimeProvider time,
        ILogger<LlamaServerHost> logger)
    {
        _models = models;
        _runtime = runtime;
        _launcher = launcher;
        _probe = probe;
        _ports = ports;
        _core = core;
        _options = options;
        _time = time;
        _logger = logger;
    }

    /// <exception cref="ModelNotReadyException">The runtime or the model is not installed.</exception>
    /// <exception cref="LlamaServerStartException">The server exited or did not become ready in time.</exception>
    public async Task<ServerLease> AcquireAsync(InferenceRole role, ModelDescriptor model, CancellationToken ct)
    {
        var roleLock = LockFor(role);
        await roleLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            Instance? current;
            lock (_gate)
                _instances.TryGetValue(role, out current);

            if (current is not null && (current.Process.HasExited || current.Model.Id != model.Id))
            {
                await StopAsync(role, current).ConfigureAwait(false);
                current = null;
            }

            current ??= await StartAsync(role, model, ct).ConfigureAwait(false);

            lock (_gate)
            {
                current.Leases++;
                current.IdleTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            }

            var instance = current;
            return new ServerLease(instance.Client, () => Release(instance));
        }
        finally
        {
            roleLock.Release();
        }
    }

    /// <summary>
    /// Starts the server of the role (when it is not running) and gives it back at once, so it is ready for the next
    /// request and unloads when it stays idle.
    /// </summary>
    public async Task WarmAsync(InferenceRole role, ModelDescriptor model, CancellationToken ct)
    {
        using var lease = await AcquireAsync(role, model, ct).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        List<InferenceRole> roles;
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            roles = _locks.Keys.ToList();
        }

        foreach (var role in roles)
        {
            var roleLock = LockFor(role);
            await roleLock.WaitAsync().ConfigureAwait(false);
            try
            {
                Instance? instance;
                lock (_gate)
                    _instances.TryGetValue(role, out instance);
                if (instance is not null)
                    await StopAsync(role, instance).ConfigureAwait(false);
            }
            finally
            {
                roleLock.Release();
            }
        }
    }

    private SemaphoreSlim LockFor(InferenceRole role)
    {
        lock (_gate)
        {
            if (!_locks.TryGetValue(role, out var roleLock))
                _locks[role] = roleLock = new SemaphoreSlim(1, 1);
            return roleLock;
        }
    }

    private async Task<Instance> StartAsync(InferenceRole role, ModelDescriptor model, CancellationToken ct)
    {
        RequireInstalled(_runtime);
        RequireInstalled(model);

        var executable = Path.Combine(_models.GetModelDirectory(_runtime.Id), _runtime.ExtractedFiles[0]);
        var modelPath = Path.Combine(_models.GetModelDirectory(model.Id), model.Assets[0].RelativePath);
        var endpoint = new Uri($"http://127.0.0.1:{_ports.NextLoopbackPort()}/");
        var launch = new LlamaServerLaunch(executable, BuildArguments(modelPath, endpoint), endpoint);

        _logger.LogInformation("Starting llama-server for {Role} with {Model} at {Endpoint}", role, model.Id, endpoint);
        var process = _launcher.Start(launch);
        try
        {
            await WaitUntilReadyAsync(process, endpoint, ct).ConfigureAwait(false);
        }
        catch
        {
            await process.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
        var idleTimer = _time.CreateTimer(_ => OnIdle(role), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        var instance = new Instance(
            model, process, http, new OpenAiChatClient(http, new Uri(endpoint, "v1/chat/completions")), idleTimer);
        lock (_gate)
            _instances[role] = instance;
        return instance;
    }

    private void RequireInstalled(ModelDescriptor descriptor)
    {
        if (_models.HasAllExpectedLocalAssets(descriptor))
            return;

        throw new ModelNotReadyException(
            descriptor.Type,
            descriptor.Id,
            $"'{descriptor.DisplayName}' is not installed.",
            "Download it first.");
    }

    private List<string> BuildArguments(string modelPath, Uri endpoint)
    {
        var arguments = new List<string>
        {
            "--model", modelPath,
            "--host", "127.0.0.1",
            "--port", endpoint.Port.ToString(),
            "--ctx-size", _options.ContextSize.ToString(),
            "--parallel", "1",
            "--n-gpu-layers", "99",
            "--reasoning-budget", "0"
        };
        if (_core.InferenceThreads > 0)
            arguments.AddRange(["--threads", _core.InferenceThreads.ToString()]);
        return arguments;
    }

    private async Task WaitUntilReadyAsync(ILlamaProcess process, Uri endpoint, CancellationToken ct)
    {
        var started = Stopwatch.GetTimestamp();
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            if (process.HasExited)
                throw new LlamaServerStartException($"llama-server exited during start with code {process.ExitCode}.");
            if (await _probe.IsReadyAsync(endpoint, ct).ConfigureAwait(false))
                return;
            if (Stopwatch.GetElapsedTime(started) >= _options.StartTimeout)
                throw new LlamaServerStartException(
                    $"llama-server did not become ready within {_options.StartTimeout.TotalSeconds:0} s.");

            await Task.Delay(_options.PollInterval, ct).ConfigureAwait(false);
        }
    }

    private void Release(Instance instance)
    {
        lock (_gate)
        {
            if (--instance.Leases == 0 && !instance.Stopped)
                instance.IdleTimer.Change(_options.IdleTimeout, Timeout.InfiniteTimeSpan);
        }
    }

    private void OnIdle(InferenceRole role) => _ = UnloadIfIdleAsync(role);

    private async Task UnloadIfIdleAsync(InferenceRole role)
    {
        var roleLock = LockFor(role);
        await roleLock.WaitAsync().ConfigureAwait(false);
        try
        {
            Instance? idle;
            lock (_gate)
                idle = _instances.TryGetValue(role, out var found) && found.Leases == 0 ? found : null;

            if (idle is not null)
            {
                _logger.LogInformation("Unloading idle llama-server for {Role}", role);
                await StopAsync(role, idle).ConfigureAwait(false);
            }
        }
        finally
        {
            roleLock.Release();
        }
    }

    private async Task StopAsync(InferenceRole role, Instance instance)
    {
        lock (_gate)
        {
            instance.Stopped = true;
            if (_instances.TryGetValue(role, out var registered) && ReferenceEquals(registered, instance))
                _instances.Remove(role);
        }

        await instance.IdleTimer.DisposeAsync().ConfigureAwait(false);
        instance.Http.Dispose();
        await instance.Process.DisposeAsync().ConfigureAwait(false);
    }
}
