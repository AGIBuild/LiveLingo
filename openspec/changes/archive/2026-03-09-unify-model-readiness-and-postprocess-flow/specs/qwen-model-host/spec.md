## MODIFIED Requirements

### Requirement: Lazy model loading
`QwenModelHost` SHALL load the Qwen GGUF model lazily on first use. Model directory resolution SHALL use `IModelManager.GetModelDirectory(ModelRegistry.Qwen25_15B.Id)` as the single source of truth.

#### Scenario: First call loads model from manager-resolved path
- **WHEN** `GetWeightsAsync` is called and no model is loaded
- **THEN** the host SHALL resolve the model directory via `IModelManager` and load `.gguf` from that directory

#### Scenario: Subsequent calls return cached instance
- **WHEN** `GetWeightsAsync` is called after the model is loaded
- **THEN** the cached `LLamaWeights` instance SHALL be returned without reloading

### Requirement: Thread-safe loading
Model loading SHALL remain protected by a semaphore to prevent duplicate concurrent load operations.

#### Scenario: Concurrent load requests
- **WHEN** two threads call `GetWeightsAsync` concurrently while model is unloaded
- **THEN** only one load operation SHALL execute and both callers SHALL receive the same loaded weights instance
