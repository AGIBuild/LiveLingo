# LiveLingo · 面向 AI 结对与 AI 办公的多场景润色 · 实施计划

**目标：** 将 [多场景润色完整设计](../specs/2026-11-27-multi-scenario-polish-design.md) 与 [交互原型 v5](../prototypes/livelingo-ux-v5/README.md) 完整落地到 `LiveLingo.Core`、`LiveLingo.Desktop` 及自动化测试与探针套件中，确保 `./build.sh Build`（0 警告）与 `./build.sh Test`（含 WebKit/Chromium 双引擎页面测试与覆盖率门禁）全部通过。

**工程红线：**
- 代码、注释均为英文；`zh-CN.json` 与 `en-US.json` 键集严格一致。
- 严禁用任何脚本修改代码文件，统一使用直接编辑/创建工具。
- 前端自有资源（`src/LiveLingo.Desktop/Web/Assets/app/*.js`）必须使用 Preact + `htm`，严禁使用 `innerHTML` 等禁用 API（由 `WebPageTests` 守卫）。
- 同一职责唯一实现；翻译输入严格与结构化润色隔离（`TranslationDocumentInput` 只消费 `natural` 保真稿）。

---

## 任务拆解与进度跟踪

| 编号 | 模块 | 核心交付物 | 依赖 | 状态 |
|---|---|---|---|---|
| **P1** | `Core.Cleanup` / `Dictation` | `PolishPreset`、`PolishPresets`、起步模板、`GateReason` 扩展、`FidelityGate` 自我改口数字豁免、`FactAnchorGate` 及单测 | 无 | 已完成 |
| **P2** | `Core.Drafting` / `Cleanup` | `Draft` 按模式隔离 `DocumentResult` 缓存、2400 字全局单窗统合 + 超长 Map-Reduce、5 模式生产级提示词、`<think>` 剥离、同模式单次定向修复重试及单测 | P1 | 已完成 |
| **P3** | `Desktop.Configuration` / `Preferences` / `Settings` | `SettingsModel` 预设持久化与旧 `PolishPrompt` 无损迁移、`ComposerPreferences` 宿主智能初始落位与应用级预设绑定、`SettingsViewModel` 预设 CRUD + 双语 i18n | P1 | 已完成 |
| **P4** | `Desktop.Composer` / `App` | `ComposerViewModel` 润色模式切换、快捷键映射、动态更新 `PolishScheduler` 与云端原生润色旁路判定、`RecentDraft` 状态保持及单测 | P2, P3 | 已完成 |
| **P5** | `Desktop.Web` / `Web.Tests` | 封闭 `WebIntent`、Composer 输出胶囊分组菜单与保真语法微高亮、Settings 预设管理与一键模板弹窗、应用偏好行内下拉、更新黄金夹具并通过 WebKit + Chromium 双引擎测试 | P4 | 已完成 |
| **P6** | `Core.Tests/Probes` / `Build` | 新增 `PolishQualityProbeTests` 与 Nuke `ProbePolish` 目标，跑通全套 `./build.sh Build`、`./build.sh Test`（1929 用例，行覆盖 91.0% / 分支覆盖 82.0%）与 `./build.sh ProbePolish`（6/6 真实模型通过） | P5 | 已完成 |

---

## 详细任务卡

### P1：Core 预设模型与双层协同门禁（`PolishPreset` + `FactAnchorGate`）

**涉及文件：**
- 新增：`src/LiveLingo.Core/Cleanup/PolishPreset.cs`、`src/LiveLingo.Core/Cleanup/FactAnchorGate.cs`
- 修改：`src/LiveLingo.Core/Cleanup/PolishStyle.cs`、`src/LiveLingo.Core/Dictation/FidelityGate.cs`、`src/LiveLingo.Core/Drafting/GateRejection.cs`
- 测试：`tests/LiveLingo.Core.Tests/Cleanup/PolishPresetTests.cs`、`tests/LiveLingo.Core.Tests/Cleanup/FactAnchorGateTests.cs`、`tests/LiveLingo.Core.Tests/Dictation/FidelityGateTests.cs`

