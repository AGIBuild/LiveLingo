using LiveLingo.Core.Dictation;
using LiveLingo.Core.Drafting;
using LiveLingo.Desktop.Platform;

namespace LiveLingo.Desktop.Composer;

/// <summary>Speech input into a draft: opens the microphone and runs a dictation session until stopped.</summary>
public interface IVoiceInput : IAsyncDisposable
{
    /// <exception cref="LiveLingo.Core.Models.ModelNotReadyException">A speech model is not installed.</exception>
    /// <exception cref="InvalidOperationException">There is no input device or it could not be opened.</exception>
    Task StartAsync(DraftSession draft, IReadOnlyList<string> glossary, CancellationToken ct);

    /// <summary>Closes the microphone, settles what was said into the draft and releases the recognizers.</summary>
    Task StopAsync();
}

public sealed class MicrophoneVoiceInput(IDictationSessionFactory factory, IMicrophone microphone) : IVoiceInput
{
    private DictationSession? _session;

    public async Task StartAsync(DraftSession draft, IReadOnlyList<string> glossary, CancellationToken ct)
    {
        if (_session is not null)
            throw new InvalidOperationException("Already listening.");

        var session = await factory.CreateAsync(draft, glossary, ct).ConfigureAwait(false);
        session.Start();
        try
        {
            microphone.Start(session.Push);
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
        microphone.Stop();
        await session.DisposeAsync().ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);
}
