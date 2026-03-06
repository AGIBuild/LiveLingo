# P3 Spec: AI Post-processing

## 1. LLamaSharp 集成

### 1.1 NuGet 包

```xml
<!-- LiveLingo.Core.csproj -->
<PackageReference Include="LLamaSharp" Version="0.*" />

<!-- 运行时后端 — 按平台选择一个 -->
<PackageReference Include="LLamaSharp.Backend.Cpu" Version="0.*" />
<!-- 未来 GPU 支持: LLamaSharp.Backend.Cuda12 -->
```

### 1.2 模型文件

| 项 | 值 |
|----|----|
| 模型 | Qwen2.5-1.5B-Instruct |
| 格式 | GGUF (Q4_K_M 量化) |
| 文件名 | qwen2.5-1.5b-instruct-q4_k_m.gguf |
| 大小 | ~1.0 GB |
| 来源 | https://huggingface.co/Qwen/Qwen2.5-1.5B-Instruct-GGUF |
| 上下文窗口 | 32K tokens (实际使用限制到 2K) |

### 1.3 模型生命周期管理

```csharp
namespace LiveLingo.Core.Processing;

public class QwenModelHost : IDisposable
{
    private LLamaWeights? _weights;
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private Timer? _unloadTimer;

    private readonly string _modelPath;
    private readonly ILogger<QwenModelHost> _logger;

    private static readonly TimeSpan UnloadTimeout = TimeSpan.FromMinutes(5);

    /// <summary>
    /// 获取或加载模型。首次调用会加载模型 (3-5s)。
    /// 5 分钟无调用后自动卸载释放内存。
    /// </summary>
    public async Task<LLamaWeights> GetWeightsAsync(CancellationToken ct)
    {
        ResetUnloadTimer();

        if (_weights is not null)
            return _weights;

        await _loadLock.WaitAsync(ct);
        try
        {
            if (_weights is not null)
                return _weights;

            _logger.LogInformation("Loading Qwen model from {Path}...", _modelPath);
            var parameters = new ModelParams(_modelPath)
            {
                ContextSize = 2048,
                GpuLayerCount = 0,    // CPU only (P3 scope)
                Threads = Environment.ProcessorCount / 2
            };
            _weights = await Task.Run(
                () => LLamaWeights.LoadFromFile(parameters), ct);
            _logger.LogInformation("Qwen model loaded");
            return _weights;
        }
        finally
        {
            _loadLock.Release();
        }
    }

    private void ResetUnloadTimer()
    {
        _unloadTimer?.Dispose();
        _unloadTimer = new Timer(_ => Unload(), null, UnloadTimeout, Timeout.InfiniteTimeSpan);
    }

    private void Unload()
    {
        _loadLock.Wait();
        try
        {
            if (_weights is null) return;
            _logger.LogInformation("Unloading Qwen model (idle timeout)");
            _weights.Dispose();
            _weights = null;
        }
        finally
        {
            _loadLock.Release();
        }
    }

    public void Dispose()
    {
        _unloadTimer?.Dispose();
        _weights?.Dispose();
    }
}
```

### 1.4 加载状态通知

QwenModelHost 暴露加载状态事件，供 UI 显示：

```csharp
public enum ModelLoadState { Unloaded, Loading, Loaded }

public event Action<ModelLoadState>? StateChanged;
```

OverlayViewModel 监听此事件更新 StatusText：
- `Loading` → "Loading AI model..."
- `Loaded` → 正常流程

## 2. 三种后处理器

### 2.1 共通基类

