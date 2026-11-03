using Microsoft.Extensions.Logging;

namespace LiveLingo.Desktop.Services.Update;

/// <summary>
/// The one place that decides when the application looks for updates — once at start and then every interval, until a
/// version is downloaded, and whenever the user asks — and the one place that knows how it is going. Every check, from
/// either source, reports through <see cref="State"/>; a failure never throws, it becomes the state (and the log).
/// </summary>
public sealed class UpdateChecks : IDisposable
{
    private readonly IUpdateService _service;
    private readonly TimeSpan _interval;
    private readonly ILogger _logger;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _one = new(1, 1);
    private ITimer? _timer;
    private UpdateState _state;

    public UpdateChecks(IUpdateService service, TimeSpan interval, ILogger logger, TimeProvider? time = null)
    {
        _service = service;
        _interval = interval;
        _logger = logger;
        _time = time ?? TimeProvider.System;
        _state = new UpdateState(service.CanUpdate ? UpdatePhase.Idle : UpdatePhase.Unsupported);
    }

    /// <summary>A version was downloaded and is ready to install. Raised once, on a background thread.</summary>
    public event Action<string>? Ready;

    /// <summary><see cref="State"/> changed. Raised on a background thread.</summary>
    public event Action? StateChanged;

    public UpdateState State => Volatile.Read(ref _state);

    public string? ReadyVersion => _service.ReadyVersion;

    public string? DownloadPage => _service.DownloadPage;

    /// <summary>Checks now and then every interval until a version is ready.</summary>
    public void Start()
    {
        if (!_service.CanUpdate)
        {
            _logger.LogInformation("Not checking for updates: the application was not installed by the installer");
            return;
        }

        _timer = _time.CreateTimer(_ => _ = CheckNowAsync(), null, _interval, _interval);
        _ = CheckNowAsync();
    }

    /// <summary>
    /// Looks for a newer version and downloads it. Does nothing while a check is already running (it answers for both)
    /// or when this copy cannot update itself.
    /// </summary>
    public async Task CheckNowAsync(CancellationToken ct = default)
    {
        if (!_service.CanUpdate || _service.ReadyVersion is not null || !_one.Wait(0, CancellationToken.None))
            return;

        try
        {
            Set(new UpdateState(UpdatePhase.Checking));
            var version = await _service.FindAsync(ct).ConfigureAwait(false);
            if (version is null)
            {
                Set(new UpdateState(UpdatePhase.UpToDate, At: _time.GetUtcNow()));
                return;
            }

            Set(new UpdateState(UpdatePhase.Downloading, version));
            await _service.DownloadAsync(percent => SetPercent(version, percent), ct).ConfigureAwait(false);

            _timer?.Dispose();
            Set(new UpdateState(UpdatePhase.Ready, version));
            Ready?.Invoke(version);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            Set(new UpdateState(UpdatePhase.Idle));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "The update check failed");
            Set(new UpdateState(UpdatePhase.Failed, At: _time.GetUtcNow(), Error: ex.Message));
        }
        finally
        {
            _one.Release();
        }
    }

    public void ApplyAndRestart()
    {
        if (_service.ReadyVersion is not null)
            _service.ApplyAndRestart();
    }

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

    private void SetPercent(string version, int percent)
    {
        var clamped = Math.Clamp(percent, 0, 100);
        if (State is { Phase: UpdatePhase.Downloading } current && current.Percent != clamped)
            Set(current with { Version = version, Percent = clamped });
    }

    private void Set(UpdateState state)
    {
        Volatile.Write(ref _state, state);
        StateChanged?.Invoke();
    }
}

