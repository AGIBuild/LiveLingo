## Context

当前实现中，模型就绪逻辑分布在多个层级：

1. `MarianOnnxEngine` 内部按需 `EnsureModelAsync`（翻译模型）。
2. `OverlayViewModel` 在调用 Pipeline 前检查 Qwen 是否已安装（后处理模型）。
3. `QwenModelHost` 自身使用硬编码目录名解析模型路径。

这会造成以下问题：
- 逻辑分散导致行为不一致（翻译模型与后处理模型的就绪判断路径不同）。
- UI 层出现告警噪音（未启用后处理场景也容易出现误解）。
- 路径约定重复（`ModelRegistry` 与 `QwenModelHost` 各自维护目录规则）。
- Setup Wizard 仍下载 FastText，但默认检测链路基于 `ScriptBasedDetector`，存在“下载却未使用”。

## Goals / Non-Goals

**Goals:**
- 在 Core 层统一模型就绪判定入口，并提供标准异常语义。
- 将模型就绪检查集中到 Pipeline，移除 Overlay 预检分叉逻辑。
- 统一 Qwen 模型路径解析到 `IModelManager.GetModelDirectory()`。
- 明确 UI 语义：`Active Model` 只对应翻译模型；后处理由 `Processing.DefaultMode` 控制。
- 移除 FastText 作为向导必下模型，降低首次下载成本。

**Non-Goals:**
- 不引入自动下载策略（仍由用户在 Models 页主动下载）。
- 不重构 Marian/Qwen 推理实现本身（仅调整编排与就绪判断）。
- 不修改现有设置文件结构（不新增 schema 迁移）。

## Decisions

### D1: 新增模型就绪服务（Core）

**Decision**: 新增 `IModelReadinessService`（Core），统一暴露模型就绪判断与按能力检查接口，例如：

```csharp
public interface IModelReadinessService
{
    bool IsInstalled(string modelId);
    void EnsureTranslationModelReady(string sourceLanguage, string targetLanguage);
    void EnsurePostProcessingModelReady();
}
```

并引入 `ModelNotReadyException`，携带 `ModelType`、`ModelId` 与推荐动作，供上层统一处理。

**Rationale**:
- 消除 UI 层与 Core 层重复判断。
- 使“模型缺失”成为可测试、可观测的领域错误，而非字符串约定。

**Alternatives**:
- 继续在 `OverlayViewModel` 预检：职责继续分散，且易产生体验噪音。
- 仅依赖底层 `FileNotFoundException`：语义不稳定，提示不可控。

### D2: Pipeline 统一前置检查

**Decision**: 在 `TranslationPipeline.ProcessAsync` 前置阶段统一调用 `IModelReadinessService`：
- 始终检查翻译模型就绪（按语言对）。
- 当 `request.PostProcessing != null` 时检查后处理模型就绪。

后处理模型未就绪时抛 `ModelNotReadyException(ModelType.PostProcessing, "qwen25-1.5b", ...)`。

**Rationale**:
- 由 Core 统一判定“是否能执行该请求”，减少 UI 推测。
- 错误出口一致，便于 Overlay 做“提示 + 降级”。

### D3: Qwen 路径由 ModelManager 统一解析

**Decision**: `QwenModelHost` 不再拼接硬编码目录，改为通过 `IModelManager.GetModelDirectory(ModelRegistry.Qwen25_15B.Id)` 定位模型目录，再在目录下查找 `.gguf`。

**Rationale**:
- 路径规则单一来源，避免目录名漂移。
- 与 `ModelManager.ListInstalled()` 语义对齐。

### D4: Desktop 语义收敛与降级策略

**Decision**:
- `SettingsViewModel` 的 `AvailableTranslationModels` 仅保留已安装 `ModelType.Translation`。
- Overlay 不再主动预检 Qwen；当捕获 `ModelNotReadyException(PostProcessing)` 时：
  1) 更新状态文案为可操作提示（去 Settings -> Models 下载）；
  2) 自动降级为 translation-only 再执行一次请求（保留主链路可用性）；
  3) 日志降级到 `Information/Debug`，避免误导性 `Warning` 噪音。

**Rationale**:
- 让“翻译主链路可用”成为默认。
- 后处理缺失不会误伤主流程，也不会制造高等级噪音。

### D5: FastText 从 required 集合移除

**Decision**: `ModelRegistry.RequiredModels` 与 `GetRequiredModelsForLanguagePair` 移除 `FastTextLid`，向导 Step 2 基线下载改为 Marian-only。

**Rationale**:
- 当前默认检测实现未使用 FastText，必下策略与实际执行路径不一致。

## Risks / Trade-offs

- **[Risk] Overlay 降级重试可能导致一次额外请求延迟** -> **Mitigation**: 仅在捕获 `PostProcessing` 缺失异常时触发一次重试。
- **[Risk] 现有测试依赖旧日志/异常文本** -> **Mitigation**: 用类型化异常与行为断言替代字符串断言。
- **[Trade-off] FastText 不再必下后，未来若切回 FastText 检测需补充下载入口** -> **Mitigation**: 保留 `FastTextLid` 描述符但不纳入 required，后续可按能力再开启。

## Migration Plan

1. 引入 Core readiness 服务与异常类型，接入 DI。
2. 改造 Pipeline 调用 readiness，完成单元测试。
3. 改造 QwenModelHost 路径解析，验证缺模型与已安装路径。
4. 改造 Overlay 捕获/降级行为与提示文案。
5. 调整 ModelRegistry required 语义与 Setup Wizard 文案。
6. 完成 Desktop/Core 回归测试并清理旧日志断言。