```csharp
namespace LiveLingo.Core.Processing;

public abstract class QwenTextProcessor : ITextProcessor
{
    private readonly QwenModelHost _modelHost;
    private readonly ILogger _logger;

    public abstract string Name { get; }
    protected abstract string SystemPrompt { get; }

    protected QwenTextProcessor(QwenModelHost modelHost, ILogger logger)
    {
        _modelHost = modelHost;
        _logger = logger;
    }

    public async Task<string> ProcessAsync(
        string text, string language, CancellationToken ct)
    {
        var weights = await _modelHost.GetWeightsAsync(ct);

        using var context = weights.CreateContext(new ModelParams(string.Empty)
        {
            ContextSize = 2048
        });

        var executor = new InstructExecutor(context);
        var inferenceParams = new InferenceParams
        {
            MaxTokens = 512,
            Temperature = 0.3f,
            TopP = 0.9f,
            AntiPrompts = ["\n\n", "Text:", "Input:"]
        };

        var prompt = BuildPrompt(text);
        var result = new StringBuilder();

        await foreach (var token in executor.InferAsync(prompt, inferenceParams, ct))
        {
            result.Append(token);

            // 安全上限
            if (result.Length > text.Length * 3)
                break;
        }

        var processed = result.ToString().Trim();
        _logger.LogDebug("[{Name}] {InputLen} → {OutputLen} chars",
            Name, text.Length, processed.Length);

        return string.IsNullOrWhiteSpace(processed) ? text : processed;
    }

    private string BuildPrompt(string text)
    {
        return $"""
            <|im_start|>system
            {SystemPrompt}<|im_end|>
            <|im_start|>user
            {text}<|im_end|>
            <|im_start|>assistant
            """;
    }
}
```

### 2.2 SummarizeProcessor

```csharp
public class SummarizeProcessor : QwenTextProcessor
{
    public override string Name => "summarize";

    protected override string SystemPrompt =>
        """
        You are a concise text editor. Your task:
        - Shorten the input text while preserving its core meaning
        - Remove redundancy and filler words
        - Keep the same language as the input
        - Output ONLY the shortened text, nothing else
        - If the text is already short (under 20 words), return it unchanged
        """;
}
```

### 2.3 OptimizeProcessor

```csharp
public class OptimizeProcessor : QwenTextProcessor
{
    public override string Name => "optimize";

    protected override string SystemPrompt =>
        """
        You are a professional text editor. Your task:
        - Fix any grammar or spelling errors
        - Improve clarity and natural flow
        - Keep the original meaning and tone
        - Keep the same language as the input
        - Output ONLY the improved text, nothing else
        - If the text is already well-written, return it unchanged
        """;
}
```

### 2.4 ColloquializeProcessor

```csharp
public class ColloquializeProcessor : QwenTextProcessor
{
    public override string Name => "colloquialize";

    protected override string SystemPrompt =>
        """
        You are a casual chat writer. Your task:
        - Rewrite the input in a relaxed, friendly conversational tone
        - Make it suitable for workplace chat (Slack, Teams, Discord)
        - Keep it natural, brief, and approachable
        - Keep the same language as the input
        - Output ONLY the rewritten text, nothing else
        - Do NOT add greetings or sign-offs unless they were in the original
        """;
}
```

## 3. DI 注册

```csharp
// P3 阶段的 AddLiveLingoCore 扩展
public static IServiceCollection AddLiveLingoCore(
    this IServiceCollection services,
    Action<CoreOptions>? configure = null)
{
    // ... (P2 注册内容)

    // P3: 后处理
    services.AddSingleton<QwenModelHost>();
    services.AddSingleton<ITextProcessor, SummarizeProcessor>();
    services.AddSingleton<ITextProcessor, OptimizeProcessor>();
    services.AddSingleton<ITextProcessor, ColloquializeProcessor>();

    return services;
}
```

Pipeline 通过 `IEnumerable<ITextProcessor>` 注入所有处理器，按名称选择。

## 4. Overlay UI 变化

### 4.1 后处理模式选择器

在状态栏增加下拉选择（替换 PoC 中的 toggle button）：

```
┌──────────────────────────────────────────────────────┐
│ Ctrl+Enter paste & send         [Off ▾] [Paste&Send] │
│                                  ├─ Off              │
│                                  ├─ Summarize        │
│                                  ├─ Optimize         │
│                                  └─ Colloquial       │
└──────────────────────────────────────────────────────┘
```

### 4.2 OverlayViewModel 变化

```csharp
[ObservableProperty]
private ProcessingMode _postProcessMode = ProcessingMode.Off;

// 上次选择持久保持
private static ProcessingMode _lastPostProcessMode = ProcessingMode.Off;

partial void OnPostProcessModeChanged(ProcessingMode value)
{
    _lastPostProcessMode = value;
    // 如果已有翻译结果，触发重新后处理
    if (!string.IsNullOrWhiteSpace(TranslatedText))
        RerunPostProcessing();
}
```

