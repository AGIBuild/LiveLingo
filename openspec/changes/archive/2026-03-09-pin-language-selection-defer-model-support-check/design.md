## Context

当前桌面端语言选择存在两类耦合：

1. **数据源耦合**：`Settings` 与 `Overlay` 语言列表来自引擎声明（`ITranslationEngine.SupportedLanguages`），向导又维护独立列表，导致来源不一致。
2. **时序耦合**：`OverlayViewModel` 在翻译前主动调用 `SupportsLanguagePair` 做预判，用户在未尝试翻译时就被阻断，且与“按需下载模型”的产品路径冲突。

本次变更目标是把“语言选择”与“模型支持/安装状态”解耦：先允许选择与尝试翻译，失败时再通过错误路径引导用户下载模型。

## Goals / Non-Goals

**Goals:**
- 建立固定语言目录，作为设置页、浮窗、向导的统一选择来源。
- 移除翻译前主动支持性拦截（`SupportsLanguagePair` 预检）。
- 保持现有设置结构、模型下载入口与翻译引擎接口兼容。
- 保证 UI 行为一致：各入口可选语言与顺序一致。

**Non-Goals:**
- 不修改 Marian/Qwen 模型注册规则与下载协议。
- 不新增自动下载策略（仍由用户自行下载所需模型）。
- 不重构 `ITranslationEngine` 接口或核心推理实现。

## Decisions

### D1: 引入固定语言目录服务（Desktop 层）

**Decision**: 在 `LiveLingo.Desktop` 引入统一语言目录（例如 `ILanguageCatalog` + `LanguageCatalog`），提供稳定 `IReadOnlyList<LanguageInfo>`。

**Rationale**:
- 语言选择属于 UI/产品策略，而非推理引擎能力声明。
- 统一目录可消除向导/设置/浮窗三处重复定义与漂移。

**Alternatives**:
- 继续复用 `ITranslationEngine.SupportedLanguages`：会继续把 UI 选择与当前引擎实现耦合，不满足“固定语言”目标。
- 在每个 ViewModel 内各自维护常量列表：会扩大重复和不一致风险。

### D2: 语言选择不再按模型支持性动态收缩

**Decision**: 语言下拉仅由固定目录决定，不按 `SupportsLanguagePair`、已安装模型、已缓存 session 进行禁用或过滤。

**Rationale**:
- 符合“先选语言、后决策下载模型”的用户路径。
- UI 反馈更稳定，不因运行时环境变化导致选项抖动。

### D3: Overlay 去除翻译前主动支持性预检

**Decision**: 移除 `OverlayViewModel` 的 `SupportsLanguagePair` 早期返回逻辑，直接执行 pipeline。

**Rationale**:
- 避免过早阻断，确保错误由翻译流程统一产生。
- 失败时可复用现有错误处理（模型缺失/不支持）并引导下载。

**Alternatives**:
- 保留预检并仅改文案：仍然是“前置阻断”，与目标冲突。

### D4: DI 注册策略

**Decision**: 在应用启动 DI 中注册固定语言目录服务为 `Singleton`，并注入到 `SetupWizardViewModel` / `SettingsViewModel` / `OverlayViewModel`。

**Rationale**:
- 目录数据只读且全局共享，单例成本最低。
- 与现有 MVVM + DI 结构一致。

Dependency graph (logical):
- `SettingsViewModel` -> `ILanguageCatalog`
- `OverlayViewModel` -> `ILanguageCatalog`
- `SetupWizardViewModel` -> `ILanguageCatalog`
- `ITranslationEngine` 保持独立，仅用于翻译执行

## Risks / Trade-offs

- **[Risk] 用户更容易选择到当前未安装模型对应语对** -> **Mitigation**: 保持明确错误提示与模型下载入口，文案强调“可在模型页下载所需模型”。
- **[Risk] 固定目录与未来引擎真实能力偏差** -> **Mitigation**: 固定目录由产品策略维护；引擎失败路径保持权威并可观测。
- **[Trade-off] 去掉预检后失败在后置阶段出现** -> **Mitigation**: 统一错误文案，减少“为何不能选”的困惑。
