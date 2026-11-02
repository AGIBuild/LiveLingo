# LiveLingo Composer 实施计划

设计见 [composer 设计](../specs/2026-10-31-livelingo-composer-design.md)。**接手后续工作请从 [Web UI 迁移与发布交接计划](./2026-11-01-web-ui-and-release-handoff.md) 开始**；本文件保留 M0–M4 的实施记录与实测数据。本计划按优先级拆分；**M0、M1 给出可直接执行的步骤**，M2 起在前一个里程碑验收后再细化（其前提取决于 M1 的实测结果，如 S1）。

规则：每个任务先写失败的测试再实现；每步结束运行 `dotnet build LiveLingo.slnx` 与相关测试；每个任务一个本地提交，英文提交信息，带 `Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>`；不推送。

## 总览

| 里程碑 | 级别 | 范围 | 验收 |
|---|---|---|---|
| M0 地基 | P0 | 分支、基线提交、删除旧实现、门禁配置 | 解决方案可构建；保留模块测试全绿；覆盖 / 变异配置指向新模块 |
| M1 打字翻译 | P0 | Draft 域、文本单元、翻译管线、固定运行时与目录、Composer 与回写 | 离线：唤起 → 输入 / 粘贴 → 译文 → 回写；目标失效不乱写；真实模型探针通过 |
| M2 语音 | P1 | 麦克风、Sherpa 2.0.1、DictationSession 接入 Draft、整理与保真、听写交互 | POC 评测语料不退化；录音中逐句出译文 |
| M3 完整性 | P2 | 转录导入 / 时间线 / SRT、术语、草稿恢复、云端（Foundry / OpenAI 兼容）、宽屏、托盘、无障碍、Windows 对齐 | 逐项验收见任务卡 |
| M4 发布 | P3 | M2/16 GB 与 Windows 16 GB 验证、安装包与签名、设置迁移、旧模型清理、文档 | 开放问题 S1–S7 全部关闭或有明确结论 |

## 进度与偏差（随实施更新）

| 任务 | 状态 | 说明 |
|---|---|---|
| M0（0.1–0.3） | 完成 | 基线提交；旧实现已删除；覆盖与变异配置已指向新模块 |
| 1.1 Draft 域 | 完成 | 23 → 28 个测试；`Restore`（草稿恢复）推迟到 M3 |
| 1.2 文本单元 | 完成 | 唯一的 `TextUnits`（拆分、整段判定、拼接） |
| 1.3 保护与提示 | 完成 | `ProtectedText`、`TranslationPrompt` |
| 1.4 ChatClient | 完成 | `OpenAiChatClient`、`IChatCompletionClient`（本地与云端共用） |
| 1.5 调度 | 完成 | `TranslationScheduler` + `DraftSession` |
| 1.6 目录与校验 | 完成 | 每个资产必带 SHA256；新增 tar.gz / zip；`ModelCatalog` 固定版本 |
| 1.7 LlamaServerHost | 完成 | 按角色实例、回环端口、闲置卸载；真实模型端到端探针通过（macOS arm64，1.8B：首字约 200 ms，整句约 400 ms） |
| 1.8 ComposerViewModel | 完成 | 19 个测试；无 Avalonia 依赖 |
| 1.9 视图与接线 | 完成 | `ComposerWindow`、`IWindowTracker.IsAlive`、热键 → 浮窗；Windows 的 `IsAlive` 未在实机验证 |
| 1.10 验收 | 部分 | `ProbeTranslation` 已跑通；**macOS 手工验收与 Windows 验证未做** |

**相对原计划的偏差（均有意为之）**

