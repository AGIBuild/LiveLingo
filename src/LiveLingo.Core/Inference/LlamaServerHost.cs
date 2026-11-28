using System.Diagnostics;
using LiveLingo.Core.Models;
using Microsoft.Extensions.Logging;

namespace LiveLingo.Core.Inference;

public sealed record LlamaServerHostOptions(
    TimeSpan IdleTimeout,
    TimeSpan StartTimeout,
    TimeSpan PollInterval,
    int ContextSize = 4096)
{
    public static LlamaServerHostOptions Default { get; } =
        new(TimeSpan.FromMinutes(10), TimeSpan.FromSeconds(90), TimeSpan.FromMilliseconds(150));
}

/// <summary>
/// A running server that is not unloaded for idleness while a lease is held. Model replacement waits for leases;
/// disposing the host stops the server immediately.
/// </summary>
public sealed class ServerLease : IDisposable
{
    private readonly Action _release;
    private int _released;

    internal ServerLease(ModelDescriptor model, IChatCompletionClient client, Action release)
    {
        Model = model;
        Client = client;
        _release = release;
    }

    public IChatCompletionClient Client { get; }
    public ModelDescriptor Model { get; }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _released, 1) == 0)
            _release();
    }
}

/// <summary>
/// Runs the pinned llama-server, sharing one process when multiple <see cref="InferenceRole"/>s resolve to the same
/// physical <see cref="ModelDescriptor.ArtifactId"/>, and keeping distinct role models side-by-side only when they fit
/// within the host's co-residency budget (otherwise evicting idle peer servers before loading the requested model).
/// A server is started on first use, reused while active, restarted on the next request after it exits, and unloaded
/// after <see cref="LlamaServerHostOptions.IdleTimeout"/> without leases.
/// A failed start is an explicit error; nothing is retried behind the caller's back.
/// </summary>
public sealed class LlamaServerHost : IAsyncDisposable
{
    private sealed class Instance(
        ModelDescriptor artifact,
        ModelDescriptor representativeModel,
        ILlamaProcess process,
        HttpClient http,
        IChatCompletionClient client,
        ITimer idleTimer)
    {
        public ModelDescriptor Artifact { get; } = artifact;
        public ModelDescriptor RepresentativeModel { get; set; } = representativeModel;
        public ILlamaProcess Process { get; } = process;
        public HttpClient Http { get; } = http;
        public IChatCompletionClient Client { get; } = client;
        public ITimer IdleTimer { get; } = idleTimer;
        public int Leases { get; set; }
        public TaskCompletionSource Drained { get; set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
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
    private readonly HardwareProfile _hardware;
    private readonly Dictionary<string, Instance> _instances = new(StringComparer.Ordinal);
    private readonly Dictionary<InferenceRole, string> _roleBindings = [];
    private readonly SemaphoreSlim _hostLock = new(1, 1);
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
        ILogger<LlamaServerHost> logger,
        HardwareProfile? hardware = null)
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
        _hardware = hardware ?? HardwareProfile.Baseline32GbAppleMetal;
    }

    /// <exception cref="ModelNotReadyException">The runtime or the model is not installed.</exception>
    /// <exception cref="LlamaServerStartException">The server exited or did not become ready in time.</exception>
    public async Task<ServerLease> AcquireAsync(InferenceRole role, ModelDescriptor model, CancellationToken ct)
    {
        await _hostLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            var artifact = model.ResolveArtifact();
            Instance? target;
            List<Instance> toStop = [];

            lock (_gate)
            {
                _instances.TryGetValue(artifact.Id, out target);
                if (target is not null && target.Process.HasExited)
                {
                    toStop.Add(target);
                    target = null;
                }

                var previousArtifactId = _roleBindings.TryGetValue(role, out var boundId) ? boundId : null;
                foreach (var candidate in _instances.Values)
                {
                    if (string.Equals(candidate.Artifact.Id, artifact.Id, StringComparison.Ordinal))
                        continue;

                    var ownedOnlyByThisRole = string.Equals(candidate.Artifact.Id, previousArtifactId, StringComparison.Ordinal)
                        && !HasOtherRoleBoundUnsafe(candidate.Artifact.Id, role);
                    var exceedsCoResidency = target is null && !CanCoResideWith(candidate.RepresentativeModel, model);

                    if (candidate.Process.HasExited || ownedOnlyByThisRole || exceedsCoResidency)
                        toStop.Add(candidate);
                }
            }

            if (target is null)
            {
                RequireInstalled(_runtime);
                RequireInstalled(model);
            }

            foreach (var retiring in toStop)
            {
                if (!retiring.Process.HasExited)
                {
                    Task drained;
                    lock (_gate)
                        drained = retiring.Leases == 0 ? Task.CompletedTask : retiring.Drained.Task;
                    await drained.WaitAsync(ct).ConfigureAwait(false);
                }
            }

            foreach (var retiring in toStop)
                await StopAsync(retiring).ConfigureAwait(false);

            target ??= await StartAsync(role, model, ct).ConfigureAwait(false);

            lock (_gate)
            {
                _roleBindings[role] = artifact.Id;
                target.RepresentativeModel = model;
                if (target.Leases == 0)
                    target.Drained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                target.Leases++;
                target.IdleTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            }

            var instance = target;
            return new ServerLease(model, instance.Client, () => Release(instance));
        }
        finally
        {
            _hostLock.Release();
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
        await _hostLock.WaitAsync().ConfigureAwait(false);
        try
        {
            List<Instance> running;
            lock (_gate)
            {
                if (_disposed)
                    return;
                _disposed = true;
                running = _instances.Values.ToList();
            }

            foreach (var instance in running)
                await StopAsync(instance).ConfigureAwait(false);
        }
        finally
        {
            _hostLock.Release();
        }
    }

