# P2 Spec: Translation Core

## 1. ModelManager

### 1.1 职责

- 从远程（HuggingFace）下载模型文件到本地
- 支持断点续传（HTTP Range header）
- 报告下载进度
- 管理本地模型目录（列出、删除、计算磁盘占用）
- 校验模型完整性（文件大小或 SHA256）

### 1.2 接口实现

```csharp
namespace LiveLingo.Core.Models;

public class ModelManager : IModelManager
{
    private readonly CoreOptions _options;
    private readonly ILogger<ModelManager> _logger;
    private readonly HttpClient _httpClient;

    public ModelManager(
        IOptions<CoreOptions> options,
        ILogger<ModelManager> logger,
        HttpClient httpClient)
    {
        _options = options.Value;
        _logger = logger;
        _httpClient = httpClient;
    }

    public async Task EnsureModelAsync(
        ModelDescriptor descriptor,
        IProgress<ModelDownloadProgress>? progress,
        CancellationToken ct)
    {
        var modelDir = GetModelDirectory(descriptor.Id);

        if (IsModelInstalled(descriptor.Id))
        {
            _logger.LogDebug("Model {Id} already installed", descriptor.Id);
            return;
        }

        Directory.CreateDirectory(modelDir);
        await DownloadModelAsync(descriptor, modelDir, progress, ct);
        WriteManifest(descriptor, modelDir);
    }

    // ...
}
```

### 1.3 本地存储结构

```
{ModelStoragePath}/
├── marian-zh-en/
│   ├── manifest.json          // 模型元数据
│   ├── model.onnx             // ONNX 翻译模型
│   ├── source.spm             // SentencePiece 源语言词表
│   ├── target.spm             // SentencePiece 目标语言词表
│   └── vocab.json             // 可选的词汇映射
│
├── fasttext-lid/
│   ├── manifest.json
│   └── lid.176.ftz            // FastText 语言检测模型
│
└── qwen2.5-1.5b-q4/          // P3 才使用
    ├── manifest.json
    └── qwen2.5-1.5b-instruct-q4_k_m.gguf
```

### 1.4 manifest.json 格式

```json
{
  "id": "marian-zh-en",
  "displayName": "Chinese → English",
  "type": "Translation",
  "version": "1.0.0",
  "sizeBytes": 31457280,
  "sha256": "abc123...",
  "installedAt": "2026-03-05T12:00:00Z",
  "files": [
    { "name": "model.onnx", "sizeBytes": 28000000 },
    { "name": "source.spm", "sizeBytes": 800000 },
    { "name": "target.spm", "sizeBytes": 800000 }
  ]
}
```

### 1.5 模型注册表

预定义可用模型（硬编码，后续可改为远程 registry）：

```csharp
public static class ModelRegistry
{
    public static readonly ModelDescriptor MarianZhEn = new(
        Id: "marian-zh-en",
        DisplayName: "Chinese → English",
        DownloadUrl: "https://huggingface.co/Helsinki-NLP/opus-mt-zh-en/resolve/main/",
        SizeBytes: 31_457_280,
        Type: ModelType.Translation
    );

    public static readonly ModelDescriptor MarianJaEn = new(
        Id: "marian-ja-en",
        DisplayName: "Japanese → English",
        DownloadUrl: "https://huggingface.co/Helsinki-NLP/opus-mt-ja-en/resolve/main/",
        SizeBytes: 29_360_128,
        Type: ModelType.Translation
    );

    public static readonly ModelDescriptor FastTextLid = new(
        Id: "fasttext-lid",
        DisplayName: "Language Detection",
        DownloadUrl: "https://dl.fbaipublicfiles.com/fasttext/supervised-models/lid.176.ftz",
        SizeBytes: 917_734,
        Type: ModelType.LanguageDetection
    );

    public static readonly ModelDescriptor Qwen25_15B = new(
        Id: "qwen2.5-1.5b-q4",
        DisplayName: "AI Polish (Qwen2.5-1.5B)",
        DownloadUrl: "https://huggingface.co/Qwen/Qwen2.5-1.5B-Instruct-GGUF/resolve/main/qwen2.5-1.5b-instruct-q4_k_m.gguf",
        SizeBytes: 1_073_741_824,
        Type: ModelType.PostProcessing
    );

    public static IReadOnlyList<ModelDescriptor> All => [MarianZhEn, MarianJaEn, FastTextLid, Qwen25_15B];
    public static IReadOnlyList<ModelDescriptor> TranslationModels => [MarianZhEn, MarianJaEn];
}
```