1. **文本拼接**：听写追加的句子之间不再强制 `\n`（原型 `draft.js` 如此），而是沿用 POC 的同行拼接规则（只在两侧都是 ASCII 时加空格）。原因：向聊天框写入 `\n` 可能直接发送消息。
2. **前文上下文**：评测未测出收益，`TranslationRequest.Context` 保留但调度器不使用；复测有收益再接入。
3. **术语表**：M1 不接入；`TranslationRequest.Terms` 与提示词已支持，设置界面与失效逻辑在 M3。
4. **Windows 运行时**：登记 CPU 版（`win-cpu-x64`）。Vulkan 版待 Windows 实机评估（S3）。
5. **镜像回退**：删除了旧代码里“Hugging Face 不可达时自动改走 hf-mirror.com”的隐式回退；只用用户显式配置的镜像。
6. **已知风险**：应用崩溃（非正常退出）会留下孤儿 llama-server 进程；需要在 M1 验收中确认并加启动时清理或父进程监控。

## M2 进度（随实施更新）

| 任务 | 状态 | 说明 |
|---|---|---|
| 2.3 DictationSession → Draft | 完成 | 17 个行为测试（POC 的音频侧用例逐条移植）；分句复用唯一的 `TextUnits`（新增 `；`、`…`、连续终结符）；识别器由会话持有并释放 |
| 2.4 CleanupScheduler + FidelityGate | 完成 | 门禁 23 个测试原样移植；调度含“后文等待 2 s”、服务级失败整体失败、用户编辑优先 |
| 2.4 整理改走 llama-server（S2） | **完成，结论：可用** | 117 句对照：门禁一致 109/117（93%）；双方都通过时文本逐字相同 80/98（82%，差异多为空格与标点）；拒绝 14 vs POC 16；均值 649 ms/句（M3 Pro）。过程中发现并修复：连接复用导致 5/117 请求失败（现每次请求 `Connection: close`） |
| 2.1 麦克风 / 权限 | 部分 | `IMicrophone`（PortAudio，唯一实现）与 `IMicrophonePermission`（macOS AVFoundation / Windows 注册表）已建；**未在真机上验证采集与权限提示** |
| 2.2 Sherpa 适配器（S4） | **完成（macOS）** | X-ASR 流式、Qwen3-ASR、Silero 迁入 Core；带 SHA256 的目录实测通过（真实 tar.bz2 下载、校验、解压）。用 POC 语料经产品管线回放：同一段困难合成语音（terms-basic）原文 CER 0.737 vs POC 0.729，错写几乎一致；其余 5 段与 POC 行为一致（口头语与重复被整理、停顿切开的句子被缝合）。模型加载 3.4–6.3 s；停止后整理收尾 1.5–4.2 s |
| 2.5 交互与横幅 | 完成 | 点按 / 按住麦克风（350 ms）、录音中实时文字、编辑锁定、Enter 先停止再排队写入、下载同意、权限 / 无设备 / “没有声音”（输入连续 5 分钟静音才关闭麦克风；停顿不会结束录音）横幅，以及“N 句保留原话（整理未通过保真检查）”提示，均有测试；结果框与横幅文字可选中复制（完成后整段为一个可选块） |
| 2.6 回放对照 | 部分 | `ProbeVoice` 可重复运行；**尚未与 POC 做逐指标回归对照**（只做了上面的文本级对照）；真实语料（非合成）未重跑 |

## M3 进度（随实施更新）

