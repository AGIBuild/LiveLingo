## Why

P2 提供了真实的机器翻译能力，但 MarianMT 的输出是直译，缺少职场聊天场景所需的口语化、简洁化处理。用户需要一键将"正式翻译"润色为自然的聊天语气，或者缩短冗长内容。引入小型 LLM（Qwen2.5-1.5B）作为后处理器，可以在不增加云端依赖的前提下提供 AI 润色能力。

## What Changes

- 引入 `LLamaSharp` NuGet 依赖到 Core 项目，用于 Qwen2.5-1.5B GGUF 模型推理
- 实现 `QwenModelHost`：管理 LLM 模型生命周期（懒加载、5 分钟自动卸载、并发安全）
- 实现三个 `ITextProcessor`：`SummarizeProcessor`、`OptimizeProcessor`、`ColloquializeProcessor`，各自携带专用 system prompt
- 在 `TranslationPipeline` 中串联后处理器，实现两阶段 preview（先显示原始翻译，再显示润色结果）
- Overlay UI 添加后处理模式选择器（Off / Summarize / Optimize / Colloquial）
- 支持按需下载：用户首次选择非 Off 模式时提示下载 Qwen 模型（~1GB）

## Capabilities

### New Capabilities
- `qwen-model-host`: Qwen2.5-1.5B 模型生命周期管理（LLamaSharp 加载/卸载/线程安全/状态通知）
- `text-processors`: 三个后处理器实现（Summarize/Optimize/Colloquialize）及 prompt 工程
- `postprocess-ui`: Overlay 后处理模式选择器 UI 和两阶段 preview 交互

### Modified Capabilities

(无现有 spec 需要修改)

## Impact

- **NuGet 依赖**: Core 新增 `LLamaSharp` + `LLamaSharp.Backend.Cpu`（~20MB native libs）
- **模型体积**: Qwen2.5-1.5B Q4_K_M GGUF ~1GB，按需下载
- **内存**: LLM 加载后占用 ~2GB RAM；5 分钟无调用自动卸载
- **延迟**: 后处理增加 1-4s（取决于输入长度），但翻译结果立即可见（两阶段 preview）
- **UI**: Overlay 状态栏新增模式切换按钮
