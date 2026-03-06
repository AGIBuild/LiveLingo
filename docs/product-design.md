# LiveLingo 正式产品技术设计

> 基于 PoC 验证结果（见 poc-design.md），面向正式产品的架构设计。

## 1. 产品定位

桌面端实时翻译输入工具。用户在 Slack/Teams/Discord 等 IM 中按全局快捷键唤出翻译浮窗，
输入母语后实时翻译，支持 AI 驱动的总结、优化、口语化后处理，最终注入目标应用。

## 2. 技术决策

| 项 | 选型 | 理由 |
|----|------|------|
| 运行时 | .NET 10 | LTS，LibraryImport 性能好，跨平台 |
| UI | Avalonia 11.x | 跨平台桌面 UI，无边框透明窗口 |
| MVVM | CommunityToolkit.Mvvm | 源码生成器，零反射 |
| 翻译引擎 | MarianMT + ONNX Runtime | 每语言对 ~30MB，推理 <200ms |
| 后处理 LLM | Qwen2.5-1.5B + LLamaSharp | GGUF Q4 ~1GB，口语化/润色/总结 |
| 语言检测 | FastText (ONNX) 或 CLD3 | <1ms，自动识别源语言 |
| 架构预留 | ITranslationEngine 抽象 | v2 可替换为 CTranslate2 / Bergamot |

## 3. 分层架构

```
┌─────────────────────────────────────────────────┐
│              LiveLingo.App (Avalonia)             │
│         UI · 生命周期 · DI 组装 · 用户配置         │
│                                                   │
│  ┌─────────────────────────────────────────────┐ │
│  │           Platform (内部抽象)                 │ │
│  │  IPlatformServices                           │ │
│  │    ├── IHotkeyService                        │ │
│  │    ├── IWindowTracker                        │ │
│  │    ├── ITextInjector                         │ │
│  │    └── IClipboardService                     │ │
│  │                                              │ │
│  │  ┌──────────────┐  ┌──────────────────────┐ │ │
│  │  │   Windows     │  │      macOS           │ │ │
│  │  │ WH_KEYBOARD_LL│  │ CGEventTap           │ │ │
│  │  │ SendInput     │  │ AXUIElement          │ │ │
│  │  │ FindWindowEx  │  │ NSWorkspace          │ │ │
│  │  └──────────────┘  └──────────────────────┘ │ │
│  └─────────────────────────────────────────────┘ │
├─────────────────────────────────────────────────┤
│            LiveLingo.Core (可复用)                │
│                                                   │
│  ITranslationPipeline                            │
│    ├── ILanguageDetector                         │
│    ├── ITranslationEngine  (MarianMT/ONNX)       │
│    └── ITextProcessor[]    (Qwen/LLamaSharp)     │
│                                                   │
│  IModelManager                                   │
│    ├── 模型下载 / 缓存 / 版本管理                  │
│    └── 按需加载 / 卸载                             │
└─────────────────────────────────────────────────┘
```

### 3.1 层职责

| 层 | 项目 | 职责 | 可复用 |
|----|------|------|--------|
| App | LiveLingo.App | Avalonia UI、DI 组装、平台分发、配置持久化 | 否 |
| Platform | LiveLingo.App 内部 | 全局快捷键、窗口识别、文本注入、剪贴板 | 否 |
| Core | LiveLingo.Core | 翻译管线、AI 模型管理、语言检测、后处理 | **是** |

### 3.2 依赖方向

```
App ──依赖──▶ Core
App ──内含──▶ Platform (接口 + 实现)
Core ──不依赖──▶ App / Platform
```

Core 对外零依赖（除 ONNX Runtime / LLamaSharp 等推理库），
其他产品引用 Core 即可使用完整的翻译管线。

## 4. 翻译管线（Pipeline）

### 4.1 管线数据流

```
用户输入 (母语)
  │
  ▼
┌───────────────────┐
│   语言检测          │  FastText / CLD3 (<1ms)
│   auto → "zh"      │
└─────────┬─────────┘
          ▼
┌───────────────────┐
│   机器翻译          │  MarianMT via ONNX Runtime
│   zh → en           │  ~100-200ms/句
└─────────┬─────────┘
          ▼
┌───────────────────┐  ← 用户可选开关
│   后处理器链        │  Qwen2.5-1.5B via LLamaSharp
│   ┌─────────────┐ │
│   │ Summarize   │ │  缩短冗长内容
│   ├─────────────┤ │
│   │ Optimize    │ │  语法润色
│   ├─────────────┤ │
│   │ Colloquial  │ │  口语化（聊天风格）
│   └─────────────┘ │
└─────────┬─────────┘
          ▼
    TranslationResult
```

### 4.2 实时状态机

文本变化即触发翻译。前一次未完成则取消，使用最新内容重新翻译。