**验收标准：**
- [x] 定义 5 个内置模式（`ai-task`、`natural`、`formal`、`todo`、`report`）与 3 个起步模板（`bug-report`、`spec-ac`、`git-commit`），提供校验与规范化逻辑。
- [x] `FidelityGate.IsPrunableSpokenNumber` 识别数字后 24 字窗口内的自我改口词及紧邻前导对比否定词，`VerbatimGate` 与 `FactAnchorGate` 共享该规则。
- [x] `FactAnchorGate` 拦截空输出、越界回答/未请求代码块（`ExecutionHijack`）、凭空角色扮演套话（`RoleplaySlop`）、丢失生效数字/编造数字（豁免列表序号）、丢失保护字面量。

### P2：Draft 多模式文档缓存、全局单窗路由与同模式定向修复

**涉及文件：**
- 修改：`src/LiveLingo.Core/Drafting/Draft.cs`、`src/LiveLingo.Core/Drafting/Draft.Documents.cs`、`src/LiveLingo.Core/Drafting/DocumentWork.cs`、`src/LiveLingo.Core/Cleanup/IDictationPolisher.cs`、`src/LiveLingo.Core/Cleanup/LlmDictationPolisher.cs`、`src/LiveLingo.Core/Cleanup/LlmTextCleaner.cs`、`src/LiveLingo.Core/Cleanup/PolishScheduler.cs`
- 测试：`tests/LiveLingo.Core.Tests/Drafting/FinalDocumentTests.cs`、`tests/LiveLingo.Core.Tests/Cleanup/LlmDictationPolisherTests.cs`、`tests/LiveLingo.Core.Tests/Cleanup/PolishSchedulerTests.cs`

**验收标准：**
- [x] `Draft` 支持 `PolishModeId`，每个模式独立持有 `DocumentResult`（切换模式瞬时复用已成稿并保护人工编辑稿，新增源文本使人工稿进入 `Stale`）；`TranslationDocumentInput()` 只读取 `"natural"` 保真整理稿。
- [x] `DocumentWork` 支持 `Holistic` 路由（`≤ 2400` 字跨换行不切碎单次推理；`> 2400` 字分段抽要点再单次统合成篇）。
- [x] `LlmTextCleaner.Tidy` 剥离闭合 `<think>...</think>` 与元前缀；`LlmDictationPolisher` 按契约使用 `VerbatimFrame` / `StructuredFrame` 及自适应温度。
- [x] `PolishScheduler` 支持运行时切换活跃 `PolishPreset`，并在门禁拒绝时执行 1 次携带具体 `GateRejection` 的同模式定向修复重试。

### P3：设置持久化、旧版平滑迁移与宿主智能偏好

**涉及文件：**
- 修改：`src/LiveLingo.Desktop/Services/Configuration/SettingsModel.cs`、`src/LiveLingo.Desktop/Services/Preferences/PreferencesData.cs`、`src/LiveLingo.Desktop/Services/Preferences/ComposerPreferences.cs`、`src/LiveLingo.Desktop/Settings/SettingsViewModel.cs`、`src/LiveLingo.Desktop/Settings/SettingsTexts.cs`、`src/LiveLingo.Desktop/Resources/i18n/zh-CN.json`、`src/LiveLingo.Desktop/Resources/i18n/en-US.json`
- 测试：`tests/LiveLingo.Desktop.Tests/Services/Configuration/SettingsModelTests.cs`、`tests/LiveLingo.Desktop.Tests/Services/Preferences/ComposerPreferencesTests.cs`、`tests/LiveLingo.Desktop.Tests/Settings/SettingsViewModelTests.cs`、`tests/LiveLingo.Desktop.Tests/Services/Localization/LocalizationServiceTests.cs`

**验收标准：**
- [x] 旧配置中的自定义 `PolishPrompt` 自动无损迁移至 `"natural"` 预设覆盖；内置微调与一键恢复默认、自定义预设增删改查校验完备。
- [x] `ComposerPreferences` 实现「用户应用偏好 > 已知宿主类别智能推荐（Cursor/VS Code/Claude/终端 → `ai-task`、Linear/Jira → `todo`、飞书文档/Notion → `report`、Mail/Outlook → `formal`）> 全局默认润色方式」，并支持从设置页直接预配任意应用。
- [x] 中英文资源键 100% 对齐并通过 `LocalizationServiceTests`。

### P4：Composer 视图模型与应用生命周期接线

