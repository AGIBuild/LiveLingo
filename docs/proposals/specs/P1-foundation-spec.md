# P1 Spec: Foundation & Architecture

## 1. 项目与 Solution 结构

### 1.1 Solution 文件

```xml
<!-- LiveLingo.slnx -->
<Solution>
  <Folder Name="/src/">
    <Project Path="src/LiveLingo.Core/LiveLingo.Core.csproj" />
    <Project Path="src/LiveLingo.App/LiveLingo.App.csproj" />
  </Folder>
  <Folder Name="/tests/">
    <Project Path="tests/LiveLingo.Core.Tests/LiveLingo.Core.Tests.csproj" />
  </Folder>
</Solution>
```

### 1.2 LiveLingo.Core.csproj

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="10.*" />
    <PackageReference Include="Microsoft.Extensions.Options" Version="10.*" />
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="10.*" />
  </ItemGroup>
</Project>
```

P1 阶段 Core 不引入 ONNX Runtime / LLamaSharp（P2/P3 才加）。

### 1.3 LiveLingo.App.csproj

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <ApplicationManifest>app.manifest</ApplicationManifest>
    <AvaloniaUseCompiledBindingsByDefault>true</AvaloniaUseCompiledBindingsByDefault>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\LiveLingo.Core\LiveLingo.Core.csproj" />
    <PackageReference Include="Avalonia" Version="11.*" />
    <PackageReference Include="Avalonia.Desktop" Version="11.*" />
    <PackageReference Include="Avalonia.Themes.Fluent" Version="11.*" />
    <PackageReference Include="Avalonia.Fonts.Inter" Version="11.*" />
    <PackageReference Include="CommunityToolkit.Mvvm" Version="8.*" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="10.*" />
  </ItemGroup>
</Project>
```

## 2. LiveLingo.Core 公开接口

### 2.1 翻译管线

```csharp
namespace LiveLingo.Core.Translation;

public interface ITranslationPipeline
{
    Task<TranslationResult> ProcessAsync(
        TranslationRequest request,
        CancellationToken ct = default);
}

public record TranslationRequest(
    string SourceText,
    string? SourceLanguage,           // null = 自动检测
    string TargetLanguage,
    ProcessingOptions? PostProcessing  // null = 不后处理
);

public record TranslationResult(
    string Text,
    string DetectedSourceLanguage,
    string RawTranslation,
    TimeSpan TranslationDuration,
    TimeSpan? PostProcessingDuration
);
```

### 2.2 翻译引擎

```csharp
namespace LiveLingo.Core.Engines;

public interface ITranslationEngine : IDisposable
{
    Task<string> TranslateAsync(
        string text,
        string sourceLanguage,
        string targetLanguage,
        CancellationToken ct = default);

    bool SupportsLanguagePair(string sourceLanguage, string targetLanguage);
}
```

### 2.3 文本后处理器

```csharp
namespace LiveLingo.Core.Processing;

public interface ITextProcessor : IDisposable
{
    string Name { get; }

    Task<string> ProcessAsync(
        string text,
        string language,
        CancellationToken ct = default);
}

public record ProcessingOptions(
    bool Summarize = false,
    bool Optimize = false,
    bool Colloquialize = false
);

public enum ProcessingMode
{
    Off,
    Summarize,
    Optimize,
    Colloquialize
}
```

### 2.4 语言检测

```csharp
namespace LiveLingo.Core.LanguageDetection;

public interface ILanguageDetector : IDisposable
{
    Task<DetectionResult> DetectAsync(
        string text,
        CancellationToken ct = default);
}

public record DetectionResult(
    string Language,       // ISO 639-1 code: "zh", "en", "ja"
    float Confidence       // 0.0 - 1.0
);
```

### 2.5 模型管理

```csharp
namespace LiveLingo.Core.Models;

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
    string Id,                // "marian-zh-en"
    string DisplayName,       // "Chinese → English"
    string DownloadUrl,       // HuggingFace URL
    long SizeBytes,
    ModelType Type
);

public enum ModelType
{
    Translation,
    PostProcessing,
    LanguageDetection
}

public record InstalledModel(
    string Id,
    string DisplayName,
    string LocalPath,
    long SizeBytes,
    ModelType Type,
    DateTime InstalledAt
);

public record ModelDownloadProgress(
    string ModelId,
    long BytesDownloaded,
    long TotalBytes
)
{
    public double Percentage => TotalBytes > 0
        ? (double)BytesDownloaded / TotalBytes * 100
        : 0;
}
```

### 2.6 DI 注册扩展