```
                      ┌──────────────────────────┐
                      │          IDLE             │
                      └────────────┬─────────────┘
                                   │ 文本变化 (debounce)
                                   ▼
  文本再次变化        ┌──────────────────────────┐
  (cancel+restart) ──▶│      TRANSLATING          │ MarianMT
                      │  preview = "翻译中..."     │
                      └────────────┬─────────────┘
                                   │ 翻译完成
                                   ▼
  文本再次变化        ┌──────────────────────────┐
  (cancel+restart) ──▶│    POST-PROCESSING        │ Qwen (如果启用)
                      │  preview = 原始翻译结果    │
                      └────────────┬─────────────┘
                                   │ 后处理完成
                                   ▼
                      ┌──────────────────────────┐
                      │         READY             │
                      │  preview = 最终润色结果    │
                      └──────────────────────────┘

任何阶段均可 Ctrl+Enter 注入当前 preview 文本（所见即所得）。
```

### 4.3 取消机制

```csharp
// ViewModel 中的核心逻辑
partial void OnSourceTextChanged(string value)
{
    _pipelineCts?.Cancel();
    _pipelineCts = new CancellationTokenSource();
    _ = RunPipelineAsync(value, _pipelineCts.Token);
}

async Task RunPipelineAsync(string text, CancellationToken ct)
{
    // Stage 1: MarianMT 翻译
    TranslatedText = await _pipeline.TranslateAsync(text, ct);
    ct.ThrowIfCancellationRequested();

    // Stage 2: Qwen 后处理 (如果启用)
    if (_options.PostProcessingEnabled)
        TranslatedText = await _pipeline.PostProcessAsync(TranslatedText, _options, ct);
}
```

## 5. Core 公开 API

### 5.1 管线接口

```csharp
public interface ITranslationPipeline
{
    Task<TranslationResult> ProcessAsync(
        TranslationRequest request, CancellationToken ct = default);
}

public record TranslationRequest(
    string SourceText,
    string? SourceLanguage,           // null = 自动检测
    string TargetLanguage,
    ProcessingOptions? PostProcessing  // null = 不后处理
);

public record ProcessingOptions(
    bool Summarize = false,
    bool Optimize = false,
    bool Colloquialize = false
);

public record TranslationResult(
    string Text,                      // 最终文本
    string DetectedSourceLanguage,
    string RawTranslation,            // MarianMT 原始翻译（后处理前）
    TimeSpan TranslationDuration,
    TimeSpan? PostProcessingDuration
);
```

### 5.2 引擎接口（架构预留替换）

```csharp
public interface ITranslationEngine
{
    Task<string> TranslateAsync(
        string text, string sourceLang, string targetLang,
        CancellationToken ct = default);

    IReadOnlyList<string> SupportedLanguagePairs { get; }
}

public interface ITextProcessor
{
    string Name { get; }                  // "summarize" / "optimize" / "colloquialize"

    Task<string> ProcessAsync(
        string text, string language,
        CancellationToken ct = default);
}

public interface ILanguageDetector
{
    Task<DetectionResult> DetectAsync(
        string text, CancellationToken ct = default);
}
```

### 5.3 模型管理

```csharp
public interface IModelManager
{
    Task EnsureModelAsync(
        ModelDescriptor descriptor,
        IProgress<ModelDownloadProgress>? progress = null,
        CancellationToken ct = default);

    IReadOnlyList<InstalledModel> ListInstalled();
    Task DeleteModelAsync(string modelId, CancellationToken ct = default);
    long GetTotalDiskUsage();
}

public record ModelDescriptor(
    string Id,              // "marian-zh-en"
    string DisplayName,     // "Chinese → English"
    string DownloadUrl,
    long SizeBytes,
    ModelType Type          // Translation / PostProcessing / LanguageDetection
);
```

### 5.4 DI 注册

```csharp
// 其他产品使用 Core
services.AddLiveLingoCore(options =>
{
    options.ModelStoragePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LiveLingo", "models");
    options.DefaultTargetLanguage = "en";
    options.PostProcessing.Enabled = true;
    options.PostProcessing.DefaultMode = ProcessingMode.Colloquialize;
});

// LiveLingo.App 额外注册平台服务
if (OperatingSystem.IsWindows())
    services.AddSingleton<IPlatformServices, WindowsPlatformServices>();
else if (OperatingSystem.IsMacOS())
    services.AddSingleton<IPlatformServices, MacPlatformServices>();
```

## 6. 模型规格

### 6.1 MarianMT (翻译)

| 项 | 值 |
|----|----|
| 来源 | Helsinki-NLP/opus-mt-{src}-{tgt} (HuggingFace) |
| 格式 | ONNX |
| 大小 | ~30MB/语言对 |
| 推理框架 | ONNX Runtime (Microsoft.ML.OnnxRuntime) |
| 推理速度 | <200ms/句 (CPU) |
| 分词 | SentencePiece (随模型分发) |
| 量化 | INT8 可用 |

