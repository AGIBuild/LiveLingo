## Why

LiveLingo 目前仅支持 Windows 平台。macOS 是开发者和国际化团队的主要工作平台之一，缺少 macOS 支持将损失大量目标用户。P1 建立的 `IPlatformServices` 抽象层已为跨平台扩展做好准备，P4 只需实现 macOS 侧的平台服务即可让全部 Core 功能（翻译 + AI 后处理）在 macOS 上运行。

## What Changes

- 实现 `CoreGraphicsHotkeyService`：通过 `CGEventTap` 监听全局快捷键
- 实现 `MacWindowTracker`：通过 `NSWorkspace` + `CGWindowListCopyWindowInfo` 获取前台窗口信息
- 实现 `MacTextInjector`：主策略使用 `AXUIElement.SetValue` 直接设值，备选策略使用 `CGEventPost` 模拟 Cmd+V 粘贴
- 实现 `MacClipboardService`：通过 `NSPasteboard` 操作系统剪贴板
- 创建 `MacPlatformServices` 聚合类
- 添加 Accessibility 权限检测和首次启动引导 UI
- 在 DI 注册中添加 macOS 分支

## Capabilities

### New Capabilities
- `macos-hotkey`: CGEventTap 全局快捷键服务（含 macOS key code 映射）
- `macos-window-tracker`: NSWorkspace + CGWindowList 前台窗口追踪
- `macos-text-injection`: AXUIElement 文本注入（主策略）+ CGEventPost Cmd+V（备选策略）
- `macos-permissions`: Accessibility 权限检测、授权引导 UI、重启提示

### Modified Capabilities

(无现有 spec 需要修改)

## Impact

- **P/Invoke**: 新增大量 macOS native API 声明（CoreGraphics, ApplicationServices, AppKit, CoreFoundation）
- **权限**: 需要 Accessibility 权限（用户手动授权），Info.plist 需添加 `NSAccessibilityUsageDescription`
- **分发**: macOS 版本不能沙箱化（CGEventTap 需要非沙箱环境），需 DMG/PKG 分发
- **DI**: `App.axaml.cs` 添加 `OperatingSystem.IsMacOS()` 分支注册 `MacPlatformServices`
- **测试**: 需要真实 macOS 硬件测试（Accessibility API 不可模拟）
