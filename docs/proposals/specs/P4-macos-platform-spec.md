# P4 Spec: macOS Platform

## 1. 前置条件

### 1.1 系统要求

- macOS 12 Monterey+（Accessibility API 稳定性）
- .NET 10 + Avalonia 11 (macOS arm64 / x64)

### 1.2 权限要求

```xml
<!-- Info.plist 中需声明 -->
<key>NSAccessibilityUsageDescription</key>
<string>LiveLingo needs Accessibility access to detect text input areas and inject translated text.</string>
```

用户必须在 System Settings → Privacy & Security → Accessibility 中授权 LiveLingo。

### 1.3 权限检测

```csharp
namespace LiveLingo.App.Platform.macOS;

public static class AccessibilityPermission
{
    [DllImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
    private static extern bool AXIsProcessTrustedWithOptions(IntPtr options);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern IntPtr CFDictionaryCreate(
        IntPtr allocator, IntPtr[] keys, IntPtr[] values,
        nint numValues, IntPtr keyCallBacks, IntPtr valueCallBacks);

    public static bool IsGranted()
        => AXIsProcessTrustedWithOptions(IntPtr.Zero);

    public static bool RequestAndCheck()
    {
        // kAXTrustedCheckOptionPrompt = true
        // 这会弹出系统授权对话框
        var key = CFStringCreate("AXTrustedCheckOptionPrompt");
        var value = CFBooleanTrue();
        var dict = CFDictionaryCreate(..., [key], [value], 1, ...);
        return AXIsProcessTrustedWithOptions(dict);
    }
}
```

## 2. 全局快捷键 — CGEventTap

### 2.1 方案对比

| 方案 | 优点 | 缺点 |
|------|------|------|
| CGEventTap | 系统级拦截，可消费事件 | 需要 Accessibility 权限 |
| NSEvent.addGlobalMonitorForEvents | 不需要 Accessibility | 不能消费事件，可能与 Slack 冲突 |
| Carbon RegisterEventHotKey | 传统方案，专为全局热键设计 | 已 deprecated |

选择 CGEventTap：功能最强，且已需要 Accessibility 权限（文本注入也需要）。

### 2.2 实现

```csharp
namespace LiveLingo.App.Platform.macOS;

public class CoreGraphicsHotkeyService : IHotkeyService, IDisposable
{
    private IntPtr _eventTap;
    private IntPtr _runLoopSource;
    private readonly Dictionary<string, HotkeyBinding> _bindings = new();
    private Thread? _tapThread;

    public event Action<HotkeyEventArgs>? HotkeyTriggered;

    public void Register(HotkeyBinding binding)
    {
        _bindings[binding.Id] = binding;

        if (_eventTap == IntPtr.Zero)
            StartEventTap();
    }

    public void Unregister(string hotkeyId)
    {
        _bindings.Remove(hotkeyId);
        if (_bindings.Count == 0)
            StopEventTap();
    }

    private void StartEventTap()
    {
        _tapThread = new Thread(() =>
        {
            _eventTap = CGEventTapCreate(
                tap: CGEventTapLocation.CGHIDEventTap,
                place: CGEventTapPlacement.HeadInsertEventTap,
                options: CGEventTapOptions.ListenOnly,  // 不消费事件
                eventsOfInterest: CGEventMask.KeyDown,
                callback: TapCallback,
                userInfo: IntPtr.Zero
            );

            if (_eventTap == IntPtr.Zero)
                throw new PlatformNotSupportedException(
                    "CGEventTap creation failed. Accessibility permission may be missing.");

            _runLoopSource = CFMachPortCreateRunLoopSource(
                IntPtr.Zero, _eventTap, 0);
            CFRunLoopAddSource(
                CFRunLoopGetCurrent(), _runLoopSource, kCFRunLoopCommonModes);
            CGEventTapEnable(_eventTap, true);
            CFRunLoopRun();
        })
        {
            IsBackground = true,
            Name = "CGEventTap"
        };
        _tapThread.Start();
    }

    private IntPtr TapCallback(
        IntPtr proxy, CGEventType type, IntPtr eventRef, IntPtr userInfo)
    {
        if (type == CGEventType.KeyDown)
        {
            var keyCode = CGEventGetIntegerValueField(eventRef, CGEventField.KeyboardEventKeycode);
            var flags = CGEventGetFlags(eventRef);

            foreach (var (id, binding) in _bindings)
            {
                if (MatchesBinding(keyCode, flags, binding))
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(
                        () => HotkeyTriggered?.Invoke(new HotkeyEventArgs(id)));
                    break;
                }
            }
        }

        return eventRef;  // 不修改事件
    }

    private static bool MatchesBinding(long keyCode, CGEventFlags flags, HotkeyBinding binding)
    {
        if (MacKeyMap.GetKeyCode(binding.Key) != keyCode)
            return false;

        var required = CGEventFlags.None;
        if (binding.Modifiers.HasFlag(KeyModifiers.Ctrl))
            required |= CGEventFlags.MaskControl;
        if (binding.Modifiers.HasFlag(KeyModifiers.Alt))
            required |= CGEventFlags.MaskAlternate;
        if (binding.Modifiers.HasFlag(KeyModifiers.Shift))
            required |= CGEventFlags.MaskShift;
        if (binding.Modifiers.HasFlag(KeyModifiers.Meta))
            required |= CGEventFlags.MaskCommand;

        return (flags & required) == required;
    }

    public void Dispose()
    {
        StopEventTap();
    }

    private void StopEventTap()
    {
        if (_eventTap != IntPtr.Zero)
        {
            CGEventTapEnable(_eventTap, false);
            CFRunLoopStop(CFRunLoopGetCurrent());
            CFRelease(_eventTap);
            _eventTap = IntPtr.Zero;
        }
    }
}
```

