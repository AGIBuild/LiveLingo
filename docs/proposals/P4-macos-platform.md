# P4: macOS Platform

> 实现 macOS 平台的全部平台服务，使 LiveLingo 在 Mac 上完整可用。

## 前置依赖

- P1 完成（平台接口已定义）
- 可与 P2/P3 并行开发

## 目标

- 实现 IHotkeyService：CGEventTap 全局键盘事件
- 实现 IWindowTracker：NSWorkspace 前台应用识别
- 实现 ITextInjector：Accessibility API (AXUIElement) 文本注入
- 实现 IClipboardService：NSPasteboard
- 权限引导 UI（Accessibility + Input Monitoring）

## 不做

- 不实现 App Store 分发（sandbox 限制太多）
- 不实现 Linux 平台

## 交付内容

### 1. macOS 平台服务映射

| 接口 | macOS 实现 | 技术 |
|------|-----------|------|
| IHotkeyService | MacHotkeyService | CGEventTap (Core Graphics) |
| IWindowTracker | MacWindowTracker | NSWorkspace.frontmostApplication |
| ITextInjector | MacTextInjector | AXUIElement.setValue |
| IClipboardService | MacClipboardService | NSPasteboard.generalPasteboard |

### 2. macOS 与 Windows 关键差异

```
Windows 注入流程:
  FindChromeRenderer ──▶ SendInput Ctrl+V ──▶ 文本出现在输入框
  (需要找到 Chrome 子窗口)    (需要前台权限)

macOS 注入流程:
  AXUIElementCopyAttributeValue ──▶ AXUIElementSetAttributeValue
  (Accessibility API 直接操作 UI 元素的 value 属性)
  无需 Chrome 子窗口 hack，无需前台权限
  但需要用户授予 Accessibility 权限
```

macOS 方案更优雅但权限更严格。

### 3. 权限管理

```
首次启动
  │
  ▼
┌─────────────────────────────────────────┐
│  LiveLingo needs permissions            │
│                                         │
│  ☐ Accessibility                        │
│    Required to inject text into apps    │
│    [Open System Settings]               │
│                                         │
│  ☐ Input Monitoring                     │
│    Required for global hotkey           │
│    [Open System Settings]               │
│                                         │
│  Status: Waiting for permissions...     │
└─────────────────────────────────────────┘
```

权限检测 API：
- `AXIsProcessTrusted()` — Accessibility 权限
- `CGPreflightListenEventAccess()` — Input Monitoring 权限

### 4. .NET macOS 互操作

macOS native API 调用方式：
- **ObjCRuntime** (.NET 内建)：访问 NSWorkspace、NSPasteboard
- **P/Invoke**：调用 Core Graphics (CGEventTap)、Accessibility (AXUIElement)
- 或考虑使用 **MonoMac / MacCatalyst** 绑定

关键 P/Invoke：
```csharp
// Core Graphics - 全局键盘事件
[DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
static extern IntPtr CGEventTapCreate(...);

// Accessibility - UI 元素操作
[DllImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
static extern int AXUIElementSetAttributeValue(IntPtr element, IntPtr attribute, IntPtr value);

// 权限检测
[DllImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
static extern bool AXIsProcessTrusted();
```

### 5. 文本注入策略

macOS 注入比 Windows 简单——Accessibility API 支持直接设置 UI 元素值：

```
1. 获取前台应用的 focused UI element
   AXUIElementCopyAttributeValue(app, kAXFocusedUIElementAttribute)

2. 获取当前文本值
   AXUIElementCopyAttributeValue(element, kAXValueAttribute)

3. 在光标位置插入翻译文本
   AXUIElementSetAttributeValue(element, kAXValueAttribute, newValue)

4. [可选] 模拟 Enter 键发送
   CGEventPost(kCGHIDEventTap, enterKeyEvent)
```

对 Electron 应用（Slack macOS）同样有效，因为 Chromium 实现了 Accessibility API。

### 6. 项目结构

```
LiveLingo.App/
└── Platform/
    └── macOS/
        ├── MacPlatformServices.cs
        ├── MacHotkeyService.cs
        ├── MacWindowTracker.cs
        ├── MacTextInjector.cs
        ├── MacClipboardService.cs
        ├── MacPermissionChecker.cs
        └── NativeMethods.macOS.cs
```

## 验收标准

- [ ] macOS 上 Ctrl+Alt+T (或 Cmd+Option+T) 呼出 overlay
- [ ] overlay 正确显示在前台应用上方
- [ ] 输入框获取焦点，可正常输入
- [ ] Ctrl+Enter 将翻译文本注入到 Slack macOS 输入框
- [ ] 缺少权限时显示引导 UI
- [ ] 授权后无需重启应用即可使用
