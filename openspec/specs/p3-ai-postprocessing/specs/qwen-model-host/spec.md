## ADDED Requirements

### Requirement: Lazy model loading
`QwenModelHost` SHALL load the Qwen GGUF model lazily on first use. Model directory resolution SHALL use `IModelManager.GetModelDirectory(ModelRegistry.Qwen25_15B.Id)` as the single source of truth.

#### Scenario: First call loads model from manager-resolved path
- **WHEN** `GetWeightsAsync` is called and no model is loaded
- **THEN** the host SHALL resolve the model directory via `IModelManager` and load `.gguf` from that directory

#### Scenario: Subsequent calls return cached instance
- **WHEN** `GetWeightsAsync` is called after the model is loaded
- **THEN** the cached `LLamaWeights` instance SHALL be returned without reloading

### Requirement: Automatic unloading after idle timeout
`QwenModelHost` SHALL automatically unload (dispose) the model weights after 5 minutes of inactivity. Each `GetWeightsAsync` call SHALL reset the idle timer.

#### Scenario: Unload after 5 minutes
- **WHEN** no `GetWeightsAsync` call occurs for 5 minutes
- **THEN** the model weights SHALL be disposed and memory released

#### Scenario: Activity resets timer
- **WHEN** `GetWeightsAsync` is called within the 5-minute window
- **THEN** the unload timer SHALL be reset to 5 minutes from the latest call

### Requirement: Thread-safe loading
Model loading SHALL remain protected by a semaphore to prevent duplicate concurrent load operations.

#### Scenario: Concurrent load requests
- **WHEN** two threads call `GetWeightsAsync` concurrently while model is unloaded
- **THEN** only one load operation SHALL execute and both callers SHALL receive the same loaded weights instance

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
