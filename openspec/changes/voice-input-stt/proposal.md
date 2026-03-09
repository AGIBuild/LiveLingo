## Why

当前输入仅支持手动键入，长句或高频场景下效率较低，也限制了可访问性。需要提供本地语音输入能力，在不引入云依赖的前提下将语音快速转为可翻译文本。

## What Changes

- 新增平台音频采集抽象，统一 Windows/macOS 麦克风录音与权限状态。
- 新增离线语音转文本抽象与本地 STT 引擎实现（Whisper 系）。
- 新增 `ISpeechInputCoordinator` 统一编排权限检查、模型检查/下载、录音与转写，作为 Overlay 唯一语音入口。
- 在 Overlay 中新增语音输入交互（开始/停止、状态提示、错误提示、转写结果回填），并与现有翻译状态分离展示。
- 将 STT 结果接入现有翻译链路，复用现有取消与重翻机制。
- 增加语音输入配置项与回归测试（权限、录音失败、模型缺失下载、转写失败、并发边界、正常路径）。

## Capabilities

### New Capabilities
- `platform-audio-capture`: 定义跨平台麦克风采集、权限检查与音频流抽象。
- `offline-speech-to-text`: 定义离线 STT 引擎接口、模型管理与转写行为。
- `overlay-voice-dictation`: 定义 Overlay 语音输入交互与与翻译管线的衔接规则。

### Modified Capabilities
- (none)

## Impact

- `LiveLingo.Core`: 新增语音契约（音频结果、转写结果、错误码）、STT 接口与转写服务；扩展模型分层（STT 模型）。
- `LiveLingo.Desktop`: 平台录音服务、语音协调器、权限与下载提示、Overlay 语音按钮与状态展示、Settings 语音配置项。
- 测试：Core STT/契约测试、Desktop 协调器与 ViewModel/UI 交互测试、权限与异常及并发边界测试。
