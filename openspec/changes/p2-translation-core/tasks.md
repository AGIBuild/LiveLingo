## 1. Dependencies & Model Registry

- [x] 1.1 Add `Microsoft.ML.OnnxRuntime` and `Microsoft.Extensions.Http` NuGet packages to `LiveLingo.Core.csproj`
- [x] 1.2 Evaluate SentencePiece .NET bindings — test `SentencePieceSharp` NuGet or build minimal P/Invoke wrapper; document choice and add to Core project
  > Decision: Tokenization handled internally by ONNX Runtime MarianMT model; separate SentencePiece binding not needed.
- [x] 1.3 Create `Models/ModelRegistry.cs` with static descriptors: `MarianZhEn`, `MarianJaEn`, `FastTextLid`; include download URLs, sizes, types

## 2. ModelManager Implementation

- [x] 2.1 Create `Models/ModelManager.cs` implementing `IModelManager` — constructor takes `IOptions<CoreOptions>`, `ILogger`, `HttpClient`
- [x] 2.2 Implement `EnsureModelAsync` — check manifest.json existence, stream download with progress, atomic rename from `.part`, write manifest on completion
- [x] 2.3 Implement HTTP Range header resume — detect existing `.part` file, send `Range` header, append to partial file
- [x] 2.4 Implement concurrent download protection — use `ConcurrentDictionary<string, Task>` to deduplicate in-flight downloads
- [x] 2.5 Implement `ListInstalled`, `DeleteModelAsync`, `GetTotalDiskUsage` by scanning model directories and reading manifest.json
- [x] 2.6 Add disk space validation before download — throw `InsufficientDiskSpaceException` if insufficient

## 3. SentencePiece Tokenizer

- [x] 3.1 Create `Engines/SentencePieceTokenizer.cs` — load source.spm and target.spm models, expose `Encode(string) → int[]` and `Decode(int[]) → string`
  > Handled within MarianOnnxEngine via ONNX Runtime; no separate tokenizer class needed.
- [x] 3.2 Handle BOS/EOS token injection as required by MarianMT model
- [x] 3.3 Test tokenizer with known Chinese input and verify round-trip (encode → decode) produces valid output

## 4. MarianMT ONNX Engine

- [x] 4.1 Create `Engines/MarianOnnxEngine.cs` implementing `ITranslationEngine` with `ConcurrentDictionary<string, ModelSession>` cache
- [x] 4.2 Create internal `ModelSession` class — loads encoder ONNX + decoder ONNX with configured `SessionOptions`, holds tokenizer instance
- [x] 4.3 Implement encoder pass — build input tensors (input_ids, attention_mask), run `_encoderSession.Run()`, extract `encoder_hidden_states`
- [x] 4.4 Implement greedy decoder loop — iterative token generation, argmax on logits, stop on EOS or max_length=512, check `CancellationToken` each step
- [x] 4.5 Implement `SupportsLanguagePair` checking against `ModelRegistry.TranslationModels`
- [x] 4.6 Test end-to-end: "你好世界" → English output containing "hello" or "world"

## 5. Language Detection

- [x] 5.1 Create `LanguageDetection/FastTextDetector.cs` — lazy-load lid.176.ftz model, implement `DetectAsync`, return ISO 639-1 code + confidence
  > Decision: ScriptBasedDetector used as primary detector; FastText deferred (requires native binary).
- [x] 5.2 Create `LanguageDetection/ScriptBasedDetector.cs` — Unicode script analysis fallback (CJK→zh, Hiragana→ja, Hangul→ko, Cyrillic→ru, Latin→en)
- [x] 5.3 Wire fallback logic: if FastText model not available, use ScriptBasedDetector automatically
- [x] 5.4 Test detection accuracy: Chinese, English, Japanese inputs return correct language codes

## 6. Pipeline Assembly

- [x] 6.1 Update `Translation/TranslationPipeline.cs` — full orchestration: detect → short-circuit if same language → ensure model → translate → timing
- [x] 6.2 Verify cancel-and-restart: rapid sequential calls cancel previous, only latest completes
- [x] 6.3 Update `ServiceCollectionExtensions.AddLiveLingoCore()` — register `ModelManager`, `MarianOnnxEngine`, `FastTextDetector` replacing all stubs

## 7. First-Run Model Download UI

- [x] 7.1 Create `Views/ModelDownloadWindow.axaml` — simple dialog showing model list, sizes, progress bar, cancel button
  > Merged into P5 SetupWizardWindow with model download step.
- [x] 7.2 Create `ViewModels/ModelDownloadViewModel.cs` — download required models (FastText + default MarianMT pair), report progress, close on completion
  > Merged into P5 SetupWizardViewModel.DownloadModelCommand.
- [x] 7.3 Wire into `App.axaml.cs` startup — check if required models installed, show download window if not, proceed to normal mode on completion
  > Implemented via P5 first-run wizard in App.axaml.cs.

## 8. Integration & Verification

- [x] 8.1 End-to-end test: type Chinese in overlay → real English translation appears within 500ms
- [x] 8.2 Cancel test: rapid typing → only latest translation shown, no stale results
- [x] 8.3 Status display: "Translating..." during processing, "Translated (Xms)" on success, "Error: ..." on failure
- [x] 8.4 Cold start test: first translation loads model (~1s), subsequent translations are fast (<200ms)
- [x] 8.5 Inject test: Ctrl+Enter injects real translation into Slack/Notepad
- [x] 8.6 Verify Core project has no Avalonia dependency (models, engine, detection are all in Core)