| 任务 | 状态 | 说明 |
|---|---|---|
| 设置 v1 | 完成 | 托盘 → 设置：唤起快捷键（校验，改后立即重新注册）、界面语言、翻译模型档位（均衡 1.8B / 质量 7B）、术语表。设置窗口改的是副本，保存才生效 |
| 术语表 | 完成 | `Glossary`：每行一个；只写词＝原样保留（接入二遍识别热词、整理、保真门禁）；`中文=English`＝固定译法（接入翻译提示词，按目标语言自动反向）。每次唤起时读取当前设置 |
| 云端（可选） | 完成（未连真实端点验证） | 设置里“翻译/整理使用云端”两个独立开关，默认关；只发送文字，不发送音频；端点必须 https（回环地址除外），API 密钥存系统密钥库、不写设置文件；`RoutedChatClient` 每次调用读取路由，云端失败直接报错、不回退本地；选云端的角色不再需要本地模型。语音识别始终在本机。需用真实 Foundry 部署验证 |
| 草稿恢复与最近草稿（内存版） | 完成 | `RecentDrafts`：窗口关闭时记下原话（最多 5 条、新的在前、同文去重）；上次未写入的草稿**不再自动恢复**：每次唤起都是空白，空白状态下提供一键“继续上次草稿”（只在上次没写入时出现）；头部“最近”菜单可恢复任一条（仅在空闲且为空时可点）并可清除。只放内存，不落盘，退出应用即清除；导入的转录不记录。落盘保存需要隐私设置，暂不做 |
| 转录导入 / SRT | 完成（仅 WAV） | `WavReader`（PCM 8/16/24/32、float、扩展格式，任意声道与采样率→16 kHz 单声道）→ Silero VAD 按停顿切分（0.5 s）→ Qwen3-ASR 二遍识别；单段超过 12 s 时在其后 4 s 内最安静处切开（真实探针发现：Qwen3-ASR 对 36–43 s 的片段会截断并输出乱码）。结果进 `Draft` 的逐句单元，与语音共用整理、翻译、保真；时间线只读；导出 SRT 用当前输出（整理 / 译文 / 原话）。M3 Pro 上 81 s 录音 7.3–10.5 s 转完（约 8–11 倍实时）。**不支持 m4a / mp3 等压缩格式**（需要各平台解码器，见开放问题） |
| 宽屏 | 完成 | 转录与整段文本自动切为左右对照（980 px），其余堆叠（680 px）；结果区最高 320 px 内部滚动 |
| 历史（云端同步等） | 不做 | 原型只有内存中的最近 5 份草稿，已由上面的“最近草稿”覆盖 |
| 设置迁移（schema 2 → 3） | 完成 | `SettingsMigration`：只保留热键、界面语言、更新设置、高级路径与级别；其余按新设计丢弃，云端保持关闭；其它版本从默认开始。顺带修复：`schemaVersion` 不是数字时会抛 `InvalidOperationException` 使启动崩溃 |
| 旧模型清理（S7） | 完成 | 设置里列出不在目录中的模型目录（数量与大小），用户点“删除旧模型”才删除，不静默删除 |
| 无障碍 | 完成（基础） | 无可见文字的控件有可访问名称（麦克风、互换方向、原话输入框、设置里的输入框）；状态行为礼貌播报、横幅为立即播报；全部文字可选可复制；键盘：Enter 写入、Esc 逐级退出、麦克风 Space/Enter。**未用读屏软件实测** |
| Windows 对齐 | 构建级 | `dotnet publish -r win-x64 --self-contained` 成功，产物含 `portaudio.dll`、`onnxruntime.dll`、`sherpa-onnx-c-api.dll`；Windows 平台服务实现了全部接口（含 `IsAlive`、麦克风与权限）；llama.cpp 已登记 `win-x64` CPU 包（`llama-server.exe`）。**没有在 Windows 上运行过任何东西** |
| macOS 打包 | 修复 | `Info.plist` 缺 `NSMicrophoneUsageDescription`、entitlements 的 audio-input 为 false，打包后的应用无法打开麦克风；已补上。未重新打包签名验证 |
| 许可审计（S5） | 完成 | 逐项核对上游许可文件与模型卡：Hy-MT2、Qwen3.5、X-ASR、Qwen3-ASR、sherpa-onnx 均 Apache-2.0；llama.cpp、Silero VAD 为 MIT。模型只在用户同意后从原站下载，不随应用分发。写入 `THIRD_PARTY_NOTICES.md` 并随产物复制。**仓库没有 `LICENSE` 文件（1.x README 的 MIT 徽章无依据），需要负责人决定**；安装包尚未附带 Apache-2.0 许可全文 |

