# LiveLingo 2.0 · Composer：完整设计与实施路线

日期：2026-10-31。状态：**设计已确认方向（原型 v3），实现从本文开始**。

依据：[语音识别设计](./2026-10-29-voice-dictation-design.md)、[翻译评测报告](../../../poc/text-translation/REPORT.md)、[交互原型 v3](../prototypes/livelingo-ux-v3/README.md)。凡是“已验证”都指向这些文档中的实测；凡是“未验证”都列在第 11 节，不当作结论。

## 1. 目标与不做什么

**目标**：Windows + macOS 桌面工具。全局热键在任意输入框旁唤起浮窗；说话、打字或粘贴；实时看到识别结果；按需整理 / 翻译；用户确认后写回原输入框。默认本地，云端显式开启。

**验证基线**：Apple M2 及以上、16 GB 及以上；Windows 使用同档 16 GB 设备独立验证。不以最低配置定义目标。

**不做（本阶段）**：自动发送（原型已去掉“写入并发送”，见 D12）；系统声音捕获、说话人分离、语音合成（无已验证方案）；除中英外的语言（POC 只验证了中英）；插入符精确定位（见 11 节 S6）。

**工程规则（来自需求）**：代码、注释、commit 用英文；同一功能只由一个组件实现；不写防御性备选分支与“只修局部”的补丁式设计；不为兼容性堆 if-else；凭据不得硬编码。

## 2. 现有实现的处置

现有 Core / Desktop 约 2 万行，围绕 Gemma 4 26B（约 17 GB）+ Marian + 多级回退路由。依据：

- 与基线冲突：26B 模型加载失败才回退到 E4B；
- 违反规则：`ModelSelectionPolicy`（489 行）、`FallbackTranslationInvoker`（468 行）、`HybridTranslationEngine` 是多级备选链；`Translation/` 内有两个断句器；
- 下载无完整性校验（`src/LiveLingo.Core` 中没有任何 SHA256 校验），llama.cpp 运行时每次解析 `releases/latest` 后下载并执行（`NativeRuntimeUpdater.cs:58`）。

| 范围 | 处置 | 理由 |
|---|---|---|
| `Core/Engines`、`LanguageDetection`、旧 `Translation`（含 `Segmentation`）、旧 `Speech`、`Processing` 中的处理器与 Qwen 宿主 | **删除** | 被新管线完整替代；方向由用户显式选择，不需要语言检测（连带移除 Lingua、Marian 的 OnnxRuntime / Tokenizers 依赖） |
| `Core/Models` 中的路由、选择、回退、遥测、Ollama / OpenAI 兼容 provider、`TranslationChatClient` | **删除** | 回退链；云端改用新的单一 OpenAI 兼容客户端（D2） |
| `Core/Models/Downloads`、`Installations`、`ModelManager`、下载协调器、`ModelDescriptor` | **保留并扩展** | 断点续传、解压、磁盘守卫已可用；新增 SHA256 与固定修订 |
| `Processing/LlamaServerProcessManager`、`NativeRuntimeUpdater` | **重写** 为 `Inference/LlamaServerHost` | 现为单实例、未固定版本；新设计需要按角色多实例、固定版本、哈希校验 |
| `Desktop/Platform` 热键、文本注入、窗口跟踪、剪贴板、密钥库（Windows / macOS） | **保留**，增加 `IsAlive` | 难度高、已在两个平台使用 |
| `Desktop/Platform` 两套音频采集（Mac 429 行、Win 244 行）、`IAudioCaptureService` | **删除**，改用单一 PortAudio 实现 | 一个功能一个实现；POC 已用 PortAudio 验证 macOS（Windows 见 S3） |
| `Desktop/Views`、`ViewModels`（含九个 Settings 协作者）、`Services/Speech`、`Cloud`、`LocalModelRuntimePresentation`、`TranslationTraceRow` | **删除** | 由 Composer 与精简设置替代 |
| 本地化（`LocalizationService` + `i18n/*.json`）、更新（Velopack）、`Build`（Nuke / WiX / pkg）、`HotkeyRecorder` | **保留** | 与功能无关的基础设施 |
| `tools/TranslationBenchmark` 及其测试 | **删除** | POC 语料评测（`poc/text-translation/scripts`）取代 |
| `src/LiveLingo.HfGguf` | **保留** | 仅负责 HF 解析 URL，职责单一 |
| `poc/` | **源码入库，数据不入库** | 语料与结果共 4.8 GB，含非商业许可数据；忽略 `corpus*/`、`results/`、`*.wav` |

