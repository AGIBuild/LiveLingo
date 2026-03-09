## Why

当前模型使用路径在 Core 与 Desktop 间职责分散：翻译模型由引擎按需确保，后处理模型由 Overlay 预检并记录告警，导致“未下载 Qwen 但出现噪音日志”的体验问题。同时，Setup Wizard 仍把 FastText 作为必下模型，但当前默认检测链路并未使用该模型，造成下载成本与认知负担。

## What Changes

- 在 Core 层新增统一模型就绪编排能力，集中翻译/后处理模型的就绪判断与异常语义。
- 将 Pipeline 作为模型就绪检查入口，移除 Overlay 的分散预检逻辑，统一错误出口。
- 统一 Qwen 模型路径解析方式：改为通过 `IModelManager.GetModelDirectory()` 获取，移除硬编码目录。
- 收敛 Settings/Overlay 模型语义：`Active Model` 仅表达翻译模型；后处理仅由 `Processing.DefaultMode` 控制。
- 将 FastText 从向导“必下模型”集合移除，仅保留翻译主链路所需 Marian 基线模型。

## Capabilities

### New Capabilities

- `model-readiness-orchestration`: Core 提供统一模型就绪服务与标准化异常，供 Pipeline/UI 一致消费。

### Modified Capabilities

- `translation-pipeline`: 从“仅编排翻译处理”扩展为“编排 + 模型就绪前置检查 + 统一错误语义”。
- `qwen-model-host`: 模型目录解析由硬编码切换为 `IModelManager` 驱动。
- `postprocess-ui`: 缺失后处理模型时改为可操作引导与翻译降级，不再由 UI 预检告警驱动体验。
- `model-management`: 调整 required 模型语义（FastText 不再属于向导必下集）。
- `setup-wizard`: Step 2 下载基线改为 Marian-only。

## Impact

- Affected code:
  - `src/LiveLingo.Core/Translation/TranslationPipeline.cs`
  - `src/LiveLingo.Core/ServiceCollectionExtensions.cs`
  - `src/LiveLingo.Core/Processing/QwenModelHost.cs`
  - `src/LiveLingo.Core/Models/ModelRegistry.cs`
  - `src/LiveLingo.Desktop/ViewModels/OverlayViewModel.cs`
  - `src/LiveLingo.Desktop/ViewModels/SettingsViewModel.cs`
  - `src/LiveLingo.Desktop/ViewModels/SetupWizardViewModel.cs`
  - `src/LiveLingo.Desktop/Resources/i18n/*.json`
- Behavior impact:
  - 未启用后处理时，不再触发 Qwen 缺失噪音告警。
  - 启用后处理且 Qwen 缺失时，用户得到一致可操作提示并可继续 translation-only 主链路。
  - 首次向导下载体积与等待时间下降（移除 FastText 必下）。
- Risks:
  - Pipeline 前置检查后，异常语义变化可能影响现有测试；需同步回归用例与日志断言。