## M4 进度（随实施更新）

| 项目 | 状态 | 说明 |
|---|---|---|
| S1 内存（开发机数据） | 有数据，**目标机未测** | `ResidentMemoryProbeTests`（`LIVELINGO_ENABLE_VOICE_PROBE=1`，仅 macOS）：识别（实时 + 二遍 + VAD）、整理 4B、翻译全部常驻并各跑过一次请求后，合计常驻内存（M3 Pro 36 GB）：**均衡档 6.6 GB**（应用进程含语音模型 1.8 GB、整理 llama-server 3.1 GB、翻译 1.8B 1.7 GB）；**质量档 10.5 GB**（翻译 7B 的 llama-server 5.8 GB）。结论：均衡档在 16 GB 机器上有余量，可继续作默认；质量档加上系统与其他应用已很紧，不应自动选用。M2/16 GB 上的换页、首字与整句延迟仍需实机 |
| S3 Windows 实机 | 未做 | 需要 Windows 16 GB 机器：PortAudio 采集、热键、注入、CPU / Vulkan 速度 |
| S5 许可 | 完成 | 见上 |
| S7 旧模型 | 完成 | 见上 |
| 安装包与签名 | 部分 | `./build.sh PackMac --Runtime osx-arm64` 成功：`.pkg` 内的 `Info.plist` 含麦克风说明，`libportaudio` / `libonnxruntime` / `libsherpa-onnx-c-api` 与 `THIRD_PARTY_NOTICES.md` 都在包内。**未签名、未公证、未安装运行**（需要证书与公证账号）；Windows 的 `Pack` / `PackMsi` 未在本机验证 |
| CI 门禁 | 通过 | `./build.sh Test`：Core 445、Desktop 359、HfGguf 9 全绿；行覆盖 86.2%、分支覆盖 81.6%（升级 coverlet 10 后分母变大，仍高于门槛 80 / 65）。`./build.sh Mutate`：变异得分 81.99%（门槛 80%，升级包之前测得，界面重写后只改了 Desktop，不在变异范围内）。修复了变异目标从未能运行的问题（Nuke 传 `--break-at 80` 而配置 `low: 60`；命令行 `--mutate` 还覆盖了配置里的范围），现在范围与阈值只由 `stryker-config.json` 决定 |
| 包升级 | 完成 | Avalonia 12.1.4、Microsoft.Extensions 10.0.14（`Http.Resilience` 10.11.0）、CommunityToolkit.Mvvm 8.4.2、Serilog 4.4.0 / Sinks.File 7.0.0、Velopack 1.2.161、测试包与工具（vpk、stryker 5、wix 7）全部最新稳定版。`xunit.v3` 留在 3.2.2：4.x 只支持 Microsoft Testing Platform，.NET 10 SDK 的 `dotnet test` 无法运行。移除未使用的 `Avalonia.Diagnostics` 与 `Avalonia.Fonts.Inter`（界面改用系统字体，与原型一致）。`./build.sh Build`、`Test`、`Run` 均 0 警告 |
| 原型一致性重做 | 完成（实机待验） | 按 `livelingo-ux-v3` 逐项重做：设计令牌（浅 / 深色、跟随系统）、浮窗头部（去向、隐私、输出药丸、更多、关闭）、横幅与行内操作、句子悬停工具与行内编辑、麦克风点按 / 按住、保留 Esc 逐级退出、三个菜单、最近草稿与导出对话框、设置即改即存。用无头 Skia 渲染截图（浅色 / 深色 / 听写 / 横幅 / 宽屏 / 设置四页）与原型截图逐张对照，图标、开关、按钮已修正 |
| 文档 | 完成 | README 改写为 2.0；CHANGELOG 增加 2.0.0 草案；CONTRIBUTING 更新旧模型清理与设置迁移 |
| 真实语料对照 POC | 部分 | 语音：6 个场景回放通过（M2）；录音导入在 81 s 合成长录音上跑通，识别有同音错字（合成语音所致），未做 CER 对照；没有真实人声语料 |