旧版以 tag `v1.4.2` 保留，不再维护两套。

## 3. 总体架构

```text
                 ┌────────────────── LiveLingo.Desktop (Avalonia 12) ──────────────────┐
 全局热键 ───────▶│ App / Tray ─▶ ComposerWindow ◀──▶ ComposerViewModel (纯 C#，无 Avalonia) │
                 │                     │                      │                         │
                 │             Platform（热键/窗口/注入/剪贴板/密钥/麦克风）              │
                 └─────────────────────┼──────────────────────┼─────────────────────────┘
                                       ▼                      ▼
 ┌──────────────────────────────── LiveLingo.Core（逻辑，无 UI 依赖）────────────────────────────┐
 │ Drafting   Draft · DraftUnit · TextUnits · Readiness · CommitSnapshot   ← 唯一的草稿模型       │
 │ Dictation  DictationSession（音频→原文单元）· FidelityGate · 识别器/VAD 契约                  │
 │ Cleanup    CleanupScheduler · ITextCleaner                                                    │
 │ Translation TranslationScheduler · ProtectedText · TranslationPrompt · ITranslator            │
 │ Inference  LlamaServerHost（固定版本、按角色多实例）· ChatClient（OpenAI 兼容，本地 / 云端共用）│
 │ Speech     Sherpa 适配器（实时 / 二遍 / VAD）                                                 │
 │ Models     固定修订目录 · 下载 / 校验 / 安装（复用并扩展）                                    │
 └────────────────────────────────────────────────────────────────────────────────────────────┘
```

**一份草稿，三条视图**（原型已验证交互）：`Draft` 按顺序保存 `DraftUnit`。每个单元有 `Raw`（识别 / 输入原文）、`Clean`、`Translation` 三个独立结果，各自状态为 `Pending / Ready / Error / Empty`，另有 `Owned`（用户修改过）。输出选择 `Clean | Translation | Raw` 决定写入哪一份。

### 3.1 单元与任务（唯一实现）

| 职责 | 唯一组件 | 说明 |
|---|---|---|
| 草稿状态、就绪判定、提交快照 | `Drafting.Draft` | 取代 POC `Dictation.Core.Draft`、TranslationPoc `TranslationSession` 内的 `Entry`、原型 `draft.js` |
| 断句与处理路径 | `Drafting.TextUnits` | 取代 POC 的 `SentenceSplitter` 与 `Units`、旧 Core 的两套分句。支持完整句、行与缩进保留、受保护字面量、尾部强制成句；`RouteFor(text)` 判定“按句 / 整段” |
| 音频到原文单元 | `Dictation.DictationSession` | 持续流式识别 + 片段二遍；只产出原文单元与未成句尾部 |
| 整理 | `Cleanup.CleanupScheduler` + `FidelityGate` | 观察 `Draft`，对需要整理的单元排队；前文、`Continues` 由草稿推出 |
| 翻译 | `Translation.TranslationScheduler` | 观察 `Draft`，对需要翻译的单元排队；依赖键包含目标语言、命中术语、前文 |
| 本地 / 云端模型调用 | `Inference.ChatClient` | 本地 llama-server 与云端端点同一协议、同一客户端 |

调度器不持有状态副本：任务以 `(epoch, unitId, translationRevision)` 作为票据，结果回写时由 `Draft` 校验；**过期、被删除或被用户修改的单元一律拒收**（原型 21 个模型测试与 28 个浏览器场景覆盖了这些不变量）。

### 3.2 关键不变量

1. 音频采集不等待模型推理；已归并的单元顺序不变。
2. 用户修改（`Owned`）优先，晚到结果不得覆盖。
3. 整理 / 翻译任务只改自己的那一个单元的那一个视图。
4. 失败是单元状态，不是静默降级：翻译失败不会自动换成原文；整理被保真门禁拒绝时保留原话并显示原因。
5. 回填只使用提交瞬间的**冻结快照**；录音中、选定输出未完成、目标失效时不可回填（目标失效仍可复制）。
6. 回填前重新校验目标仍存在；不向新的前台窗口注入。
7. 一个会话的 `Commit` 返回同一结果；**这不等于操作系统注入具备恰好一次语义**（验证见 S6）。