### 2.3 macOS Key Code 映射

```csharp
internal static class MacKeyMap
{
    private static readonly Dictionary<string, long> KeyCodes = new()
    {
        ["A"] = 0x00, ["B"] = 0x0B, ["C"] = 0x08, ["D"] = 0x02,
        ["E"] = 0x0E, ["F"] = 0x03, ["G"] = 0x05, ["H"] = 0x04,
        ["I"] = 0x22, ["J"] = 0x26, ["K"] = 0x28, ["L"] = 0x25,
        ["M"] = 0x2E, ["N"] = 0x2D, ["O"] = 0x1F, ["P"] = 0x23,
        ["Q"] = 0x0C, ["R"] = 0x0F, ["S"] = 0x01, ["T"] = 0x11,
        ["U"] = 0x20, ["V"] = 0x09, ["W"] = 0x0D, ["X"] = 0x07,
        ["Y"] = 0x10, ["Z"] = 0x06,
        ["Space"] = 0x31, ["Return"] = 0x24, ["Tab"] = 0x30,
        ["Escape"] = 0x35,
    };

    public static long GetKeyCode(string key)
        => KeyCodes.TryGetValue(key, out var code) ? code
            : throw new ArgumentException($"Unknown key: {key}");
}
```

## 3. 窗口追踪 — NSWorkspace

### 3.1 实现

```csharp
namespace LiveLingo.App.Platform.macOS;

public class MacWindowTracker : IWindowTracker
{
    public TargetWindowInfo? GetForegroundWindowInfo()
    {
        // 1. 获取前台应用
        var frontApp = NSWorkspace.SharedWorkspace.FrontmostApplication;
        if (frontApp is null) return null;

        // 2. 通过 CGWindowListCopyWindowInfo 获取窗口详情
        var windowList = CGWindowListCopyWindowInfo(
            CGWindowListOption.OptionOnScreenOnly | CGWindowListOption.ExcludeDesktopElements,
            kCGNullWindowID);

        var appPid = frontApp.ProcessIdentifier;

        // 3. 遍历找到属于前台应用的第一个窗口
        foreach (var windowInfo in windowList)
        {
            if (windowInfo.OwnerPID != appPid) continue;
            if (windowInfo.Layer != 0) continue; // 只关注标准窗口层级

            return new TargetWindowInfo(
                Handle: (nint)windowInfo.WindowNumber,
                InputChildHandle: (nint)windowInfo.WindowNumber, // macOS 不区分子窗口
                ProcessName: frontApp.LocalizedName ?? frontApp.BundleIdentifier ?? "unknown",
                Title: windowInfo.Name ?? "",
                Left: (int)windowInfo.Bounds.X,
                Top: (int)windowInfo.Bounds.Y,
                Width: (int)windowInfo.Bounds.Width,
                Height: (int)windowInfo.Bounds.Height
            );
        }

        return null;
    }
}
```