**涉及文件：**
- 修改：`src/LiveLingo.Desktop/Composer/ComposerTypes.cs`、`src/LiveLingo.Desktop/Composer/ComposerViewModel.cs`、`src/LiveLingo.Desktop/Composer/ComposerViewModel.Choices.cs`、`src/LiveLingo.Desktop/Composer/ComposerViewModel.Document.cs`、`src/LiveLingo.Desktop/Composer/ComposerShortcuts.cs`、`src/LiveLingo.Desktop/Composer/ComposerTexts.cs`、`src/LiveLingo.Desktop/App.axaml.cs`
- 测试：`tests/LiveLingo.Desktop.Tests/Composer/ComposerChoiceTests.cs`、`tests/LiveLingo.Desktop.Tests/Composer/ComposerDocumentTests.cs`、`tests/LiveLingo.Desktop.Tests/Composer/ComposerShortcutsTests.cs`、`tests/LiveLingo.Desktop.Tests/Composer/ComposerTextsTests.cs`

**验收标准：**
- [x] `ComposerViewModel` 支持 `ChoosePolishMode(modeId)`，同步更新 `Draft.PolishModeId`、`PolishScheduler` 活跃预设、云端原生润色是否需二次 LLM 的模型就绪计划（`ComposerModelPlan`），并触发应用偏好记忆。
- [x] `RecentDraft` 完整保存并恢复当时的 `PolishMode` 与多模式文档状态。

### P5：Web 界面、封闭意图、黄金快照与双引擎浏览器测试

**涉及文件：**
- 修改：`src/LiveLingo.Desktop/Web/WebIntent.cs`、`src/LiveLingo.Desktop/Web/ComposerIntents.cs`、`src/LiveLingo.Desktop/Web/SettingsIntents.cs`、`src/LiveLingo.Desktop/Web/ComposerViewState.cs`、`src/LiveLingo.Desktop/Web/SettingsViewState.cs`、`src/LiveLingo.Desktop/Web/Assets/page.html`、`src/LiveLingo.Desktop/Web/Assets/styles/composer.css`、`src/LiveLingo.Desktop/Web/Assets/styles/settings.css`、`src/LiveLingo.Desktop/Web/Assets/app/20-composer-parts.js`、`src/LiveLingo.Desktop/Web/Assets/app/22-composer-result.js`、`src/LiveLingo.Desktop/Web/Assets/app/23-composer-menus.js`、`src/LiveLingo.Desktop/Web/Assets/app/24-composer.js`、`src/LiveLingo.Desktop/Web/Assets/app/31-settings.js`
- 测试与夹具：`tests/LiveLingo.Desktop.Tests/Web/*`、`tests/LiveLingo.Web.Tests/*`

**验收标准：**
- [x] Composer 右上角胶囊与菜单支持 5 种内置模式 + 自定义预设 + 翻译 + 原话 + 管理入口；文档结果区呈现保真语法微高亮（纯 Preact VNode，零 `innerHTML`）与门禁状态徽章；键盘焦点环高对比可见。
- [x] Settings「语音与翻译」升级为预设卡片列表 + 一键起步模板弹窗，「通用」应用偏好支持行内下拉直接修改模式，「模型」对 `Qwen3.5-2B` 展示结构化质量提示。
- [x] 更新黄金快照夹具后，`WebPageTests`、`ComposerInteractionTests`、`ComposerDocumentTests`、`SettingsPageTests` 在 WebKit 与 Chromium 下全绿（无控制台错误、无 `<12px` 文字、无无名控件）。

### P6：真实模型探针与全仓构建/覆盖率门禁

**涉及文件：**
- 新增：`tests/LiveLingo.Core.Tests/Probes/PolishQualityProbeTests.cs`
- 修改：`build/BuildTask.cs`（新增 `ProbePolish` 目标）

**验收标准：**
- [x] `PolishQualityProbeTests` 覆盖改口数字剪枝、倒叙目标归位、短句纠偏免套壳、防越界写代码、待办提取与 BLUF 汇报六类对抗样本。
- [x] `./build.sh Build` 0 警告，`./build.sh Test` 全绿且行覆盖 ≥ 80%、分支覆盖 ≥ 65%（实测 91.0% / 82.0%，`ProbePolish` 6/6 全绿）。