### 4.3 Preview 两阶段更新

```
用户打字 → RunPipelineAsync:
  │
  ├─ Stage 1: MarianMT
  │  TranslatedText = "The meeting is scheduled for next Monday to discuss the proposal."
  │  StatusText = "Translated (120ms)"
  │
  ├─ (如果 PostProcessMode != Off)
  │  StatusText = "Polishing..."
  │
  └─ Stage 2: Qwen
     TranslatedText = "Let's meet Monday to go over the proposal"
     StatusText = "Polished (1.8s)"
```

用户在 Stage 1 完成后立即看到原始翻译，然后看到润色版替换上去。

## 5. 模型按需下载

### 5.1 触发时机

用户首次选择非 Off 的后处理模式时：

```
用户选择 "Colloquial"
  │
  ├─ Qwen 模型已下载? → 正常执行
  │
  └─ 未下载 → 弹出下载确认
       "AI Polish requires Qwen2.5-1.5B model (1.0 GB). Download now?"
       [Download]  [Cancel]
       │
       └─ 下载中显示进度 → 完成后自动执行后处理
```

### 5.2 下载进度集成

在 overlay 状态栏或单独弹窗显示下载进度。
下载完成后自动切换到 loaded 状态。

## 6. 推理参数

| 参数 | 值 | 说明 |
|------|-----|------|
| max_tokens | 512 | 防止无限生成 |
| temperature | 0.3 | 低随机性，保持忠实 |
| top_p | 0.9 | 核采样 |
| context_size | 2048 | 输入+输出上限 |
| threads | CPU cores / 2 | CPU 推理线程数 |
| anti_prompts | `["\n\n"]` | 遇到双换行停止 |

### 6.1 输入长度限制

- Qwen 上下文 2048 tokens
- System prompt 约 100 tokens
- 保留 512 tokens 给输出
- 输入文本上限约 1400 tokens (~5000 字符)
- 超过上限时截断并在日志中警告

## 7. 错误处理

| 场景 | 处理 |
|------|------|
| 模型加载失败 | StatusText 显示错误，后处理降级为 Off |
| 推理超时 (>10s) | 取消推理，返回原始翻译，StatusText 提示 |
| 输出为空/无意义 | 回退到原始翻译 |
| OOM | 捕获异常，卸载模型，降级为 Off，建议用户关闭其他程序 |
| 取消 | OperationCanceledException 静默处理 |

## 8. 性能基准（预期）

| 输入长度 | MarianMT | Qwen 后处理 | 总计 |
|----------|----------|-------------|------|
| 10 字 | ~100ms | ~1.0s | ~1.1s |
| 50 字 | ~150ms | ~2.0s | ~2.2s |
| 200 字 | ~250ms | ~4.0s | ~4.3s |

CPU: 现代 4 核以上。GPU 加速可将 Qwen 提速 5-10x（P3 scope 外）。

## 9. 测试

### 9.1 单元测试

```
Processing/
├── QwenTextProcessorTests.cs
│   ├── ProcessAsync_ReturnsNonEmpty
│   ├── ProcessAsync_RespectsCancel
│   ├── ProcessAsync_FallsBackOnEmpty
│   └── ProcessAsync_RespectsMaxLength
│
├── SummarizeProcessorTests.cs
│   └── ProcessAsync_ShorterThanInput    (集成测试, 需要模型)
│
└── QwenModelHostTests.cs
    ├── GetWeightsAsync_LoadsOnce
    ├── Unload_AfterTimeout
    └── Dispose_ReleasesResources
```

### 9.2 集成测试 checklist

- [ ] Qwen 模型加载成功 (<5s)
- [ ] Colloquialize 输出比输入更口语化（人工验证）
- [ ] Summarize 输出长度 < 输入长度
- [ ] Optimize 输出语法正确（人工验证）
- [ ] 连续调用复用同一模型实例
- [ ] 5 分钟无调用后模型自动卸载
- [ ] 取消不泄漏资源