    private bool HasOtherRoleBoundUnsafe(string artifactId, InferenceRole excludingRole)
    {
        foreach (var (boundRole, boundArtifactId) in _roleBindings)
        {
            if (boundRole != excludingRole && string.Equals(boundArtifactId, artifactId, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private bool CanCoResideWith(ModelDescriptor existingModel, ModelDescriptor incomingModel) =>
        ModelAdvisor.CanCoResideLlmModels(existingModel, incomingModel, _core.Models.Speech, _hardware);

    private async Task<Instance> StartAsync(InferenceRole role, ModelDescriptor model, CancellationToken ct)
    {
        var artifact = model.ResolveArtifact();
        var executable = Path.Combine(_models.GetModelDirectory(_runtime.Id), _runtime.ExtractedFiles[0]);
        var modelPath = Path.Combine(_models.GetModelDirectory(artifact.Id), artifact.Assets[0].RelativePath);
        var endpoint = new Uri($"http://127.0.0.1:{_ports.NextLoopbackPort()}/");
        var launch = new LlamaServerLaunch(executable, BuildArguments(modelPath, endpoint), endpoint);

        _logger.LogInformation("Starting llama-server for {Role} with {Model} ({Artifact}) at {Endpoint}", role, model.Id, artifact.Id, endpoint);
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
        var idleTimer = _time.CreateTimer(_ => OnIdle(artifact.Id), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        var instance = new Instance(
            artifact, model, process, http, new OpenAiChatClient(http, new Uri(endpoint, "v1/chat/completions")), idleTimer);
        lock (_gate)
            _instances[artifact.Id] = instance;
        return instance;
    }

    private void RequireInstalled(ModelDescriptor descriptor)
    {
        if (_models.HasAllExpectedLocalAssets(descriptor.ResolveArtifact()))
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
            if (--instance.Leases == 0)
            {
                instance.Drained.TrySetResult();
                if (!instance.Stopped)
                    instance.IdleTimer.Change(_options.IdleTimeout, Timeout.InfiniteTimeSpan);
            }
        }
    }

    private void OnIdle(string artifactId) => _ = UnloadIfIdleAsync(artifactId);

    private async Task UnloadIfIdleAsync(string artifactId)
    {
        await _hostLock.WaitAsync().ConfigureAwait(false);
        try
        {
            Instance? idle;
            lock (_gate)
                idle = _instances.TryGetValue(artifactId, out var found) && found.Leases == 0 ? found : null;

            if (idle is not null)
            {
                _logger.LogInformation("Unloading idle llama-server for artifact {Artifact}", artifactId);
                await StopAsync(idle).ConfigureAwait(false);
            }
        }
        finally
        {
            _hostLock.Release();
        }
    }

    private async Task StopAsync(Instance instance)
    {
        lock (_gate)
        {
            instance.Stopped = true;
            if (_instances.TryGetValue(instance.Artifact.Id, out var registered) && ReferenceEquals(registered, instance))
            {
                _instances.Remove(instance.Artifact.Id);
                foreach (var role in _roleBindings.Where(kv => string.Equals(kv.Value, instance.Artifact.Id, StringComparison.Ordinal)).Select(kv => kv.Key).ToList())
                    _roleBindings.Remove(role);
            }
        }

        await instance.IdleTimer.DisposeAsync().ConfigureAwait(false);
        instance.Http.Dispose();
        await instance.Process.DisposeAsync().ConfigureAwait(false);
    }
}
