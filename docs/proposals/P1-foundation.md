# P1: Foundation & Architecture

> 将 PoC 单项目结构重构为正式多项目架构，定义所有接口，迁移现有实现。

## 目标

- 建立 LiveLingo.Core + LiveLingo.App 多项目结构
- 定义 Core 层全部公开接口（翻译管线、引擎、后处理、语言检测、模型管理）
- 定义 App 层平台抽象接口（快捷键、窗口追踪、文本注入、剪贴板）
- 将 PoC Windows 代码迁移到平台接口实现
- 引入 DI 容器，替代 static 类直接调用
- 保留 stub 翻译，确保功能与 PoC 完全一致

## 不做

- 不引入任何 AI/ML 依赖
- 不改变 Windows 注入行为
- 不添加新功能

## 交付内容

### 1. Solution 结构

```
LiveLingo.slnx
├── src/
│   ├── LiveLingo.Core/
│   │   ├── LiveLingo.Core.csproj        (netstandard2.1 或 net10.0)
│   │   ├── ServiceCollectionExtensions.cs
│   │   ├── CoreOptions.cs
│   │   ├── Translation/
│   │   │   ├── ITranslationPipeline.cs
│   │   │   ├── TranslationPipeline.cs   (stub: 直通或 [EN] prefix)
│   │   │   ├── TranslationRequest.cs
│   │   │   └── TranslationResult.cs
│   │   ├── Engines/
│   │   │   ├── ITranslationEngine.cs
│   │   │   └── StubTranslationEngine.cs
│   │   ├── Processing/
│   │   │   ├── ITextProcessor.cs
│   │   │   └── ProcessingOptions.cs
│   │   ├── LanguageDetection/
│   │   │   └── ILanguageDetector.cs
│   │   └── Models/
│   │       ├── IModelManager.cs
│   │       ├── ModelDescriptor.cs
│   │       └── ModelDownloadProgress.cs
│   │
│   └── LiveLingo.App/
│       ├── LiveLingo.App.csproj          (引用 LiveLingo.Core)
│       ├── Program.cs
│       ├── App.axaml.cs                  (DI 组装)
│       ├── Views/
│       ├── ViewModels/
│       │   └── OverlayViewModel.cs       (注入 ITranslationPipeline)
│       └── Platform/
│           ├── IPlatformServices.cs
│           ├── IHotkeyService.cs
│           ├── IWindowTracker.cs
│           ├── ITextInjector.cs
│           ├── IClipboardService.cs
│           ├── TargetWindowInfo.cs
│           └── Windows/
│               ├── WindowsPlatformServices.cs
│               ├── Win32HotkeyService.cs     (← GlobalKeyboardHook.cs)
│               ├── Win32WindowTracker.cs      (← WindowTracker.cs)
│               ├── Win32TextInjector.cs       (← TextInjector.cs)
│               ├── Win32ClipboardService.cs   (← 从 TextInjector 提取)
│               └── NativeMethods.cs
```

### 2. Core 公开接口

见 product-design.md 第 5 节。此阶段只需定义接口 + stub 实现。

### 3. DI 注册

```csharp
// App.axaml.cs OnFrameworkInitializationCompleted
var services = new ServiceCollection();

services.AddLiveLingoCore(opt =>
{
    opt.ModelStoragePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LiveLingo", "models");
});

if (OperatingSystem.IsWindows())
    services.AddSingleton<IPlatformServices, WindowsPlatformServices>();

_serviceProvider = services.BuildServiceProvider();
```

### 4. ViewModel 改造

OverlayViewModel 当前直接 `new` 和调用 static 方法。改为构造函数注入：

```
Before:  OverlayViewModel(TargetWindowInfo target)
         内部直接调用 TextInjector.InjectText(...)

After:   OverlayViewModel(TargetWindowInfo target,
                          ITranslationPipeline pipeline)
         注入由 OverlayWindow 传入
```

### 5. PoC 代码迁移映射

| PoC 文件 | 迁移到 | 变化 |
|----------|--------|------|
| GlobalKeyboardHook.cs | Platform/Windows/Win32HotkeyService.cs | 实现 IHotkeyService |
| WindowTracker.cs | Platform/Windows/Win32WindowTracker.cs | 实现 IWindowTracker |
| TextInjector.cs | Platform/Windows/Win32TextInjector.cs | 实现 ITextInjector (async) |
| NativeMethods.cs | Platform/Windows/NativeMethods.cs | 不变 |
| InjectionTest.cs | 保留或移至 tests/ | 诊断工具 |
| SlackAutoTest.cs | 保留或移至 tests/ | 诊断工具 |
| WindowDiagnostic.cs | 保留或移至 tests/ | 诊断工具 |

### 6. 诊断工具处理

PoC 的三个 CLI 诊断工具（--test-inject, --diag-window, --test-slack）：
- 保留在 App 项目中作为开发模式功能
- 或迁移到独立的 test/tools 项目

## 验收标准

- [ ] `dotnet build` 编译通过
- [ ] 运行应用，Ctrl+Alt+T 呼出 overlay
- [ ] 输入文字，显示 `[EN] xxx` stub 翻译
- [ ] Ctrl+Enter 注入到 Slack/Notepad（与 PoC 行为一致）
- [ ] OverlayViewModel 通过构造函数接收 ITranslationPipeline
- [ ] 无任何 static 类直接调用（NativeMethods 除外）
- [ ] LiveLingo.Core 项目无 Avalonia / 平台依赖
