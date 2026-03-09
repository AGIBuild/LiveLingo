## ADDED Requirements

### Requirement: Offline speech-to-text abstraction
The system SHALL define a core speech-to-text interface that performs on-device transcription from captured audio bytes.

```csharp
public interface ISpeechToTextEngine : IDisposable
{
    Task<SpeechTranscriptionResult> TranscribeAsync(
        AudioCaptureResult audio,
        CancellationToken ct = default);
}

public record SpeechTranscriptionResult(
    string Text,
    string Language,
    float Confidence);
```

#### Scenario: Transcribe captured voice to text
- **WHEN** valid recorded audio is passed to `TranscribeAsync`
- **THEN** the engine SHALL return non-empty transcription text and detected language metadata

### Requirement: Speech contracts must be platform-agnostic
All audio/transcription contracts consumed by `ISpeechToTextEngine` SHALL be defined in Core abstraction layer (or shared contracts namespace), and SHALL NOT depend on platform-specific namespaces or types.

#### Scenario: Build with platform implementation swapped
- **WHEN** only a different platform capture implementation is changed
- **THEN** STT contracts and engine interface SHALL remain unchanged and reusable

### Requirement: Transcription must run fully on-device
The STT engine SHALL perform inference locally and SHALL NOT require network calls to external speech APIs.

#### Scenario: Offline environment
- **WHEN** network is unavailable
- **THEN** `TranscribeAsync` SHALL still function if local STT model files are installed

### Requirement: STT model is optional and on-demand
The STT model SHALL be managed as an optional model and downloaded only when user first uses voice input or explicitly installs it.

#### Scenario: Model missing when user starts voice input
- **WHEN** transcription is requested and STT model is not installed
- **THEN** the system SHALL provide a download prompt/guidance and SHALL NOT crash the overlay flow

#### Scenario: Retry after explicit model download
- **WHEN** user explicitly completes STT model download after a model-missing error
- **THEN** a new transcription request in current app session SHALL proceed without app restart

### Requirement: Cancellation and errors must be surfaced explicitly
The STT engine SHALL support cancellation and SHALL surface actionable errors for model missing, decode failure, and unsupported audio format.

#### Scenario: User cancels while transcribing
- **WHEN** cancellation token is triggered during `TranscribeAsync`
- **THEN** the method SHALL throw `OperationCanceledException` and release transient resources
