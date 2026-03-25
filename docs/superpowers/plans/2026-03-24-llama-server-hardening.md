# Llama Server Hardening Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Harden the local llama-server integration so chat responses are parsed consistently, startup survives caller cancellation, and runtime downloads are more resilient.

**Architecture:** Centralize `/v1/chat/completions` parsing in a single helper used by translation and text-processing flows. Keep model startup as a shared background task owned by `QwenModelHost`, and strengthen runtime download behavior inside `NativeRuntimeUpdater` so transport failures do not corrupt the bootstrap path.

**Tech Stack:** .NET 10, xUnit, NSubstitute, System.Text.Json, HttpClient

---

### Task 1: Centralize chat response parsing

**Files:**
- Modify: `src/LiveLingo.Core/Engines/LlamaTranslationEngine.cs`
- Modify: `src/LiveLingo.Core/Processing/QwenTextProcessor.cs`
- Create: `src/LiveLingo.Core/Processing/LlamaServerChatResponse.cs`
- Test: `tests/LiveLingo.Core.Tests/Processing/LlamaServerChatResponseTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public void GetAssistantText_reads_text_from_content_array()
{
    const string json = """
        {"choices":[{"message":{"content":[{"type":"text","text":"Hi"}]}}]}
        """;
    using var doc = JsonDocument.Parse(json);
    Assert.Equal("Hi", LlamaServerChatResponse.GetAssistantText(doc.RootElement));
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/LiveLingo.Core.Tests/LiveLingo.Core.Tests.csproj --filter LlamaServerChatResponseTests`
Expected: FAIL because the helper does not yet exist or does not handle the response shape.

- [ ] **Step 3: Write minimal implementation**

Create a helper that:
- reads `choices[0].message.content`
- falls back to `reasoning_content` when `content` is blank
- accepts either string content or OpenAI-style content arrays
- strips Qwen `<think>` wrappers in one place
- emits a short diagnostic string for empty-output logs

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/LiveLingo.Core.Tests/LiveLingo.Core.Tests.csproj --filter LlamaServerChatResponseTests`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/LiveLingo.Core/Engines/LlamaTranslationEngine.cs \
  src/LiveLingo.Core/Processing/QwenTextProcessor.cs \
  src/LiveLingo.Core/Processing/LlamaServerChatResponse.cs \
  tests/LiveLingo.Core.Tests/Processing/LlamaServerChatResponseTests.cs
git commit -m "test: harden llama chat response parsing"
```

### Task 2: Make Qwen model startup shareable across cancelled callers

**Files:**
- Modify: `src/LiveLingo.Core/Processing/QwenModelHost.cs`
- Test: `tests/LiveLingo.Core.Tests/Processing/QwenModelHostTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public async Task GetOrStartServerAsync_keeps_background_load_running_after_first_waiter_cancels()
{
    // Arrange host with a gate-controlled server startup.
    // Cancel the first caller before startup completes.
    // Assert a second caller later receives the same loaded endpoint.
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/LiveLingo.Core.Tests/LiveLingo.Core.Tests.csproj --filter QwenModelHostTests`
Expected: FAIL because startup is still bound to the first caller cancellation path.

- [ ] **Step 3: Write minimal implementation**

Update `QwenModelHost` so:
- one shared `_ensureServerTask` owns startup/download work
- the task is created under lock and run with `CancellationToken.None`
- each caller can still cancel only its own wait via `WaitAsync(ct)`
- reset paths clear the cached task
- completion still verifies the server reached `Loaded`

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/LiveLingo.Core.Tests/LiveLingo.Core.Tests.csproj --filter QwenModelHostTests`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/LiveLingo.Core/Processing/QwenModelHost.cs \
  tests/LiveLingo.Core.Tests/Processing/QwenModelHostTests.cs
git commit -m "test: preserve shared llama startup across cancellations"
```

### Task 3: Make runtime downloads resumable and non-destructive

**Files:**
- Modify: `src/LiveLingo.Core/Processing/NativeRuntimeUpdater.cs`
- Test: `tests/LiveLingo.Core.Tests/Processing/NativeRuntimeUpdaterTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public async Task EnsureLatestLlamaServerAsync_resumes_partial_archive_download()
{
    // Arrange an HTTP handler that first returns partial content and then serves the tail.
    // Seed an archive file with partial bytes.
    // Assert the completed file length matches the advertised total.
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/LiveLingo.Core.Tests/LiveLingo.Core.Tests.csproj --filter NativeRuntimeUpdaterTests`
Expected: FAIL because downloads always restart from scratch.

- [ ] **Step 3: Write minimal implementation**

Add a resumable download helper that:
- reuses a stable archive filename
- sends `Range` when partial bytes already exist
- appends only on `206 Partial Content`
- retries transient HTTP / IO / timeout failures with backoff
- validates final size when `Content-Range` exposes total length

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/LiveLingo.Core.Tests/LiveLingo.Core.Tests.csproj --filter NativeRuntimeUpdaterTests`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/LiveLingo.Core/Processing/NativeRuntimeUpdater.cs \
  tests/LiveLingo.Core.Tests/Processing/NativeRuntimeUpdaterTests.cs
git commit -m "test: resume llama runtime downloads"
```

### Task 4: Verify integration points only for the redesigned path

**Files:**
- Modify: `src/LiveLingo.Core/Processing/LlamaServerProcessManager.cs`
- Verify: `tests/LiveLingo.Core.Tests/Processing/LlamaServerChatResponseTests.cs`
- Verify: `tests/LiveLingo.Core.Tests/Processing/QwenModelHostTests.cs`
- Verify: `tests/LiveLingo.Core.Tests/Processing/NativeRuntimeUpdaterTests.cs`

- [ ] **Step 1: Add the smallest failing test or assertion for startup arguments if needed**

```csharp
// Add only if there is a practical seam for argument verification.
```

- [ ] **Step 2: Run targeted tests to verify the redesigned path fails where expected**

Run: `dotnet test tests/LiveLingo.Core.Tests/LiveLingo.Core.Tests.csproj --filter "LlamaServerChatResponseTests|QwenModelHostTests|NativeRuntimeUpdaterTests"`
Expected: FAIL until all redesigned-path changes are complete.

- [ ] **Step 3: Finalize implementation**

Ensure the server starts with reasoning disabled and that translation now fails loudly on empty assistant output instead of silently echoing source text.

- [ ] **Step 4: Run targeted verification**

Run: `dotnet test tests/LiveLingo.Core.Tests/LiveLingo.Core.Tests.csproj --filter "LlamaServerChatResponseTests|QwenModelHostTests|NativeRuntimeUpdaterTests"`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/LiveLingo.Core/Processing/LlamaServerProcessManager.cs \
  src/LiveLingo.Core/Engines/LlamaTranslationEngine.cs \
  src/LiveLingo.Core/Processing/QwenTextProcessor.cs \
  src/LiveLingo.Core/Processing/QwenModelHost.cs \
  src/LiveLingo.Core/Processing/NativeRuntimeUpdater.cs
git commit -m "feat: harden local llama server integration"
```
