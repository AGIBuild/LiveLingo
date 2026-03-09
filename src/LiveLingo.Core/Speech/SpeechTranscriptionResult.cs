namespace LiveLingo.Core.Speech;

public record SpeechTranscriptionResult(
    string Text,
    string Language,
    float Confidence);
