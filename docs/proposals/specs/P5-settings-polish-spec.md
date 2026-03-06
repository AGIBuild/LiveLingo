# P5 Spec: Settings & Polish

## 1. 配置模型

### 1.1 UserSettings

```csharp
namespace LiveLingo.Core.Configuration;

public class UserSettings
{
    public HotkeySettings Hotkey { get; set; } = new();
    public TranslationSettings Translation { get; set; } = new();
    public ProcessingSettings Processing { get; set; } = new();
    public UISettings UI { get; set; } = new();
    public AdvancedSettings Advanced { get; set; } = new();
}

public class HotkeySettings
{
    public string OverlayHotkey { get; set; } = "Ctrl+Alt+T";
    public string InjectHotkey { get; set; } = "Ctrl+Enter";
}

public class TranslationSettings
{
    public string DefaultSourceLanguage { get; set; } = "auto";
    public string DefaultTargetLanguage { get; set; } = "en";

    /// <summary>
    /// 用户配置的语言对列表。
    /// "auto→en", "zh→ja", "en→zh" 等。
    /// 在 Overlay 中切换使用。
    /// </summary>
    public List<string> LanguagePairs { get; set; } = ["auto→en"];
}

public class ProcessingSettings
{
    /// <summary>
    /// 默认后处理模式。用户在 Overlay 中可切换。
    /// </summary>
    public ProcessingMode DefaultPostProcessMode { get; set; } = ProcessingMode.Off;

    /// <summary>
    /// 注入模式。PasteOnly 或 PasteAndSend。
    /// </summary>
    public InjectionMode DefaultInjectionMode { get; set; } = InjectionMode.PasteAndSend;
}

public class UISettings
{
    /// <summary>
    /// Overlay 窗口透明度 (0.5 - 1.0)
    /// </summary>
    public double OverlayOpacity { get; set; } = 0.9;

    /// <summary>
    /// 上次 Overlay 位置 (null = 自动定位到目标窗口上方)
    /// </summary>
    public OverlayPosition? LastOverlayPosition { get; set; }
}

public class AdvancedSettings
{
    /// <summary>
    /// 模型存储路径
    /// </summary>
    public string ModelStoragePath { get; set; } = "";  // 空 = 默认路径

    /// <summary>
    /// MarianMT ONNX 推理线程数 (0 = 自动)
    /// </summary>
    public int TranslationThreads { get; set; } = 0;

    /// <summary>
    /// Qwen 推理线程数 (0 = 自动)
    /// </summary>
    public int LlmThreads { get; set; } = 0;

    /// <summary>
    /// 翻译 debounce 时间 (ms)
    /// </summary>
    public int TranslationDebounceMs { get; set; } = 0;

    /// <summary>
    /// 日志级别
    /// </summary>
    public string LogLevel { get; set; } = "Information";
}

public record OverlayPosition(int X, int Y);
```

### 1.2 InjectionMode / ProcessingMode 枚举

这些已在 P1/P3 中定义，此处确认来源：

```csharp
// LiveLingo.Core.Processing (P1)
public enum ProcessingMode { Off, Summarize, Optimize, Colloquialize }

// LiveLingo.App.Platform (P1)
public enum InjectionMode { PasteOnly, PasteAndSend }
```

## 2. 配置持久化

### 2.1 存储位置

| 平台 | 路径 |
|------|------|
| Windows | `%LOCALAPPDATA%\LiveLingo\settings.json` |
| macOS | `~/Library/Application Support/LiveLingo/settings.json` |

### 2.2 ISettingsService 接口

```csharp
namespace LiveLingo.App.Services;

public interface ISettingsService
{
    UserSettings Current { get; }
    event Action<UserSettings>? SettingsChanged;
    Task LoadAsync(CancellationToken ct = default);
    Task SaveAsync(CancellationToken ct = default);
    void Update(Action<UserSettings> mutate);
}
```

### 2.3 JsonSettingsService 实现

