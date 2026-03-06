# P2: Translation Core

> 实现模型管理、MarianMT 翻译引擎、语言检测、翻译管线，替换 stub 为真实翻译。

## 前置依赖

- P1 完成（Core 接口已定义，DI 已就位）

## 目标

- 实现 ModelManager：模型下载（带进度）、本地缓存、版本管理
- 实现 MarianMT 翻译引擎：ONNX Runtime 推理 + SentencePiece 分词
- 实现语言检测器：自动识别源语言
- 组装 TranslationPipeline：语言检测 → 翻译，cancel-and-restart
- 基础模型下载 UI
- 替换 stub，用户首次体验到真实翻译

## 不做

- 不实现 Qwen 后处理（P3）
- 不实现 macOS 平台（P4）
- 不实现持久化配置（P5）

## 交付内容

### 1. 模型管理

```csharp
// ModelManager 职责
public class ModelManager : IModelManager
{
    // 检查模型是否已下载
    // 从 HuggingFace 下载模型文件（断点续传）
    // 报告下载进度
    // 管理本地缓存目录
    // 列出已安装模型
    // 删除模型释放空间
    // 计算磁盘占用
}
```

模型存储结构：
```
{LocalAppData}/LiveLingo/models/
├── marian-zh-en/
│   ├── model.onnx
│   ├── source.spm        (SentencePiece source vocab)
│   ├── target.spm        (SentencePiece target vocab)
│   └── manifest.json     (版本、大小、来源)
├── marian-ja-en/
│   └── ...
└── fasttext-lid/
    └── lid.176.ftz        (语言检测模型)
```

### 2. MarianMT 翻译引擎

```
输入文本
  │
  ▼
┌──────────────────────────┐
│ SentencePiece Encode     │  source.spm
│ "你好世界" → [token_ids]  │
└────────────┬─────────────┘
             ▼
┌──────────────────────────┐
│ ONNX Runtime Inference   │  model.onnx
│ [src_ids] → [tgt_ids]    │
└────────────┬─────────────┘
             ▼
┌──────────────────────────┐
│ SentencePiece Decode     │  target.spm
│ [tgt_ids] → "Hello world"│
└──────────────────────────┘
```

关键技术点：
- ONNX 模型的 beam search 解码需要自行实现或使用 ONNX Runtime Extensions
- SentencePiece 分词器需要 .NET 绑定（SentencePieceSharp 或自建 P/Invoke）
- 模型加载应延迟到首次翻译时（lazy loading）
- InferenceSession 线程安全，可复用

### 3. 语言检测

使用 FastText 的 lid.176.ftz 模型（~1MB）：
- 支持 176 种语言
- 推理 <1ms
- 确定源语言后自动选择对应的 MarianMT 模型

### 4. 翻译管线

```csharp
public class TranslationPipeline : ITranslationPipeline
{
    public async Task<TranslationResult> ProcessAsync(
        TranslationRequest request, CancellationToken ct)
    {
        // 1. 语言检测（如果未指定源语言）
        var srcLang = request.SourceLanguage
            ?? (await _detector.DetectAsync(request.SourceText, ct)).Language;

        ct.ThrowIfCancellationRequested();

        // 2. 确保模型已下载
        var modelId = $"marian-{srcLang}-{request.TargetLanguage}";
        await _modelManager.EnsureModelAsync(
            _registry.Get(modelId), progress: null, ct);

        // 3. 翻译
        var sw = Stopwatch.StartNew();
        var translated = await _engine.TranslateAsync(
            request.SourceText, srcLang, request.TargetLanguage, ct);
        var translationTime = sw.Elapsed;

        ct.ThrowIfCancellationRequested();

        // 4. 后处理（P3 实现，此阶段跳过）
        // ...

        return new TranslationResult(
            Text: translated,
            DetectedSourceLanguage: srcLang,
            RawTranslation: translated,
            TranslationDuration: translationTime,
            PostProcessingDuration: null);
    }
}
```

### 5. ViewModel 集成

```csharp
// OverlayViewModel — cancel-and-restart 模式
private CancellationTokenSource? _pipelineCts;

partial void OnSourceTextChanged(string value)
{
    _pipelineCts?.Cancel();
    _pipelineCts = new CancellationTokenSource();
    _ = RunPipelineAsync(value, _pipelineCts.Token);
}

private async Task RunPipelineAsync(string text, CancellationToken ct)
{
    if (string.IsNullOrWhiteSpace(text))
    {
        TranslatedText = string.Empty;
        return;
    }

    try
    {
        StatusText = "Translating...";
        var result = await _pipeline.ProcessAsync(
            new TranslationRequest(text, null, _targetLanguage, null), ct);
        TranslatedText = result.Text;
        StatusText = $"Translated ({result.TranslationDuration.TotalMilliseconds:0}ms)";
    }
    catch (OperationCanceledException) { }
    catch (Exception ex)
    {
        StatusText = $"Error: {ex.Message}";
    }
}
```

### 6. 基础模型下载 UI

首次启动检测模型缺失时，显示简单下载对话框：
- 显示需要下载的模型列表和大小
- 下载进度条
- 下载完成后自动进入正常模式

不需要复杂的多语言对选择（P5 做）。v1 默认只下载 zh→en + FastText。

## 新增 NuGet 依赖

| 包 | 项目 | 用途 |
|----|------|------|
| Microsoft.ML.OnnxRuntime | Core | MarianMT + FastText 推理 |
| Microsoft.Extensions.DependencyInjection | Core | DI 基础设施 |
| Microsoft.Extensions.Options | Core | 配置模型 |
| Microsoft.Extensions.Logging.Abstractions | Core | 日志 |

SentencePiece 分词器：评估 SentencePieceSharp 或自建 native 绑定。

## 验收标准

- [ ] 首次启动自动下载 MarianMT zh→en 模型 + FastText 模型
- [ ] 输入中文，200ms 内显示英文翻译
- [ ] 快速连续输入时，前一次翻译被取消，只显示最新结果
- [ ] 输入日文/韩文，语言自动检测正确（但翻译失败 — 模型未下载，显示错误信息）
- [ ] Ctrl+Enter 注入真实翻译结果到 Slack
- [ ] 模型文件缓存在 LocalAppData，二次启动无需下载
- [ ] Core 项目可独立编译，无 Avalonia 依赖
