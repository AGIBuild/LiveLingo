## ADDED Requirements

### Requirement: Translation preserves content across blank lines
The translation engine SHALL translate the full input text including content separated by blank lines (double newlines).

#### Scenario: Multi-paragraph text
- **WHEN** user enters text with blank lines separating paragraphs (e.g., "你好\n\n世界")
- **THEN** the translation result includes translations of all paragraphs, not just the first

#### Scenario: Single paragraph unchanged
- **WHEN** user enters text with no blank lines
- **THEN** translation behavior is identical to current behavior

### Requirement: Remove \n\n from anti-prompts
The `LlamaTranslationEngine` SHALL NOT use `"\n\n"` as an anti-prompt (stop sequence). Valid stop tokens are `</s>` and `<|im_end|>`.

#### Scenario: Anti-prompt configuration
- **WHEN** `LlamaTranslationEngine.TranslateAsync` configures `InferenceParams`
- **THEN** `AntiPrompts` contains only `["</s>", "<|im_end|>"]` and does not contain `"\n\n"`