```csharp
public class JsonSettingsService : ISettingsService
{
    private readonly string _filePath;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions;
    private UserSettings _current = new();

    public UserSettings Current => _current;
    public event Action<UserSettings>? SettingsChanged;

    public JsonSettingsService()
    {
        _filePath = Path.Combine(GetSettingsDirectory(), "settings.json");
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() }
        };
    }

    public async Task LoadAsync(CancellationToken ct)
    {
        if (!File.Exists(_filePath))
        {
            _current = new UserSettings();
            return;
        }

        await _lock.WaitAsync(ct);
        try
        {
            var json = await File.ReadAllTextAsync(_filePath, ct);
            _current = JsonSerializer.Deserialize<UserSettings>(json, _jsonOptions)
                       ?? new UserSettings();
        }
        catch (JsonException)
        {
            // 配置损坏，使用默认值
            _current = new UserSettings();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveAsync(CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            var json = JsonSerializer.Serialize(_current, _jsonOptions);
            await File.WriteAllTextAsync(_filePath, json, ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Update(Action<UserSettings> mutate)
    {
        mutate(_current);
        SettingsChanged?.Invoke(_current);
        _ = SaveAsync(CancellationToken.None);  // fire-and-forget
    }

    private static string GetSettingsDirectory()
    {
        if (OperatingSystem.IsMacOS())
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library", "Application Support", "LiveLingo");

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LiveLingo");
    }
}
```

### 2.4 settings.json 示例

```json
{
  "hotkey": {
    "overlayHotkey": "Ctrl+Alt+T",
    "injectHotkey": "Ctrl+Enter"
  },
  "translation": {
    "defaultSourceLanguage": "auto",
    "defaultTargetLanguage": "en",
    "languagePairs": ["auto→en", "zh→ja", "en→zh"]
  },
  "processing": {
    "defaultPostProcessMode": "Off",
    "defaultInjectionMode": "PasteAndSend"
  },
  "ui": {
    "overlayOpacity": 0.9,
    "lastOverlayPosition": { "x": 500, "y": 300 }
  },
  "advanced": {
    "modelStoragePath": "",
    "translationThreads": 0,
    "llmThreads": 0,
    "translationDebounceMs": 0,
    "logLevel": "Information"
  }
}
```

## 3. 首次运行引导

### 3.1 判断逻辑

```csharp
bool isFirstRun = !File.Exists(settingsFilePath);
```

无需额外标记——settings.json 不存在即为首次运行。

### 3.2 引导流程

```
App 启动
  │
  ├─ settings.json 存在 → 正常启动
  │
  └─ settings.json 不存在 → 显示 SetupWizardWindow
       │
       ├─ Step 1: 语言选择
       │   ┌──────────────────────────────────────────┐
       │   │  Welcome to LiveLingo!                    │
       │   │                                           │
       │   │  What languages do you work with?         │
       │   │                                           │
       │   │  I write in:    [Chinese      ▾]          │
       │   │  Translate to:  [English      ▾]          │
       │   │                                           │
       │   │                      [Next →]             │
       │   └──────────────────────────────────────────┘
       │
       ├─ Step 2: 模型下载
       │   ┌──────────────────────────────────────────┐
       │   │  Downloading required models...            │
       │   │                                           │
       │   │  ☑ Language Detection (1 MB)     Done     │
       │   │  ◻ Chinese → English (30 MB)  ███░░ 60%   │
       │   │                                           │
       │   │                      [Cancel]             │
       │   └──────────────────────────────────────────┘
       │
       ├─ Step 3: 快捷键确认
       │   ┌──────────────────────────────────────────┐
       │   │  Your shortcuts:                          │
       │   │                                           │
       │   │  Open overlay:   [Ctrl+Alt+T]  [Change]   │
       │   │  Inject text:    [Ctrl+Enter]  [Change]   │
       │   │                                           │
       │   │  Default mode:   [Paste & Send ▾]         │
       │   │                                           │
       │   │               [← Back]  [Finish →]        │
       │   └──────────────────────────────────────────┘
       │
       └─ 完成 → 保存 settings.json → 进入正常模式
```

### 3.3 SetupWizardViewModel

