# Changelog

All notable changes to LiveLingo are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

The voice-input arc — replacing the legacy Whisper engine with a sherpa-onnx-based
pipeline, surfacing model routing in the UI, and reclaiming disk used by the retired
bundle. Plus the model-download globalization pass that makes downloads a true
process-wide task with shared progress and dedup across every UI surface.

### Added

- **Streaming STT segment-commit**: `ISpeechInputCoordinator` exposes
  `SegmentCommitted` (immutable, append-only) and `PartialPreview` (transient,
  replace-only). Each VAD-bounded utterance becomes its own segment, transcribed
  exactly once, with a 60s safeguard for users who never pause. The recognizer
  is serialized through a single `SemaphoreSlim` and previews skip themselves
  whenever a real commit is in flight, so accuracy is never starved by
  best-effort live previews.

### Fixed

- **Voice input no longer drops earlier sentences**: the previous design fed a
  10s sliding window into STT for partial transcriptions and then overwrote
  `OverlayViewModel.SourceText` with each new partial. Long utterances or
  speakers who continued past the window lost the earlier audio entirely, and
  `StopAndTranscribeAsync` finalized only the last 30s. The new segment-commit
  flow streams every committed VAD segment through `SegmentCommitted`, the
  overlay APPENDS to a per-session buffer, and `StopAndTranscribeAsync` drains
  the uncommitted tail through the same channel before returning the full
  concatenated transcript.
- **Cross-thread crash on voice stop**: `SpeechInputCoordinator.SetState`,
  `SegmentCommitted` and `PartialPreview` were synchronously firing on the STT
  background thread, which let `OverlayViewModel` mutate observable properties
  off the UI thread. The next `PropertyChanged` subscriber inside Avalonia
  (`OverlayWindow.UpdateMicIconState`) then crashed with
  `InvalidOperationException: The calling thread cannot access this object`.
  Every coordinator handler now routes through a single `MarshalToUi` helper
  that posts to the captured UI `SynchronizationContext`, matching the
  pre-existing `OnDownloadStateChanged` pattern. (ViewModel still owns no
  Avalonia.* dependency — only the BCL `SynchronizationContext`.)
- **Voice STT mis-recognized as the wrong language**: `ToggleVoiceInputAsync`
  unconditionally reset `SelectedVoiceLanguage = SelectedSourceLanguage` on
  every Start, silently wiping any explicit voice-language pick from a previous
  session, and a null pick was passed straight through to sherpa-onnx, which
  then ran internal language ID and routinely mis-detected short Chinese
  utterances as English / Japanese. The overlay now preserves an explicit pick
  (`SelectedVoiceLanguage ??= SelectedSourceLanguage`) and always passes a
  non-empty hint by walking the `voice → source pick → resolved source code`
  fallback chain, biasing Cohere Transcribe / SenseVoice toward the language
  the user is actually speaking.

### Added (continued)

- **Global model download visibility**: `IModelDownloadCoordinator` is now the
  sole UI-facing entry point for model downloads. `SetupWizardViewModel`,
  `SpeechInputCoordinator.EnsureSttModelAsync`, `OverlayViewModel` and the
  Settings → Models cards all subscribe to its `StateChanged` event, so a
  download started from any surface immediately reflects in every other one.
  Concurrent `StartAsync` calls for the same model collapse to the in-flight
  session — no more duplicate downloads when the wizard, settings and overlay
  observe the same model in parallel. The `OverlayViewModel` shows live STT
  download percentage and surfaces the download link / error state in lockstep
  with the coordinator.
- **API guidance**: `IModelManager.EnsureModelAsync` is annotated with
  `[EditorBrowsable(Advanced)]` and an XML doc that points new callers at the
  coordinator. Direct `EnsureModelAsync` calls remain valid for engine-internal
  on-demand model loads (where byte-level dedup is already provided by
  `InflightDownloadRegistry`); they are simply de-emphasized in IntelliSense.
- **STT engine**: sherpa-onnx **Cohere Transcribe 14-Lang int8** (~1.6 GB)
  bundle as the new default speech recognizer. Top of the Open ASR Leaderboard,
  built-in punctuation + ITN, single recognizer reused across requests after
  warm-up. Mapped to `SttRoutingMode.AccuracyFirst`.
- **STT engine**: sherpa-onnx **SenseVoice Small int8** (~228 MB) bundle as a
  compact CJK-tuned alternative (中 / 粤 / 英 / 日 / 韩, on-model language
  identification). Mapped to `SttRoutingMode.MultilingualFirst`.
- **Settings → Speech tab**: new tab between *Translation* and *Models*. Exposes
  `SttRoutingMode` (AccuracyFirst / StreamingFirst / MultilingualFirst), shows
  the resolved active model with name / size / install status, and offers a
  one-click jump to the *Models* tab when the active model is missing on disk.
- **Speech model routing helper**: new `SpeechModelRouting` static helper —
  single source of truth that maps `SttRoutingMode` (and an optional model-id
  override) to a concrete `ModelDescriptor`. Used by both
  `DefaultSpeechEngineSelector` (runtime) and `SettingsViewModel` (UI) so the
  values shown in settings always match what runs.
- **Engine base class**: `SherpaOfflineRecognizerEngineBase` consolidates
  recognizer lifecycle, PCM16 → float conversion, threading and disposal.
  Concrete engines (`SherpaCohereTranscribeEngine`,
  `SherpaSenseVoiceTranscribeEngine`) only declare the descriptor they serve
  and the model-specific bits of `OfflineRecognizerConfig`.
