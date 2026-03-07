## Context

P2 delivers real translation via MarianMT. Translations are accurate but formal/literal. For workplace chat (Slack/Teams), users need casual, natural-sounding text. P3 adds a local LLM (Qwen2.5-1.5B) as a post-processing step that can summarize, optimize grammar, or colloquialize the translated output.

Reference: docs/proposals/specs/P3-ai-postprocessing-spec.md contains full implementation blueprints.

## Goals / Non-Goals

**Goals:**
- Three post-processing modes (Summarize, Optimize, Colloquialize) powered by Qwen2.5-1.5B
- On-device inference only — no cloud API dependency
- Memory-efficient model lifecycle (lazy load, auto-unload after 5 min idle)
- Two-stage preview in overlay (raw translation → polished version)
- On-demand model download (~1GB) with user consent

**Non-Goals:**
- GPU acceleration (CPU-only in P3; GPU is v2 scope)
- Custom/user-defined prompts
- Streaming token display during LLM inference
- Fine-tuning or model training

## Decisions

### D1: Qwen2.5-1.5B via LLamaSharp

**Decision**: Use `LLamaSharp` (.NET binding for llama.cpp) to run Qwen2.5-1.5B-Instruct in GGUF Q4_K_M format.

**Alternatives considered**:
- **ONNX Runtime GenAI**: Microsoft's generative AI extension for ONNX Runtime. Rejected: less mature for chat models, Qwen ONNX export not straightforward.
- **Semantic Kernel**: Microsoft's AI orchestration. Rejected: overkill for single-model local inference, adds unnecessary abstraction.
- **Direct llama.cpp P/Invoke**: Full control. Rejected: LLamaSharp already provides well-tested bindings, no need to duplicate.

### D2: Shared QwenModelHost singleton

**Decision**: A single `QwenModelHost` singleton manages model lifecycle for all three processors. Processors receive it via constructor injection and call `GetWeightsAsync()` to obtain the shared weights.

**Rationale**: Loading model weights takes 3-5s and consumes ~2GB RAM. Sharing one instance across processors avoids triple memory usage and triple load time. The `SemaphoreSlim` in the host ensures thread-safe access.

### D3: InstructExecutor per inference call

**Decision**: Each `ProcessAsync` call creates a new `LLamaContext` from the shared weights and a new `InstructExecutor`. The context is disposed after each call.

**Rationale**: `LLamaContext` is not thread-safe. Creating a new context per call (~10ms overhead) is simpler than pooling and provides natural isolation. The weights themselves are read-only and safely shared.

### D4: Two-stage preview (no streaming)

**Decision**: Show raw translation immediately after MarianMT, then replace with polished text after Qwen completes. No token-by-token streaming.

**Rationale**: Streaming adds complexity (UI updates, partial text display) with limited benefit for short chat messages. Two-stage preview gives immediate feedback (raw translation usable instantly) while polish happens in background. User can Ctrl+Enter at any time to inject whatever is currently displayed.

### D5: 5-minute idle unload

**Decision**: Auto-dispose model weights after 5 minutes of no `GetWeightsAsync` calls, using a `Timer` that resets on each call.

**Rationale**: Qwen model consumes ~2GB RAM. Users may go minutes/hours between translation sessions. Auto-unload prevents persistent memory pressure. 5 minutes balances quick re-access (model stays loaded during active use) vs memory release (freed when user is done). Next use re-loads in 3-5s.

### D6: On-demand download with consent

**Decision**: Qwen model (~1GB) is NOT downloaded during first-run setup. Instead, download is triggered when user first selects a non-Off post-processing mode, with an explicit consent dialog.

**Rationale**: 1GB download is significant. Not all users need AI polish. Downloading only when needed respects user bandwidth and disk space. The consent dialog sets expectations about size and download time.

## DI Registration

```
QwenModelHost                    (Singleton) — shared model weights
SummarizeProcessor  : ITextProcessor  (Singleton) — injected with QwenModelHost
OptimizeProcessor   : ITextProcessor  (Singleton) — injected with QwenModelHost
ColloquializeProcessor : ITextProcessor (Singleton) — injected with QwenModelHost

TranslationPipeline receives IEnumerable<ITextProcessor> — selects by Name based on ProcessingOptions
```

## Risks / Trade-offs

- **[Risk] LLamaSharp API stability**: llama.cpp evolves rapidly; LLamaSharp API may break across versions. → **Mitigation**: Pin to specific LLamaSharp version. Abstract behind `ITextProcessor` so implementation can be swapped.
- **[Risk] CPU inference speed**: ~10 tok/s on CPU for 1.5B model. 50-word input → ~4s processing. → **Mitigation**: Two-stage preview ensures raw translation is immediately usable. Users see final result after brief wait.
- **[Risk] Memory pressure**: 2GB for Qwen + ~500MB for MarianMT sessions. Low-RAM systems may struggle. → **Mitigation**: Auto-unload timer frees Qwen memory. If OOM occurs, gracefully degrade to Off mode.
- **[Trade-off] Quality vs speed**: 1.5B model is small; quality is acceptable for casual text editing but not professional translation quality. → Acceptable for chat context. Larger models (7B) are v2 scope.
- **[Trade-off] CPU-only in P3**: No GPU acceleration. → Keeps dependency simple. GPU support (CUDA/Metal) can be added later by changing `GpuLayerCount` and backend package.
