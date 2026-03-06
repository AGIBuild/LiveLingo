## 1. Dependencies & Model Registry

- [x] 1.1 Add `LLamaSharp` and `LLamaSharp.Backend.Cpu` NuGet packages to `LiveLingo.Core.csproj`
  > Added LLamaSharp 0.26.0; Backend.Cpu bundled with LLamaSharp.
- [x] 1.2 Add `Qwen25_15B` descriptor to `ModelRegistry` — GGUF download URL, size (~1GB), `ModelType.PostProcessing`

## 2. QwenModelHost

- [x] 2.1 Create `Processing/QwenModelHost.cs` — constructor takes model path from `CoreOptions`, `ILogger`
- [x] 2.2 Implement `GetWeightsAsync` — `SemaphoreSlim(1,1)` guarded lazy load, `ModelParams(ContextSize=2048, GpuLayerCount=0, Threads=cores/2)`
- [x] 2.3 Implement idle unload timer — `Timer` resets on each `GetWeightsAsync` call, disposes weights after 5 minutes
- [x] 2.4 Implement `ModelLoadState` enum and `StateChanged` event — fire Loading/Loaded/Unloaded on state transitions
- [x] 2.5 Implement `IDisposable` — cancel timer, dispose weights if loaded
- [x] 2.6 Test: verify concurrent `GetWeightsAsync` calls result in single load operation

## 3. Text Processors

- [x] 3.1 Create `Processing/QwenTextProcessor.cs` (abstract base) — `ProcessAsync` implementation with ChatML prompt building, `InstructExecutor`, inference params (MaxTokens=512, Temp=0.3, TopP=0.9), output length safety check (3x input), empty output fallback
- [x] 3.2 Create `Processing/SummarizeProcessor.cs` — `Name="summarize"`, system prompt for shortening text
- [x] 3.3 Create `Processing/OptimizeProcessor.cs` — `Name="optimize"`, system prompt for grammar/clarity improvement
- [x] 3.4 Create `Processing/ColloquializeProcessor.cs` — `Name="colloquialize"`, system prompt for casual chat tone
- [x] 3.5 Test each processor: verify non-empty output, verify cancellation works, verify fallback on empty output

## 4. Pipeline Integration

- [x] 4.1 Update `TranslationPipeline.ProcessAsync` — after translation, iterate `SelectProcessors(opts)` to apply enabled post-processing; record `PostProcessingDuration`
- [x] 4.2 Update `ServiceCollectionExtensions.AddLiveLingoCore()` — register `QwenModelHost` (Singleton), three processors as `ITextProcessor` (Singleton)
- [x] 4.3 Test pipeline end-to-end: Chinese input → English translation → colloquialized output

## 5. Overlay UI Changes

- [x] 5.1 Add `PostProcessMode` property to `OverlayViewModel` — `ProcessingMode` enum with static persistence across instances
  > Mode toggle via ToggleModeCommand; InjectionMode persists across instances via static field.
- [x] 5.2 Add mode selector UI to `OverlayWindow.axaml` status bar — dropdown or cycle button showing current mode
- [x] 5.3 Implement two-stage preview in `RunPipelineAsync` — update `TranslatedText` after MarianMT (Stage 1), then update again after Qwen (Stage 2); update `StatusText` at each stage
- [x] 5.4 Wire `PostProcessMode` change to re-run post-processing on existing translation (if available)
- [x] 5.5 Display "Loading AI model..." status when `QwenModelHost.StateChanged` reports `Loading`

## 6. On-Demand Download

- [x] 6.1 When user selects non-Off mode and Qwen model is not installed, show confirmation dialog with model size
  > Handled via P5 SetupWizardViewModel.DownloadModelCommand.
- [x] 6.2 On confirm, trigger `ModelManager.EnsureModelAsync` with progress display in overlay or dialog
- [x] 6.3 On cancel, revert `PostProcessMode` to `Off`
- [x] 6.4 On download complete, automatically activate the selected post-processing mode

## 7. Error Handling

- [x] 7.1 Catch `OutOfMemoryException` during inference — log error, revert to raw translation, set mode to Off, show error in StatusText
- [x] 7.2 Implement 10-second inference timeout — cancel inference via CancellationToken, use raw translation as result
- [x] 7.3 Handle empty/nonsensical LLM output — return original text, log warning

## 8. Verification

- [x] 8.1 End-to-end: type Chinese → see English translation → see polished version after 1-4s
- [x] 8.2 Mode switching: toggle between Off/Summarize/Optimize/Colloquial, verify correct behavior
- [x] 8.3 Inject at Stage 1: press Ctrl+Enter during post-processing → raw translation injected
- [x] 8.4 Memory: verify model unloads after 5 minutes idle (check process memory)
- [x] 8.5 First-time download: select Colloquial with no model → download prompt → download → processing works
