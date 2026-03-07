## ADDED Requirements

### Requirement: Lazy model loading
`QwenModelHost` SHALL load the Qwen GGUF model lazily on the first `GetWeightsAsync` call. Loading SHALL occur on a background thread to avoid blocking the UI.

#### Scenario: First call loads model
- **WHEN** `GetWeightsAsync` is called and no model is loaded
- **THEN** the model SHALL be loaded from `{ModelStoragePath}/qwen2.5-1.5b-q4/qwen2.5-1.5b-instruct-q4_k_m.gguf` and returned

#### Scenario: Subsequent calls return cached instance
- **WHEN** `GetWeightsAsync` is called after the model is already loaded
- **THEN** the cached `LLamaWeights` instance SHALL be returned immediately without reloading

### Requirement: Automatic unloading after idle timeout
`QwenModelHost` SHALL automatically unload (dispose) the model weights after 5 minutes of inactivity. Each `GetWeightsAsync` call SHALL reset the idle timer.

#### Scenario: Unload after 5 minutes
- **WHEN** no `GetWeightsAsync` call occurs for 5 minutes
- **THEN** the model weights SHALL be disposed and memory released

#### Scenario: Activity resets timer
- **WHEN** `GetWeightsAsync` is called within the 5-minute window
- **THEN** the unload timer SHALL be reset to 5 minutes from the latest call

### Requirement: Thread-safe loading
Model loading SHALL be protected by a `SemaphoreSlim(1,1)` to prevent concurrent load attempts. Only one thread SHALL load the model; others SHALL await the same load operation.

#### Scenario: Concurrent load requests
- **WHEN** two threads call `GetWeightsAsync` simultaneously while model is not loaded
- **THEN** only one load operation SHALL occur; both threads SHALL receive the same `LLamaWeights` instance

### Requirement: Model load state notifications
`QwenModelHost` SHALL expose a `StateChanged` event reporting `ModelLoadState` (Unloaded, Loading, Loaded). The overlay ViewModel SHALL use this to display "Loading AI model..." status.

#### Scenario: State transitions during load
- **WHEN** `GetWeightsAsync` triggers a model load
- **THEN** `StateChanged` SHALL fire with `Loading`, then `Loaded` upon completion

### Requirement: LLamaSharp model parameters
Model loading SHALL use `ModelParams` with: `ContextSize = 2048`, `GpuLayerCount = 0` (CPU only), `Threads = Environment.ProcessorCount / 2`.

#### Scenario: CPU-only inference
- **WHEN** the model is loaded
- **THEN** all inference SHALL run on CPU threads with no GPU acceleration attempted

### Requirement: Graceful disposal
`QwenModelHost.Dispose()` SHALL cancel the unload timer and dispose the loaded weights if present.

#### Scenario: Dispose while model loaded
- **WHEN** `Dispose()` is called while model is loaded
- **THEN** the model weights SHALL be disposed and `StateChanged` SHALL fire with `Unloaded`
