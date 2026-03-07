## ADDED Requirements

### Requirement: QwenTextProcessor base class
An abstract `QwenTextProcessor` SHALL implement `ITextProcessor`, using `QwenModelHost` for model access and `InstructExecutor` for inference. Each subclass SHALL provide a distinct `Name` and `SystemPrompt`.

#### Scenario: Process text with Qwen
- **WHEN** `ProcessAsync("The meeting is scheduled for next Monday to discuss the project proposal.", "en")` is called
- **THEN** the processor SHALL return a non-empty string in English

#### Scenario: Process respects cancellation
- **WHEN** `CancellationToken` is cancelled during inference
- **THEN** `OperationCanceledException` SHALL be thrown

### Requirement: Fallback on empty output
If the LLM produces an empty or whitespace-only response, the processor SHALL return the original input text unchanged.

#### Scenario: Empty LLM output
- **WHEN** LLM inference returns empty string
- **THEN** `ProcessAsync` SHALL return the original `text` parameter

### Requirement: Output length safety limit
The processor SHALL stop inference if the output exceeds `text.Length * 3` characters, to prevent runaway generation.

#### Scenario: Runaway generation
- **WHEN** LLM output grows beyond 3x the input length
- **THEN** inference SHALL be stopped and the partial output SHALL be returned

### Requirement: SummarizeProcessor
`SummarizeProcessor` SHALL have `Name = "summarize"` and a system prompt instructing the LLM to shorten text while preserving core meaning, keeping the same language.

#### Scenario: Summarize long text
- **WHEN** `ProcessAsync` is called on a 100-word English text
- **THEN** the output SHALL be shorter than the input

#### Scenario: Short text unchanged
- **WHEN** input is under 20 words
- **THEN** the output SHALL be approximately the same length (no unnecessary expansion)

### Requirement: OptimizeProcessor
`OptimizeProcessor` SHALL have `Name = "optimize"` and a system prompt instructing the LLM to fix grammar, improve clarity, while keeping meaning and tone.

#### Scenario: Fix grammar errors
- **WHEN** `ProcessAsync` is called with "He go to store yesterday"
- **THEN** the output SHALL contain corrected grammar (e.g. "He went to the store yesterday")

### Requirement: ColloquializeProcessor
`ColloquializeProcessor` SHALL have `Name = "colloquialize"` and a system prompt instructing the LLM to rewrite text in a relaxed, friendly conversational tone suitable for workplace chat.

#### Scenario: Formalize to casual
- **WHEN** `ProcessAsync` is called with "I would like to inform you that the meeting has been rescheduled to next Monday."
- **THEN** the output SHALL be more casual (e.g. "Hey, just a heads up - meeting's moved to Monday")

### Requirement: Inference parameters
All processors SHALL use: `MaxTokens = 512`, `Temperature = 0.3`, `TopP = 0.9`, `AntiPrompts = ["\n\n"]`. These SHALL be consistent across all three processors.

#### Scenario: Low temperature produces consistent output
- **WHEN** the same input is processed twice
- **THEN** the outputs SHALL be similar (low variance due to temperature=0.3)

### Requirement: ChatML prompt format
Processors SHALL use Qwen's ChatML format: `<|im_start|>system\n{prompt}<|im_end|>\n<|im_start|>user\n{text}<|im_end|>\n<|im_start|>assistant\n`.

#### Scenario: Prompt format matches ChatML
- **WHEN** a prompt is built for processing
- **THEN** it SHALL contain `<|im_start|>` and `<|im_end|>` delimiters in the correct order
