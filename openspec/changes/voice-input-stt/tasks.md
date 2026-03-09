## 1. Core Contracts & DI

- [x] 1.1 在 Core 抽象层新增语音契约：`AudioCaptureResult`、`SpeechTranscriptionResult`、`SpeechInputErrorCode`。验收：契约不依赖平台命名空间，编译通过。
- [x] 1.2 新增 `IAudioCaptureService`、`ISpeechToTextEngine`、`ISpeechInputCoordinator` 接口与 DI 注册骨架。验收：Overlay 仅依赖协调器即可编译运行。
- [x] 1.3 提供空实现兜底与可恢复错误映射。验收：未实现平台能力时返回可解释错误而非崩溃。

## 2. Platform Audio Capture

- [x] 2.1 实现 Windows 录音服务（开始/停止/权限状态/错误映射），统一输出 STT 所需音频格式。验收：Windows 下可获得非空录音数据。
- [x] 2.2 实现 macOS 录音服务与权限检查逻辑。验收：权限授予与拒绝路径都能被上层正确识别。
- [x] 2.3 增加录音边界约束（重复开始/停止、最大时长、关闭时取消）。验收：越界场景返回明确错误码并释放资源。
- [x] 2.4 为平台录音服务补充测试替身与关键单元测试。验收：开始/停止、权限拒绝、重复调用、超时场景可被测试覆盖。

## 3. Offline STT Engine

- [x] 3.1 接入离线 STT 后端适配器并将 STT 模型纳入可选模型管理。验收：模型可被列举与安装，且不影响现有翻译模型逻辑。
- [x] 3.2 实现 `TranscribeAsync` 主流程（加载、转写、取消、异常映射）。验收：成功场景返回文本，取消场景抛 `OperationCanceledException`。
- [x] 3.3 实现"模型缺失 -> 显式下载 -> 重试转写"链路（通过协调器触发）。验收：下载成功后可在当前 Overlay 会话内直接重试成功。
- [x] 3.4 增加 STT 测试：离线可用、模型缺失、音频格式错误、取消。验收：核心测试全部通过。

## 4. Overlay Voice Dictation Integration

- [x] 4.1 在 `OverlayViewModel` 接入协调器命令（开始录音/停止录音/下载模型），并增加语音状态机字段。验收：状态按 `Idle -> Recording -> Transcribing -> Idle|Error` 正常流转。
- [x] 4.2 在 `OverlayWindow` 增加语音按钮、下载引导入口与状态展示，并接入错误提示。验收：权限拒绝与模型缺失均有可见且可操作反馈。
- [x] 4.3 分离语音状态与翻译状态展示（避免覆盖同一状态文案）。验收：语音进行中不影响翻译状态可观测性，反之亦然。
- [x] 4.4 将转写结果回填 `SourceText` 并复用现有翻译节流/取消机制。验收：语音转写后自动触发翻译，且不破坏手动输入路径。

## 5. Verification

- [x] 5.1 运行并修复相关自动化测试（Core + Desktop）。验收：受影响测试全部通过，无新增失败。
- [ ] 5.2 执行手动回归（权限拒绝、模型缺失下载、成功转写、重复点击开始/停止、继续手输、关闭时取消）。验收：六条路径行为与规格一致并记录结果。
