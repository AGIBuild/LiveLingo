## Context

P1 established the layered architecture with interfaces and stub implementations. Users see `[EN] xxx` placeholder text. P2 replaces stubs with real AI translation: MarianMT for machine translation, FastText for language detection, and a ModelManager for downloading and caching model files. This is the first milestone where the product delivers actual value.

Reference: docs/proposals/specs/P2-translation-core-spec.md contains detailed implementation blueprints.

## Goals / Non-Goals

**Goals:**
- Real-time translation of Chinese → English using MarianMT ONNX models
- Automatic language detection with FastText lid.176.ftz
- Robust model download pipeline (resume, progress, validation)
- Cancel-and-restart translation for real-time responsiveness
- Basic first-run download UI for required models

**Non-Goals:**
- Qwen/LLM post-processing (P3)
- Multiple language pair management UI (P5)
- macOS platform support (P4)
- Beam search optimization (v2 scope)

## Decisions

### D1: ONNX Runtime for MarianMT inference

**Decision**: Use `Microsoft.ML.OnnxRuntime` for MarianMT model inference.

**Alternatives considered**:
- **CTranslate2**: Optimized for translation, faster inference. Rejected: no mature .NET bindings, would require C++ interop.
- **Torch.NET / TorchSharp**: PyTorch port. Rejected: heavy dependency (~500MB), overkill for inference-only use case.
- **Custom ONNX parser**: Rejected: massive effort, no benefit over existing runtime.

ONNX Runtime is the standard .NET inference runtime, well-maintained, supports CPU/GPU, ~50MB native libs.

### D2: SentencePiece tokenizer strategy

**Decision**: Evaluate `SentencePieceSharp` NuGet first. If unavailable or broken, build minimal P/Invoke bindings to native `sentencepiece` library.

**Rationale**: MarianMT models require SentencePiece tokenization. There's no built-in .NET tokenizer. SentencePieceSharp is the simplest path if it works. Fallback to native bindings gives full control.

### D3: Greedy decoding (beam_size=1) as initial strategy

**Decision**: Start with greedy search for autoregressive decoding instead of full beam search.

**Rationale**: Greedy is simpler to implement, has no quality difference for short sentences (<50 tokens), and can be upgraded to beam search later without changing interfaces. MarianMT models produce good results with greedy for conversational text.

### D4: FastText for language detection with script-based fallback

**Decision**: Primary detector is FastText lid.176.ftz (~1MB, 176 languages). Fallback is Unicode script analysis if model is not available.

**Alternatives**:
- **CLD3 (Compact Language Detector)**: Google's library. No .NET bindings readily available.
- **Lingua**: .NET library but adds large dependency, slower than FastText.

FastText is the best balance of size, speed, and accuracy.

### D5: HttpClient streaming download with Range header resume

**Decision**: Download models using `HttpClient` with `ResponseHeadersRead` for streaming, `Range` header for resume, `.part` files for partial downloads.

**Rationale**: Models are 1-30MB. Streaming avoids memory bloat. Resume avoids re-downloading on flaky connections. `.part` files are atomic (renamed on completion).

### D6: Model session lazy loading and caching

**Decision**: ONNX `InferenceSession` instances are loaded lazily on first translation request for each language pair and cached in a `ConcurrentDictionary<string, ModelSession>`. Sessions are disposed when the engine is disposed.

**Rationale**: Loading a model takes ~500ms-1s. Eager loading all pairs would delay startup. Lazy loading means users only pay the cost for pairs they actually use.

## Dependency Graph

```
ModelManager ──downloads──▶ HuggingFace (HTTP)
ModelManager ──writes──▶ {LocalAppData}/LiveLingo/models/

MarianOnnxEngine ──uses──▶ ModelManager (EnsureModel)
MarianOnnxEngine ──loads──▶ ONNX InferenceSession (encoder + decoder)
MarianOnnxEngine ──uses──▶ SentencePieceTokenizer (encode/decode)

FastTextDetector ──loads──▶ FastText model (lid.176.ftz)
ScriptBasedDetector ──fallback for──▶ FastTextDetector

TranslationPipeline ──orchestrates──▶ ILanguageDetector → ITranslationEngine
TranslationPipeline ──skips──▶ ITextProcessor[] (empty in P2)

AddLiveLingoCore() ──registers──▶ ModelManager, MarianOnnxEngine, FastTextDetector, TranslationPipeline
```

## Risks / Trade-offs

- **[Risk] SentencePiece .NET bindings**: No actively maintained NuGet package. → **Mitigation**: Evaluate SentencePieceSharp first; if broken, build minimal P/Invoke wrapper. Budget 1 extra day for this.
- **[Risk] HuggingFace model format variability**: Some MarianMT models have split encoder/decoder ONNX files, some have merged. → **Mitigation**: Standardize on the split format (encoder_model.onnx + decoder_model_merged.onnx) which is the default HuggingFace export. Document expected file structure in ModelRegistry.
- **[Risk] ONNX Runtime native library size**: Adds ~50MB to app distribution. → **Trade-off**: Acceptable for desktop app; users download once.
- **[Risk] First translation cold start**: Model loading takes ~500ms-1s. → **Mitigation**: Show "Loading model..." status in overlay. Subsequent translations are instant.
- **[Trade-off] Greedy vs beam search**: Greedy may produce slightly lower quality for long sentences. → Acceptable for chat messages (typically short). Upgrade path clear.