### 6.2 Qwen2.5-1.5B (后处理)

| 项 | 值 |
|----|----|
| 来源 | Qwen/Qwen2.5-1.5B-Instruct (HuggingFace) |
| 格式 | GGUF (Q4_K_M 量化) |
| 大小 | ~1GB |
| 推理框架 | LLamaSharp (llama.cpp .NET 绑定) |
| 推理速度 | ~10 tok/s (CPU)，~50 tok/s (GPU) |
| 内存占用 | ~2GB 运行时 |

### 6.3 后处理 Prompt 策略

```
[Summarize]
System: You are a text editor. Shorten the following text while keeping
its core meaning. Output only the shortened text, nothing else.

[Optimize]
System: You are a grammar editor. Fix grammar and improve clarity of the
following text. Keep the same meaning and tone. Output only the improved
text, nothing else.

[Colloquialize]
System: You are a casual chat editor. Rewrite the following text in a
relaxed, conversational tone suitable for workplace chat (Slack/Teams).
Keep it natural and friendly. Output only the rewritten text, nothing else.
```

## 7. 平台层设计（App 内部）

### 7.1 接口定义

```csharp
public interface IPlatformServices
{
    IHotkeyService Hotkey { get; }
    IWindowTracker WindowTracker { get; }
    ITextInjector TextInjector { get; }
    IClipboardService Clipboard { get; }
}

public interface IHotkeyService : IDisposable
{
    event Action<HotkeyEventArgs> HotkeyTriggered;
    void Register(HotkeyBinding binding);
    void Unregister(HotkeyBinding binding);
}

public interface IWindowTracker
{
    TargetWindowInfo? GetForegroundWindowInfo();
}

public interface ITextInjector
{
    Task InjectAsync(TargetWindowInfo target, string text, bool autoSend);
}

public interface IClipboardService
{
    Task SetTextAsync(string text);
    Task<string?> GetTextAsync();
}
```

### 7.2 平台实现对照

| 接口方法 | Windows 实现 | macOS 实现 |
|----------|-------------|------------|
| Hotkey.Register | WH_KEYBOARD_LL 全局钩子 | CGEventTap |
| WindowTracker.GetForeground | GetForegroundWindow + FindChromeRenderer | NSWorkspace.frontmostApplication |
| TextInjector.Inject (策略1) | SendInput Ctrl+V | AXUIElement.setValue |
| TextInjector.Inject (策略2) | WM_CHAR → Chrome renderer | AXUIElement.setValue (无需备选) |
| Clipboard.SetText | OpenClipboard + SetClipboardData | NSPasteboard |

### 7.3 macOS 权限

| 权限 | 用途 | 配置位置 |
|------|------|----------|
| Accessibility | AXUIElement 读写 UI 元素 | Info.plist + 系统设置 |
| Input Monitoring | CGEventTap 全局键盘事件 | 系统设置 → 隐私与安全 |

首次启动需要引导用户授权，提供清晰的引导 UI。

## 8. 项目结构

