## Why

当前运行时将 Qwen 作为主翻译引擎，而 `MarianOnnxEngine` 仍是占位实现，已偏离既有 P2/P3 的架构边界与性能预期。需要回归“Marian 负责翻译、Qwen 负责后处理”的职责拆分，恢复规格一致性并降低主链路时延与资源占用。

## What Changes

- 将 `ITranslationEngine` 默认实现切回 `MarianOnnxEngine`，`LlamaTranslationEngine` 不再承担主翻译职责。
- 完成 `MarianOnnxEngine` 的可用翻译链路（分词、ONNX 推理、解码），移除占位返回。
- 明确模型分层：`FastText + 默认 Marian 语言对` 为必需模型，`Qwen` 为后处理可选模型。
- 调整 Setup Wizard、启动健康检查与模型状态判断逻辑，按“必需/可选”分层执行下载与提示。
- 增加回归测试，覆盖 DI 绑定、语言对能力、无 Qwen 的纯翻译路径与启用 Qwen 的后处理路径。

## Capabilities

### New Capabilities
- `translation-engine-routing`: 定义翻译主链路与后处理链路的引擎职责边界和运行时路由规则。
- `model-requirement-tiering`: 定义必需模型与可选模型分层，以及下载、校验、降级行为。

### Modified Capabilities
- (none)

## Impact

- `LiveLingo.Core`: `ServiceCollectionExtensions`、`MarianOnnxEngine`、`LlamaTranslationEngine`、`TranslationPipeline`、模型注册与模型检查逻辑。
- `LiveLingo.Desktop`: `App.axaml.cs`、`SetupWizardViewModel`、`SetupWizardWindow`、Settings 中模型管理相关展示与交互。
- 测试：Core 引擎测试、管线测试、Desktop 引导与启动检查测试。
