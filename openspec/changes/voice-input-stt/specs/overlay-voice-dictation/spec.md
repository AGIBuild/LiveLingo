## ADDED Requirements

### Requirement: Overlay shall provide voice input controls
The overlay UI SHALL provide a dedicated voice input control to start and stop recording, and SHALL expose explicit states: `Idle`, `Recording`, `Transcribing`, and `Error`.

#### Scenario: User records and stops normally
- **WHEN** user starts recording from overlay and then stops recording
- **THEN** UI state SHALL transition `Idle -> Recording -> Transcribing -> Idle` after successful transcription

#### Scenario: User clicks start repeatedly
- **WHEN** user triggers "start recording" while already in `Recording` or `Transcribing`
- **THEN** overlay SHALL keep current state stable and show a recoverable busy/already-recording message

### Requirement: Transcription result must feed existing translation flow
The transcribed text SHALL be written into `SourceText` in `OverlayViewModel`, reusing existing debounce/cancel translation behavior.

#### Scenario: Voice transcription updates source text
- **WHEN** STT returns `"hello team"` from recorded audio
- **THEN** `SourceText` SHALL be set to `"hello team"` and existing translation pipeline SHALL be triggered

### Requirement: Voice failure must not break manual input
Any voice-related failure SHALL keep manual text input fully available and SHALL NOT clear existing `SourceText` automatically.

#### Scenario: Transcription fails
- **WHEN** STT raises an error during transcription
- **THEN** overlay SHALL show a user-facing error status and retain previous input text for manual editing

### Requirement: Model missing feedback must be actionable
When STT model is missing, overlay SHALL provide actionable guidance to download/install the model and allow retry after completion.

#### Scenario: Model missing on first voice use
- **WHEN** user starts voice input and STT model is not installed
- **THEN** overlay SHALL show model-missing guidance with an explicit download action, and SHALL remain usable for manual text input

#### Scenario: Retry after model download
- **WHEN** model download completes successfully from overlay guidance
- **THEN** user SHALL be able to retry voice input in the same overlay session

### Requirement: Voice and translation statuses must be independently visible
Voice dictation status and translation pipeline status SHALL be presented without overwriting each other.

#### Scenario: Dictation and translation both active in sequence
- **WHEN** voice dictation transitions to transcribing and then updates `SourceText`
- **THEN** user SHALL still see clear voice status progression and subsequent translation status updates

### Requirement: Permission denial must produce guided feedback
When microphone permission is not granted, overlay SHALL show a permission guidance message and SHALL not enter recording state.

#### Scenario: Microphone permission denied
- **WHEN** user taps voice button and permission state is `Denied`
- **THEN** the system SHALL present guidance for granting permission and remain in `Idle` state
