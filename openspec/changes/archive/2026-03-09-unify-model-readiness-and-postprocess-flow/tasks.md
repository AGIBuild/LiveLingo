## 1. OpenSpec artifacts

- [x] 1.1 完成 `proposal.md`，明确模型职责边界、user journey 与 FastText 去留；AC: proposal Capabilities 与 specs 目录一一对应
- [x] 1.2 完成 `design.md`，固化 readiness 服务、pipeline 预检与 Qwen 路径统一决策；AC: design 包含决策/替代方案/风险
- [x] 1.3 完成 `specs/*/spec.md` 与 `tasks.md`；AC: 涵盖 model-readiness、translation-pipeline、qwen-model-host、postprocess-ui、model-management、setup-wizard

## 2. Core 模型就绪统一

- [x] 2.1 新增 `IModelReadinessService` 与 `ModelNotReadyException`；AC: 异常包含 `ModelType`、`ModelId`、可操作提示
- [x] 2.2 在 `ServiceCollectionExtensions` 注册 readiness 服务并接入 `TranslationPipeline`；AC: Pipeline 不再依赖 UI 层做后处理模型预检
- [x] 2.3 改造 `QwenModelHost` 使用 `IModelManager.GetModelDirectory()` 解析路径；AC: 移除硬编码目录名

## 3. Desktop 行为与语义收敛

- [x] 3.1 `OverlayViewModel` 改为捕获 typed readiness 异常并执行 translation-only 降级；AC: 缺 Qwen 时可翻译且提示去 Models 下载
- [x] 3.2 `SettingsViewModel` 保持 `Active Model` 仅翻译模型语义，后处理仅由 `Processing.DefaultMode` 控制；AC: 不存在模型选择隐式开启后处理
- [x] 3.3 调整日志等级与文案，避免未启用后处理场景的噪音告警；AC: `Off` 模式下不再出现 Qwen 缺失 warning

## 4. 移除 FastText 必下与回归

- [x] 4.1 调整 `ModelRegistry.RequiredModels` 与 `GetRequiredModelsForLanguagePair`，移除 FastText 必下；AC: 向导 Step2 required 集合不含 `FastTextLid`
- [x] 4.2 更新向导与 i18n 文案（中英文）；AC: 文案与实际下载集合一致
- [x] 4.3 更新/新增 Core + Desktop 测试并执行受影响测试集；AC: 相关测试通过、无新增 lint 错误