```csharp
namespace LiveLingo.App.ViewModels;

public partial class SetupWizardViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly IModelManager _modelManager;

    [ObservableProperty] private int _currentStep = 0; // 0, 1, 2
    [ObservableProperty] private string _sourceLanguage = "zh";
    [ObservableProperty] private string _targetLanguage = "en";
    [ObservableProperty] private string _overlayHotkey = "Ctrl+Alt+T";
    [ObservableProperty] private string _injectHotkey = "Ctrl+Enter";
    [ObservableProperty] private InjectionMode _injectionMode = InjectionMode.PasteAndSend;

    // 下载进度
    [ObservableProperty] private double _downloadProgress;
    [ObservableProperty] private string _downloadStatus = "";
    [ObservableProperty] private bool _isDownloading;

    [RelayCommand]
    private void Next()
    {
        if (CurrentStep == 0)
        {
            CurrentStep = 1;
            _ = StartDownloadAsync();
        }
        else if (CurrentStep == 1 && !IsDownloading)
        {
            CurrentStep = 2;
        }
    }

    [RelayCommand]
    private void Back()
    {
        if (CurrentStep > 0) CurrentStep--;
    }

    [RelayCommand]
    private async Task FinishAsync()
    {
        _settings.Update(s =>
        {
            s.Translation.DefaultSourceLanguage = SourceLanguage;
            s.Translation.DefaultTargetLanguage = TargetLanguage;
            s.Translation.LanguagePairs = [$"{SourceLanguage}→{TargetLanguage}"];
            s.Hotkey.OverlayHotkey = OverlayHotkey;
            s.Hotkey.InjectHotkey = InjectHotkey;
            s.Processing.DefaultInjectionMode = InjectionMode;
        });

        await _settings.SaveAsync();
        // 关闭向导窗口 → 启动主流程
    }

    private async Task StartDownloadAsync()
    {
        IsDownloading = true;
        var modelsToDownload = new List<ModelDescriptor>
        {
            ModelRegistry.FastTextLid,
            ModelRegistry.All.First(m => m.Id == $"marian-{SourceLanguage}-{TargetLanguage}")
        };

        var totalBytes = modelsToDownload.Sum(m => m.SizeBytes);
        long downloaded = 0;

        foreach (var model in modelsToDownload)
        {
            DownloadStatus = $"Downloading {model.DisplayName}...";
            var progress = new Progress<ModelDownloadProgress>(p =>
            {
                var current = downloaded + p.BytesDownloaded;
                DownloadProgress = (double)current / totalBytes * 100;
            });

            await _modelManager.EnsureModelAsync(model, progress);
            downloaded += model.SizeBytes;
        }

        DownloadStatus = "All models ready!";
        IsDownloading = false;
    }
}
```

## 4. 可配置快捷键

### 4.1 快捷键录制控件

```csharp
namespace LiveLingo.App.Controls;

public class HotkeyRecorder : TextBox
{
    public static readonly StyledProperty<string> HotkeyProperty =
        AvaloniaProperty.Register<HotkeyRecorder, string>(nameof(Hotkey));

    public string Hotkey
    {
        get => GetValue(HotkeyProperty);
        set => SetValue(HotkeyProperty, value);
    }

    private bool _isRecording;

    protected override void OnGotFocus(GotFocusEventArgs e)
    {
        base.OnGotFocus(e);
        _isRecording = true;
        Text = "Press key combination...";
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (!_isRecording) return;
        e.Handled = true;

        // 忽略单独的修饰键
        if (IsModifierKey(e.Key)) return;

        var parts = new List<string>();
        if (e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Control))
            parts.Add("Ctrl");
        if (e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Alt))
            parts.Add("Alt");
        if (e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Shift))
            parts.Add("Shift");
        if (e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Meta))
            parts.Add(OperatingSystem.IsMacOS() ? "Cmd" : "Win");

        parts.Add(e.Key.ToString());

        Hotkey = string.Join("+", parts);
        Text = Hotkey;
        _isRecording = false;
    }

    private static bool IsModifierKey(Key key) => key is
        Key.LeftCtrl or Key.RightCtrl or
        Key.LeftAlt or Key.RightAlt or
        Key.LeftShift or Key.RightShift or
        Key.LWin or Key.RWin;
}
```

### 4.2 快捷键字符串解析

```csharp
namespace LiveLingo.App.Platform;

public static class HotkeyParser
{
    /// <summary>
    /// 将 "Ctrl+Alt+T" 解析为 HotkeyBinding
    /// </summary>
    public static HotkeyBinding Parse(string hotkeyId, string hotkeyString)
    {
        var parts = hotkeyString.Split('+', StringSplitOptions.TrimEntries);
        var modifiers = KeyModifiers.None;
        string key = "";

        foreach (var part in parts)
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl": modifiers |= KeyModifiers.Ctrl; break;
                case "alt": modifiers |= KeyModifiers.Alt; break;
                case "shift": modifiers |= KeyModifiers.Shift; break;
                case "cmd" or "win" or "meta": modifiers |= KeyModifiers.Meta; break;
                default: key = part; break;
            }
        }

        if (string.IsNullOrEmpty(key))
            throw new ArgumentException($"Hotkey string missing key: {hotkeyString}");

        return new HotkeyBinding(hotkeyId, modifiers, key);
    }
}
```

### 4.3 快捷键变更热更新

```csharp
// App.axaml.cs 中监听 SettingsChanged
settingsService.SettingsChanged += settings =>
{
    platform.Hotkey.Unregister("overlay");
    platform.Hotkey.Register(
        HotkeyParser.Parse("overlay", settings.Hotkey.OverlayHotkey));
};
```