## M5 进度：Web UI 与发布移交（随实施更新，见 `2026-11-01-web-ui-and-release-handoff.md`）

| 项目 | 状态 | 说明 |
|---|---|---|
| W0–W5 | 完成（实机待验） | Composer 与设置都由 `NativeWebView` 承载的页面渲染；状态与逻辑在 C#，页面只画快照、发封闭集合的意图；旧 Avalonia XAML、自绘控件与主题已删除。**GUI 启动、WKWebView 里的原生下拉菜单、热键录制、焦点归还需在 macOS 上手工确认；`./build.sh Run` 未在无界面环境运行** |
| R9 原型缺口 | 部分完成 | 已做：整理开关（关闭时不创建 `CleanupScheduler`、不提供“整理”输出）、运行状态对话框（不含正文 / 术语 / 地址 / 密钥）、保真检查拒绝原因按界面语言显示（`GateRejection` 取代英文句子）。**不做**：识别语言（语音引擎没有语言参数，做了就是无效入口）、保留录音（现在没有任何落盘录音的能力，需先做隐私设计） |
| R11 门禁复测 | 完成 | `./build.sh Test`：HfGguf 9、Core 445、Desktop 564、Web（Playwright，WebKit + Chromium）234 全绿；行覆盖 91.6%、分支覆盖 87.1%。`./build.sh Mutate`：变异得分 82.4%（门槛 80%）。README、CHANGELOG、CONTRIBUTING 已更新（CONTRIBUTING 里 96 / 92 的覆盖率门槛是过期写法，实际门槛 80 / 65） |
| R1–R8、R10 | 未做 | 需要真机、账号、证书或负责人决定 |

## M0 地基

### Task 0.1 分支与基线提交
- [ ] `git switch -c feat/composer-v3`
- [ ] `.gitignore` 追加：`poc/**/corpus*/`、`poc/**/results/`、`poc/**/models/`、`*.wav`、`*.gguf`、`poc/**/bin/`、`poc/**/obj/`
- [ ] `git status --short` 确认待提交内容只有文档、原型与 POC 源码；`du -sh` 检查暂存区无大文件
- [ ] 提交：`docs: add voice and translation POCs, UX prototypes and composer design`

### Task 0.2 删除旧实现
按设计第 2 节清单分批删除，每批后构建：
- [ ] 删除 `tools/TranslationBenchmark`、`tests/LiveLingo.TranslationBenchmark.Tests`，并从 `LiveLingo.slnx` 与 Nuke 引用中移除
- [ ] 删除 Core：`Engines/`、`LanguageDetection/`、旧 `Translation/`、旧 `Speech/`、`Processing/` 的处理器与 Qwen 宿主、`Models` 中的路由 / 选择 / 回退 / 遥测 / Ollama / OpenAI provider、`TranslationChatClient`；同步删除对应测试
- [ ] 删除 Desktop：`Views`、`ViewModels`、`Services/Speech`、`Services/Cloud`、两套音频采集与 `IAudioCaptureService`；同步删除对应测试
- [ ] 将 `App.axaml.cs` 缩减为：托盘 + 热键 + 空白 `ComposerWindow` 占位（后续 Task 1.9 接线）
- [ ] 移除不再使用的包引用（Lingua、Marian 用到的 OnnxRuntime / Tokenizers、旧 Sherpa 版本）
- [ ] 验证：`dotnet build LiveLingo.slnx` 0 警告 0 错误；`dotnet test LiveLingo.slnx` 全绿
- [ ] 提交：`refactor: remove legacy engines, routing and overlay UI`（可按批次拆分提交）