### 1.6 下载行为

- 使用 `HttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead)` 流式下载
- 每 100ms 报告一次进度
- 支持 `Range` header 断点续传（检测临时文件 `.part`）
- 下载完成后原子重命名 `.part` → 正式文件
- 取消时保留 `.part` 文件以便续传
- 并发下载保护（同一模型不重复下载）

### 1.7 错误处理

| 错误场景 | 处理 |
|---------|------|
| 网络断开 | 抛出 `ModelDownloadException`，保留 .part |
| 磁盘空间不足 | 下载前检查可用空间，不足时抛出 `InsufficientDiskSpaceException` |
| 文件损坏 | 校验 SHA256，不匹配则删除并抛出 `ModelCorruptException` |
| URL 404 | 抛出 `ModelNotFoundException` |

## 2. MarianMT 翻译引擎

### 2.1 ONNX 模型文件

HuggingFace 上的 MarianMT ONNX 模型包含：
- `encoder_model.onnx` — encoder 部分
- `decoder_model.onnx` 或 `decoder_model_merged.onnx` — decoder 部分
- `source.spm` — SentencePiece 源语言分词模型
- `target.spm` — SentencePiece 目标语言分词模型
- `tokenizer_config.json` — 分词器配置

注意：部分 HuggingFace 模型提供合并的单文件 `model.onnx`，部分拆分为 encoder/decoder。
需要在 ModelRegistry 中标注模型结构类型。

### 2.2 推理流程

```
输入: "你好世界"
  │
  ▼
┌────────────────────────────────────────┐
│ 1. SentencePiece Encode                │
│    source.spm.Encode("你好世界")        │
│    → token_ids: [256, 1024, 8192, ...]  │
│    → attention_mask: [1, 1, 1, ...]     │
└────────────────────┬───────────────────┘
                     ▼
┌────────────────────────────────────────┐
│ 2. ONNX Encoder                        │
│    input:  input_ids, attention_mask    │
│    output: encoder_hidden_states       │
└────────────────────┬───────────────────┘
                     ▼
┌────────────────────────────────────────┐
│ 3. ONNX Decoder (autoregressive)       │
│    循环生成 token 直到 EOS              │
│    每步输入: decoder_input_ids,         │
│             encoder_hidden_states      │
│    每步输出: next_token_logits          │
│    beam_size: 4 (可配置)                │
└────────────────────┬───────────────────┘
                     ▼
┌────────────────────────────────────────┐
│ 4. SentencePiece Decode                │
│    target.spm.Decode(output_ids)       │
│    → "Hello world"                     │
└────────────────────────────────────────┘
```

### 2.3 类设计

