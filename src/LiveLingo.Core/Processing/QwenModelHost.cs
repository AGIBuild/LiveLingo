using LLama;
using LLama.Common;
using LLama.Native;
using LiveLingo.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LiveLingo.Core.Processing;

public enum ModelLoadState { Unloaded, Loading, Loaded }

public sealed class QwenModelHost : IDisposable, ILlmModelLoadCoordinator
{
    private readonly IModelManager _modelManager;
    private readonly INativeRuntimeUpdater _nativeUpdater;
    private readonly CoreOptions _options;
    private readonly ILogger<QwenModelHost> _logger;
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private readonly Timer _idleTimer;
    private LLamaWeights? _weights;
    private volatile ModelLoadState _state = ModelLoadState.Unloaded;
    private static int _logConfigured;

    private const int IdleTimeoutMs = 300_000; // 5 minutes

    /// <summary>
    /// Primary translation LLM (registry <see cref="ModelRegistry.Qwen35_9B"/>) with optional runtime fallback to <see cref="ModelRegistry.Qwen25_15B"/>.
    /// </summary>
    private ModelDescriptor _activeModelDescriptor;

    public ModelLoadState State => _state;
    public string ModelPath { get; private set; } = string.Empty;

    /// <summary>Descriptor for the GGUF currently used by translation and post-processing.</summary>
    public ModelDescriptor ActiveModelDescriptor => _activeModelDescriptor;

    public event Action<ModelLoadState>? StateChanged;

    /// <summary>Raised when the primary Qwen model failed to load and a smaller fallback GGUF is used instead.</summary>
    public event EventHandler<QwenModelFallbackEventArgs>? ModelLoadFallbackApplied;

    public QwenModelHost(
        IModelManager modelManager,
        INativeRuntimeUpdater nativeUpdater,
        IOptions<CoreOptions> options,
        ILogger<QwenModelHost> logger)
    {
        _modelManager = modelManager;
        _nativeUpdater = nativeUpdater;
        _options = options.Value;
        _logger = logger;
        _idleTimer = new Timer(OnIdleTimeout, null, Timeout.Infinite, Timeout.Infinite);

        _activeModelDescriptor = GetActiveModelDescriptor();

        if (Interlocked.Exchange(ref _logConfigured, 1) == 0)
            NativeLogConfig.llama_log_set((level, msg) => { });
    }

    private ModelDescriptor GetActiveModelDescriptor()
    {
        if (!string.IsNullOrWhiteSpace(_options.ActiveTranslationModelId))
        {
            var byId = ModelRegistry.AllModels.FirstOrDefault(m => string.Equals(m.Id, _options.ActiveTranslationModelId, StringComparison.OrdinalIgnoreCase));
            if (byId is not null && byId.Type == ModelType.Translation && byId.Id.StartsWith("qwen", StringComparison.OrdinalIgnoreCase))
                return byId;
        }
        return ModelRegistry.Qwen35_9B;
    }

