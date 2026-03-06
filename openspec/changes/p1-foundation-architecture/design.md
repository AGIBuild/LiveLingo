## Context

PoC（docs/poc-design.md）验证了 Windows 上通过 `WH_KEYBOARD_LL` 全局快捷键 + `FindWindowExW` 查找 Electron 子窗口 + `SendInput`/`WM_CHAR` 双策略注入的方案可行。但 PoC 代码全部在单一 `LiveLingo.App` 项目中，使用 static 类（`TextInjector.InjectText()`、`WindowTracker.GetForegroundWindowInfo()`），ViewModel 直接 new 依赖，核心翻译逻辑无法复用到其他产品。

P1 的目标是将此 PoC 重构为正式分层架构，不改变运行时行为，为后续 P2-P5 建立扩展基础。

## Goals / Non-Goals

**Goals:**
- 建立 `LiveLingo.Core`（零平台依赖）+ `LiveLingo.App`（UI + 平台）双项目 solution
- Core 定义全部公开接口（翻译管线、引擎、处理器、检测器、模型管理），提供 stub 实现
- App 定义平台抽象接口（快捷键、窗口、注入、剪贴板），将 PoC Windows 代码迁移到接口实现
- 引入 DI 容器，OverlayViewModel 通过构造函数接收依赖
- 运行时行为与 PoC 100% 一致（stub 翻译 `[EN] xxx` + Windows 注入）

**Non-Goals:**
- 不引入 ONNX Runtime / LLamaSharp / 任何 AI 依赖（P2/P3）
- 不实现 macOS 平台（P4）
- 不添加用户配置/持久化（P5）
- 不改变 OverlayWindow 的 UI 布局或样式

## Decisions

### D1: Core 与 App 的分界线

**决策**: 翻译管线、引擎、处理器、检测器、模型管理放入 Core；平台服务（快捷键、窗口、注入、剪贴板）放入 App 的 `Platform/` 命名空间。

**替代方案**: 将平台接口也放入 Core 以实现"全复用"。**否决**——快捷键/窗口追踪/注入是桌面应用特有关注点，其他产品（如 Web 服务）不需要也不能使用。Core 应只包含翻译业务逻辑。

### D2: stub 而非真实翻译

**决策**: P1 阶段所有 Core 接口使用 stub 实现（`StubTranslationEngine` 返回 `[EN] {input}`）。

**理由**: 将架构重构与 AI 引擎接入解耦。P1 专注结构正确性，P2 替换 stub 为 MarianMT 真实实现。这样 P1 的验收只需验证"行为不变"。

### D3: DI 注册策略

**决策**: Core 提供 `AddLiveLingoCore()` 扩展方法注册所有 Core 服务。App 在 `App.axaml.cs` 中调用此方法并按 OS 注册平台服务。

```
ServiceCollection
  ├── AddLiveLingoCore()           → ITranslationPipeline, ITranslationEngine(stub), ILanguageDetector(stub), IModelManager(stub)
  └── AddSingleton<IPlatformServices, WindowsPlatformServices>()  → IHotkeyService, IWindowTracker, ITextInjector, IClipboardService
```

**替代方案**: 使用 Avalonia 的内置 DI（`IServiceProvider` via `Application.Current`）。**否决**——`Microsoft.Extensions.DependencyInjection` 是 .NET 生态标准，与 Core 的 `IServiceCollection` 扩展模式一致。

### D4: ITextInjector async 包装

**决策**: `ITextInjector.InjectAsync` 定义为 async 接口。Win32 实现内部使用 `Task.Run()` 包装同步的 `Thread.Sleep` + P/Invoke 调用。

**理由**: PoC 中 `TextInjector.InjectText()` 是同步阻塞（包含多个 `Thread.Sleep`）。直接定义 async 接口可避免 UI 线程阻塞，且后续 macOS 的 `AXUIElement` 实现天然支持 async。P1 阶段不重写为真正 async，仅包装。

### D5: IPlatformServices 聚合模式

**决策**: 使用聚合接口 `IPlatformServices` 持有四个子服务，而非在 DI 中分别注册四个接口。

**理由**: 平台服务之间有组合依赖（`TextInjector` 需要 `ClipboardService`），聚合使得平台实现类内部可控制生命周期。DI 注册只需一行。缺点是不支持跨平台混合（如 Windows hotkey + macOS clipboard），但无此需求。

### D6: 剪贴板服务提取

**决策**: 从 PoC 的 `TextInjector` 中提取剪贴板操作到独立的 `IClipboardService`。

**理由**: 剪贴板操作可被多个组件使用（文本注入、未来的拖放支持）。独立接口更符合单一职责原则，且测试更方便（可 mock 剪贴板）。

## Dependency Graph

```
LiveLingo.App.csproj ──ProjectReference──▶ LiveLingo.Core.csproj

LiveLingo.Core:
  ServiceCollectionExtensions ──registers──▶ TranslationPipeline
  TranslationPipeline ──uses──▶ ITranslationEngine (stub)
  TranslationPipeline ──uses──▶ ILanguageDetector (stub)
  TranslationPipeline ──uses──▶ ITextProcessor[] (empty in P1)

LiveLingo.App:
  App.axaml.cs ──creates──▶ ServiceProvider
  App.axaml.cs ──resolves──▶ IPlatformServices, ITranslationPipeline
  App.axaml.cs ──creates──▶ OverlayViewModel(target, pipeline, injector)

  WindowsPlatformServices ──composes──▶ Win32HotkeyService
  WindowsPlatformServices ──composes──▶ Win32WindowTracker
  WindowsPlatformServices ──composes──▶ Win32TextInjector(clipboard)
  WindowsPlatformServices ──composes──▶ Win32ClipboardService
```

## Risks / Trade-offs

- **[Risk] PoC 行为回归**: 重构过程中可能引入微妙的时序差异（如 Thread.Sleep 位置变化）影响注入稳定性。→ **Mitigation**: 保留 PoC 的诊断工具（`--test-slack`），每次重构后使用它验证注入行为。
- **[Risk] Task.Run 包装开销**: async 包装同步代码会占用线程池线程。→ **Mitigation**: 注入操作低频（用户手动触发），开销可忽略。P2+ 可逐步改为真正 async。
- **[Trade-off] IPlatformServices 聚合 vs 独立注册**: 聚合模式简化注册但降低灵活性。→ 当前产品不需要跨平台混合，聚合更清晰。

## Migration Plan

1. 创建 `LiveLingo.Core` 项目，定义所有接口和 stub 实现
2. 在 `LiveLingo.App` 中添加 `Platform/` 命名空间，定义抽象接口
3. 将 PoC 的 `Services/Platform/Windows/` 文件逐一迁移到 `Platform/Windows/`，实现对应接口
4. 从 `TextInjector` 提取剪贴板操作到 `Win32ClipboardService`
5. 修改 `App.axaml.cs` 使用 DI 容器组装
6. 修改 `OverlayViewModel` 使用构造函数注入
7. 修改 `OverlayWindow.axaml.cs` 从 DI 获取依赖传入 ViewModel
8. 运行诊断工具验证注入行为不变
9. 删除 PoC 旧文件路径（`Services/Platform/`），确认编译通过

**Rollback**: 如果发现严重回归，可 `git revert` 回 PoC 提交。PoC 代码已提交保存。