### 3.3 并发与错误

- `Draft` 非线程安全，由 `DraftSession` 以单一串行队列驱动；事件在锁外触发；UI 只在 `App` / View 层切回 UI 线程（沿用仓库规则）。
- 本地推理由进程隔离：llama-server 崩溃只表现为该角色的任务失败与状态变化，下一个用户请求按需重新启动，没有隐藏重试与回退链。
- 取消：Esc、新草稿、编辑源文都会使对应 `epoch` / `revision` 失效，已发出的 HTTP 请求被取消。

## 4. 推理运行时与模型

**单一本地运行时：llama.cpp `llama-server`，固定到 b11980**（POC 翻译评测所用版本）。每个角色独立进程，监听 `127.0.0.1` 随机端口，拒绝非回环地址；可执行文件与权重按 SHA256 校验后才启动。

| 角色 | 默认模型 | 说明 |
|---|---|---|
| 翻译 | Hy-MT2-7B Q4_K_M（质量）/ 1.8B（速度） | 评测：7B 显著优于 1.8B（MetricX 差 0.56），1.8B 首个可见译文约 93 ms |
| 整理 | Qwen3.5-4B Q4_K_M | 语音 POC 所用；改用 llama-server 后需验证等价（S2） |
| 实时识别 | X-ASR 480 ms 流式 int8 | sherpa-onnx 2.0.1 |
| 二遍识别 | Qwen3-ASR-0.6B int8 | sherpa-onnx |
| 切分 | Silero VAD 0.35 | sherpa-onnx |

**内存与档位**：16 GB 机器同时驻留识别、整理、翻译三类模型的总占用**未经联合实测**（S1）。因此设计成两个档位，在安装时按物理内存预选，用户可改：

- 均衡：翻译 1.8B，整理 4B（16 GB 默认，直到 S1 证明 7B 可行）；
- 质量：翻译 7B；
- 按需驻留：整理与翻译模型在对应输出首次使用时才加载，闲置超时卸载（单一 `ModelResidencyPolicy`）。

**哈希与修订**：模型、llama.cpp 压缩包、sherpa 模型包的 URL、修订、大小、SHA256 写入目录；下载器校验失败即删除并报错。许可信息随目录显示（S5）。

## 5. 音频与语音管线

沿用已验证的 `DictationSession` 行为，**搬迁而不是重写**：

- 采集：单一 PortAudio 实现，16 kHz 单声道 40 ms 帧；权限状态由各平台的小型 `IMicrophonePermission` 提供（macOS TCC，Windows 隐私设置）。录音文件保存仅在用户开启时发生，并落在用户目录。
- 切分：硬停顿 800 ms、软切分 12 s、90 字尾部强制成句、二遍音频 padding 200 ms 等参数保持 POC 值（设计文档 §4）。
- 迁移前提：`DictationSession` 从自带的句子模型改为写入统一的 `Draft`；POC 的 58 个核心测试作为特征测试一并搬迁，并用 POC 回放工具在同一授权语料上对照输出（不得退化）。

## 6. 翻译管线（已验证结论）

- 纯文本提示，原文原样放在指令之后；**不使用 JSON 契约与引号包裹**（评测：前者在 12%–19% 条目失控，后者把 `\n`、`\"` 抄进译文）。
- 单元保留前后空白，`Join` 原样恢复，版式通过率从 26% 提升到 78%。
- **处理路径由输入来源决定，不靠内容嗅探推断结构**：打字 / 听写走完整句单元（说完等待 1.5–2 s）；粘贴的完成文本走整段（质量更好、结构化通过率 94%）。`RouteFor` 只负责“是否像已完成的长文本”，用户可显式覆盖。
- 受保护字面量（代码、URL、占位符）保留可逆保护；**评测未能证明它有收益**，保留的理由只剩首轮个例，需用代码密集语料复测（开放问题，不阻塞）。
- 前文上下文（两组）同样**未测出收益**；先按评测前的设计实现，复测后简化。