### 3.2 P/Invoke 声明

```csharp
// CoreGraphics
[DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
private static extern IntPtr CGWindowListCopyWindowInfo(
    CGWindowListOption option, uint relativeToWindow);

// AppKit 通过 Objective-C runtime
[DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
private static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector);
```

实际封装会使用 Objective-C runtime interop 或 NativeReference 方式调用 AppKit。

## 4. 文本注入 — AXUIElement (Accessibility API)

### 4.1 注入策略

macOS 的注入策略与 Windows 有本质不同：

| Windows 策略 | macOS 对应 |
|-------------|-----------|
| SendInput (Ctrl+V) | CGEventPost (Cmd+V) |
| WM_CHAR (PostMessage) | AXUIElement.SetValue (直接设值) |
| Chrome_RenderWidgetHostHWND | AXFocusedUIElement (自动定位) |

**主策略：AXUIElement SetValue**
- macOS Accessibility API 可直接查询并设置 focused element 的 value
- 不需要查找子窗口句柄
- Electron/Chromium 应用支持 Accessibility（前提是已开启）

**备选策略：CGEventPost (Cmd+V)**
- 通过剪贴板 + 模拟 Cmd+V 粘贴
- 与 Windows SendInput 策略对应

### 4.2 实现

```csharp
namespace LiveLingo.App.Platform.macOS;

public class MacTextInjector : ITextInjector
{
    private readonly IClipboardService _clipboard;
    private readonly ILogger<MacTextInjector> _logger;

    public async Task InjectAsync(
        TargetWindowInfo target, string text, bool autoSend, CancellationToken ct)
    {
        // 策略1: AXUIElement 直接设值
        if (TrySetViaAccessibility(target, text))
        {
            _logger.LogDebug("AXUIElement SetValue succeeded");
            if (autoSend) await SimulateReturnKeyAsync();
            return;
        }

        // 策略2: 剪贴板 + Cmd+V
        _logger.LogDebug("Falling back to clipboard + Cmd+V");
        await _clipboard.SetTextAsync(text, ct);
        await SimulatePasteAsync();
        if (autoSend) await SimulateReturnKeyAsync();
    }

    private bool TrySetViaAccessibility(TargetWindowInfo target, string text)
    {
        // 1. 获取目标应用的 AXUIElement
        var appElement = AXUIElementCreateApplication(target.Handle);

        // 2. 获取 focused element
        AXUIElementCopyAttributeValue(
            appElement, kAXFocusedUIElementAttribute, out var focusedElement);
        if (focusedElement == IntPtr.Zero) return false;

        // 3. 获取当前值（用于 append）
        AXUIElementCopyAttributeValue(
            focusedElement, kAXValueAttribute, out var currentValue);
        var current = CFStringToManaged(currentValue) ?? "";

        // 4. 获取选中范围（光标位置）
        AXUIElementCopyAttributeValue(
            focusedElement, kAXSelectedTextRangeAttribute, out var rangeValue);
        var range = CFRangeFromAXValue(rangeValue);

        // 5. 在光标位置插入文本
        var newValue = current[..range.Location] + text + current[range.Location..];
        var cfString = CFStringCreateWithCString(newValue);
        var result = AXUIElementSetAttributeValue(
            focusedElement, kAXValueAttribute, cfString);

        // 6. 更新光标位置到插入文本末尾
        var newCursorPos = range.Location + text.Length;
        SetCursorPosition(focusedElement, newCursorPos);

        return result == 0; // kAXErrorSuccess
    }

    private async Task SimulatePasteAsync()
    {
        // CGEventPost: Cmd+V
        var source = CGEventSourceCreate(CGEventSourceStateID.HIDSystemState);

        var keyDown = CGEventCreateKeyboardEvent(source, /* V */ 0x09, true);
        CGEventSetFlags(keyDown, CGEventFlags.MaskCommand);
        CGEventPost(CGEventTapLocation.CGHIDEventTap, keyDown);

        await Task.Delay(50);

        var keyUp = CGEventCreateKeyboardEvent(source, 0x09, false);
        CGEventSetFlags(keyUp, CGEventFlags.MaskCommand);
        CGEventPost(CGEventTapLocation.CGHIDEventTap, keyUp);

        CFRelease(source);
        CFRelease(keyDown);
        CFRelease(keyUp);
    }

    private async Task SimulateReturnKeyAsync()
    {
        await Task.Delay(100);

        var source = CGEventSourceCreate(CGEventSourceStateID.HIDSystemState);

        var keyDown = CGEventCreateKeyboardEvent(source, /* Return */ 0x24, true);
        CGEventPost(CGEventTapLocation.CGHIDEventTap, keyDown);

        await Task.Delay(30);

        var keyUp = CGEventCreateKeyboardEvent(source, 0x24, false);
        CGEventPost(CGEventTapLocation.CGHIDEventTap, keyUp);

        CFRelease(source);
        CFRelease(keyDown);
        CFRelease(keyUp);
    }
}
```