- **Settings tab enum**: `SettingsTab` enum replaces hardcoded tab indices
  across view-models, message senders and tests so inserting a tab no longer
  silently breaks navigation commands.
- **Obsolete model cleanup**: `ObsoleteModelRegistry` lists model IDs that are
  no longer shipped (currently `whisper-base`); `ObsoleteModelCleaner` deletes
  matching directories on first run after upgrade and logs the disk reclaimed.
  Wired in as a fire-and-forget startup task in `App.axaml.cs`.
- **Build target**: Nuke `ProbeStt` target performs an end-to-end sherpa-onnx
  STT validation (download → extract → transcribe a real wav → assert
  expected substring). Configurable through `--probe-wav-path`,
  `--probe-stt-lang`, `--probe-stt-expected`. Falls back to the model's bundled
  `test_wavs/en.wav` when no wav path is supplied.
- **Localization**: 14 new keys for the Speech tab and routing-mode hints, in
  both `en-US.json` and `zh-CN.json`. Hint text reflects the actual model each
  routing mode resolves to.

### Changed

- **STT pipeline**: switched off the legacy Whisper-based engine entirely; no
  backwards-compat shim, no two-engine coexistence at the runtime layer (the
  selector now routes between sherpa-onnx engines only).
- **`ModelRegistry`**: `SpeechToTextModels` now contains both Cohere Transcribe
  and SenseVoice Small. `OptionalModels` and `AllModels` updated accordingly.
- **`SttRoutingMode` mapping**: previously every mode resolved to Cohere;
  `MultilingualFirst` now routes to SenseVoice. `StreamingFirst` is reserved
  for an upcoming streaming Zipformer bundle and currently falls back to
  Cohere so users always get a usable engine.
- **`SettingsViewModel`**: split into nine single-purpose collaborators (no
  behaviour change); now hooks `SpeechSettings` change-tracking and exposes
  `ActiveSttModel*` properties driven by `SpeechModelRouting`.
- **`ModelManager`**: split into single-purpose download and install
  collaborators; `IModelManager` gains `CleanObsoleteModelsAsync`.
- **`TextSegmenter`**: split into single-purpose segmentation collaborators;
  no public API change.
- **Build**: Nuke transitive dependencies pinned to clear `NU1901` low-severity
  advisories (`NuGet.Packaging`, `System.Security.Cryptography.Xml`).
- **`SetupWizardViewModel` / `SpeechInputCoordinator` / `OverlayViewModel`**:
  no longer call `IModelManager.EnsureModelAsync` directly — they all delegate
  to `IModelDownloadCoordinator.StartAsync` and reflect progress / success /
  failure from `StateChanged`. `SetupWizardViewModel` now implements
  `IDisposable` so its coordinator subscription is released when the wizard
  window closes.
- **`ISpeechInputCoordinator` event surface**: removed `PartialTranscription`
  (single-shot, overwrite-style). Subscribers must migrate to
  `SegmentCommitted` (append) and `PartialPreview` (replace). The contract for
  `StopAndTranscribeAsync` is unchanged for callers using `result.Text` — it
  now returns the concatenated transcript of the entire session, kept as a
  one-shot fallback for any caller that doesn't subscribe to the streaming
  events.

### Removed

- **Legacy Whisper STT bundle**: `whisper-base` is no longer registered, no
  longer downloaded, and is actively swept off disk on startup via the new
  `ObsoleteModelCleaner`.

### Tests

- New unit tests: `SpeechModelRoutingTests`,
  `DefaultSpeechEngineSelectorTests`, `ObsoleteModelCleanerTests`,
  `ObsoleteModelRegistryTests`.
- New probe test: `SherpaSttProbeTests` — driven by the Nuke `ProbeStt` target,
  verifies the full download → extract → recognize path on a real machine.
- `SettingsViewModelTests` extended for the Speech tab, `SttRoutingMode`
  options, active-STT-model display and installation status.
- `SetupWizardViewModelTests` rewritten against `IModelDownloadCoordinator`
  (success / cancellation / failure / HF-auth recovery / dispose paths).
- `OverlayViewModelTests` gains coordinator-subscription scenarios (downloading
  STT progress, installed clears the link, failed exposes the link, non-STT
  events ignored, post-`Dispose` events are no-ops).
- `OverlayVoiceInputTests` and `SpeechInputCoordinatorTests` rewritten around
  the new event contract: append-not-replace across multiple committed
  segments, preview replaced by commit, new-recording resets the per-session
  buffer, tail shorter than `MinSegmentSeconds` is skipped, and the empty
  preview is fired before idle.
- `OverlayVoiceInputTests` adds three regressions for the cross-thread / wrong
  language fixes: background-thread coordinator events post through a captured
  `SynchronizationContext` (asserted via a tracking sync context),
  `SelectedVoiceLanguage` is preserved across Start cycles, and STT receives a
  non-null source-language hint even when no voice language is explicitly
  picked.

## [0.1.4] - prior

See `git log v0.1.4` for the pre-CHANGELOG history.

[Unreleased]: https://github.com/user/LiveLingo/compare/v0.1.4...HEAD
[0.1.4]: https://github.com/user/LiveLingo/releases/tag/v0.1.4