## 7. 桌面应用

### 7.1 Composer（原型 v3 的实现）

- `ComposerWindow`：置顶、透明、无任务栏图标、无边框；锚定在捕获到的目标窗口底部居中，向上生长；整段文本或转录自动展开为左右对照；窄屏堆叠。
- `ComposerViewModel`（纯 C#）：持有 `DraftSession`；暴露头部（目标、隐私、输出胶囊）、原话区、结果区单元列表、横幅、页脚（麦克风 / 状态 / 复制 / 写入）。命令与快捷键语义以原型测试为准：Enter 写入，处理中按 Enter 排队，Shift+Enter 换行，Esc 逐级退出，输入法组合中不触发。
- 麦克风：点按免按住，按住超过 350 ms 为按住说话；键盘 Enter / Space 可操作。
- 异常一律横幅：没声音、没权限、模型下载中、处理很慢、目标失效。

### 7.2 设置

四组：通用（热键、回填键、外观、动画）、语音与翻译（麦克风、识别语言、整理、方向、术语、保留录音）、隐私与云端（分项授权、服务商、草稿）、模型与关于（档位、状态）。设置模型整体重建，`SchemaVersion = 3`；从旧版只迁移热键与界面语言。

### 7.3 首次运行

不用向导。直接可打字；缺的模型以横幅显示下载进度并且**需要用户显式同意**；下载完成前仍可使用不依赖它的能力。

### 7.4 平台层

保留现有热键、窗口跟踪、注入（剪贴板 + 粘贴键）、剪贴板、密钥库。新增：`IWindowTracker.IsAlive(target)`；单一 `IMicrophone`（PortAudio）与 `IMicrophonePermission`。注入后恢复用户剪贴板的行为在 P0 验证。

## 8. 隐私与云端

- 默认没有任何出站网络请求。出站只有三类，各自独立授权：模型 / 运行时下载、云端翻译、云端整理（语音不上传）。
- 云端后端是一个 OpenAI 兼容端点（可对接 Microsoft Foundry）；凭据存系统密钥库，界面只显示“已配置”，不写入日志与设置文件。云端是用户的显式选择，不作为本地失败的隐式降级。
- 日志不含正文与录音；诊断只含状态与计数。

## 9. 质量与门禁

- 保持现有 CI 门禁：行覆盖 ≥ 80%、分支覆盖达标、变异得分 ≥ 80%。**变异范围随新代码调整**：原先只覆盖 `Translation/**`，现改为 `Drafting/**`、`Translation/**`（调度、提示、保护）、`Dictation/**`（会话、门禁）、`Cleanup/**`；适配器（Sherpa、PortAudio、llama 进程）与 View 层沿用覆盖率排除并以 Nuke 的 Probe 目标做端到端验证。
- 测试：xunit v3 + NSubstitute；ViewModel 无 Avalonia 依赖，可直接测试；Avalonia Headless 只测 View 绑定。
- 行为基线：原型 `draft.test.cjs` 的 21 个用例逐条移植为 C# 测试；浏览器场景作为 Composer 验收清单。
- 回放基线：POC 的语音与翻译评测脚本不入主构建，作为发布前对照（语料不入库）。

## 10. 优先级与里程碑

| 级别 | 含义 | 内容 |
|---|---|---|
| **P0** | 没有它就没有产品 | M0 清理与地基；M1 “打字 → 翻译 → 写回”整条链路 |
| **P1** | 核心价值 | M2 语音：采集、识别、整理、保真、听写交互 |
| **P2** | 完整性 | M3 转录与导出、术语、草稿恢复、云端、宽屏与托盘、无障碍 |
| **P3** | 可发布 | M4 目标机验证（M2/16 GB、Windows 16 GB）、安装包、迁移、文档 |

