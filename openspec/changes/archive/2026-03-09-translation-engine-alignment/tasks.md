## 1. Engine Routing Alignment

- [x] 1.1 将 `ITranslationEngine` 默认 DI 注册切换为 `MarianOnnxEngine`，并移除 Qwen 作为主翻译引擎的路径。验收：运行时解析 `ITranslationEngine` 返回 Marian 实现。
- [x] 1.2 收敛管线职责边界：翻译阶段只调用 `ITranslationEngine`，后处理阶段只调用 `ITextProcessor`。验收：`PostProcessing=Off` 时不触发任何 Qwen 处理器。

## 2. Marian Translation Completion

- [x] 2.1 补全 `MarianOnnxEngine` 分词与模型文件装载逻辑，确保模型目录和文件缺失时抛出明确错误。验收：缺失模型场景返回可定位的错误信息。
- [x] 2.2 实现 Marian ONNX 推理闭环（编码->解码->文本输出），支持取消与不支持语言对错误。验收：取消时抛 `OperationCanceledException`，未知语言对抛 `NotSupportedException`。
- [x] 2.3 为 Marian 主链路添加单元测试与集成级别断言。验收：测试覆盖成功翻译、取消、不支持语言对三类场景。

## 3. Model Requirement Tiering

- [x] 3.1 按“必需/可选”重构模型检查逻辑：必需层仅包含 FastText + 默认 Marian 语言对。验收：无 Qwen 但必需模型齐全时可正常打开并使用翻译。
- [x] 3.2 调整 Setup Wizard 下载策略为“默认仅下载必需层”，并保留可选模型后续下载入口。验收：首次引导不下载 Qwen 也可完成并进入主流程。
- [x] 3.3 调整后处理启用逻辑为按需下载 Qwen。验收：用户开启后处理且未安装 Qwen 时出现明确下载引导。

## 4. Regression Verification

- [x] 4.1 更新并通过 Core/Desktop 相关测试（DI、引导、健康检查、后处理开关）。验收：相关测试全部通过且无新增失败。
- [x] 4.2 执行手动回归：首次引导、纯翻译路径、后处理路径。验收：三条路径均符合规格预期并记录验证结果。

## Regression Notes (2026-03-08)

- 首次引导路径（无可选模型阻断）：
  - `dotnet test tests/LiveLingo.Desktop.Tests/LiveLingo.Desktop.Tests.csproj --filter "FullyQualifiedName~FirstRunFlow_WizardThenTrayOnly"`
  - 结果：Passed（1/1）
- 纯翻译路径（Marian，不依赖 Qwen）：
  - `dotnet run --project build -- ProbeTranslation`
  - `dotnet run --project build -- ProbeTranslationAll`
  - 结果：Passed（2/2 + 1/1），覆盖单句与批量短句回归
- 后处理路径（Qwen 按需启用/缺失降级引导）：
  - `dotnet test tests/LiveLingo.Desktop.Tests/LiveLingo.Desktop.Tests.csproj --filter "FullyQualifiedName~Constructor_WithSettings_SkipsPostProcessing_WhenQwenMissing|FullyQualifiedName~Constructor_WithQwenSelected_UsesPostProcessingEvenWhenDefaultModeOff"`
  - 结果：Passed（2/2）
