# LiveLingo Composer 实施计划

设计见 [composer 设计](../specs/2026-10-31-livelingo-composer-design.md)。本计划按优先级拆分；**M0、M1 给出可直接执行的步骤**，M2 起在前一个里程碑验收后再细化（其前提取决于 M1 的实测结果，如 S1）。

规则：每个任务先写失败的测试再实现；每步结束运行 `dotnet build LiveLingo.slnx` 与相关测试；每个任务一个本地提交，英文提交信息，带 `Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>`；不推送。

## 总览

| 里程碑 | 级别 | 范围 | 验收 |
|---|---|---|---|
| M0 地基 | P0 | 分支、基线提交、删除旧实现、门禁配置 | 解决方案可构建；保留模块测试全绿；覆盖 / 变异配置指向新模块 |
| M1 打字翻译 | P0 | Draft 域、文本单元、翻译管线、固定运行时与目录、Composer 与回写 | 离线：唤起 → 输入 / 粘贴 → 译文 → 回写；目标失效不乱写；真实模型探针通过 |
| M2 语音 | P1 | 麦克风、Sherpa 2.0.1、DictationSession 接入 Draft、整理与保真、听写交互 | POC 评测语料不退化；录音中逐句出译文 |
| M3 完整性 | P2 | 转录导入 / 时间线 / SRT、术语、草稿恢复、云端（Foundry / OpenAI 兼容）、宽屏、托盘、无障碍、Windows 对齐 | 逐项验收见任务卡 |
| M4 发布 | P3 | M2/16 GB 与 Windows 16 GB 验证、安装包与签名、设置迁移、旧模型清理、文档 | 开放问题 S1–S7 全部关闭或有明确结论 |

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
