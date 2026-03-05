# LiveLingo PoC 技术方案

> 本文档记录 PoC 阶段验证通过的技术方案，供正式产品开发参考。

## 1. 产品目标

为多语言团队提供桌面端实时翻译输入工具。用户在 Slack 等 IM 工具中按下全局快捷键，弹出翻译输入框，输入母语后自动翻译并注入到目标应用的输入框中。

## 2. 技术选型

| 项目 | 选型 | 理由 |
|------|------|------|
| 运行时 | .NET 10 | 最新 LTS，P/Invoke + LibraryImport 性能好 |
| UI 框架 | Avalonia 11.x | 跨平台桌面 UI，支持无边框透明窗口 |
| MVVM | CommunityToolkit.Mvvm | 源码生成器驱动，零反射开销 |
| 翻译引擎 | PoC 阶段为 stub | 正式版计划使用 ONNX Runtime 本地推理 |

## 3. 架构概览

```
┌─────────────────────────────────────────────┐
│                  App.axaml.cs                │
│  ┌───────────────┐  ┌────────────────────┐  │
│  │ GlobalKeyboard│  │   WindowTracker    │  │
│  │    Hook       │──│ (捕获目标窗口信息)  │  │
│  └───────┬───────┘  └────────┬───────────┘  │
│          │                   │              │
│          ▼                   ▼              │
│  ┌───────────────────────────────────────┐  │
│  │         OverlayWindow + VM            │  │
│  │  (浮动输入框，翻译预览，模式切换)      │  │
│  └───────────────┬───────────────────────┘  │
│                  │ Ctrl+Enter               │
│                  ▼                           │
│  ┌───────────────────────────────────────┐  │
│  │           TextInjector                │  │
│  │  S1: SendInput Ctrl+V                │  │
│  │  S2: WM_CHAR → Chrome renderer       │  │
│  └───────────────────────────────────────┘  │
└─────────────────────────────────────────────┘
```

## 4. 核心机制

### 4.1 全局快捷键

使用 `WH_KEYBOARD_LL` 低级键盘钩子实现全局快捷键检测（当前为 Ctrl+Alt+T）。

- 通过 `SetWindowsHookExW` 安装钩子
- 回调中检测 `VK_T` + `GetAsyncKeyState(VK_CONTROL)` + `GetAsyncKeyState(VK_MENU)`
- 触发后通过 `Dispatcher.UIThread.Post` 回到 UI 线程

**正式版注意**：macOS 需用 `CGEventTap`，Linux 需用 `XGrabKey` 或 libinput。

### 4.2 目标窗口识别

`WindowTracker.GetForegroundWindowInfo()` 在快捷键触发时调用，此时目标应用仍是前台窗口。

捕获内容：
- 主窗口句柄（`GetForegroundWindow`）
- 进程名、窗口位置/大小
- **输入子窗口句柄**（关键）

#### 输入子窗口查找策略

```
FindInputChild(mainWindow, threadId)
  │
  ├─ 策略 1：FindChromeRenderer（递归搜索）
  │   └─ 遍历子窗口树，查找 class = "Chrome_RenderWidgetHostHWND"
  │   └─ 适用于所有 Electron/Chromium 应用（Slack, Teams, Discord, VS Code）
  │
  └─ 策略 2：GetGUIThreadInfo
      └─ 获取 hwndFocus，适用于原生 Win32 应用（Notepad 等）
```

**关键发现**：Electron 应用的 Win32 焦点（`GUITHREADINFO.hwndFocus`）停留在主窗口上，实际输入由 `Chrome_RenderWidgetHostHWND` 子窗口处理。必须使用 `FindWindowExW` 递归搜索。

Slack 的窗口结构验证结果：
```
Slack (hwnd=0x3C069A)
  ├─ 0x1300A0 [Chrome_RenderWidgetHostHWND]  ← 注入目标
  └─ 0x86017E [Intermediate D3D Window]
```

