## ADDED Requirements

### Requirement: FastText language detection
`FastTextDetector` SHALL implement `ILanguageDetector` using the FastText `lid.176.ftz` model (~1MB). It SHALL return an ISO 639-1 language code and confidence score.

#### Scenario: Detect Chinese text
- **WHEN** `DetectAsync("你好世界")` is called
- **THEN** `DetectionResult.Language` SHALL be `"zh"` and `Confidence` SHALL be > 0.8

#### Scenario: Detect English text
- **WHEN** `DetectAsync("Hello world")` is called
- **THEN** `DetectionResult.Language` SHALL be `"en"` and `Confidence` SHALL be > 0.8

#### Scenario: Detect Japanese text
- **WHEN** `DetectAsync("こんにちは世界")` is called
- **THEN** `DetectionResult.Language` SHALL be `"ja"` and `Confidence` SHALL be > 0.5

### Requirement: Lazy model loading
`FastTextDetector` SHALL load the FastText model lazily on the first `DetectAsync` call. The loaded model SHALL be cached for subsequent calls.

#### Scenario: First detection loads model
- **WHEN** `DetectAsync` is called for the first time
- **THEN** the FastText model SHALL be loaded from `{ModelStoragePath}/fasttext-lid/lid.176.ftz`

### Requirement: Script-based fallback detector
If the FastText model is not available, the system SHALL fall back to `ScriptBasedDetector` which uses Unicode script analysis: CJK Unified Ideographs → "zh", Hiragana/Katakana → "ja", Hangul → "ko", Cyrillic → "ru", Latin → "en".

#### Scenario: Detect Chinese without FastText model
- **WHEN** FastText model is not downloaded and `ScriptBasedDetector.DetectAsync("你好")` is called
- **THEN** the result SHALL be `DetectionResult("zh", 0.7f)`

#### Scenario: Detect mixed script text
- **WHEN** input contains both Latin and CJK characters (e.g. "Hello 你好")
- **THEN** the detector SHALL return the language of the dominant script by character count

### Requirement: Model not found handling
If the FastText model file does not exist at the expected path and no fallback is configured, `DetectAsync` SHALL throw `FileNotFoundException` with a message indicating the model needs to be downloaded.

#### Scenario: Missing model file
- **WHEN** `FastTextDetector.DetectAsync` is called and `lid.176.ftz` does not exist
- **THEN** `FileNotFoundException` SHALL be thrown with message containing "fasttext-lid"
