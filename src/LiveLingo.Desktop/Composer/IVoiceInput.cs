using LiveLingo.Core.Dictation;
using LiveLingo.Core.Drafting;
using LiveLingo.Core.Models;
using LiveLingo.Desktop.Platform;

namespace LiveLingo.Desktop.Composer;

/// <summary>Speech input into a draft: opens the microphone and runs a dictation session until stopped.</summary>
public interface IVoiceInput : IAsyncDisposable
{
    /// <summary>
    /// True when the open microphone has delivered only silence for a while, false when sound returns.
    /// Raised on the audio thread.
    /// </summary>
    event Action<bool>? NoSignalChanged;

    /// <summary>True while the speech models are being loaded in the background, before anybody asked to dictate.</summary>
    bool IsPreparing { get; }

    /// <summary>Raised when <see cref="IsPreparing"/> changed, on whichever thread caused it.</summary>
    event Action<bool>? PreparingChanged;

    /// <exception cref="LiveLingo.Core.Models.ModelNotReadyException">A speech model is not installed.</exception>
    /// <exception cref="InvalidOperationException">There is no input device or it could not be opened.</exception>
    Task StartAsync(DraftSession draft, IReadOnlyList<string> glossary, CancellationToken ct);

    /// <summary>Closes the microphone, settles what was said into the draft and releases the recognizers.</summary>
    Task StopAsync();
}

public sealed class MicrophoneVoiceInput : IVoiceInput
{
    /// <summary>
    /// How long the microphone may deliver only silence before it is closed. A pause while thinking must not end the
    /// recording, so this is long; it also catches a microphone that is muted or dead.
    /// </summary>
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(5);

    private readonly IDictationSessionFactory _factory;
    private readonly IMicrophone _microphone;
    private readonly ISpeechWarmup _warmup;
    private DictationSession? _session;
    private SignalMonitor? _monitor;

    public MicrophoneVoiceInput(IDictationSessionFactory factory, IMicrophone microphone, ISpeechWarmup warmup)
    {
        _factory = factory;
        _microphone = microphone;
        _warmup = warmup;
        _warmup.StateChanged += OnResidencyChanged;
    }

    public event Action<bool>? NoSignalChanged;

    public event Action<bool>? PreparingChanged;

    public bool IsPreparing => _warmup.State == ModelResidency.Loading;

    private void OnResidencyChanged(ModelResidency state) => PreparingChanged?.Invoke(state == ModelResidency.Loading);

    public async Task StartAsync(DraftSession draft, IReadOnlyList<string> glossary, CancellationToken ct)
    {
        if (_session is not null)
            throw new InvalidOperationException("Already listening.");

        var session = await _factory.CreateAsync(draft, glossary, ct).ConfigureAwait(false);
        session.Start();
        var monitor = new SignalMonitor(IdleTimeout);
        _monitor = monitor;
        try
        {
            _microphone.Start(frame =>
            {
                if (monitor.Observe(frame) is { } silent)
                    NoSignalChanged?.Invoke(silent);
                session.Push(frame);
            });
        }
        catch
        {
            await session.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        _session = session;
    }

    public async Task StopAsync()
    {
        if (_session is not { } session)
            return;

        _session = null;
        // The microphone stops first: a frame pushed after the session stopped is an error.
        _microphone.Stop();
        await session.DisposeAsync().ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        _warmup.StateChanged -= OnResidencyChanged;
        await StopAsync().ConfigureAwait(false);
    }
}
