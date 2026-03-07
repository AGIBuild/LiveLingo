## Context

Settings 页面的源语言和目标语言使用自由文本 `TextBox`，用户需手动输入语言代码。Overlay 浮动窗口的 `AvailableLanguages` 是硬编码的 10 种语言静态列表。同时 `LlamaTranslationEngine` 内部也有一份硬编码的 `LanguageNames` 字典。三处语言定义互不关联，存在重复。

`ITranslationEngine` 接口已有 `SupportsLanguagePair()` 方法，但当前 `LlamaTranslationEngine` 恒返回 `true`，未真正利用。

## Goals / Non-Goals

**Goals:**
- 扩展 `ITranslationEngine` 接口，让引擎自行声明支持的语言列表
- 提供 `LanguageInfo` 值类型统一语言代码与显示名
- Settings 页面源语言/目标语言改为 `ComboBox`，数据源来自翻译引擎
- Overlay 的可用语言从翻译引擎获取，消除硬编码
- 消除 `LlamaTranslationEngine.LanguageNames`、`OverlayViewModel.AvailableLanguages`、Settings `TextBox` 三处重复

**Non-Goals:**
- 不修改 `ModelDescriptor`（语言能力不属于模型下载元数据）
- 不实现多引擎动态切换
- 不修改翻译引擎本身的翻译逻辑

## Decisions

### 1. 语言能力由翻译引擎声明

**决定**: 在 `ITranslationEngine` 接口中添加 `IReadOnlyList<LanguageInfo> SupportedLanguages { get; }` 属性。

**理由**: 语言能力不是模型文件的固有属性，而是翻译引擎如何使用模型的结果。同一个 Qwen 模型既用于翻译也用于后处理，语言能力只在翻译场景有意义。引擎是唯一知道自己能翻译什么语言的组件。

**替代方案**: 在 `ModelDescriptor` 上添加 `SupportedLanguages` → 拒绝，因为这会把 UI 关注点混入下载基础设施，且与 `LlamaTranslationEngine.LanguageNames` 重复。

### 2. LanguageInfo 值类型

**决定**: 在 `LiveLingo.Core.Engines` 命名空间定义 `record LanguageInfo(string Code, string DisplayName)`。

**理由**: `ComboBox` 需要同时显示友好名称和存储语言代码。作为引擎 API 的一部分放在 Engines 命名空间。

### 3. 消除重复的语言定义

**决定**: 
- `LlamaTranslationEngine.LanguageNames` 字典重构为实现 `SupportedLanguages` 属性
- `OverlayViewModel.AvailableLanguages` 静态列表删除，改为从注入的 `ITranslationEngine.SupportedLanguages` 获取
- `SettingsViewModel` 注入 `ITranslationEngine` 获取语言列表

**依赖关系**:
```
ITranslationEngine.SupportedLanguages (单一数据源)
    ├── SettingsViewModel.AvailableLanguages → ComboBox 绑定
    └── OverlayViewModel (语言循环列表)
```

### 4. DI 不变

**决定**: 不需要新增 DI 注册。`ITranslationEngine` 已注册为 Singleton（`LlamaTranslationEngine`），ViewModel 直接注入现有接口即可。

### 5. UI 绑定方式

**决定**: `SettingsViewModel` 暴露 `AvailableLanguages` 属性（`IReadOnlyList<LanguageInfo>`），`ComboBox` 用 `DisplayMemberBinding` 显示 `DisplayName`，`SelectedValueBinding` 绑定 `Code`。

## Risks / Trade-offs

- **[ViewModel 依赖 Engine]** `SettingsViewModel` 需注入 `ITranslationEngine` → 可接受，因为 Settings 需要知道引擎能力来限制语言选择。仅读取属性，不调用翻译方法。
- **[引擎未加载时语言列表]** `LlamaTranslationEngine` 的语言列表是静态声明的，不依赖模型加载状态，因此始终可用。
- **[StubTranslationEngine]** 测试桩也需要实现 `SupportedLanguages`，返回测试用语言列表即可。