```csharp
namespace LiveLingo.Core;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddLiveLingoCore(
        this IServiceCollection services,
        Action<CoreOptions>? configure = null)
    {
        var options = new CoreOptions();
        configure?.Invoke(options);
        services.AddSingleton(Options.Create(options));

        services.AddSingleton<ITranslationPipeline, TranslationPipeline>();
        services.AddSingleton<IModelManager, StubModelManager>();

        // P1: stub 实现
        services.AddSingleton<ITranslationEngine, StubTranslationEngine>();
        services.AddSingleton<ILanguageDetector, StubLanguageDetector>();
        // 无 ITextProcessor 注册 (P3 才加)

        return services;
    }
}

public class CoreOptions
{
    public string ModelStoragePath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LiveLingo", "models");

    public string DefaultTargetLanguage { get; set; } = "en";
}
```

### 2.7 Stub 实现（P1 阶段临时使用）

```csharp
// StubTranslationEngine: 直接返回 "[EN] {input}"
// StubLanguageDetector: 始终返回 ("zh", 1.0)
// StubModelManager: EnsureModelAsync 立即返回，ListInstalled 返回空列表
```

## 3. Platform 接口（App 内部）

### 3.1 聚合接口

```csharp
namespace LiveLingo.App.Platform;

public interface IPlatformServices : IDisposable
{
    IHotkeyService Hotkey { get; }
    IWindowTracker WindowTracker { get; }
    ITextInjector TextInjector { get; }
    IClipboardService Clipboard { get; }
}
```

### 3.2 快捷键服务

```csharp
namespace LiveLingo.App.Platform;

public interface IHotkeyService : IDisposable
{
    event Action<HotkeyEventArgs>? HotkeyTriggered;
    void Register(HotkeyBinding binding);
    void Unregister(string hotkeyId);
}

public record HotkeyBinding(
    string Id,
    KeyModifiers Modifiers,   // Ctrl, Alt, Shift, Meta
    string Key                // "T", "Space", etc.
);

public record HotkeyEventArgs(string HotkeyId);

[Flags]
public enum KeyModifiers
{
    None = 0,
    Ctrl = 1,
    Alt = 2,
    Shift = 4,
    Meta = 8    // Win / Cmd
}
```

### 3.3 窗口追踪

```csharp
namespace LiveLingo.App.Platform;

public interface IWindowTracker
{
    TargetWindowInfo? GetForegroundWindowInfo();
}

public record TargetWindowInfo(
    nint Handle,             // 主窗口句柄 (Windows HWND / macOS window number)
    nint InputChildHandle,   // 输入子窗口 (Windows Chrome renderer / macOS unused)
    string ProcessName,
    string Title,
    int Left, int Top, int Width, int Height
);
```

### 3.4 文本注入

```csharp
namespace LiveLingo.App.Platform;

public interface ITextInjector
{
    Task InjectAsync(
        TargetWindowInfo target,
        string text,
        bool autoSend,
        CancellationToken ct = default);
}
```

注意：PoC 中 `TextInjector.InjectText` 是同步阻塞（Thread.Sleep）。
P1 将其包装为 async 接口，内部仍可用 Task.Run + 同步调用实现。
后续优化为真正的 async。

### 3.5 剪贴板服务

```csharp
namespace LiveLingo.App.Platform;

public interface IClipboardService
{
    Task SetTextAsync(string text, CancellationToken ct = default);
    Task<string?> GetTextAsync(CancellationToken ct = default);
}
```

从 TextInjector 中提取剪贴板操作到独立服务。
TextInjector 通过 IClipboardService 设置剪贴板，然后执行注入。

## 4. Windows 平台实现迁移

### 4.1 文件迁移映射

| PoC 源文件 | P1 目标文件 | 实现接口 |
|-----------|------------|----------|
| Services/Platform/Windows/GlobalKeyboardHook.cs | Platform/Windows/Win32HotkeyService.cs | IHotkeyService |
| Services/Platform/Windows/WindowTracker.cs | Platform/Windows/Win32WindowTracker.cs | IWindowTracker |
| Services/Platform/Windows/TextInjector.cs | Platform/Windows/Win32TextInjector.cs | ITextInjector |
| (从 TextInjector 提取) | Platform/Windows/Win32ClipboardService.cs | IClipboardService |
| Services/Platform/Windows/NativeMethods.cs | Platform/Windows/NativeMethods.cs | (不变) |

### 4.2 WindowsPlatformServices

```csharp
namespace LiveLingo.App.Platform.Windows;

internal class WindowsPlatformServices : IPlatformServices
{
    public IHotkeyService Hotkey { get; }
    public IWindowTracker WindowTracker { get; }
    public ITextInjector TextInjector { get; }
    public IClipboardService Clipboard { get; }

    public WindowsPlatformServices()
    {
        Clipboard = new Win32ClipboardService();
        Hotkey = new Win32HotkeyService();
        WindowTracker = new Win32WindowTracker();
        TextInjector = new Win32TextInjector(Clipboard);
    }

    public void Dispose()
    {
        Hotkey.Dispose();
    }
}
```

## 5. DI 组装（App.axaml.cs）

