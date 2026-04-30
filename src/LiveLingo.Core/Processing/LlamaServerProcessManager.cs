using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;

namespace LiveLingo.Core.Processing;

public sealed class LlamaServerProcessManager : ILlamaServerProcessManager
{
    private static readonly TimeSpan ServerStartTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan HealthPollInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan HealthProbeTimeout = TimeSpan.FromSeconds(2);

    private readonly INativeRuntimeUpdater _updater;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<LlamaServerProcessManager> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private Process? _process;
    private string? _currentModelPath;
    private string? _currentEndpointUrl;
    private volatile ModelLoadState _state = ModelLoadState.Unloaded;

    public string? CurrentEndpointUrl => _currentEndpointUrl;
    public ModelLoadState State => _state;

    public event Action<ModelLoadState>? StateChanged;

    public LlamaServerProcessManager(
        INativeRuntimeUpdater updater,
        IHttpClientFactory httpClientFactory,
        ILogger<LlamaServerProcessManager> logger)
    {
        _updater = updater;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    private void SetState(ModelLoadState state)
    {
        if (_state == state) return;
        _state = state;
        StateChanged?.Invoke(state);
    }

    public async Task EnsureServerRunningAsync(string modelPath, int contextSize, int inferenceThreads, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (_process != null && !_process.HasExited && string.Equals(_currentModelPath, modelPath, StringComparison.OrdinalIgnoreCase))
            {
                // Server is already running with the requested model
                return;
            }

            await StopServerInternalAsync();

            SetState(ModelLoadState.Loading);

            var serverExe = await _updater.EnsureLatestLlamaServerAsync(ct);
            if (serverExe is null || !File.Exists(serverExe))
            {
                throw new FileNotFoundException("Failed to locate or download llama-server executable.");
            }

            var port = GetAvailablePort();
            _currentEndpointUrl = $"http://127.0.0.1:{port}";

            var exitTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            var startInfo = new ProcessStartInfo
            {
                FileName = serverExe,
                Arguments = BuildArguments(modelPath, contextSize, inferenceThreads, port),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            _process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

            _process.OutputDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                {
                    HandleServerLog(e.Data);
                }
            };

            _process.ErrorDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                {
                    HandleServerLog(e.Data);
                }
            };

            _process.Exited += (sender, e) =>
            {
                _logger.LogWarning("llama-server process exited unexpectedly with code {ExitCode}.", _process?.ExitCode);
                _currentEndpointUrl = null;
                _currentModelPath = null;
                SetState(ModelLoadState.Unloaded);
                exitTcs.TrySetException(new InvalidOperationException($"llama-server exited prematurely with code {_process?.ExitCode}"));
            };

            _logger.LogInformation("Starting llama-server on port {Port} for model {ModelPath}", port, modelPath);
            _process.Start();
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();

            await WaitForServerHealthAsync(_currentEndpointUrl, exitTcs.Task, ct).ConfigureAwait(false);

            _currentModelPath = modelPath;
            SetState(ModelLoadState.Loaded);
        }
        catch
        {
            await StopServerInternalAsync();
            SetState(ModelLoadState.Unloaded);
            throw;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task WaitForServerHealthAsync(string endpoint, Task exitTask, CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var timeoutTask = Task.Delay(ServerStartTimeout, timeoutCts.Token);
        var client = _httpClientFactory.CreateClient(nameof(LlamaServerProcessManager));

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            if (exitTask.IsCompleted)
                await exitTask.ConfigureAwait(false);

            if (await TryProbeHealthAsync(client, endpoint, ct).ConfigureAwait(false))
                return;

            var delayTask = Task.Delay(HealthPollInterval, ct);
            var completedTask = await Task.WhenAny(delayTask, timeoutTask, exitTask).ConfigureAwait(false);
            if (completedTask == exitTask)
                await exitTask.ConfigureAwait(false);
            if (completedTask == timeoutTask)
            {
                ct.ThrowIfCancellationRequested();
                throw new TimeoutException("llama-server did not start within the expected time.");
            }
        }
    }

    private async Task<bool> TryProbeHealthAsync(HttpClient client, string endpoint, CancellationToken ct)
    {
        using var probeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        probeCts.CancelAfter(HealthProbeTimeout);

        try
        {
            using var response = await client
                .GetAsync($"{endpoint}/health", HttpCompletionOption.ResponseHeadersRead, probeCts.Token)
                .ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.OK)
                return true;

            if (response.StatusCode != HttpStatusCode.ServiceUnavailable)
            {
                _logger.LogDebug(
                    "llama-server health probe returned {StatusCode}.",
                    response.StatusCode);
            }

            return false;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return false;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private void HandleServerLog(string logLine)
    {
        if (IsServerErrorLog(logLine))
            _logger.LogError("llama-server: {Log}", logLine);
    }

    internal static bool IsServerErrorLog(string logLine)
    {
        var trimmed = logLine.Trim();
        return trimmed.StartsWith("error", StringComparison.OrdinalIgnoreCase)
               || trimmed.StartsWith("err:", StringComparison.OrdinalIgnoreCase)
               || trimmed.Contains(" error:", StringComparison.OrdinalIgnoreCase)
               || trimmed.Contains(" error ", StringComparison.OrdinalIgnoreCase)
               || trimmed.Contains(" failed", StringComparison.OrdinalIgnoreCase)
               || trimmed.StartsWith("failed", StringComparison.OrdinalIgnoreCase)
               || trimmed.Contains(" exception", StringComparison.OrdinalIgnoreCase);
    }

    internal static string BuildArguments(string modelPath, int contextSize, int inferenceThreads, int port) =>
        $"-m \"{modelPath}\" -c {contextSize} --port {port} --threads {inferenceThreads} --reasoning-format none --reasoning off";

    private int GetAvailablePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public async Task StopServerAsync()
    {
        await _lock.WaitAsync();
        try
        {
            await StopServerInternalAsync();
            SetState(ModelLoadState.Unloaded);
        }
        finally
        {
            _lock.Release();
        }
    }

    private Task StopServerInternalAsync()
    {
        if (_process != null)
        {
            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill(true);
                    _process.WaitForExit(3000);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error while killing llama-server process.");
            }
            finally
            {
                _process.Dispose();
                _process = null;
            }
        }

        _currentEndpointUrl = null;
        _currentModelPath = null;
        return Task.CompletedTask;
    }

    private bool _disposed;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            _lock.Wait();
            try
            {
                StopServerInternalAsync().GetAwaiter().GetResult();
            }
            finally
            {
                _lock.Release();
            }
        }
        catch (ObjectDisposedException) { }
        finally
        {
            _lock.Dispose();
        }
    }
}
