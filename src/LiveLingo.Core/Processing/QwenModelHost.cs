using LLama;
using LLama.Common;
using LLama.Native;
using LiveLingo.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LiveLingo.Core.Processing;

public enum ModelLoadState { Unloaded, Loading, Loaded }

public sealed class QwenModelHost : IDisposable
{
    private readonly IModelManager _modelManager;
    private readonly CoreOptions _options;
    private readonly ILogger<QwenModelHost> _logger;
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private readonly Timer _idleTimer;
    private LLamaWeights? _weights;
    private volatile ModelLoadState _state = ModelLoadState.Unloaded;
    private static int _logConfigured;

    private const int IdleTimeoutMs = 300_000; // 5 minutes

    public ModelLoadState State => _state;
    public string ModelPath { get; private set; } = string.Empty;
    public event Action<ModelLoadState>? StateChanged;

    public QwenModelHost(
        IModelManager modelManager,
        IOptions<CoreOptions> options,
        ILogger<QwenModelHost> logger)
    {
        _modelManager = modelManager;
        _options = options.Value;
        _logger = logger;
        _idleTimer = new Timer(OnIdleTimeout, null, Timeout.Infinite, Timeout.Infinite);

        if (Interlocked.Exchange(ref _logConfigured, 1) == 0)
            NativeLogConfig.llama_log_set((level, msg) => { });
    }

    public async Task<LLamaWeights> GetWeightsAsync(CancellationToken ct)
    {
        ResetIdleTimer();

        if (_weights is not null)
            return _weights;

        await _loadLock.WaitAsync(ct);
        try
        {
            if (_weights is not null)
                return _weights;

            SetState(ModelLoadState.Loading);
            var modelDir = _modelManager.GetModelDirectory(ModelRegistry.Qwen25_15B.Id);
            ModelPath = Directory.Exists(modelDir)
                ? Directory.GetFiles(modelDir, "*.gguf").FirstOrDefault() ?? ""
                : "";

            if (string.IsNullOrEmpty(ModelPath) || !File.Exists(ModelPath))
                throw new FileNotFoundException(
                    "Qwen model not found. Please download it from Settings → Models tab.",
                    string.IsNullOrWhiteSpace(ModelPath) ? Path.Combine(modelDir, "*.gguf") : ModelPath);

            var threads = _options.InferenceThreads > 0
                ? _options.InferenceThreads
                : Math.Max(2, Environment.ProcessorCount / 2);

            var parameters = new ModelParams(ModelPath)
            {
                ContextSize = 2048,
                GpuLayerCount = 0,
                Threads = threads
            };

            _weights = await LLamaWeights.LoadFromFileAsync(parameters, ct, null);
            SetState(ModelLoadState.Loaded);
            _logger.LogDebug("Qwen model loaded successfully");
            return _weights;
        }
        catch
        {
            SetState(ModelLoadState.Unloaded);
            throw;
        }
        finally
        {
            _loadLock.Release();
        }
    }

    private void OnIdleTimeout(object? state)
    {
        if (_weights is null) return;

        _loadLock.Wait();
        try
        {
            _weights?.Dispose();
            _weights = null;
            SetState(ModelLoadState.Unloaded);
            _logger.LogDebug("Qwen model unloaded (idle timeout)");
        }
        finally
        {
            _loadLock.Release();
        }
    }

    private void ResetIdleTimer()
    {
        _idleTimer.Change(IdleTimeoutMs, Timeout.Infinite);
    }

    private void SetState(ModelLoadState newState)
    {
        _state = newState;
        StateChanged?.Invoke(newState);
    }

    public void Dispose()
    {
        _idleTimer.Dispose();
        _weights?.Dispose();
        _weights = null;
        _loadLock.Dispose();
    }
}
