namespace LiveLingo.Core.Speech;

public enum SpeechInputErrorCode
{
    None,
    PermissionDenied,
    ModelMissing,
    AlreadyRecording,
    NotRecording,
    AudioFormatInvalid,
    TranscriptionFailed,
    RecordingTimeout,
    Cancelled,
    PlatformNotSupported
}
