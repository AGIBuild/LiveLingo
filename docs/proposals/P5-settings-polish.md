# P5: Settings & Polish

> 实现用户配置持久化、首次运行引导、可配置快捷键、多语言对管理。

## 前置依赖

- P3 完成（全部翻译功能就位）
- P4 完成或并行（macOS 快捷键配置需要平台适配）

## 目标

- 用户配置 JSON 持久化（语言对、后处理模式、注入模式、快捷键）
- 首次运行引导流程（模型选择 + 下载 + 权限）
- 可配置全局快捷键
- 多语言对管理 UI（添加/删除语言对）
- 主窗口设置面板

## 不做

- 不实现云同步配置
- 不实现用户账户系统
- 不实现自动更新

## 交付内容

### 1. 配置模型

```csharp
public class AppSettings
{
    public string TargetLanguage { get; set; } = "en";
    public List<string> InstalledLanguagePairs { get; set; } = ["zh-en"];
    public HotkeyConfig Hotkey { get; set; } = new();
    public InjectionMode InjectionMode { get; set; } = InjectionMode.PasteAndSend;
    public ProcessingMode PostProcessingMode { get; set; } = ProcessingMode.Off;
    public string ModelStoragePath { get; set; } = "";  // 默认 LocalAppData
}

public class HotkeyConfig
{
    public bool Ctrl { get; set; } = true;
    public bool Alt { get; set; } = true;
    public bool Shift { get; set; } = false;
    public string Key { get; set; } = "T";
}
```

存储路径：`{LocalAppData}/LiveLingo/settings.json`

### 2. 首次运行引导

```
Step 1: Welcome              Step 2: Language Setup
┌──────────────────────┐     ┌──────────────────────┐
│  Welcome to LiveLingo │     │  Select languages     │
│                       │     │                       │
│  Real-time translation│     │  Your language:       │
│  for your chat apps.  │     │  [Chinese ▾]          │
│                       │     │                       │
│         [Next →]      │     │  Translate to:        │
│                       │     │  [English ▾]          │
└──────────────────────┘     │                       │
                              │  ☐ Enable AI polish   │
                              │                       │
                              │    [← Back] [Next →]  │
                              └──────────────────────┘

Step 3: Download              Step 4: Ready
┌──────────────────────┐     ┌──────────────────────┐
│  Downloading models   │     │  You're all set!      │
│                       │     │                       │
│  ☑ Chinese→English    │     │  Press Ctrl+Alt+T     │
│    [████████░░] 80%   │     │  in any app to start  │
│                       │     │  translating.         │
│  ☑ AI Polish (Qwen)   │     │                       │
│    [██░░░░░░░░] 20%   │     │      [Start →]        │
│                       │     │                       │
└──────────────────────┘     └──────────────────────┘
```

macOS 额外步骤：权限授权引导（插入在 Step 1 和 Step 2 之间）。

### 3. 主窗口设置面板

当前主窗口只显示快捷键提示。扩展为设置面板：

```
┌─────────────────────────────────────────┐
│  LiveLingo                    [─] [□] [×]│
├─────────────────────────────────────────┤
│                                          │
│  Hotkey:    [Ctrl+Alt+T] [Change...]     │
│                                          │
│  ─── Languages ─────────────────────     │
│  ☑ Chinese → English    [30 MB]          │
│  ☐ Japanese → English   [28 MB] [Add]   │
│  ☐ Korean → English     [32 MB] [Add]   │
│                                          │
│  ─── AI Polish ─────────────────────     │
│  Model: Qwen2.5-1.5B   [1.0 GB]         │
│  Status: Loaded ✓                        │
│  Default: [Colloquial ▾]                 │
│                                          │
│  ─── Injection ─────────────────────     │
│  Default mode: [Paste & Send ▾]          │
│                                          │
│  Storage: 1.06 GB used                   │
│  [Open model folder]  [Clear cache]      │
│                                          │
└─────────────────────────────────────────┘
```

### 4. 快捷键配置

快捷键录制组件：
- 用户点击 [Change...]，进入录制模式
- 按下新组合键（如 Ctrl+Shift+Space），显示实时预览
- Enter 确认，Esc 取消
- 更新全局键盘钩子注册

需要平台适配：
- Windows: 修改 `GlobalKeyboardHook` 回调中的按键检测
- macOS: 修改 `CGEventTap` 的按键匹配

### 5. 多语言对管理

用户可在设置中添加/删除语言对：
- 添加时触发 ModelManager 下载对应 MarianMT 模型
- 删除时清理本地模型文件
- overlay 中语言选择跟随设置

可用语言对从预定义注册表获取（不是所有 MarianMT 模型都质量可用）。

## 验收标准

- [ ] 首次启动显示引导流程
- [ ] 引导中选择的语言对自动下载
- [ ] 设置修改后立即持久化到 JSON 文件
- [ ] 重启应用后配置保持
- [ ] 可成功更改全局快捷键并立即生效
- [ ] 可添加新语言对并下载对应模型
- [ ] 可删除语言对并释放磁盘空间
- [ ] 主窗口显示模型状态和磁盘占用