### 4.3 浮动输入框（Overlay）

| 特性 | 实现方式 |
|------|----------|
| 无边框透明 | `SystemDecorations="None"` + `TransparencyLevelHint="Transparent"` |
| 始终置顶 | `Topmost="True"` |
| 可拖动 | 顶部 `DragHandle` Border + `BeginMoveDrag` |
| 焦点劫持 | `ForceActivateWindows()` 使用 `AttachThreadInput` + `SetForegroundWindow` |
| 快捷键拦截 | `AddHandler(KeyDownEvent, handler, RoutingStrategies.Tunnel)` |

**焦点劫持细节**：由于 overlay 需要从其他应用手中抢夺前台焦点，简单的 `Activate()` 不够。需要：
1. `AttachThreadInput` 将当前线程与前台线程合并输入状态
2. `BringWindowToTop` + `SetForegroundWindow` 强制切换
3. `DispatcherTimer.RunOnce(150ms)` 延迟后再 `TextBox.Focus()`

**Tunnel 路由**：`TextBox(AcceptsReturn=True)` 会在 Bubble 阶段吞掉 Enter 事件。使用 Tunnel 路由在 TextBox 之前拦截 Ctrl+Enter 和 Esc。

### 4.4 文本注入

`TextInjector.InjectText` 实现双策略自动降级：

#### 策略 1：SendInput Ctrl+V（主线）

```
ReleaseAllModifiers()        // 释放物理按键状态
  → SetClipboardText()       // 写入剪贴板
  → ForceForeground(target)  // 切回目标窗口
  → Thread.Sleep(500)        // 等待前台切换完成
  → SendInput(Ctrl+V)        // 模拟键盘粘贴
  → [可选] SendInput(Enter)  // 模拟回车发送
```

**前提条件**：
- 调用进程必须是当前前台进程（或刚刚是前台）
- 目标进程与调用进程处于相同完整性级别（UIPI 限制）
- 在 Avalonia GUI 流程中自然满足（overlay 就是前台窗口）

**验证结果**：`SendInput returned 4, err=0`，Slack 处于前台时完全可用。

#### 策略 2：WM_CHAR 逐字符发送（回退）

```
foreach (char ch in text)
    PostMessageW(chromeRenderer, WM_CHAR, ch, 0)
→ [可选] PostMessageW(WM_KEYDOWN/WM_KEYUP, VK_RETURN)
```

当 SendInput 因 UIPI 被拒绝时自动降级到此策略。

**优势**：
- `PostMessage` 直接寻址目标 HWND，不依赖前台状态
- 不需要剪贴板（但 PoC 中仍先写剪贴板以供策略 1 使用）
- Chromium 的 `Chrome_RenderWidgetHostHWND` 处理 `WM_CHAR` 消息

**限制**：
- 不保证对所有 Chromium 版本有效
- BMP 以外的 Unicode 字符（emoji 等）需要代理对处理

### 4.5 注入模式配置

| 模式 | 行为 | 快捷键 |
|------|------|--------|
| Paste & Send | 粘贴翻译文本 + 自动按 Enter 发送 | Ctrl+Enter |
| Paste Only | 仅粘贴翻译文本到光标位置 | Ctrl+Enter |

模式通过 overlay 底部按钮切换，选择在 overlay 实例间通过 `static` 字段持久保持。

### 4.6 前台窗口管理

Windows 严格限制 `SetForegroundWindow` 的调用权限。`ForceForeground` 的完整流程：

```csharp
AllowSetForegroundWindow(ASFW_ANY)
ShowWindow(hwnd, SW_SHOW)
BringWindowToTop(hwnd)
AttachThreadInput(myThread, fgThread, true)   // 合并输入状态
SetForegroundWindow(hwnd)                      // 切换前台
AttachThreadInput(myThread, fgThread, false)   // 分离
```

## 5. Win32 API 清单

