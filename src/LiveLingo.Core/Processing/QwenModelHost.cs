using LiveLingo.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LiveLingo.Core.Processing;

public enum ModelLoadState { Unloaded, Loading, Loaded }

public sealed class QwenModelHost : IDisposable, ILlmModelLoadCoordinator
{
    private readonly IModelManager _modelManager;
    private readonly ILlamaServerProcessManager _serverManager;
    private readonly CoreOptions _options;
    private readonly ILogger<QwenModelHost> _logger;
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private readonly Timer _idleTimer;

    private const int IdleTimeoutMs = 300_000; // 5 minutes

    private ModelDescriptor _activeModelDescriptor;

    public ModelLoadState State => _serverManager.State;
    public string ModelPath { get; private set; } = string.Empty;

    public ModelDescriptor ActiveModelDescriptor => _activeModelDescriptor;

    public event Action<ModelLoadState>? StateChanged;
    public event EventHandler<QwenModelFallbackEventArgs>? ModelLoadFallbackApplied;

    public QwenModelHost(
        IModelManager modelManager,
        ILlamaServerProcessManager serverManager,
        IOptions<CoreOptions> options,
        ILogger<QwenModelHost> logger)
    {
        _modelManager = modelManager;
        _serverManager = serverManager;
        _options = options.Value;
        _logger = logger;
        _idleTimer = new Timer(OnIdleTimeout, null, Timeout.Infinite, Timeout.Infinite);

        _activeModelDescriptor = GetActiveModelDescriptor();
        
        _serverManager.StateChanged += s => StateChanged?.Invoke(s);
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
            await _serverManager.StopServerAsync();
            _activeModelDescriptor = GetActiveModelDescriptor();
            ModelPath = string.Empty;
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

    /// <summary>
    /// Returns the base URL (e.g. "http://127.0.0.1:8080") of the running llama-server.
    /// </summary>
    public async Task<string> GetOrStartServerAsync(CancellationToken ct)
    {
        ResetIdleTimer();

        if (_serverManager.State == ModelLoadState.Loaded && !string.IsNullOrEmpty(_serverManager.CurrentEndpointUrl))
            return _serverManager.CurrentEndpointUrl;

        await _loadLock.WaitAsync(ct);
        try
        {
            if (_serverManager.State == ModelLoadState.Loaded && !string.IsNullOrEmpty(_serverManager.CurrentEndpointUrl))
                return _serverManager.CurrentEndpointUrl;

            try
            {
                await LoadPrimaryOrFallbackAsync(ct).ConfigureAwait(false);
                _logger.LogDebug("Qwen model loaded via server: {ModelId}", _activeModelDescriptor.Id);
                return _serverManager.CurrentEndpointUrl!;
            }
            catch
            {
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
            await LoadModelFromActiveDescriptorAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ShouldTryLoadFailureFallback(ex, _activeModelDescriptor))
        {
            var primary = _activeModelDescriptor;
            var fallback = _activeModelDescriptor.LoadFailureFallback;
            if (fallback is null)
                throw;

            _logger.LogWarning(
                ex,
                "Model {ModelId} failed to load via server; falling back to {FallbackId} (lighter GGUF).",
                primary.Id,
                fallback.Id);

            await _serverManager.StopServerAsync();

            await _modelManager.EnsureModelAsync(fallback, null, ct).ConfigureAwait(false);
            _activeModelDescriptor = fallback;
            await LoadModelFromActiveDescriptorAsync(ct).ConfigureAwait(false);

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
        
        for (var e = ex; e != null; e = e.InnerException)
        {
            if (e is TimeoutException)
                return true;
            if (e is InvalidOperationException && e.Message.Contains("exited prematurely"))
                return true;
        }

        return false;
    }

    private int GetInferenceThreadCount() =>
        _options.InferenceThreads > 0
            ? _options.InferenceThreads
            : Math.Max(2, Environment.ProcessorCount / 2);

    private async Task LoadModelFromActiveDescriptorAsync(CancellationToken ct)
    {
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

        try
        {
            await _serverManager.EnsureServerRunningAsync(ModelPath, 4096, GetInferenceThreadCount(), ct).ConfigureAwait(false);
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
                "llama-server failed to load GGUF at {ModelPath} (sizeBytes={SizeBytes}). " +
                "Unsupported or multimodal GGUFs often fail here; use a text instruct model matching this app registry.",
                ModelPath,
                sizeBytes);
            throw;
        }
    }

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

    private static string? TryGetGgufFileNameFromDownloadUrl(string url)
    {
        try
        {
            var uri = new Uri(url);
            var name = Path.GetFileName(uri.AbsolutePath);
            if (name.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase))
                return name;
        }
        catch
        {
            // fallback
        }
        return null;
    }

    private void OnIdleTimeout(object? state)
    {
        if (_serverManager.State != ModelLoadState.Loaded) return;
        _logger.LogInformation("Idle timeout reached ({TimeoutMs}ms). Unloading model to free memory.", IdleTimeoutMs);
        _ = _serverManager.StopServerAsync();
    }

    private void ResetIdleTimer()
    {
        _idleTimer.Change(IdleTimeoutMs, Timeout.Infinite);
    }

    public void Dispose()
    {
        _idleTimer.Dispose();
        _serverManager.Dispose();
        _loadLock.Dispose();
    }
}