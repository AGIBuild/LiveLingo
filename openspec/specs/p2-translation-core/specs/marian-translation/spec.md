## ADDED Requirements

### Requirement: MarianMT ONNX inference pipeline
`MarianOnnxEngine` SHALL implement `ITranslationEngine` using ONNX Runtime to run MarianMT encoder-decoder models. The pipeline: SentencePiece encode → ONNX encoder → ONNX decoder (autoregressive) → SentencePiece decode.

#### Scenario: Translate Chinese to English
- **WHEN** `TranslateAsync("你好世界", "zh", "en")` is called
- **THEN** the result SHALL contain an English translation (case-insensitive match for "hello" or "world")

#### Scenario: Translate with cancellation
- **WHEN** `CancellationToken` is cancelled during decoder loop
- **THEN** `OperationCanceledException` SHALL be thrown without resource leaks

### Requirement: SentencePiece tokenizer
The engine SHALL use SentencePiece models (`source.spm`, `target.spm`) for tokenization and detokenization. The tokenizer SHALL add BOS/EOS tokens as required by MarianMT.

#### Scenario: Encode Chinese text
- **WHEN** `Tokenizer.Encode("你好")` is called with the zh→en source.spm model
- **THEN** the result SHALL be an array of integer token IDs with length > 0

#### Scenario: Decode token IDs
- **WHEN** `Tokenizer.Decode(tokenIds)` is called with valid output token IDs
- **THEN** the result SHALL be a readable English string

### Requirement: Lazy model session loading
`MarianOnnxEngine` SHALL load ONNX `InferenceSession` instances lazily on first translation request for a given language pair. Loaded sessions SHALL be cached in a `ConcurrentDictionary` for reuse.

#### Scenario: First translation loads model
- **WHEN** `TranslateAsync` is called for `zh→en` for the first time
- **THEN** the ONNX model SHALL be loaded from disk and cached for subsequent calls

#### Scenario: Subsequent translations reuse session
- **WHEN** `TranslateAsync` is called for `zh→en` a second time
- **THEN** the cached `InferenceSession` SHALL be reused without reloading

### Requirement: ONNX session configuration
ONNX `SessionOptions` SHALL be configured with `GraphOptimizationLevel.ORT_ENABLE_ALL`, `IntraOpNumThreads = 4`, `InterOpNumThreads = 2` as defaults. Thread counts SHALL be configurable via `CoreOptions`.

#### Scenario: Custom thread count
- **WHEN** `CoreOptions.TranslationThreads` is set to 8
- **THEN** `IntraOpNumThreads` SHALL be set to 8

### Requirement: Unsupported language pair error
If no model exists for a requested language pair, the engine SHALL throw `NotSupportedException` with a descriptive message.

#### Scenario: Request unsupported pair
- **WHEN** `TranslateAsync("text", "xx", "yy")` is called for an unregistered language pair
- **THEN** `NotSupportedException` SHALL be thrown with message containing the pair "xx→yy"

### Requirement: Greedy decoding as baseline
The decoder SHALL implement greedy search (beam_size=1) as the initial decoding strategy. Each step: run decoder ONNX model → argmax on logits → append token → repeat until EOS or max_length (512).

#### Scenario: Decoder stops at EOS
- **WHEN** the decoder generates the EOS token
- **THEN** decoding SHALL stop and the output tokens (excluding BOS/EOS) SHALL be returned

#### Scenario: Decoder stops at max length
- **WHEN** the decoder reaches 512 tokens without generating EOS
- **THEN** decoding SHALL stop and return the tokens generated so far