```
LiveLingo/
├── src/
│   ├── LiveLingo.Core/                        # 核心业务（可复用 NuGet 包）
│   │   ├── LiveLingo.Core.csproj
│   │   ├── ServiceCollectionExtensions.cs     # AddLiveLingoCore()
│   │   ├── CoreOptions.cs
│   │   ├── Translation/
│   │   │   ├── ITranslationPipeline.cs
│   │   │   ├── TranslationPipeline.cs
│   │   │   ├── TranslationRequest.cs
│   │   │   └── TranslationResult.cs
│   │   ├── Engines/
│   │   │   ├── ITranslationEngine.cs
│   │   │   ├── MarianOnnxEngine.cs            # MarianMT + ONNX Runtime
│   │   │   └── SentencePieceTokenizer.cs
│   │   ├── Processing/
│   │   │   ├── ITextProcessor.cs
│   │   │   ├── ProcessingOptions.cs
│   │   │   ├── QwenProcessor.cs               # Qwen + LLamaSharp
│   │   │   └── Prompts/
│   │   │       ├── SummarizePrompt.cs
│   │   │       ├── OptimizePrompt.cs
│   │   │       └── ColloquializePrompt.cs
│   │   ├── LanguageDetection/
│   │   │   ├── ILanguageDetector.cs
│   │   │   └── FastTextDetector.cs
│   │   └── Models/
│   │       ├── IModelManager.cs
│   │       ├── ModelManager.cs
│   │       ├── ModelDescriptor.cs
│   │       └── ModelDownloadProgress.cs
│   │
│   └── LiveLingo.App/                         # Avalonia 桌面应用
│       ├── LiveLingo.App.csproj
│       ├── Program.cs
│       ├── App.axaml / .cs                    # DI 组装
│       ├── Configuration/
│       │   ├── AppSettings.cs
│       │   └── SettingsStore.cs               # JSON 持久化
│       ├── ViewModels/
│       │   ├── MainWindowViewModel.cs
│       │   ├── OverlayViewModel.cs
│       │   └── ModelSetupViewModel.cs         # 首次模型下载引导
│       ├── Views/
│       │   ├── MainWindow.axaml / .cs
│       │   ├── OverlayWindow.axaml / .cs
│       │   └── ModelSetupView.axaml / .cs
│       └── Platform/
│           ├── IPlatformServices.cs
│           ├── IHotkeyService.cs
│           ├── IWindowTracker.cs
│           ├── ITextInjector.cs
│           ├── IClipboardService.cs
│           ├── TargetWindowInfo.cs
│           ├── Windows/
│           │   ├── WindowsPlatformServices.cs
│           │   ├── Win32HotkeyService.cs
│           │   ├── Win32WindowTracker.cs
│           │   ├── Win32TextInjector.cs
│           │   ├── Win32ClipboardService.cs
│           │   └── NativeMethods.cs
│           └── macOS/
│               ├── MacPlatformServices.cs
│               ├── MacHotkeyService.cs
│               ├── MacWindowTracker.cs
│               └── MacTextInjector.cs
│
├── tests/
│   ├── LiveLingo.Core.Tests/
│   │   ├── Translation/
│   │   │   └── TranslationPipelineTests.cs
│   │   ├── Engines/
│   │   │   └── MarianOnnxEngineTests.cs
│   │   └── Processing/
│   │       └── QwenProcessorTests.cs
│   └── LiveLingo.App.Tests/
│       └── ViewModels/
│           └── OverlayViewModelTests.cs
│
├── docs/
│   ├── poc-design.md                          # PoC 技术记录
│   └── product-design.md                      # 本文档
│
└── models/                                    # 本地模型存储 (gitignored)
    └── .gitkeep
```

## 9. NuGet 依赖

### LiveLingo.Core

| 包 | 用途 |
|----|------|
| Microsoft.ML.OnnxRuntime | MarianMT 推理 |
| LLamaSharp | Qwen 推理 (llama.cpp 绑定) |
| LLamaSharp.Backend.Cpu | CPU 推理后端 |
| Microsoft.Extensions.DependencyInjection.Abstractions | DI 注册 |
| Microsoft.Extensions.Options | 配置模型 |
| Microsoft.Extensions.Logging.Abstractions | 日志抽象 |

### LiveLingo.App

| 包 | 用途 |
|----|------|
| Avalonia | UI 框架 |
| Avalonia.Desktop | 桌面平台支持 |
| Avalonia.Themes.Fluent | Fluent 主题 |
| CommunityToolkit.Mvvm | MVVM 基础设施 |
| System.Text.Json | 配置持久化 |

## 10. 首次运行体验

```
┌──────────────────────────────────────────┐
│            Welcome to LiveLingo           │
│                                          │
│  Translation models need to be           │
│  downloaded for first use.               │
│                                          │
│  ┌────────────────────────────────────┐  │
│  │ ☑ Chinese → English  (MarianMT)   │  │
│  │   30 MB                           │  │
│  ├────────────────────────────────────┤  │
│  │ ☑ AI Polish (Qwen2.5-1.5B)       │  │
│  │   1.0 GB                          │  │
│  ├────────────────────────────────────┤  │
│  │ ☐ Japanese → English  (MarianMT)  │  │
│  │   28 MB                           │  │
│  └────────────────────────────────────┘  │
│                                          │
│  Total: 1.03 GB                          │
│                                          │
│  [████████████░░░░░░░░] 62%  640 MB      │
│                                          │
│              [ Download ]                │
└──────────────────────────────────────────┘
```

## 11. 注入模式（延续 PoC）

| 模式 | 行为 |
|------|------|
| Paste Only | 翻译文本粘贴到目标输入框光标位置 |
| Paste & Send | 粘贴后自动按 Enter 发送 |

模式通过 overlay 底部按钮切换。选择持久化到配置文件。

## 12. 后续迭代方向

| 版本 | 里程碑 |
|------|--------|
| v1.0 | Core + Windows 平台 + MarianMT + Qwen 后处理 |
| v1.1 | macOS 平台支持 |
| v1.2 | 用户可配置快捷键、多语言对管理 |
| v2.0 | CTranslate2 替换 ONNX（翻译性能优化） |
| v2.1 | 流式翻译预览（边翻译边显示） |
| v2.2 | 插件系统（自定义后处理器） |