### 4.3 AXUIElement 关键 API

```csharp
internal static partial class AccessibilityNative
{
    [DllImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
    public static extern IntPtr AXUIElementCreateApplication(int pid);

    [DllImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
    public static extern int AXUIElementCopyAttributeValue(
        IntPtr element, IntPtr attribute, out IntPtr value);

    [DllImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
    public static extern int AXUIElementSetAttributeValue(
        IntPtr element, IntPtr attribute, IntPtr value);

    // 常用属性名
    public static IntPtr kAXFocusedUIElementAttribute
        => CFStringCreate("AXFocusedUIElement");
    public static IntPtr kAXValueAttribute
        => CFStringCreate("AXValue");
    public static IntPtr kAXSelectedTextRangeAttribute
        => CFStringCreate("AXSelectedTextRange");
    public static IntPtr kAXRoleAttribute
        => CFStringCreate("AXRole");
}
```

### 4.4 Electron/Slack 兼容性

Slack Desktop macOS (Electron) 的 Accessibility 支持：

| 属性 | 支持情况 |
|------|---------|
| AXFocusedUIElement | ✅ 返回 AXTextArea |
| AXValue (读取) | ✅ 返回当前文本 |
| AXValue (设置) | ⚠️ 部分 Electron 版本不支持 |
| AXSelectedTextRange | ✅ 返回选中范围 |

如果 AXValue 设置不被 Slack 接受（Electron 可能忽略），自动降级到剪贴板策略。

## 5. 剪贴板服务

```csharp
namespace LiveLingo.App.Platform.macOS;

public class MacClipboardService : IClipboardService
{
    public Task SetTextAsync(string text, CancellationToken ct)
    {
        // NSPasteboard
        var pasteboard = GetGeneralPasteboard();
        PasteboardClear(pasteboard);
        PasteboardSetString(pasteboard, text);
        return Task.CompletedTask;
    }

    public Task<string?> GetTextAsync(CancellationToken ct)
    {
        var pasteboard = GetGeneralPasteboard();
        var text = PasteboardGetString(pasteboard);
        return Task.FromResult<string?>(text);
    }

    // Objective-C runtime 调用 [NSPasteboard generalPasteboard]
    private static IntPtr GetGeneralPasteboard()
    {
        var cls = objc_getClass("NSPasteboard");
        var sel = sel_registerName("generalPasteboard");
        return objc_msgSend(cls, sel);
    }
}
```

## 6. MacPlatformServices 聚合

