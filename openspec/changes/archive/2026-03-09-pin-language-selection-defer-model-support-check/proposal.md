## Why

当前语言选择入口依赖引擎声明或即时支持性判断，用户会在设置/浮窗/向导中看到被动收缩的可选项，且在未下载模型时被过早阻断。需要统一为固定语言目录，先允许用户完成选择与翻译尝试，再由用户自行决定下载对应模型。

## What Changes

- 将所有语言选择 UI（设置页、浮窗、向导）统一切换到固定语言目录，不再从引擎动态读取并收缩可选语言。
- 移除翻译前的主动 `SupportsLanguagePair` 阻断校验，改为直接走翻译流程；若模型缺失/不支持，由翻译结果错误反馈引导用户到模型管理下载。
- 统一错误提示语义：不再“提前判不支持”，而是“翻译失败时给出可操作提示（下载模型）”。
- 保持现有配置结构与模型管理入口不变，避免破坏现有用户设置与流程。

## Capabilities

### New Capabilities

- `fixed-language-catalog`: 定义并复用固定语言目录作为 UI 语言选择唯一来源，与模型安装状态解耦。

### Modified Capabilities

- `language-dropdown-ui`: 由“来源于 `ITranslationEngine.SupportedLanguages`”调整为“来源于固定语言目录”；不再因当前模型支持性动态过滤。

## Impact

- Affected code:
  - `src/LiveLingo.Desktop/ViewModels/SettingsViewModel.cs`
  - `src/LiveLingo.Desktop/ViewModels/OverlayViewModel.cs`
  - `src/LiveLingo.Desktop/ViewModels/SetupWizardViewModel.cs`
  - `src/LiveLingo.Desktop/Views/*.axaml`
- Behavior impact:
  - 用户可在所有入口始终看到一致语言选项；
  - 不支持语对不再被预检拦截，失败时由错误反馈与模型下载入口承接。
- Risks:
  - 失败路径触发频率上升（可接受）；需确保错误提示可理解且可操作。
