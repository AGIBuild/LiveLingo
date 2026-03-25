using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace LiveLingo.Core.Processing;

public sealed class LlamaServerProcessManager : ILlamaServerProcessManager
{
    private readonly INativeRuntimeUpdater _updater;
    private readonly ILogger<LlamaServerProcessManager> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);
    
    private Process? _process;
    private string? _currentModelPath;
    private string? _currentEndpointUrl;
    private volatile ModelLoadState _state = ModelLoadState.Unloaded;

    public string? CurrentEndpointUrl => _currentEndpointUrl;
    public ModelLoadState State => _state;

    public event Action<ModelLoadState>? StateChanged;

    public LlamaServerProcessManager(INativeRuntimeUpdater updater, ILogger<LlamaServerProcessManager> logger)
    {
        _updater = updater;
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

            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

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
                    HandleServerLog(e.Data, tcs);
                }
            };
            
            _process.ErrorDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                {
                    HandleServerLog(e.Data, tcs);
                }
            };

            _process.Exited += (sender, e) =>
            {
                _logger.LogWarning("llama-server process exited unexpectedly with code {ExitCode}.", _process?.ExitCode);
                _currentEndpointUrl = null;
                _currentModelPath = null;
                SetState(ModelLoadState.Unloaded);
                tcs.TrySetException(new InvalidOperationException($"llama-server exited prematurely with code {_process?.ExitCode}"));
            };

            _logger.LogInformation("Starting llama-server on port {Port} for model {ModelPath}", port, modelPath);
            _process.Start();
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();

            // Wait until server reports it's ready or fails
            using var reg = ct.Register(() => tcs.TrySetCanceled());
            
            // Timeout just in case it hangs
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(60), ct);
            var completedTask = await Task.WhenAny(tcs.Task, timeoutTask);
            
            if (completedTask == timeoutTask)
            {
                await StopServerInternalAsync();
                throw new TimeoutException("llama-server did not start within the expected time.");
            }

            await tcs.Task; // throw if fault/cancelled

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

    private void HandleServerLog(string logLine, TaskCompletionSource tcs)
    {
        // llama-server typically says "server is listening on http..." when ready.
        if (logLine.Contains("server is listening", StringComparison.OrdinalIgnoreCase))
        {
            tcs.TrySetResult();
        }
        else if (logLine.Contains("ERR", StringComparison.OrdinalIgnoreCase) || 
                 logLine.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
                 logLine.Contains("error", StringComparison.OrdinalIgnoreCase))
        {
            // Specifically log errors to avoid spamming Serilog with normal progress
            // Ignore some common false positives
            if (!logLine.Contains("failed to initialize", StringComparison.OrdinalIgnoreCase) || logLine.Contains("llama_model_load"))
            {
                _logger.LogError("llama-server: {Log}", logLine);
            }
        }
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
