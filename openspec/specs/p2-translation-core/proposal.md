## Why

P1 建立了分层架构和接口定义，但翻译功能仍使用 stub（返回 `[EN] xxx`）。用户需要真实的翻译能力才能验证产品价值。P2 替换 stub 为真实的 AI 翻译引擎，实现从"架构就绪"到"功能就绪"的跨越。

## What Changes

- 实现 `ModelManager`：从 HuggingFace 下载 ONNX 模型，支持断点续传、进度报告、本地缓存管理
- 实现 `MarianOnnxEngine`：通过 ONNX Runtime 推理 + SentencePiece 分词，支持 encoder-decoder 架构的翻译
- 实现 `FastTextDetector`（或 script-based 降级方案）：自动检测输入文本语言
- 完善 `TranslationPipeline`：串联检测 → 翻译，支持 cancel-and-restart
- 添加模型注册表（`ModelRegistry`）：预定义可用模型元数据
- 添加基础模型下载 UI：首次启动提示下载必需模型
- 引入 `Microsoft.ML.OnnxRuntime` NuGet 依赖到 Core 项目

## Capabilities

### New Capabilities
- `model-management`: 模型下载（断点续传）、本地存储结构（manifest.json）、缓存管理、磁盘占用统计
- `marian-translation`: MarianMT ONNX 推理引擎、SentencePiece 分词器、beam search / greedy 解码、ModelSession 管理（lazy load + 复用）
- `language-detection`: FastText lid.176.ftz 语言检测或 Unicode script 降级方案、自动选择对应翻译模型
- `translation-pipeline`: 完整翻译管线组装（检测 → 翻译）、cancel-and-restart 模式、状态显示（Translating / Translated / Error）

### Modified Capabilities

(无现有 spec 需要修改)

## Impact

- **NuGet 依赖**: Core 新增 `Microsoft.ML.OnnxRuntime`（~50MB native libs）
- **应用体积**: 首次下载需要 ~31MB MarianMT 模型 + ~1MB FastText 模型
- **首次启动**: 需要网络连接下载模型，增加模型下载 UI
- **DI 注册**: `AddLiveLingoCore()` 注册真实实现替代 stub
- **性能**: 翻译延迟从 0ms（stub）变为 ~100-200ms（MarianMT CPU 推理）
