# P3: AI Post-processing

> 集成 Qwen2.5-1.5B 本地 LLM，实现总结、优化、口语化三种后处理模式。

## 前置依赖

- P2 完成（TranslationPipeline + ModelManager 已工作）

## 目标

- 集成 LLamaSharp + Qwen2.5-1.5B (GGUF Q4)
- 实现三种 ITextProcessor：Summarize / Optimize / Colloquialize
- 管线增加后处理阶段（翻译完成后自动执行）
- Overlay UI 增加后处理模式选择
- Cancel-and-restart 覆盖后处理阶段

## 不做

- 不实现 GPU 加速（CPU 先行，后续优化）
- 不实现自定义 prompt（使用预设 prompt）
- 不实现流式输出到 preview（完整结果后一次更新）

## 交付内容

### 1. QwenProcessor 实现

```
ITextProcessor
  ├── SummarizeProcessor    → 缩短内容，保留核心意思
  ├── OptimizeProcessor     → 语法修正，表达润色
  └── ColloquializeProcessor → 口语化，适合 IM 聊天
```

三者共享同一个 Qwen 模型实例，仅 system prompt 不同。

```csharp
internal class QwenProcessor : ITextProcessor
{
    private readonly LLamaWeights _model;      // 共享模型权重
    private readonly string _systemPrompt;      // 按模式不同

    public async Task<string> ProcessAsync(
        string text, string language, CancellationToken ct)
    {
        using var context = _model.CreateContext(new ModelParams { ... });
        var executor = new InstructExecutor(context);

        var result = new StringBuilder();
        await foreach (var token in executor.InferAsync(
            $"{_systemPrompt}\n\nText: {text}", ct))
        {
            result.Append(token);
            ct.ThrowIfCancellationRequested();
        }

        return result.ToString().Trim();
    }
}
```

### 2. Prompt 模板

```
[Summarize]
System: You are a concise editor. Shorten the following translated text
while preserving its core meaning. Remove redundancy. Output only the
shortened text, nothing else.

[Optimize]
System: You are a professional editor. Improve the grammar, clarity, and
natural flow of the following translated text. Keep the original meaning
and tone. Output only the improved text, nothing else.

[Colloquialize]
System: You are a casual chat writer. Rewrite the following translated
text in a relaxed, friendly tone suitable for workplace chat apps like
Slack or Teams. Keep it natural and brief. Output only the rewritten
text, nothing else.
```

### 3. 管线两阶段流转

```
用户输入变化
  │
  ▼
┌─────────────────────────────────────────┐
│  Stage 1: MarianMT 翻译                  │
│  preview 更新为原始翻译                    │
│  StatusText = "Translated (150ms)"       │
└─────────────┬───────────────────────────┘
              │ (如果后处理已启用)
              ▼
┌─────────────────────────────────────────┐
│  Stage 2: Qwen 后处理                     │
│  preview 更新为润色结果                    │
│  StatusText = "Polished (1.2s)"          │
└─────────────────────────────────────────┘
```

如果后处理未启用，Stage 2 跳过。用户在任何阶段按 Ctrl+Enter 都注入当前 preview。

### 4. 模型生命周期

```
App 启动
  │
  ├─ Qwen 模型不预加载（1GB 太大）
  │
  ▼
用户首次启用后处理
  │
  ├─ ModelManager.EnsureModelAsync("qwen2.5-1.5b-q4")
  │   ├─ 已下载 → 跳过
  │   └─ 未下载 → 下载对话框 (1GB)
  │
  ├─ 加载模型到内存 (~2GB RAM, 耗时 3-5s)
  │   └─ 首次加载显示 loading 状态
  │
  └─ 后续调用复用已加载模型
      └─ 5 分钟无调用 → 卸载释放内存
```

### 5. Overlay UI 变化

在 overlay 底部状态栏增加后处理模式选择：

```
┌─────────────────────────────────────────────┐
│  [输入框]                                     │
│  [翻译预览]                                   │
├─────────────────────────────────────────────┤
│ Ctrl+Enter paste & send                      │
│              [Paste & Send] [Off ▾] Target:EN │
│                              ├── Off         │
│                              ├── Summarize   │
│                              ├── Optimize    │
│                              └── Colloquial  │
└─────────────────────────────────────────────┘
```

### 6. Cancel 覆盖范围

```csharp
async Task RunPipelineAsync(string text, CancellationToken ct)
{
    // Stage 1
    StatusText = "Translating...";
    var result = await _pipeline.ProcessAsync(
        new TranslationRequest(text, null, _targetLang, null), ct);
    TranslatedText = result.Text;

    ct.ThrowIfCancellationRequested();  // ← 在此取消时不跑 Stage 2

    // Stage 2 (如果启用)
    if (_postProcessMode != ProcessingMode.Off)
    {
        StatusText = "Polishing...";
        TranslatedText = await _pipeline.PostProcessAsync(
            result.Text, _postProcessMode, ct);
    }

    StatusText = "Ready";
}
```

用户在 Stage 2 进行中打字 → cancel → 从 Stage 1 重新开始。

## 新增 NuGet 依赖

| 包 | 项目 | 用途 |
|----|------|------|
| LLamaSharp | Core | Qwen 推理 |
| LLamaSharp.Backend.Cpu | Core | CPU 推理后端 |

## 验收标准

- [ ] 选择 "Colloquial" 模式后，翻译结果被口语化改写
- [ ] 选择 "Summarize" 模式后，长文本翻译被缩短
- [ ] 选择 "Optimize" 模式后，翻译语法被润色
- [ ] 选择 "Off" 时，只显示 MarianMT 原始翻译
- [ ] 翻译完成后 1-3 秒内后处理结果出现
- [ ] 快速打字时，前一次后处理被正确取消
- [ ] 5 分钟无操作后 Qwen 模型自动卸载
- [ ] Ctrl+Enter 注入的是 preview 中显示的文本