### Task 0.3 门禁配置
- [ ] `tests/LiveLingo.Core.Tests/stryker-config.json` 的 `mutate` 改为 `Drafting/**`、`Translation/**`、`Dictation/**`、`Cleanup/**`
- [ ] `test.runsettings` 排除 `Inference/LlamaServerHost*`、`Speech/Native/**`、`Platform/**`、Views 与 Program / App
- [ ] 更新 `README.md` 与 `CONTRIBUTING.md` 中被删除的概念，保留已有的架构规则
- [ ] 提交：`build: retarget coverage and mutation gates to the new core modules`

## M1 打字翻译（P0）

### Task 1.1 Draft 领域模型（`Core/Drafting`）
对应 `draft.js` 与 `draft.test.cjs` 的 21 个用例，逐条移植为 xunit 测试（文件 `DraftTests.cs`），**先红后绿**。
- [ ] 测试：翻译与整理互相独立；人工编辑不被晚到结果覆盖；单句可改用原话；整理被拒时展示原因并保留原话；切换目标语言只使该语言的译文失效；录音中与所选输出未完成时禁止提交；翻译失败不显示成功；显式回退原话后取消晚到整理；目标丢失阻止写入但可复制；无目标时返回复制动作；提交快照不可变且晚到任务无法改写；空的人工译文视为不可用；`Reset` 使所有旧任务失效；源文变更后调和（未变单元保留结果与编辑）
- [ ] 实现：`Draft`、`DraftUnit`、`UnitView`、`UnitStatus`、`OutputKind`、`CommitResult`（`Text` / `Action` / `Reason`）、`JobTicket(epoch, unitId, revision)`
- [ ] 命令：`dotnet test tests/LiveLingo.Core.Tests --filter "FullyQualifiedName~Drafting"`
- [ ] 提交：`feat(drafting): add draft aggregate with ticketed results and frozen commit snapshot`

### Task 1.2 文本单元（`Core/Drafting/TextUnits`）
唯一的分句与路径判定。以 `poc/text-translation` 的 `Units` 测试与 `draft.js` 的 `splitSentences` / `routeFor` 为特征测试。
- [ ] 测试：中英句末标点；省略号与数字小数点不断句；行与缩进保留；`Join` 逆运算恒等；代码围栏、≥3 行结构化行、超过 400 字符判为整段
- [ ] 实现 `TextUnits.Split`、`TextUnits.Join`、`TextUnits.RouteFor`
- [ ] 提交：`feat(drafting): add unified text unit segmentation and route detection`

### Task 1.3 保护与提示（`Core/Translation`）
- [ ] 从 `poc/text-translation` 搬迁 `ProtectedText` 与 `Prompt` 及其测试；纯文本提示，无 JSON / 引号契约
- [ ] 提交：`feat(translation): add protected literals and plain-text prompt`

### Task 1.4 ChatClient（`Core/Inference`）
- [ ] 测试（`HttpMessageHandler` 桩）：流式增量、取消、非 2xx 的错误类型、拒绝非回环本地地址
- [ ] 实现单一 OpenAI 兼容客户端；基地址、凭据由配置注入
- [ ] 提交：`feat(inference): add OpenAI-compatible streaming chat client`

### Task 1.5 TranslationScheduler
- [ ] 测试：新编辑使旧任务取消并丢弃结果；依赖键（目标语言 / 前文）变化触发重译；失败成为单元状态；`Draft` 拒收过期票据
- [ ] 实现：观察 `Draft`，最新优先，单一串行队列；整段路径一次请求
- [ ] 提交：`feat(translation): schedule latest-wins translation over the draft`

### Task 1.6 目录与完整性校验
- [ ] `ModelDescriptor` / `ModelAsset` 增加 `Revision`、`Sha256`、`SizeBytes`、`License`
- [ ] 下载完成后校验；失败删除文件并抛出明确错误（测试覆盖）
- [ ] 目录登记：llama-server b11980（macOS arm64、Windows x64）、Hy-MT2-1.8B、Hy-MT2-7B；哈希取自 `poc/text-translation/scripts/fetch-models.sh` 所下载的真实文件并核对
- [ ] 提交：`feat(models): pin revisions and verify SHA256 on install`