## 5. 多语言对管理

### 5.1 UI

在 Settings 窗口中：

```
┌────────────────────────────────────────────────┐
│  Language Pairs                                 │
│                                                 │
│  ┌────────────────────────────────────────────┐ │
│  │ Auto detect → English            [Remove]  │ │
│  │ Chinese     → Japanese           [Remove]  │ │
│  │ English     → Chinese            [Remove]  │ │
│  └────────────────────────────────────────────┘ │
│                                                 │
│  [+ Add pair]                                   │
│                                                 │
│  Default pair: [Auto detect → English ▾]        │
└────────────────────────────────────────────────┘
```

### 5.2 Overlay 中切换

在 Overlay 状态栏增加语言对切换按钮：

```
┌──────────────────────────────────────────────────────┐
│ [auto→en ▾] [Off ▾] Ctrl+Enter paste & send [Paste&Send] │
└──────────────────────────────────────────────────────┘
```

点击 `auto→en` 弹出下拉，列出 settings 中配置的所有语言对。
切换后立即重新翻译当前 SourceText。

### 5.3 OverlayViewModel 变化

```csharp
[ObservableProperty]
private string _selectedLanguagePair = "auto→en";

public IReadOnlyList<string> AvailableLanguagePairs
    => _settings.Current.Translation.LanguagePairs;

partial void OnSelectedLanguagePairChanged(string value)
{
    var parts = value.Split('→');
    _sourceLanguage = parts[0] == "auto" ? null : parts[0];
    _targetLanguage = parts[1];

    // 重新翻译
    if (!string.IsNullOrWhiteSpace(SourceText))
    {
        _pipelineCts?.Cancel();
        _pipelineCts = new CancellationTokenSource();
        _ = RunPipelineAsync(SourceText, _pipelineCts.Token);
    }
}
```

## 6. 支持的语言列表

| ISO 代码 | 显示名 | MarianMT 模型 |
|----------|--------|---------------|
| zh | Chinese | opus-mt-zh-en, opus-mt-en-zh |
| ja | Japanese | opus-mt-ja-en, opus-mt-en-ja |
| ko | Korean | opus-mt-ko-en, opus-mt-en-ko |
| de | German | opus-mt-de-en, opus-mt-en-de |
| fr | French | opus-mt-fr-en, opus-mt-en-fr |
| es | Spanish | opus-mt-es-en, opus-mt-en-es |
| ru | Russian | opus-mt-ru-en, opus-mt-en-ru |
| pt | Portuguese | opus-mt-pt-en, opus-mt-en-pt |
| en | English | (as target) |

v1.0 优先支持 CJK ↔ English。
非英语之间的翻译通过 pivot（源→en→目标）实现。

## 7. Settings 窗口

### 7.1 入口

系统托盘图标右键菜单中添加 "Settings" 选项。

macOS: 菜单栏图标。
Windows: 系统托盘图标。

### 7.2 Settings 窗口 Tab 结构

```
┌─ General ─┬─ Translation ─┬─ AI ─┬─ Advanced ─┐
│                                                │
│  [General Tab]                                 │
│                                                │
│  Shortcuts:                                    │
│    Open overlay:  [Ctrl+Alt+T]  [Record]       │
│    Inject text:   [Ctrl+Enter]  [Record]       │
│                                                │
│  Injection mode:  ○ Paste only                 │
│                   ● Paste & Send               │
│                                                │
│  Overlay opacity: ──●──────── 90%              │
│                                                │
│  [ ] Start with OS                             │
│                                                │
│                         [Save]  [Reset]        │
└────────────────────────────────────────────────┘
```

**Translation Tab:**
- 语言对管理（参见 5.1）
- 默认翻译方向

**AI Tab:**
- 默认后处理模式（Off / Summarize / Optimize / Colloquial）
- 已下载模型列表 + 磁盘占用
- [Delete] 按钮删除不需要的模型

**Advanced Tab:**
- 模型存储路径（带文件夹选择按钮）
- 翻译线程数
- LLM 线程数
- Debounce 时间
- 日志级别
- [Open Log Folder] 按钮

### 7.3 SettingsViewModel

