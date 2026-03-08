## Context

当前实现中，翻译主链路绑定为 `LlamaTranslationEngine`，而 `MarianOnnxEngine` 尚未完成真实推理；同时启动引导与健康检查将 Qwen 视为必需模型。该状态与既有架构目标（Marian 做翻译、Qwen 做后处理）不一致，带来链路职责混杂、模型要求不清晰、首启成本偏高等问题。

本次变更是跨模块对齐：涉及 Core 引擎与管线、Desktop 首启引导、模型管理和健康检查，需要先统一设计再实施。

依赖关系（目标态）：

```text
OverlayViewModel
   -> ITranslationPipeline
      -> ILanguageDetector (FastText / Script fallback)
      -> ITranslationEngine (MarianOnnxEngine)
      -> ITextProcessor[] (Qwen-based processors, optional)
```

## Goals / Non-Goals

**Goals:**
- 恢复单一职责：Marian 只负责翻译，Qwen 只负责后处理。
- 重新定义模型分层：必需模型（可启动主功能）与可选模型（增强功能）。
- 在不破坏现有 UI 交互的前提下，完成翻译主链路对齐与可观测错误处理。
- 确保“无 Qwen”时仍可完成翻译与注入，“有 Qwen”时可启用后处理增强。

**Non-Goals:**
- 不实现多引擎策略选择 UI（例如用户手动选择 Marian/Llama 作为翻译引擎）。
- 不引入 GPU 推理或更换推理框架（保持 ONNX Runtime + LLamaSharp）。
- 不在本次变更中扩展新的语言对或新增模型仓库。

## Decisions

### 1) 翻译与后处理链路职责固定

**Decision**: `ITranslationEngine` 默认注册为 `MarianOnnxEngine`；`LlamaTranslationEngine` 退出翻译主链路，仅保留 Qwen 后处理处理器链。

**Why**:
- 与产品设计和已有规格一致，避免职责重叠。
- Marian 翻译路径可提供更稳定的时延边界，Qwen 只在用户开启后处理时触发。

**Alternatives**:
- 动态路由（按长度/语言自动选 Marian 或 Llama）被拒绝：策略复杂、测试矩阵扩大、规格边界变模糊。

### 2) 模型分层与可用性规则

**Decision**:
- 必需层：`FastText + 默认 Marian 语言对`。
- 可选层：`Qwen`（仅后处理开启时按需下载/加载）。

**Why**:
- 降低首启门槛，保持主功能可达。
- 将“增强质量”与“基础可用”解耦，避免单模型缺失导致全链路不可用。

**Alternatives**:
- 继续将 Qwen 设为必需：被拒绝，首启成本高且与链路职责不匹配。

### 3) Marian 主链路补全策略

**Decision**: 完成 `MarianOnnxEngine` 真实推理闭环（分词 -> ONNX 推理 -> 解码），并在缺失文件、不支持语言对、取消请求时提供明确错误语义。

**Why**:
- 当前占位实现无法作为正式主链路。
- 明确错误边界可提升 UI 状态提示与测试可验证性。

**Alternatives**:
- 保留占位实现并由上层兜底：被拒绝，会掩盖主链路不可用问题。

### 4) UI 层行为对齐

**Decision**:
- Setup Wizard 仅下载必需层模型。
- Settings 模型页继续管理全部模型，但标记必需/可选。
- 启动健康检查只对必需层缺失阻断主流程；可选层缺失仅在用户启用相关功能时提示。

**Why**:
- 保证引导路径与运行时规则一致，减少用户认知偏差。

## Risks / Trade-offs

- **[Risk] Marian 分词/解码实现复杂度较高** -> **Mitigation**: 先完成最小可用 greedy 解码和集成测试，再迭代优化质量/速度。
- **[Risk] 引导流程变化影响现有测试快照** -> **Mitigation**: 同步更新 SetupWizard 与健康检查测试，新增“无 Qwen 仍可翻译”回归用例。
- **[Trade-off] 不做动态引擎路由** -> 架构更清晰但失去部分灵活性；后续可在独立 change 中引入策略层。
- **[Trade-off] 保持 CPU-only** -> 开发风险低但性能上限受限；GPU 优化留到独立性能迭代。

## Migration Plan

1. 切换 DI：`ITranslationEngine` -> `MarianOnnxEngine`。
2. 完成并验证 Marian 推理闭环，替换占位逻辑。
3. 调整模型分层与引导/健康检查规则。
4. 增加并通过回归测试（Core + Desktop）。
5. 手动验证：首次启动下载、无 Qwen 翻译、开启后处理下载 Qwen 后可用。

## Open Questions

- SentencePiece 绑定采用现有 NuGet 还是最小 P/Invoke 包装，哪个在 macOS 包体与稳定性上更优？
- 默认 Marian 语言对是否固定为设置中的默认对，还是允许首次引导多选下载？