```csharp
namespace LiveLingo.Core.Engines;

public class MarianOnnxEngine : ITranslationEngine
{
    private readonly IModelManager _modelManager;
    private readonly ILogger<MarianOnnxEngine> _logger;
    private readonly ConcurrentDictionary<string, ModelSession> _sessions = new();

    public async Task<string> TranslateAsync(
        string text, string sourceLang, string targetLang, CancellationToken ct)
    {
        var modelId = $"marian-{sourceLang}-{targetLang}";
        var session = await GetOrLoadSessionAsync(modelId, ct);

        var inputIds = session.Tokenizer.Encode(text);
        var result = await session.RunInferenceAsync(inputIds, ct);
        return session.Tokenizer.Decode(result);
    }

    public bool SupportsLanguagePair(string src, string tgt)
        => ModelRegistry.TranslationModels.Any(m => m.Id == $"marian-{src}-{tgt}");

    private async Task<ModelSession> GetOrLoadSessionAsync(string modelId, CancellationToken ct)
    {
        if (_sessions.TryGetValue(modelId, out var cached))
            return cached;

        var descriptor = ModelRegistry.All.First(m => m.Id == modelId);
        await _modelManager.EnsureModelAsync(descriptor, progress: null, ct);

        var modelPath = Path.Combine(_modelManager.GetModelPath(modelId));
        var session = new ModelSession(modelPath);
        _sessions.TryAdd(modelId, session);
        return session;
    }

    public void Dispose()
    {
        foreach (var session in _sessions.Values)
            session.Dispose();
        _sessions.Clear();
    }
}
```

### 2.4 ModelSession（内部类）

```csharp
internal class ModelSession : IDisposable
{
    public SentencePieceTokenizer Tokenizer { get; }
    private readonly InferenceSession _encoderSession;
    private readonly InferenceSession _decoderSession;

    public ModelSession(string modelDirectory)
    {
        var sessionOptions = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            InterOpNumThreads = 2,
            IntraOpNumThreads = 4
        };

        _encoderSession = new InferenceSession(
            Path.Combine(modelDirectory, "encoder_model.onnx"), sessionOptions);
        _decoderSession = new InferenceSession(
            Path.Combine(modelDirectory, "decoder_model_merged.onnx"), sessionOptions);

        Tokenizer = new SentencePieceTokenizer(
            Path.Combine(modelDirectory, "source.spm"),
            Path.Combine(modelDirectory, "target.spm"));
    }

    public async Task<int[]> RunInferenceAsync(int[] inputIds, CancellationToken ct)
    {
        // Encoder
        var encoderOutput = RunEncoder(inputIds);
        ct.ThrowIfCancellationRequested();

        // Decoder (autoregressive beam search)
        return await Task.Run(() => BeamSearchDecode(encoderOutput, ct), ct);
    }

    // ...
}
```

### 2.5 SentencePiece 分词器

.NET 生态中 SentencePiece 的选项：

| 方案 | 特点 |
|------|------|
| SentencePieceSharp (NuGet) | 社区维护，可能不活跃 |
| 自建 P/Invoke 绑定 | 控制力强，需编译 native lib |
| 预处理为 BPE 词表 + 纯 C# 实现 | 无 native 依赖，但兼容性风险 |

推荐：先评估 SentencePieceSharp，如不可用再自建 P/Invoke。

```csharp
public class SentencePieceTokenizer : IDisposable
{
    private readonly IntPtr _sourceProcessor;
    private readonly IntPtr _targetProcessor;

    public int[] Encode(string text)
    {
        // 调用 sp_encode_as_ids
        // 添加 BOS/EOS token
    }

    public string Decode(int[] ids)
    {
        // 调用 sp_decode_from_ids
        // 去除特殊 token
    }
}
```

### 2.6 Beam Search 解码

简化实现（beam_size=1 即 greedy search 作为起点）：

```
初始化: decoder_input = [BOS_TOKEN]
循环:
  1. decoder(decoder_input, encoder_output) → logits
  2. next_token = argmax(logits[-1])
  3. if next_token == EOS_TOKEN: break
  4. decoder_input = append(decoder_input, next_token)
  5. if len(decoder_input) > max_length: break
返回: decoder_input (不含 BOS/EOS)
```

后续可优化为 beam_size=4 的标准 beam search。

## 3. 语言检测

### 3.1 FastText 集成

FastText `lid.176.ftz` 模型 (~1MB) 可通过 ONNX 或原生方式推理。