    public async Task RequestRetryPrimaryTranslationModelAsync(CancellationToken cancellationToken = default)
    {
        await _loadLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _weights?.Dispose();
            _weights = null;
            _activeModelDescriptor = GetActiveModelDescriptor();
            ModelPath = string.Empty;
            SetState(ModelLoadState.Unloaded);
            _idleTimer.Change(Timeout.Infinite, Timeout.Infinite);
            _logger.LogInformation(
                "Translation LLM reset to prefer primary model {PrimaryId} on next load.",
                _activeModelDescriptor.Id);
        }
        finally
        {
            _loadLock.Release();
        }
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
            try
            {
                await LoadPrimaryOrFallbackAsync(ct).ConfigureAwait(false);
                SetState(ModelLoadState.Loaded);
                _logger.LogDebug("Qwen model loaded: {ModelId}", _activeModelDescriptor.Id);
                return _weights!;
            }
            catch
            {
                SetState(ModelLoadState.Unloaded);
                throw;
            }
        }
        finally
        {
            _loadLock.Release();
        }
    }

    private async Task LoadPrimaryOrFallbackAsync(CancellationToken ct)
    {
        _activeModelDescriptor = GetActiveModelDescriptor();
        try
        {
            await LoadWeightsFromActiveDescriptorAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ShouldTryLoadFailureFallback(ex, _activeModelDescriptor))
        {
            var primary = _activeModelDescriptor;
            var fallback = _activeModelDescriptor.LoadFailureFallback;
            if (fallback is null)
                throw;

            _logger.LogWarning(
                ex,
                "Model {ModelId} failed to load; falling back to {FallbackId} (lighter GGUF).",
                primary.Id,
                fallback.Id);

            _weights?.Dispose();
            _weights = null;

            await _modelManager.EnsureModelAsync(fallback, null, ct).ConfigureAwait(false);
            _activeModelDescriptor = fallback;
            await LoadWeightsFromActiveDescriptorAsync(ct).ConfigureAwait(false);

            ModelLoadFallbackApplied?.Invoke(
                this,
                new QwenModelFallbackEventArgs { Primary = primary, Fallback = fallback });
        }
    }

    private static bool ShouldTryLoadFailureFallback(Exception ex, ModelDescriptor descriptor)
    {
        if (descriptor.LoadFailureFallback is null)
            return false;
        if (ex is FileNotFoundException or DirectoryNotFoundException)
            return false;
        if (ex is OperationCanceledException)
            return false;
        if (ex is OutOfMemoryException)
            return true;
        if (ex.GetType().Name.Contains("LoadWeightsFailedException"))
            return true;

        for (var e = ex; e != null; e = e.InnerException)
        {
            if (e is OutOfMemoryException)
                return true;
            if (e.GetType().Name.Contains("LoadWeightsFailedException"))
                return true;
            var msg = e.Message;
            if (msg.Contains("cannot allocate", StringComparison.OrdinalIgnoreCase))
                return true;
            if (msg.Contains("out of memory", StringComparison.OrdinalIgnoreCase))
                return true;
            if (msg.Contains("GGML_ASSERT", StringComparison.OrdinalIgnoreCase))
                return true;
            if (msg.Contains("unknown model architecture", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Build <see cref="ModelParams"/> for <see cref="StatelessExecutor"/> so context creation matches
    /// <see cref="LLamaWeights.LoadFromFileAsync"/> (threads, GPU offload, context size).
    /// </summary>
    /// <exception cref="InvalidOperationException">When <see cref="ModelPath"/> is not set (model not loaded yet).</exception>
    public ModelParams CreateExecutorModelParams()
    {
        if (string.IsNullOrWhiteSpace(ModelPath))
            throw new InvalidOperationException("Qwen model is not loaded; call GetWeightsAsync first.");

        return new ModelParams(ModelPath)
        {
            ContextSize = 4096,
            GpuLayerCount = 0,
            Threads = GetInferenceThreadCount()
        };
    }

    private int GetInferenceThreadCount() =>
        _options.InferenceThreads > 0
            ? _options.InferenceThreads
            : Math.Max(2, Environment.ProcessorCount / 2);

    private async Task LoadWeightsFromActiveDescriptorAsync(CancellationToken ct)
    {
        if (_activeModelDescriptor.Id == ModelRegistry.Qwen35_9B.Id)
        {
            await _nativeUpdater.EnsureLatestNativeRuntimeAsync(ct).ConfigureAwait(false);
        }

        await _modelManager.EnsureModelAsync(_activeModelDescriptor, null, ct).ConfigureAwait(false);

        var modelDir = _modelManager.GetModelDirectory(_activeModelDescriptor.Id);
        ModelPath = Directory.Exists(modelDir)
            ? ResolvePrimaryGgufPath(modelDir, _activeModelDescriptor) ?? ""
            : "";

        if (string.IsNullOrEmpty(ModelPath) || !File.Exists(ModelPath))
        {
            var expected = TryGetGgufFileNameFromDownloadUrl(_activeModelDescriptor.DownloadUrl);
            var hint = expected is not null
                ? $" Expected file '{expected}' is missing (remove stale .gguf files in the model folder or use Settings → Models to repair)."
                : " Download the model from Settings → Models.";
            throw new FileNotFoundException(
                $"Qwen GGUF not found for '{_activeModelDescriptor.DisplayName}'.{hint}",
                string.IsNullOrWhiteSpace(ModelPath) ? Path.Combine(modelDir, "*.gguf") : ModelPath);
        }

        var parameters = new ModelParams(ModelPath)
        {
            ContextSize = 4096, // Increased to support longer texts, but limited by user text length in VM
            GpuLayerCount = 0,
            Threads = GetInferenceThreadCount()
        };

        try
        {
            _weights = await LLamaWeights.LoadFromFileAsync(parameters, ct, null).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            long sizeBytes = 0;
            try
            {
                if (File.Exists(ModelPath))
                    sizeBytes = new FileInfo(ModelPath).Length;
            }
            catch (IOException)
            {
                // best-effort for logging only
            }

            _logger.LogError(
                ex,
                "LLamaSharp failed to load GGUF at {ModelPath} (sizeBytes={SizeBytes}). " +
                "Unsupported or multimodal GGUFs often fail here; use a text instruct model matching this app registry.",
                ModelPath,
                sizeBytes);
            throw;
        }
    }

    /// <summary>
    /// Prefer the GGUF named in the descriptor URL; exclude projector blobs (e.g. mmproj).
    /// When the URL implies a specific filename, do not fall back to another .gguf (avoids loading wrong multimodal weights).
    /// </summary>
    internal static string? ResolvePrimaryGgufPath(string modelDir, ModelDescriptor descriptor)
    {
        var files = Directory.GetFiles(modelDir, "*.gguf", SearchOption.TopDirectoryOnly);
        var candidates = files
            .Where(f => !Path.GetFileName(f).Contains("mmproj", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (candidates.Count == 0)
            return null;

        var expected = TryGetGgufFileNameFromDownloadUrl(descriptor.DownloadUrl);
        if (expected is not null)
        {
            var match = candidates.FirstOrDefault(f =>
                string.Equals(Path.GetFileName(f), expected, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
                return match;
            return null;
        }

        return candidates
            .OrderByDescending(f => new FileInfo(f).Length)
            .First();
    }

    private static string? TryGetGgufFileNameFromDownloadUrl(string downloadUrl)
    {
        if (!downloadUrl.Contains(".gguf", StringComparison.OrdinalIgnoreCase))
            return null;
        try
        {
            var uri = new Uri(downloadUrl, UriKind.Absolute);
            var last = uri.Segments.LastOrDefault()?.TrimEnd('/');
            if (string.IsNullOrEmpty(last) || !last.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase))
                return null;
            return last;
        }
        catch (UriFormatException)
        {
            return null;
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