- **M0 地基**：分支与基线提交；`.gitignore` 排除语料；删除第 2 节所列旧实现，使解决方案重新可构建、保留模块的测试通过；调整变异与覆盖配置。
- **M1 打字翻译（P0）**：`Drafting`（含 `TextUnits`、就绪与快照）→ `ProtectedText` / `TranslationPrompt` → `ChatClient` → 固定修订目录与 SHA256 校验 → `LlamaServerHost` → `TranslationScheduler` → `ComposerViewModel` → Composer 视图与应用接线 → 真实模型端到端探针。**验收**：离线状态下，在任意应用的输入框旁唤起，打字或粘贴，译文写回，目标失效不乱写；全部新逻辑达到门禁。
- **M2 语音（P1）**：`IMicrophone` → Sherpa 适配器（2.0.1）→ `DictationSession` 接入 `Draft` → 整理与保真 → 麦克风交互与横幅 → 回放对照 POC。**验收**：POC 评测语料上不退化；翻译在录音中逐句出现。
- **M3 完整性（P2）**、**M4 发布（P3）**：任务卡见实施计划。

## 11. 开放问题与验证关口

| 编号 | 问题 | 时机 | 验证方法 |
|---|---|---|---|
| S1 | 16 GB 上识别 + 整理 + 翻译同时驻留的内存与延迟 | M1 末，**决定默认档位** | M2 / 16 GB 实机；记录峰值 RSS、首字与整句延迟 |
| S2 | 整理改走 llama-server 是否与 POC（LLamaSharp）等价 | M2 开始 | POC 回放工具同语料对照门禁拒绝率与 CER |
| S3 | Windows：PortAudio 采集、热键、注入、llama.cpp CPU / Vulkan 速度 | M2–M4 | 16 GB Windows 实机 |
| S4 | sherpa-onnx 1.12.38 → 2.0.1 的回归 | M2 | 回放对照 |
| S5 | 所有模型与运行时的许可，是否允许按用户授权下载并展示 | M1 | 逐项核对许可文本，目录显示许可 |
| S6 | 目标窗口内光标位置获取、跨应用注入的可靠性与恰好一次 | M1–M3 | 原生输入框、Electron、浏览器、终端逐类实测；取不到时锚定窗口底部 |
| S7 | 1.4.2 用户磁盘上约 17 GB 的旧模型如何清理 | M4 | 显式提示后清理，不静默删除 |

## 12. 关键决策与备选

| 编号 | 决策 | 备选 | 取舍 |
|---|---|---|---|
| D1 | 在分支上重写领域层，复用基础设施 | 在旧结构上渐进改造 | 旧结构以多级回退为核心，渐进改造要同时维护两套；旧版保留在 tag |
| D2 | 本地 + 云端共用一个 OpenAI 兼容客户端 | 为每种服务商各写适配器 | 协议相同；少一套适配器与回退路由 |
| D3 | 只用 llama-server（进程隔离） | LLamaSharp 进程内；两者混用 | 一种运行时、可固定可校验、崩溃不拖垮 UI；代价是整理需验证等价（S2） |
| D4 | 单一 PortAudio 采集 | 保留各平台原生采集 | 一个实现、帧格式与 POC 一致；Windows 待验证（S3） |
| D5 | 统一草稿模型 | 各管线保留自己的状态 | 同一概念只实现一次；代价是迁移 `DictationSession` 需特征测试保护 |
| D6 | 纯文本提示，无 JSON 契约 | JSON / 引号包裹 | 评测推翻了后两者 |
| D7 | 不引入 OpenSpec | OpenSpec 管理变更提案 | 仓库已有 `docs/superpowers` 的设计与计划约定；再加一层工具只增加维护成本，价值不足 |
| D8 | 设置重建（schema 3），只迁移热键与语言 | 兼容全部旧字段 | 旧字段大多对应被删除的路由与引擎；兼容会堆积无用分支 |
| D9 | 没有隐式云端降级 | 本地失败自动走云端 | 隐私要求与“一个功能一个路径” |
| D10 | 浮窗锚定目标窗口，不依赖插入符 | 精确插入符定位 | 各应用差异大，先保证稳定；S6 再评估 |
| D11 | 变异范围随新模块调整 | 沿用原范围 | 原范围会让新核心逻辑不受门禁约束 |
| D12 | 取消“写入并发送” | 保留 | 原型决定回填只写入；误发代价高于多按一次键。若用户需要，作为 P2 的显式可选项重新加入 |
| D13 | 默认均衡档（1.8B 翻译） | 默认 7B | 7B 的联合内存未经验证（S1）；评测显示 1.8B 差距可测但可用 |
