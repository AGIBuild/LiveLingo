# New Design Coverage Sweep Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Raise test coverage around the redesigned llama-server call boundary without drifting into unrelated legacy test repair.

**Architecture:** Add focused unit tests around the new shared chat request / response path, especially the two concrete callers that still lacked direct behavioral tests: `LlamaTranslationEngine` and `QwenTextProcessor`. Keep production changes minimal and only when tests reveal missing seams or mismatched behavior.

**Tech Stack:** .NET 10, xUnit, NSubstitute, HttpClient, System.Text.Json

---

### Task 1: Cover `LlamaTranslationEngine` request/response behavior

**Files:**
- Create: `tests/LiveLingo.Core.Tests/Engines/LlamaTranslationEngineTests.cs`
- Modify: `src/LiveLingo.Core/Engines/LlamaTranslationEngine.cs` (only if tests expose a missing seam)

- [ ] **Step 1: Write the failing tests**
  - request body includes shared stop sequences and `stream = false`
  - response content arrays are parsed into final translation text
  - empty assistant output throws `InvalidOperationException`
- [ ] **Step 2: Run `dotnet test tests/LiveLingo.Core.Tests/LiveLingo.Core.Tests.csproj --filter LlamaTranslationEngineTests` and verify red**
- [ ] **Step 3: Apply the minimal production fix only if needed**
- [ ] **Step 4: Re-run the same filter and verify green**

### Task 2: Cover `QwenTextProcessor` fallback behavior

**Files:**
- Create: `tests/LiveLingo.Core.Tests/Processing/QwenTextProcessorTests.cs`
- Modify: `src/LiveLingo.Core/Processing/QwenTextProcessor.cs` (only if tests expose a missing seam)

- [ ] **Step 1: Write the failing tests**
  - request body uses the shared request factory defaults
  - empty assistant output falls back to original text
  - transport failure falls back to original text
- [ ] **Step 2: Run `dotnet test tests/LiveLingo.Core.Tests/LiveLingo.Core.Tests.csproj --filter QwenTextProcessorTests` and verify red**
- [ ] **Step 3: Apply the minimal production fix only if needed**
- [ ] **Step 4: Re-run the same filter and verify green**

### Task 3: Re-verify the redesigned Core path

**Files:**
- Verify only

- [ ] **Step 1: Run** `dotnet test tests/LiveLingo.Core.Tests/LiveLingo.Core.Tests.csproj --filter "LlamaTranslationEngineTests|QwenTextProcessorTests|LlamaServerChatRequestTests|LlamaServerChatResponseTests|QwenModelHostTests|NativeRuntimeUpdaterTests|LlamaServerProcessManagerTests|TranslationPipelineTests"`
- [ ] **Step 2: Confirm all targeted redesigned-path tests pass**
