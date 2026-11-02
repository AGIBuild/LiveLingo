using Microsoft.Extensions.Logging;

namespace LiveLingo.Desktop.Services.Update;

/// <summary>
/// The one place that decides when the application looks for updates: once at start and then every interval, until a
/// version is downloaded; and whenever the user asks. Background failures (no network, a feed that is not there yet)
/// only reach the log; a check the user asked for reports them.
/// </summary>
public sealed class UpdateChecks : IDisposable
{
    private readonly IUpdateService _service;
    private readonly TimeSpan _interval;
    private readonly ILogger _logger;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _one = new(1, 1);
    private ITimer? _timer;

    public UpdateChecks(IUpdateService service, TimeSpan interval, ILogger logger, TimeProvider? time = null)
    {
        _service = service;
        _interval = interval;
        _logger = logger;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>A version was downloaded and is ready to install. Raised once, on a background thread.</summary>
    public event Action<string>? Ready;

    public string? ReadyVersion => _service.ReadyVersion;

    /// <summary>Checks now and then every interval until a version is ready.</summary>
    public void Start()
    {
        _timer = _time.CreateTimer(_ => _ = CheckInBackgroundAsync(), null, _interval, _interval);
        _ = CheckInBackgroundAsync();
    }

    /// <summary>A check the user asked for. True when a version is ready to install.</summary>
    public async Task<bool> CheckNowAsync(CancellationToken ct = default)
    {
        await _one.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_service.ReadyVersion is not null)
                return true;
            if (!await _service.CheckAndDownloadAsync(ct).ConfigureAwait(false))
                return false;

            _timer?.Dispose();
            Ready?.Invoke(_service.ReadyVersion!);
            return true;
        }
        finally
        {
            _one.Release();
        }
    }

    public void ApplyAndRestart() => _service.ApplyAndRestart();

    /// <summary>Installs the downloaded version silently after this process exited; nothing when there is none.</summary>
    public void ApplyOnExit()
    {
        if (_service.ReadyVersion is not null)
            _service.ApplyOnExit();
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _one.Dispose();
    }

    private async Task CheckInBackgroundAsync()
    {
        try
        {
            await CheckNowAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Background update check failed");
        }
    }
}