方案 A — FastText 原生（推荐）：
- 使用 FastText 的二进制格式直接解析
- 社区有 .NET 绑定：FastText.NetWrapper 或自建
- 推理极快 (<1ms)

方案 B — 简化实现：
- 使用 n-gram + 字符分布的简单启发式算法
- 区分 CJK / Latin / Cyrillic 等脚本
- 精度较低但零依赖

```csharp
namespace LiveLingo.Core.LanguageDetection;

public class FastTextDetector : ILanguageDetector
{
    private readonly string _modelPath;
    private FastTextModel? _model;

    public async Task<DetectionResult> DetectAsync(string text, CancellationToken ct)
    {
        _model ??= await Task.Run(() => FastTextModel.Load(_modelPath), ct);
        var prediction = _model.Predict(text, k: 1);
        return new DetectionResult(
            Language: prediction.Label.Replace("__label__", ""),
            Confidence: prediction.Score
        );
    }
}
```

### 3.2 降级策略

如果 FastText 模型未下载，使用 Unicode script 启发式检测：

```csharp
public class ScriptBasedDetector : ILanguageDetector
{
    public Task<DetectionResult> DetectAsync(string text, CancellationToken ct)
    {
        // CJK Unified Ideographs → "zh"
        // Hiragana/Katakana → "ja"
        // Hangul → "ko"
        // Cyrillic → "ru"
        // Latin (default) → "en"
        // 其他 → "unknown"
    }
}
```

## 4. TranslationPipeline 组装

### 4.1 完整实现

```csharp
namespace LiveLingo.Core.Translation;

public class TranslationPipeline : ITranslationPipeline
{
    private readonly ILanguageDetector _detector;
    private readonly ITranslationEngine _engine;
    private readonly IEnumerable<ITextProcessor> _processors;
    private readonly ILogger<TranslationPipeline> _logger;

    public TranslationPipeline(
        ILanguageDetector detector,
        ITranslationEngine engine,
        IEnumerable<ITextProcessor> processors,
        ILogger<TranslationPipeline> logger)
    {
        _detector = detector;
        _engine = engine;
        _processors = processors;
        _logger = logger;
    }

    public async Task<TranslationResult> ProcessAsync(
        TranslationRequest request, CancellationToken ct)
    {
        // 1. 语言检测
        var srcLang = request.SourceLanguage;
        if (string.IsNullOrEmpty(srcLang))
        {
            var detection = await _detector.DetectAsync(request.SourceText, ct);
            srcLang = detection.Language;
            _logger.LogDebug("Detected language: {Lang} ({Conf:P0})",
                detection.Language, detection.Confidence);
        }

        // 源语言与目标语言相同时直接返回
        if (srcLang == request.TargetLanguage)
        {
            return new TranslationResult(
                request.SourceText, srcLang, request.SourceText,
                TimeSpan.Zero, null);
        }

        ct.ThrowIfCancellationRequested();

        // 2. 翻译
        var sw = Stopwatch.StartNew();
        var translated = await _engine.TranslateAsync(
            request.SourceText, srcLang, request.TargetLanguage, ct);
        var translationDuration = sw.Elapsed;

        ct.ThrowIfCancellationRequested();

        // 3. 后处理 (P2 阶段: processors 为空，跳过)
        var finalText = translated;
        TimeSpan? postDuration = null;

        if (request.PostProcessing is { } opts)
        {
            sw.Restart();
            foreach (var proc in SelectProcessors(opts))
            {
                ct.ThrowIfCancellationRequested();
                finalText = await proc.ProcessAsync(
                    finalText, request.TargetLanguage, ct);
            }
            postDuration = sw.Elapsed;
        }

        return new TranslationResult(
            finalText, srcLang, translated,
            translationDuration, postDuration);
    }

    private IEnumerable<ITextProcessor> SelectProcessors(ProcessingOptions opts)
    {
        if (opts.Summarize)
            yield return _processors.First(p => p.Name == "summarize");
        if (opts.Optimize)
            yield return _processors.First(p => p.Name == "optimize");
        if (opts.Colloquialize)
            yield return _processors.First(p => p.Name == "colloquialize");
    }
}
```