| API | 用途 |
|-----|------|
| `SetWindowsHookExW(WH_KEYBOARD_LL)` | 全局键盘钩子 |
| `GetForegroundWindow` | 获取前台窗口 |
| `GetWindowThreadProcessId` | 获取窗口的线程和进程 ID |
| `GetWindowRect` | 获取窗口位置大小 |
| `FindWindowExW` | 遍历子窗口树 |
| `GetClassNameW` | 获取窗口类名（用于识别 Chrome renderer） |
| `GetGUIThreadInfo` | 获取线程的焦点窗口 |
| `SetForegroundWindow` | 切换前台窗口 |
| `BringWindowToTop` | 提升窗口 Z 序 |
| `AttachThreadInput` | 合并/分离线程输入状态 |
| `AllowSetForegroundWindow` | 授予前台切换权限 |
| `SendInput` | 注入键盘事件到系统输入队列 |
| `PostMessageW` | 直接向指定窗口投递消息 |
| `GetAsyncKeyState` | 查询物理按键状态 |
| `OpenClipboard / SetClipboardData` | 剪贴板操作 |
| `GlobalAlloc / GlobalLock` | 剪贴板内存管理 |

## 6. 已知限制与正式版改进方向

| 限制 | 改进方向 |
|------|----------|
| 仅支持 Windows | macOS: Accessibility API + CGEventTap; Linux: AT-SPI + XTest |
| 翻译为 stub | 集成 ONNX Runtime 本地推理（如 NLLB-200、MarianMT） |
| 注入使用 Thread.Sleep 阻塞 UI 线程 | 改为 async + TaskCompletionSource，或独立注入线程 |
| 模式配置仅内存持久 | 持久化到本地配置文件（JSON / SQLite） |
| 快捷键硬编码 | 用户可配置快捷键 |
| 单向翻译（母语→英语） | 自动检测语言，双向翻译 |
| 目标语言固定英语 | 可选目标语言 |
| 无错误恢复 | SendInput 失败时弹出通知，提示以管理员运行 |
| Chrome renderer 子窗口只取第一个 | 多 renderer 场景需结合窗口可见性/大小判断 |

## 7. 项目结构

```
LiveLingo/
├── LiveLingo.slnx
├── docs/
│   └── poc-design.md          ← 本文档
└── src/
    └── LiveLingo.App/
        ├── LiveLingo.App.csproj
        ├── Program.cs              # 入口，支持 --test-inject / --diag-window / --test-slack
        ├── App.axaml / .cs         # 应用生命周期，全局快捷键注册，overlay 管理
        ├── app.manifest
        ├── Views/
        │   ├── MainWindow.axaml / .cs      # 主窗口（显示快捷键提示）
        │   └── OverlayWindow.axaml / .cs   # 浮动翻译输入框
        ├── ViewModels/
        │   └── OverlayViewModel.cs         # 输入/翻译/模式 状态管理
        └── Services/Platform/Windows/
            ├── NativeMethods.cs            # P/Invoke 声明（LibraryImport）
            ├── GlobalKeyboardHook.cs       # WH_KEYBOARD_LL 全局键盘钩子
            ├── WindowTracker.cs            # 前台窗口信息 + Chrome renderer 查找
            ├── TextInjector.cs             # 双策略文本注入
            ├── InjectionTest.cs            # 交互式注入测试（Notepad/Slack）
            ├── SlackAutoTest.cs            # 非交互式 Slack 注入测试
            └── WindowDiagnostic.cs         # 窗口树诊断工具
```

## 8. CLI 诊断工具

```bash
# 交互式注入测试（支持 Notepad / Slack / 前台窗口）
dotnet run -- --test-inject

# 窗口树诊断（不指定进程则 5 秒内点击目标窗口）
dotnet run -- --diag-window Slack

# 非交互式 Slack 三策略注入测试（WM_CHAR / SendInput / WM_PASTE）
dotnet run -- --test-slack
```