```csharp
public partial class App : Application
{
    private ServiceProvider? _serviceProvider;

    public override void OnFrameworkInitializationCompleted()
    {
        var services = new ServiceCollection();

        services.AddLiveLingoCore();

        if (OperatingSystem.IsWindows())
            services.AddSingleton<IPlatformServices, WindowsPlatformServices>();
        // macOS: P4

        _serviceProvider = services.BuildServiceProvider();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;

            var platform = _serviceProvider.GetRequiredService<IPlatformServices>();
            platform.Hotkey.Register(new HotkeyBinding("overlay", KeyModifiers.Ctrl | KeyModifiers.Alt, "T"));
            platform.Hotkey.HotkeyTriggered += args =>
                Dispatcher.UIThread.Post(() => ShowOverlay(platform));
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void ShowOverlay(IPlatformServices platform)
    {
        var target = platform.WindowTracker.GetForegroundWindowInfo();
        if (target is null) return;

        // 跳过自身窗口
        // ...

        var pipeline = _serviceProvider!.GetRequiredService<ITranslationPipeline>();
        var vm = new OverlayViewModel(target, pipeline, platform.TextInjector);
        var overlay = new OverlayWindow(vm);
        // 定位、显示...
    }
}
```

## 6. OverlayViewModel 改造

```csharp
public partial class OverlayViewModel : ObservableObject
{
    private readonly TargetWindowInfo _target;
    private readonly ITranslationPipeline _pipeline;
    private readonly ITextInjector _injector;
    private CancellationTokenSource? _pipelineCts;

    // 构造函数注入取代 static 调用
    public OverlayViewModel(
        TargetWindowInfo target,
        ITranslationPipeline pipeline,
        ITextInjector injector)
    {
        _target = target;
        _pipeline = pipeline;
        _injector = injector;
    }

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
            var result = await _pipeline.ProcessAsync(
                new TranslationRequest(text, null, "en", null), ct);
            TranslatedText = result.Text;
        }
        catch (OperationCanceledException) { }
    }

    // 注入调用改为通过 ITextInjector
    public async Task InjectAsync(bool autoSend)
    {
        if (string.IsNullOrWhiteSpace(TranslatedText)) return;
        await _injector.InjectAsync(_target, TranslatedText, autoSend);
    }
}
```

## 7. 诊断工具处理

PoC 的三个 CLI 工具保留在 App 项目内，仅在 DEBUG 配置下编译：

```csharp
#if DEBUG
if (args.Contains("--test-inject")) { InjectionTest.Run(); return; }
if (args.Contains("--diag-window")) { WindowDiagnostic.Run(...); return; }
if (args.Contains("--test-slack")) { SlackAutoTest.Run(); return; }
#endif
```

## 8. 目录结构（最终）

```
src/
├── LiveLingo.Core/
│   ├── LiveLingo.Core.csproj
│   ├── CoreOptions.cs
│   ├── ServiceCollectionExtensions.cs
│   ├── Translation/
│   │   ├── ITranslationPipeline.cs
│   │   ├── TranslationPipeline.cs
│   │   ├── TranslationRequest.cs
│   │   └── TranslationResult.cs
│   ├── Engines/
│   │   ├── ITranslationEngine.cs
│   │   └── StubTranslationEngine.cs
│   ├── Processing/
│   │   ├── ITextProcessor.cs
│   │   └── ProcessingOptions.cs
│   ├── LanguageDetection/
│   │   ├── ILanguageDetector.cs
│   │   ├── DetectionResult.cs
│   │   └── StubLanguageDetector.cs
│   └── Models/
│       ├── IModelManager.cs
│       ├── ModelDescriptor.cs
│       ├── InstalledModel.cs
│       ├── ModelDownloadProgress.cs
│       └── StubModelManager.cs
│
└── LiveLingo.App/
    ├── LiveLingo.App.csproj
    ├── Program.cs
    ├── App.axaml
    ├── App.axaml.cs
    ├── app.manifest
    ├── Views/
    │   ├── MainWindow.axaml / .cs
    │   └── OverlayWindow.axaml / .cs
    ├── ViewModels/
    │   └── OverlayViewModel.cs
    └── Platform/
        ├── IPlatformServices.cs
        ├── IHotkeyService.cs
        ├── IWindowTracker.cs
        ├── ITextInjector.cs
        ├── IClipboardService.cs
        ├── TargetWindowInfo.cs
        ├── HotkeyBinding.cs
        └── Windows/
            ├── WindowsPlatformServices.cs
            ├── Win32HotkeyService.cs
            ├── Win32WindowTracker.cs
            ├── Win32TextInjector.cs
            ├── Win32ClipboardService.cs
            ├── NativeMethods.cs
            └── Diagnostics/          (#if DEBUG)
                ├── InjectionTest.cs
                ├── SlackAutoTest.cs
                └── WindowDiagnostic.cs
```
