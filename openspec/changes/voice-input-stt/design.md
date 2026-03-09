## Context

当前 Overlay 仅支持键盘输入，未提供麦克风采集与语音转写链路。为了保持“本地、隐私优先”定位，语音输入需基于离线 STT，并遵守现有分层约束（ViewModel 不依赖平台 API，平台差异经接口抽象）。

该变更跨 Core 与 Desktop：既要引入 STT 引擎与模型管理，也要新增平台录音能力与 UI 交互状态机。

目标依赖关系：

```text
OverlayViewModel
  -> ISpeechInputCoordinator (Desktop service)
     -> IAudioCaptureService (Platform layer)
     -> ISpeechToTextEngine (Core layer)
     -> IModelManager (Core model management)
  -> ITranslationPipeline (existing)
```

## Goals / Non-Goals

**Goals:**
- 提供本地语音输入到文本的可用链路，并回填到 Overlay 输入框。
- 保持跨平台抽象，平台差异仅在 `Platform/Windows` 与 `Platform/macOS` 实现层处理。
- 与现有翻译管线无缝衔接（语音转文本后复用现有翻译和取消机制）。
- 提供清晰的权限、失败、超时与取消反馈，避免静默失败。

**Non-Goals:**
- 不在本次实现“边录边实时字幕”与词级时间戳编辑。
- 不引入云端 STT 服务或上传音频。
- 不实现复杂音频后处理（VAD/降噪增强）策略调优。

## Decisions

### 1) 增加 `ISpeechInputCoordinator` 作为 Overlay 唯一语音入口

**Decision**:
- `OverlayViewModel` 只依赖 `ISpeechInputCoordinator`，不直接编排录音、模型检查与转写。
- 协调器串行化语音会话，统一产出状态与错误码供 UI 映射。

**Why**:
- 避免语音状态机散落在 ViewModel，降低并发分支复杂度。
- 保持“UI 不测试逻辑，逻辑不依赖 UI”边界，便于独立测试语音流程。

**Alternatives**:
- 在 ViewModel 直接调用录音/STT：拒绝，破坏分层并显著增加测试难度。

### 2) 录音与转写仍分离为两个可替换接口

**Decision**:
- 在平台层新增 `IAudioCaptureService`（开始录音、停止录音、权限状态、错误码）。
- 在 Core 层新增 `ISpeechToTextEngine`（输入归一化音频，输出转写文本）。
- 新增共享契约 `AudioCaptureResult` 到 Core 抽象层（或独立 Contracts 命名空间），禁止由平台层反向定义。

**Why**:
- 录音与转写可独立替换，利于插件化扩展。
- 避免 `ISpeechToTextEngine` 依赖平台实现类型，保持依赖方向稳定。

**Alternatives**:
- 在 STT 接口中直接传平台专有句柄：拒绝，跨平台不可复用且难测试。

### 3) 采用离线 Whisper 系作为首个 STT 后端，并显式接入模型下载链路

**Decision**:
- 首个实现采用本地 Whisper 系后端（封装为 `ISpeechToTextEngine`），模型按需下载与加载。
- 当模型缺失时，协调器返回 `ModelMissing` 可恢复错误；UI 展示引导并触发显式下载动作（用户确认后调用 `IModelManager.EnsureModelAsync`）。
- 下载成功后允许立即重试语音，不要求重启 Overlay。

**Why**:
- 将“提示下载”变成可执行的闭环，避免只报错不落地。
- 保持模型下载行为与现有模型管理一致，减少重复实现。

**Alternatives**:
- 自动后台静默下载：拒绝，缺乏用户感知且增加不可控流量/存储占用。

### 4) Overlay 采用“显式开始/停止”且严格互斥的会话状态机

**Decision**:
- 语音状态定义为 `Idle -> Recording -> Transcribing -> Idle|Error`。
- 同一时刻仅允许一个语音会话；重复开始/停止返回可恢复错误（如 `AlreadyRecording` / `NotRecording`）。
- Overlay 关闭或页面销毁时，协调器必须取消进行中的录音/转写并释放资源。

**Why**:
- 首版状态机可预测，边界一致，便于写完整单元测试。
- 避免竞态导致的状态错乱或资源泄漏。

**Alternatives**:
- 支持录音中抢占重入：拒绝，首版复杂度高且收益有限。

### 5) 语音状态与翻译状态分离展示，避免文案互相覆盖

**Decision**:
- 保留现有翻译 `StatusText` 语义。
- 新增独立语音状态字段（如 `VoiceState` + `VoiceStatusText`），UI 以区域化方式展示。
- 语音成功回填 `SourceText` 后继续复用现有翻译节流/取消机制。

**Why**:
- 防止语音流程与翻译流程争抢同一状态文本造成闪烁。
- 降低对既有翻译反馈逻辑的破坏风险。

### 6) 权限与失败路径必须可恢复，手动输入始终可用

**Decision**:
- 麦克风权限缺失时，显示明确引导，不启动录音。
- 转写失败不清空现有 `SourceText`，用户可继续手输并翻译。
- 任一语音错误不阻断 Overlay 核心输入/翻译链路。

**Why**:
- 将语音能力作为增强项，不影响核心翻译主路径可用性。

## Risks / Trade-offs

- **[Risk] 平台录音 API 差异较大** -> **Mitigation**: 统一 PCM 输出格式（16k/mono）并将差异收敛到平台实现层。
- **[Risk] STT 首次加载耗时较高** -> **Mitigation**: 按需加载 + 状态提示 + 可取消等待。
- **[Risk] 模型体积增加** -> **Mitigation**: 作为可选模型按需下载，不阻断主翻译流程。
- **[Trade-off] 非流式转写** -> 用户等待最终结果更久，但实现复杂度和稳定性更可控。
- **[Risk] 语音与翻译状态并发更新导致 UI 抖动** -> **Mitigation**: 分离状态字段并定义 UI 展示优先级。
- **[Risk] 录音长时会话占用内存** -> **Mitigation**: 设定最大录音时长与超时错误码，超限后引导重试。

## Migration Plan

1. 增加 Core 语音契约（`AudioCaptureResult`、`SpeechTranscriptionResult`、`SpeechInputErrorCode`）与 DI 骨架。
2. 增加平台录音抽象及 Windows/macOS 实现（统一输出 16k/mono PCM）。
3. 实现离线 STT 适配器，并将 STT 模型纳入可选模型管理。
4. 实现 `ISpeechInputCoordinator`（权限检查、模型检查/下载、录音/转写编排、取消与互斥）。
5. 在 Overlay/ViewModel 接入语音状态与命令，分离语音/翻译状态展示。
6. 完成自动化测试与手动回归（权限拒绝、模型缺失下载、成功转写、继续手输、关闭时取消）。

## Open Questions

- 首版默认 STT 模型选择（tiny/base）如何在精度与时延间平衡？
- 是否需要在后续迭代提供“按住说话（PTT）”全局快捷键扩展？
- 最大录音时长是否默认限制为 60s（可配置）？