```csharp
public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly IModelManager _modelManager;

    // General
    [ObservableProperty] private string _overlayHotkey;
    [ObservableProperty] private string _injectHotkey;
    [ObservableProperty] private InjectionMode _injectionMode;
    [ObservableProperty] private double _overlayOpacity;

    // Translation
    [ObservableProperty] private ObservableCollection<string> _languagePairs;
    [ObservableProperty] private string _defaultLanguagePair;

    // AI
    [ObservableProperty] private ProcessingMode _defaultPostProcessMode;
    [ObservableProperty] private ObservableCollection<InstalledModelViewModel> _installedModels;

    // Advanced
    [ObservableProperty] private string _modelStoragePath;
    [ObservableProperty] private int _translationThreads;
    [ObservableProperty] private int _llmThreads;

    [RelayCommand]
    private async Task SaveAsync()
    {
        _settings.Update(s =>
        {
            s.Hotkey.OverlayHotkey = OverlayHotkey;
            s.Hotkey.InjectHotkey = InjectHotkey;
            s.Processing.DefaultInjectionMode = InjectionMode;
            s.UI.OverlayOpacity = OverlayOpacity;
            s.Translation.LanguagePairs = LanguagePairs.ToList();
            s.Processing.DefaultPostProcessMode = DefaultPostProcessMode;
            s.Advanced.ModelStoragePath = ModelStoragePath;
            s.Advanced.TranslationThreads = TranslationThreads;
            s.Advanced.LlmThreads = LlmThreads;
        });
    }

    [RelayCommand]
    private void Reset()
    {
        // 重置为默认值
        var defaults = new UserSettings();
        OverlayHotkey = defaults.Hotkey.OverlayHotkey;
        // ...
    }

    [RelayCommand]
    private async Task DeleteModelAsync(string modelId)
    {
        await _modelManager.DeleteModelAsync(modelId);
        InstalledModels.Remove(InstalledModels.First(m => m.Id == modelId));
    }
}
```

## 8. 系统托盘 / 菜单栏

### 8.1 Windows 系统托盘

```csharp
// 使用 Avalonia TrayIcon
var trayIcon = new TrayIcon
{
    Icon = new WindowIcon("Assets/tray-icon.ico"),
    ToolTipText = "LiveLingo",
    Menu = new NativeMenu
    {
        new NativeMenuItem("Settings") { Command = OpenSettingsCommand },
        new NativeMenuItemSeparator(),
        new NativeMenuItem("Quit") { Command = QuitCommand }
    }
};
```

### 8.2 macOS 菜单栏

Avalonia 在 macOS 上自动支持 TrayIcon 显示在菜单栏。
无需额外平台代码。

## 9. Overlay 位置记忆

### 9.1 行为

- 首次打开：自动定位到目标窗口上方居中
- 用户拖动后：记录位置到 `UI.LastOverlayPosition`
- 再次打开：使用记忆位置（如果在屏幕内）

```csharp
// OverlayWindow.axaml.cs
protected override void OnPositionChanged(PixelPointEventArgs e)
{
    base.OnPositionChanged(e);
    if (_isDragging)
    {
        _settings.Update(s =>
            s.UI.LastOverlayPosition = new OverlayPosition(Position.X, Position.Y));
    }
}
```

## 10. DI 注册

```csharp
// P5 阶段完整 DI
public static IServiceCollection AddAppServices(this IServiceCollection services)
{
    services.AddSingleton<ISettingsService, JsonSettingsService>();
    return services;
}
```

## 11. 测试

### 11.1 单元测试

```
Configuration/
├── JsonSettingsServiceTests.cs
│   ├── Load_NoFile_ReturnsDefaults
│   ├── SaveAndLoad_Roundtrip
│   ├── Load_CorruptJson_ReturnsDefaults
│   ├── Update_TriggersChanged
│   └── Update_SavesAsync
│
├── HotkeyParserTests.cs
│   ├── Parse_CtrlAltT
│   ├── Parse_CmdShiftSpace
│   ├── Parse_SingleKey_Throws
│   └── Parse_EmptyString_Throws
│
└── SetupWizardViewModelTests.cs
    ├── Next_Step0To1_StartsDownload
    ├── Finish_SavesSettings
    └── Back_DecrementsStep
```

### 11.2 集成测试 checklist

- [ ] 首次启动（删除 settings.json）→ 显示向导
- [ ] 向导完成 → settings.json 正确生成
- [ ] 再次启动 → 跳过向导，加载已有配置
- [ ] 修改快捷键 → 热更新生效
- [ ] 切换语言对 → 按需下载模型 → 翻译使用新语言对
- [ ] 删除模型 → 磁盘空间释放
- [ ] Overlay 位置 → 拖动后记忆，再次打开恢复
- [ ] settings.json 手动损坏 → 优雅回退到默认值