```csharp
namespace LiveLingo.App.Platform.macOS;

internal class MacPlatformServices : IPlatformServices
{
    public IHotkeyService Hotkey { get; }
    public IWindowTracker WindowTracker { get; }
    public ITextInjector TextInjector { get; }
    public IClipboardService Clipboard { get; }

    public MacPlatformServices(ILoggerFactory loggerFactory)
    {
        Clipboard = new MacClipboardService();
        WindowTracker = new MacWindowTracker();
        Hotkey = new CoreGraphicsHotkeyService();
        TextInjector = new MacTextInjector(
            Clipboard, loggerFactory.CreateLogger<MacTextInjector>());
    }

    public void Dispose()
    {
        Hotkey.Dispose();
    }
}
```

DI 注册：

```csharp
// App.axaml.cs
if (OperatingSystem.IsMacOS())
    services.AddSingleton<IPlatformServices, MacPlatformServices>();
```

## 7. 首次启动权限引导

### 7.1 流程

```
App 启动
  │
  ├─ AccessibilityPermission.IsGranted()
  │   ├─ true  → 正常启动
  │   └─ false → 显示引导窗口
  │
  ▼ 引导窗口:
  ┌──────────────────────────────────────────────────────┐
  │  LiveLingo needs Accessibility permission              │
  │                                                        │
  │  1. Click "Open Settings" to open System Settings      │
  │  2. Find LiveLingo in the list                         │
  │  3. Toggle it ON                                       │
  │  4. Click "Verify" to continue                         │
  │                                                        │
  │  [Open Settings]              [Verify]  [Quit]         │
  └──────────────────────────────────────────────────────┘
  │
  ├─ [Open Settings] → NSWorkspace.open("x-apple.systempreferences:...")
  ├─ [Verify] → 重新检测 → 成功则继续，失败则提示
  └─ [Quit] → 退出应用
```

### 7.2 自动重试

用户授权后可能需要重启 App 才生效（macOS 限制）。
如果 `AXIsProcessTrustedWithOptions` 在授权后仍返回 false，提示用户重启。

## 8. 目录结构

```
src/LiveLingo.App/
└── Platform/
    └── macOS/
        ├── MacPlatformServices.cs
        ├── CoreGraphicsHotkeyService.cs
        ├── MacWindowTracker.cs
        ├── MacTextInjector.cs
        ├── MacClipboardService.cs
        ├── AccessibilityPermission.cs
        ├── MacKeyMap.cs
        └── Native/
            ├── CoreGraphicsNative.cs     // CGEvent* P/Invoke
            ├── AccessibilityNative.cs    // AXUIElement* P/Invoke
            ├── CoreFoundationNative.cs   // CF* P/Invoke
            └── ObjCRuntime.cs            // objc_msgSend, sel_*, class_*
```

## 9. 已知限制与风险

| 风险 | 缓解措施 |
|------|---------|
| Electron AXValue 设置不生效 | 自动降级到 Cmd+V 策略 |
| CGEventTap 在 sandboxed app 中受限 | 不沙箱发布（DMG 分发） |
| macOS 版本间 Accessibility API 行为差异 | 测试覆盖 macOS 12-15 |
| 用户拒绝 Accessibility 权限 | 优雅降级：仅快捷键不可用时提示 |
| objc_msgSend 签名问题 (arm64) | 使用 [UnmanagedCallersOnly] 和正确的 calling convention |

## 10. 测试

### 10.1 测试 checklist

- [ ] Accessibility 权限检测返回正确结果
- [ ] 未授权时显示引导窗口
- [ ] CGEventTap 捕获 Ctrl+Alt+T
- [ ] CGEventTap 不干扰其他快捷键
- [ ] 窗口追踪返回 Slack 窗口信息
- [ ] AXUIElement 策略在 TextEdit 中注入成功
- [ ] AXUIElement 策略在 Slack 中注入成功（或降级）
- [ ] Cmd+V 策略在 Slack 中注入成功
- [ ] Return 键模拟在 Slack 中触发发送
- [ ] 剪贴板操作不丢失用户原有内容（保存/恢复）