### Task 1.7 LlamaServerHost
- [ ] 测试（进程接口桩）：按角色启动与复用；端口随机且仅回环；就绪探测超时有界；闲置超时卸载；崩溃后下一请求重启；没有隐藏重试
- [ ] 实现并从目录取得固定版本的可执行文件；替换旧 `LlamaServerProcessManager` 与 `NativeRuntimeUpdater`
- [ ] 提交：`feat(inference): host pinned llama-server instances per role`

### Task 1.8 ComposerViewModel
以 `livelingo-ux-v3` 的行为为准，纯 C#（不引用 Avalonia）。
- [ ] 测试：Enter 写入 / 处理中排队 / Shift+Enter / Esc 逐级 / 输入法组合中忽略；输出胶囊与语言互换；横幅状态；目标失效只允许复制；排队写入的取消条件（Esc、编辑、输出变更、失败）；二次 Enter 无操作
- [ ] 实现 `ComposerViewModel` 与子模型（头部、原话、结果、页脚）
- [ ] 提交：`feat(composer): add composer view model`

### Task 1.9 视图与应用接线
- [ ] 新增 `IWindowTracker.IsAlive`（两个平台实现 + 测试）
- [ ] `ComposerWindow`：样式令牌取自原型 `style.css`；锚定目标窗口底部居中；动画可关；绑定与布局，不含逻辑
- [ ] `App.axaml.cs`：热键 → 捕获目标 → 显示窗口；Enter → `ITextInjector.InjectAsync(target, text, autoSend: false)`
- [ ] 提交：`feat(desktop): show composer at the target window and write back`

### Task 1.10 端到端验证（M1 验收）
- [ ] Nuke 目标 `ProbeTranslate`：启动真实 llama-server，翻译一段固定文本并断言非空、无提示词泄漏
- [ ] 手工（macOS）：TextEdit、浏览器输入框、VS Code、终端；断网状态下完整走通；记录结果到 `docs/superpowers/plans/m1-acceptance.md`
- [ ] 记录 S1 的数据点（开发机为 M3 Pro 36 GB，不能代替 M2/16 GB；结论写明）
- [ ] 提交：`test: add translation probe and M1 acceptance record`

## M2 语音（P1，细化时机：M1 验收后）

| 任务 | 内容 |
|---|---|
| 2.1 | `IMicrophone`（PortAudio）+ `IMicrophonePermission`（macOS / Windows），替换已删除的采集 |
| 2.2 | Sherpa 适配器（2.0.1）：流式识别、二遍、VAD；放入 `Speech/Native` |
| 2.3 | 迁移 `DictationSession` 与 FidelityGate 到 `Core/Dictation`，写入 `Draft`；搬迁 58 个测试 |
| 2.4 | `CleanupScheduler` + 保真门禁；整理改走 llama-server（S2：与 POC 对照） |
| 2.5 | 麦克风点按 / 按住、横幅（无声音、权限、下载）、录音中禁用单元编辑 |
| 2.6 | 回放对照：POC 语料上 CER 与门禁拒绝率不退化；录音中逐句出译文 |

## M3 完整性（P2）
转录导入 / 时间线 / SRT 导出；术语表；草稿恢复；云端（授权开关 + 凭据入密钥库 + 一个 OpenAI 兼容客户端）；宽屏对照；托盘；无障碍（键盘、读屏、对比度）；Windows 对齐。

## M4 发布（P3）
M2/16 GB 与 Windows 16 GB 实测（S1、S3）；安装包与签名；设置 schema 3 迁移；旧模型清理提示（S7）；许可审计（S5）；文档。