### 4.2 DI 注册（P2 阶段）

```csharp
public static IServiceCollection AddLiveLingoCore(
    this IServiceCollection services,
    Action<CoreOptions>? configure = null)
{
    // options
    var options = new CoreOptions();
    configure?.Invoke(options);
    services.AddSingleton(Options.Create(options));

    // infrastructure
    services.AddHttpClient<IModelManager, ModelManager>();

    // engines (P2: 真实实现)
    services.AddSingleton<ITranslationEngine, MarianOnnxEngine>();
    services.AddSingleton<ILanguageDetector, FastTextDetector>();

    // pipeline
    services.AddSingleton<ITranslationPipeline, TranslationPipeline>();

    // processors: P2 不注册, P3 才加

    return services;
}
```

## 5. ViewModel 集成

### 5.1 状态显示

翻译过程中 OverlayViewModel 的 StatusText 变化：

```
IDLE:         "Ctrl+Enter paste & send · Esc cancel"
TRANSLATING:  "Translating..."
TRANSLATED:   "Translated (150ms) · Ctrl+Enter paste & send"
ERROR:        "Error: Model not found for ja→en"
```

### 5.2 Cancel 行为

```
用户打字 "你" → 触发翻译 #1
用户打字 "你好" → 取消 #1, 触发翻译 #2
用户打字 "你好世" → 取消 #2, 触发翻译 #3
用户停止打字 → 翻译 #3 完成, 显示结果
```

无 debounce（PoC 有 200ms debounce）。
MarianMT 足够快 (<200ms)，每次变化直接触发。
如果发现性能问题再加 debounce。

## 6. 基础模型下载 UI

### 6.1 触发时机

App 启动时检查核心模型是否存在：
- `fasttext-lid` — 语言检测（必需）
- `marian-zh-en` — 默认翻译对（必需）

如果任一缺失，显示下载对话框后才进入正常模式。

### 6.2 UI

简单的 Window，显示进度：

```
┌─────────────────────────────────────────┐
│  Setting up LiveLingo...                 │
│                                          │
│  Downloading translation models:         │
│                                          │
│  ☑ Language Detection (1 MB)      Done   │
│  ◻ Chinese → English (30 MB)  ████░ 80%  │
│                                          │
│                        [Cancel]          │
└─────────────────────────────────────────┘
```

## 7. 新增 NuGet 依赖

| 包 | 版本 | 项目 | 用途 |
|----|------|------|------|
| Microsoft.ML.OnnxRuntime | 1.* | Core | MarianMT ONNX 推理 |
| Microsoft.Extensions.Http | 10.* | Core | HttpClient DI |

SentencePiece 绑定根据评估结果选定（SentencePieceSharp 或自建）。
FastText 绑定根据评估结果选定。

## 8. 测试策略

### 8.1 单元测试

```
LiveLingo.Core.Tests/
├── Models/
│   └── ModelManagerTests.cs       // mock HttpClient, 测试下载/缓存/删除
├── Engines/
│   └── MarianOnnxEngineTests.cs   // 集成测试, 需要真实模型文件
├── LanguageDetection/
│   └── FastTextDetectorTests.cs   // 集成测试
└── Translation/
    └── TranslationPipelineTests.cs // mock engine/detector, 测试编排
```

### 8.2 集成测试 checklist

- [ ] 下载 MarianMT zh→en 模型到临时目录
- [ ] 翻译 "你好世界" → 包含 "hello" / "world" (不区分大小写)
- [ ] 检测 "你好" → "zh", 置信度 > 0.8
- [ ] 检测 "hello" → "en", 置信度 > 0.8
- [ ] Pipeline 端到端: 中文输入 → 英文输出
- [ ] 取消: 翻译进行中取消, 不抛非 OperationCanceledException 的异常
