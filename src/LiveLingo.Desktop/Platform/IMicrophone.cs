namespace LiveLingo.Desktop.Platform;

/// <summary>Captures the default input device as 16 kHz mono float frames.</summary>
public interface IMicrophone : IDisposable
{
    bool IsRecording { get; }

    /// <summary>
    /// Starts capturing and returns the device name. <paramref name="onFrame"/> runs on the audio thread: it must
    /// return quickly and never block.
    /// </summary>
    /// <exception cref="InvalidOperationException">There is no input device, or capture is already running.</exception>
    string Start(Action<float[]> onFrame);

    void Stop();
}

public enum MicrophoneAccess
{
    /// <summary>The system has not asked yet; the first capture triggers the prompt.</summary>
    NotDetermined,
    Granted,
    Denied
}

public interface IMicrophonePermission
{
    MicrophoneAccess Status { get; }

    /// <summary>Opens the system page where the user can allow microphone access.</summary>
    void OpenSettings();
}
