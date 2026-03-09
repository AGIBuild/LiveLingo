namespace LiveLingo.Core.Speech;

public record SpeechInputResult(
    bool Success,
    string? Text,
    SpeechInputErrorCode ErrorCode,
    string? ErrorMessage = null);
