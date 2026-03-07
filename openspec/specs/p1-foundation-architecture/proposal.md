## Why

PoC 阶段验证了 Windows 平台的全局快捷键、窗口追踪和文本注入方案可行，但所有代码堆砌在单一项目中，使用 static 类直接调用，无法复用核心翻译逻辑，也无法扩展到 macOS 平台。需要将 PoC 重构为正式的分层架构，为后续 P2-P5 的 AI 引擎接入、跨平台支持和配置管理建立基础。

## What Changes

- 创建 `LiveLingo.Core` 项目，定义翻译管线、引擎、后处理、语言检测、模型管理的全部公开接口
- 创建 `LiveLingo.App` 项目，定义平台抽象接口（快捷键、窗口追踪、文本注入、剪贴板）
- 将 PoC 的 Windows 实现代码迁移到 `IPlatformServices` 接口实现
- 引入 DI 容器（`Microsoft.Extensions.DependencyInjection`），替代所有 static 类直接调用
- OverlayViewModel 改为构造函数注入 `ITranslationPipeline` 和 `ITextInjector`
- 提供 stub 翻译实现（`[EN] {input}`），确保功能与 PoC 完全一致
- 从 `TextInjector` 中提取剪贴板操作到独立的 `IClipboardService`
- PoC 诊断工具保留在 `#if DEBUG` 条件编译中

## Capabilities

### New Capabilities
- `core-interfaces`: Core 层公开接口定义（ITranslationPipeline, ITranslationEngine, ITextProcessor, ILanguageDetector, IModelManager）及 stub 实现
- `platform-abstractions`: App 层平台服务抽象（IPlatformServices, IHotkeyService, IWindowTracker, ITextInjector, IClipboardService）
- `windows-platform`: PoC Windows 实现迁移到接口实现（Win32HotkeyService, Win32WindowTracker, Win32TextInjector, Win32ClipboardService）
- `di-assembly`: DI 容器组装、ServiceCollectionExtensions、App 启动流程重构

### Modified Capabilities

(无现有 spec 需要修改)

## Impact

- **项目结构**: 从单项目变为 `LiveLingo.Core` + `LiveLingo.App` 双项目 solution
- **依赖**: Core 仅依赖 `Microsoft.Extensions.*` 抽象包，不引入 ONNX/LLamaSharp
- **API**: OverlayViewModel 构造函数签名变更（breaking，但仅内部使用）
- **行为**: 运行时行为与 PoC 完全一致（stub 翻译 + Windows 注入）
