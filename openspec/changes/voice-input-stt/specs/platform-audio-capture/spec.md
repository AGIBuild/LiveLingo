## ADDED Requirements

### Requirement: Platform audio capture abstraction
The system SHALL define a platform-level audio capture interface that encapsulates microphone recording start/stop operations and returns captured audio payload.

```csharp
public interface IAudioCaptureService : IDisposable
{
    bool IsRecording { get; }
    Task StartAsync(CancellationToken ct = default);
    Task<AudioCaptureResult> StopAsync(CancellationToken ct = default);
    Task<MicrophonePermissionState> GetPermissionStateAsync(CancellationToken ct = default);
}
```

#### Scenario: Start and stop recording successfully
- **WHEN** `StartAsync()` is called and permission is granted
- **THEN** `IsRecording` SHALL become `true`, and `StopAsync()` SHALL return non-empty audio data

### Requirement: Permission state must be explicit
The audio capture service SHALL expose microphone permission status to callers before recording starts.

#### Scenario: Permission denied
- **WHEN** `GetPermissionStateAsync()` returns `Denied`
- **THEN** recording SHALL NOT start and caller SHALL receive a recoverable permission-related error

### Requirement: Captured audio format must be normalized
Captured audio SHALL be normalized to a STT-friendly format before returning from `StopAsync` (mono, 16kHz PCM-compatible payload).

#### Scenario: Platform-specific capture backend
- **WHEN** audio is captured on Windows or macOS
- **THEN** the returned payload SHALL satisfy the normalized format contract required by STT engine

### Requirement: Recording session must be single-active and explicit
The audio capture service SHALL allow at most one active recording session per service instance and SHALL return recoverable errors for invalid transitions.

#### Scenario: Start called twice while already recording
- **WHEN** `StartAsync()` is called while `IsRecording` is already `true`
- **THEN** the service SHALL reject the call with a recoverable "already recording" error and keep session stable

#### Scenario: Stop called before start
- **WHEN** `StopAsync()` is called while `IsRecording` is `false`
- **THEN** the service SHALL return a recoverable "not recording" error

### Requirement: Ongoing recording must be cancellable on teardown
When overlay/session is disposed or cancelled, an ongoing recording SHALL stop promptly and release native resources.

#### Scenario: Overlay closes during recording
- **WHEN** view model teardown triggers cancellation while recording is active
- **THEN** capture service SHALL stop recording and release microphone handles without crashing
