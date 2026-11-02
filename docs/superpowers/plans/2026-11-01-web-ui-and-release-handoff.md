# LiveLingo 2.0 · Web UI 迁移与发布 · 交接计划

> **给接手的 Agent：** 先读第 0、2 节，再从第 4 节的 **W0** 开始，按顺序做。每个任务先写失败的测试再实现，一个任务一个本地提交。用 `- [ ]` 跟踪步骤。

**目标：** 把 Composer 与设置的界面从 Avalonia XAML 换成 Web（Avalonia 外壳 + `NativeWebView`），然后完成真机验证与发布。

**架构：** 状态与业务留在 C#（`ComposerViewModel`、`SettingsViewModel`、Core）；Web 只渲染 C# 推来的快照并回传“意图”。设计与契约见 [Web UI 外壳设计](../specs/2026-11-01-web-ui-design.md)。

**技术栈：** .NET 10、Avalonia 12.1.4、Avalonia.Controls.WebView 12.1.1、Preact 10 + htm（无 Node、无构建步骤）、Playwright .NET、xUnit、Nuke。

---

## 0. 文档与代码在哪里

| 要找什么 | 位置 |
|---|---|
| 产品设计（需求、架构、决策 D1–D13、开放问题 S1–S7） | [specs/2026-10-31-livelingo-composer-design.md](../specs/2026-10-31-livelingo-composer-design.md) |
| **Web UI 设计（本轮新增，待确认）** | [specs/2026-11-01-web-ui-design.md](../specs/2026-11-01-web-ui-design.md) |
| 已完成的实施计划与实测记录（M0–M4 进度表，含数据） | [plans/2026-10-31-composer-implementation.md](./2026-10-31-composer-implementation.md) |
| 界面规格（视觉、交互、28 个场景、状态模型） | [prototypes/livelingo-ux-v3/](../prototypes/livelingo-ux-v3/)：`style.css`、`index.html`、`app.js`、`draft.js`、`browser.test.cjs`、`*.png` |
| 语音与翻译的评测、POC | `docs/superpowers/specs/2026-10-29-voice-dictation-design.md`、`plans/2026-10-29-translation-poc.md`、`poc/` |
| Web 可行性验证程序 | 已完成并移除（`tools/WebViewSpike`，结论见 `specs/2026-11-01-web-ui-design.md` §3；需要时从 git 历史找回） |
| 业务核心 | `src/LiveLingo.Core`：`Drafting`、`Translation`、`Dictation`、`Cleanup`、`Transcription`、`Inference`、`Models`、`Cloud` |
| 桌面外壳 | `src/LiveLingo.Desktop`：`Composer/`、`Settings/`、`Platform/{macOS,Windows}`、`Services/`、`App.axaml.cs` |
| 构建与门禁 | `build/BuildTask.cs`（Nuke）、`test.runsettings`、`tests/LiveLingo.Core.Tests/stryker-config.json`、`.github/workflows/ci.yml` |

## 1. 当前状态（分支 `feat/composer-v3`，仅本地提交，未推送）

| 项 | 状态 |
|---|---|
| 提交 | `c76e407` 包升级；`7b5b375` Avalonia 版界面按 v3 重做（最新）；之前 `bab55ce` 及更早的 M0–M4 |
| 功能 | 打字 / 语音 → 整理（保真检查）/ 翻译 → 回写；录音导入（仅 WAV）与 SRT；术语表；云端（可选，未连真实端点）；最近草稿；设置迁移；旧模型清理；无障碍基础 |
| 测试 | Core 445、Desktop 359、HfGguf 9 全绿；行覆盖 86.2%、分支 81.6%；变异得分 81.99%（界面重写前测得） |
| 构建 | `./build.sh Build`、`Test`、`Run` 均 0 警告；NuGet 全部为最新稳定版（`xunit.v3` 留 3.2.2，见 §2） |
| 界面 | Avalonia 实现，已与原型逐图对照；**无背景模糊**；设置页多了云端三行；没有的设置：识别语言、保留录音、整理方式、诊断 |
| **未验证** | 真机整条流程（热键、真实麦克风、写入 Cursor / Slack / 浏览器 / 终端）；Windows 任何运行；M2 / 16 GB 联合内存与延迟；真实云端；读屏；真实人声语料的 CER 回归 |
| 本轮新增 | Web UI 设计 + 可行性验证程序（macOS 上桥与 CSP 通过；透明、焦点、输入法、Windows 未验证 → W0） |

## 2. 规则与陷阱（必须遵守）

规则：
- 代码、注释、提交信息一律英文；回复用户用中文、简明。
- **不要用脚本改文件**（python / sed / awk / 重定向）。用编辑 / 创建工具。批量机械替换需先告知用户。
- 同一功能只有一个实现；不写防御性回退分支、不为兼容堆 `if-else`；云端必须用户显式开启，无隐式降级；凭据不得硬编码、不入日志与设置文件。
- 本地提交，英文信息，结尾带 `Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>`；**不推送**。
- 验证基线：Apple **M2 及以上、16 GB 以上**；不要拿最低配设备当目标。
- 不引入 OpenSpec；沿用 `docs/superpowers` 的设计 / 计划约定（决策 D7）。
- 完成声明前必须贴出验证命令的真实输出；**0 警告**（`./build.sh Build`、`Test`、`Run`）。

陷阱：
- `xunit.v3` 固定 3.2.2：4.x 只支持 Microsoft Testing Platform，.NET 10 SDK 的 `dotnet test` 跑不了。
- Agent 进程没有 macOS“辅助功能”与“屏幕录制”权限：热键注册会警告，`screencapture` 失败。真机流程与肉眼检查要请用户在自己的终端里做；视觉回归用 Playwright 截图。
- `create` 工具不能覆盖已有文件；改文件用 `edit`，需要重写就先 `rm` 再 `create`（分两次调用，不要并行）。
- 并行调用时，同批的 `build` / `test` 会读到编辑前的旧文件；编辑完成后再构建。
- Avalonia 12 API：焦点重写用 `FocusChangedEventArgs`；`TextBox.PlaceholderText`；`InvokeScript` 的脚本不能返回 Promise（用 `void (...)` 包裹）。
- 无头测试：`TestApp.Initialize` 里要先引用 `typeof(App).Assembly`，否则找不到编译好的 XAML。
- 不能用 `pkill`，只能 `kill <PID>`。
- 本地化：`zh-CN.json` 与 `en-US.json` 的键必须一致（`LocalizationServiceTests.ShellKeys_AreCoveredByBothLanguages` 校验）。
- 变异测试（`./build.sh Mutate`）约 9–10 分钟，范围只由 `stryker-config.json` 决定。

常用命令：

```bash
./build.sh Build         # 构建，必须 0 警告
./build.sh Test          # 全部测试 + 覆盖率门禁（行 ≥ 80%、分支 ≥ 65%）
./build.sh Run           # 运行应用（须在用户终端才有辅助功能权限）
./build.sh Mutate        # 变异测试，门槛 80%
./build.sh ProbeTranslation   # 真实模型翻译探针
LIVELINGO_ENABLE_VOICE_PROBE=1 ./build.sh ProbeVoice   # 语音探针（仅 macOS）
```

## 3. 路线图

| 级别 | 任务 | 说明 | 依赖 |
|---|---|---|---|
| **P0** | **W0** 可行性关口 | 透明 / 焦点 / 输入法 / 预热 / 内存，macOS 与 Windows | 无 |
| P0 | W1 Web 工程脚手架 | Preact + htm + 令牌 + `WebPage` 拼页 + 嵌入 + Playwright .NET 烟雾测试 | W0 通过 |
| P0 | W2 契约与宿主 | `WebSurface`、`WebBridge`、DTO、意图、Schema → TS、安全策略 | W1 |
| P0 | W3 Composer 视图 | 原型标记移植、状态渲染、Playwright 场景与截图 | W2 |
| P0 | W4 Composer 窗口替换 | 外壳窗口、定位 / 尺寸 / 焦点 / 预热，删除旧 XAML 实现 | W3 |
| P0 | W5 设置迁移 | Web 版设置，删除旧设置窗口与控件 | W4 |
| P1 | R1 macOS 真机整条流程 | S6：各类目标应用回填、恰好一次、剪贴板恢复 | W4 |
| P1 | R2 M2 / 16 GB 联合内存与延迟 | S1，含 WebView 占用 | W4 |
| P1 | R3 Windows 16 GB 实机 | S3，含 WebView2 透明 | W4 |
| P1 | R4 质量回归 | 真实人声语料：CER、保真拒绝率对 POC | 无 |
| P1 | R9 原型缺口 | 识别语言 / 整理方式 / 保留录音 / 诊断设置；原话保留原因本地化 | W5 |
| P2 | R5 云端真实验证 | Foundry 部署 | 无 |
| P2 | R6 读屏实测 | VoiceOver / Narrator | W4 |
| P2 | R8 压缩音频导入 | m4a / mp3 解码方案 | 无 |
| P2 | R10 背景模糊 | D21 复议 | R3 |
| **P3** | R7 发布 | 签名与公证、Windows 安装包与 WebView2、LICENSE、许可全文 | R1–R3 |
| P3 | R11 门禁复测 | 删除旧界面后重测覆盖率与变异得分，更新文档 | W5 |

迁移期间**不删除**旧 Avalonia 界面，直到 W4 / W5 的验收通过；删除与替换在同一提交完成，最终不保留两套界面。

## 4. 任务卡（W 系列）

### W0：可行性关口（先做，半天到一天）

**文件：** `tools/WebViewSpike/`（已完成并移除，可从 git 历史找回）；结果写回 `specs/2026-11-01-web-ui-design.md` §3 表格。

- [ ] **步骤 1：** 在用户终端运行 `dotnet run --project tools/WebViewSpike -- --auto`，确认 4 项 PASS（macOS 已通过；在 Windows 上再跑一次）。
- [ ] **步骤 2（S8a、S8b、S8c，需要肉眼，请用户配合）：** 不带参数运行。检查：窗口四周只有圆角面板与阴影、没有白底或黑角（浅色与深色壁纸各看一次）；窗口出现后 `textarea` 已有焦点可直接输入；用系统拼音输入“缓存穿透”，候选窗位置正确，回车确认候选时 C# 侧收到的 `enter` 消息带 `composing:true`。
- [ ] **步骤 3（S8d、S8e）：** 在 spike 里加“隐藏 / 再显示”与计时，测热键到首帧可输入的 p95（目标 ≤ 150 ms）；用活动监视器 / 任务管理器记录 WebView 相关进程内存。
- [ ] **步骤 4（S8f）：** 在 Windows 上若透明不成立，设置 `WindowsWebView2EnvironmentRequestedEventArgs.ExperimentalOffscreen = true` 再测；记录结论。
- [ ] **步骤 5（S8g）：** 试 `WebViewAdapterType.Headless` 是否能让 C# 测试在无界面环境运行。
- [ ] **步骤 6：** 把每项的结论（通过 / 失败 / 采用的退路）写进设计文档 §3，提交：`docs: record the web shell feasibility results`。**任一项失败且退路也不成立 → 停止，向负责人汇报，保留现有 Avalonia 界面。**

验收：S8a–S8f 均有记录；目标机型 M2 或更新、16 GB。

### W1：Web 工程脚手架

**文件（无 Node，设计 §7 / D20）：** 新建 `src/LiveLingo.Desktop/Web/{WebPage.cs,Assets/page.html,Assets/styles/*,Assets/vendor/*,Assets/app/*}`、`tests/LiveLingo.Web.Tests/*`；修改 `LiveLingo.Desktop.csproj`（嵌入 `Web/Assets/**`，`InternalsVisibleTo` 加 `LiveLingo.Web.Tests`）、`LiveLingo.slnx`（加入测试项目）、`THIRD_PARTY_NOTICES.md`（preact 的 MIT、htm 的 Apache-2.0，及版本与 SHA-256）、`.github/workflows/ci.yml`（无需 Node；仅在需要时安装 Playwright 浏览器依赖）。

- [ ] **步骤 1（先写失败测试）：** `WebPage.Build(nonce)` 返回含 nonce 的单页；页面带 CSP meta（设计 §6.2）；预算 ≤ 400 KB；自有源文件无禁用 API；WebKit 与 Chromium 中页面加载后 `LL` 已定义、渲染出注入的快照、零控制台错误、零网络请求。
- [ ] **步骤 2：** 固定版本放入 `vendor/`：`preact@10.29.8`（`dist/preact.min.js`）、`htm@3.1.1`（`dist/htm.js`），记录 SHA-256，许可写入 `THIRD_PARTY_NOTICES.md`。
- [ ] **步骤 3：** 把原型 `style.css` 拆为 `styles/tokens.css`（令牌，名称不变）与 `styles/base.css`；`page.html` 带 `__NONCE__`、`__STYLES__`、`__SCRIPTS__` 占位。
- [ ] **步骤 4：** `WebPage.cs` 按固定顺序拼接嵌入资源；`app/` 下只放最小占位（`99-main.js`：读取入口并渲染“最新快照的 rev”），W3 再替换。
- [ ] **步骤 5：** 新测试项目接入 `LiveLingo.slnx`；夹具里首次使用时安装 Playwright 浏览器。
- [ ] **步骤 6：** 运行 `./build.sh Build`（0 警告）与 `./build.sh Test`，提交：`build: add the web UI assets, page builder and browser tests`。

验收：`./build.sh Build` 与 `Test` 在没有 Node 的机器上成功；`WebPage.Build` 产出单页字符串；WebKit 与 Chromium 烟雾用例通过；禁用 API 扫描与预算测试通过。

### W2：契约与宿主

**文件：** 新建 `src/LiveLingo.Desktop/Web/{WebSurface.cs,WebBridge.cs,WebIntent.cs,ComposerViewState.cs,SettingsViewState.cs,ComposerIntents.cs,SettingsIntents.cs}`；测试 `tests/LiveLingo.Desktop.Tests/Web/*` 与快照黄金文件 `tests/LiveLingo.Web.Tests/Fixtures/*.json`。

- [ ] **步骤 1（先写失败测试）：** `ComposerViewState.From(vm, texts)` 对原型的每种状态给出期望 DTO（空、听写中、翻译完成、整段宽屏、各横幅、编辑中、写入排队、已写入、目标丢失、仅复制）。
- [ ] **步骤 2：** `WebIntent` 封闭联合与解析：未知名、缺字段、超长、错误类型一律拒绝并记不含正文的日志；测试覆盖。
- [ ] **步骤 3：** `WebBridge`：16 ms 合并推送、`rev` 单调递增、`source{text,rev}` 规则（设计 §5.3）、`strings` 在 `ready` 与语言变化时发送；用假 `IWebHost` 测。
- [ ] **步骤 4：** `WebSurface`：承载 `NativeWebView`；`NavigateToString` + nonce 模板；取消一切导航与新窗口；环境设置（macOS 非持久存储；Windows 私有模式 + 临时目录）；所有 `InvokeScript` 用 `void (...)`。
- [ ] **步骤 5：** `ComposerIntents` / `SettingsIntents`：把意图映射到现有 VM 命令；`saveKey` 只经一次消息并立即写密钥库；测试断言快照与日志里没有密钥。
- [ ] **步骤 6：** 快照黄金文件：把步骤 1 的各状态序列化为 `Fixtures/*.json` 并逐字比对（设计 §4.3）；Web 场景测试复用同一批文件。
- [ ] **步骤 7：** `./build.sh Test` 全绿且 0 警告；提交：`feat(web): add the bridge, the intents and the view state`。

验收：新增 C# 代码行 / 分支覆盖不低于现有门禁；快照黄金文件与 C# 一致；日志里没有任何正文或密钥。

### W3：Composer 视图（Web）

**文件：** `src/LiveLingo.Desktop/Web/Assets/app/{00-store,10-bridge,20-composer-*}.js`、`tests/LiveLingo.Web.Tests/*`。

- [ ] **步骤 1（先写失败测试）：** 把原型 `browser.test.cjs` 的场景改写为“`Fixtures` 快照序列 → 断言 DOM 与发出的意图（交给 C# `WebIntent` 解析）”，Playwright .NET 跑 WebKit 与 Chromium。
- [ ] **步骤 2：** 移植原型标记与样式：头部（目的地 / 隐私 / 输出胶囊 / 更多 / 关闭）、横幅、原话区（编辑 / 实时）、结果行（骨架、警告条、悬停工具、行内编辑）、页脚（麦克风 / 状态 / 复制 / 写入）、三个菜单、最近草稿与导出对话框、Toast。
- [ ] **步骤 3：** 键盘语义（设计 §5）：Enter 写入（按设置也可 Ctrl+Enter）、Shift+Enter 换行、Esc 逐级、输入法组合中不触发（WebKit 里确认候选的回车是 `isComposing:false` 且 `keyCode:229`，必须同时判 `e.isComposing || e.keyCode === 229`，W3 用 WebKit 项目加用例）、麦克风 Space / Enter；麦克风只发 `micDown` / `micUp`，时序由 C# 决定；`pointer capture`。
- [ ] **步骤 4：** `source{text,rev}` 规则与光标保持；`ResizeObserver` 上报高度。
- [ ] **步骤 5：** 视觉回归（对原型 PNG 的像素差阈值）、axe 零违规、无小于 12 px 文字、零控制台错误、零网络请求；深浅色与减少动画。
- [ ] **步骤 6：** `./build.sh Test` 全绿；提交：`feat(web): render the composer from view-state snapshots`。

验收：28 个原型场景全部对应一个通过的用例；截图与原型差异在阈值内。没有 JS 行覆盖率门禁（设计 §7，已知上限）。

### W4：Composer 窗口替换

**文件：** 修改 `Composer/ComposerWindow.*`（改为薄外壳）、`App.axaml.cs`；删除旧 XAML 视图与 `ComposerDialogs.cs`、`Controls/Icon.cs`、相关窗口测试；新增外壳测试。

- [ ] **步骤 1：** 外壳窗口：透明、置顶、无边框、不在任务栏；承载 `WebSurface`；沿用 `ComposerPlacement`；宽度由 `layout.wide` 决定，高度来自 `size` 意图。
- [ ] **步骤 2：** 激活与焦点：显示后 `web.Focus()` 并发 `focus{source}`；`Deactivated` → `VM.WindowDeactivatedAsync()`；关闭后焦点还给目标应用（回填仍走现有 `ITextInjector`）。
- [ ] **步骤 3：** 预热：应用启动后创建窗口并加载页面，之后**不要 `Hide()`**（W0 实测 `Hide()` 再 `Show()` 有 330–650 ms 尾部），而是保持显示、透明、移出屏幕（“停放”）；唤起时移回原位 + `reset` + 新状态；测 p95（W0 的 park 数据：首帧 p95 116 ms）。
- [ ] **步骤 4：** 文件选择器与导出对话框的“选择文件”仍在 C#（`StorageProvider`）；其余对话框在 Web。
- [ ] **步骤 5：** 删除旧实现与测试，更新 `App.axaml.cs`；`./build.sh Test` 全绿、`Run` 0 警告。
- [ ] **步骤 6：** 在用户终端完成 **macOS 手工清单**：唤起 → 打字 → 回车写入；语音听写（点按与按住）；翻译；导入录音；各横幅；深浅色；减少动画。提交：`feat(desktop): host the composer in a web view`。

验收：S8 全部通过；手工清单每项勾选；旧界面代码全部删除。

### W5：设置迁移

**文件：** `src/LiveLingo.Desktop/Web/Assets/app/30-settings-*.js`；`Settings/SettingsWindow` 改为薄外壳；删除 `SettingsWindow.axaml(.cs)`、`Controls/HotkeyRecorder.cs`、多余的 `AppTheme.axaml` 样式及其测试。

- [x] **步骤 1：** 四组设置（通用 / 语音与翻译 / 隐私与云端 / 模型与关于），即改即存；云端开关由 C# 决定是否弹同意（同意对话框在 Web 内）。
- [x] **步骤 2：** 热键录制：Web 采集 `event.code` + 修饰键，发 `setting{summonHotkey}`，由 C# `HotkeyParser` 校验并重新注册。
- [x] **步骤 3：** 密钥：`saveKey` 一次性意图；界面只显示“已保存”与“忘记密钥”。
- [x] **步骤 4：** 测试与删除，同 W4；提交：`feat(settings): render the settings in the web view`。

验收：设置所有行为与现有 `SettingsViewModelTests` 一致；旧设置界面代码全部删除。

## 5. 其余任务卡（R 系列，摘要）

| 编号 | 做什么 | 完成标准 |
|---|---|---|
| R1 | 在原生输入框、Electron（Cursor / Slack）、浏览器、终端中逐类回填；中途切走窗口；目标关闭 | 恰好一次、不乱写；用户剪贴板被恢复；失败有横幅与复制兜底；记录进计划的进度表 |
| R2 | M2 / 16 GB：语音 + 整理 + 翻译 + WebView 常驻的峰值 RSS 与首字 / 整句延迟，均衡档与质量档 | 数据写入进度表；决定默认档位（D13）；质量档是否禁用 |
| R3 | Windows 16 GB：PortAudio、热键、注入、`IsAlive`、llama.cpp CPU 与 Vulkan 速度、WebView2 透明 | S3 关闭；`win-x64` 的 `Pack` / `PackMsi` 实际安装运行 |
| R4 | 用真实人声（会议、与 Agent 对话、技术术语）重跑 POC 回放；CER、保真拒绝率对 POC | 不退化；语料不入库 |
| R5 | 用真实 Foundry 部署跑云端翻译 / 整理 | 失败直接报错、不降级；密钥只在密钥库 |
| R6 | VoiceOver 与 Narrator 走完整条流程 | 横幅与状态播报正确；焦点顺序合理 |
| R8 | 设计 m4a / mp3 解码（各平台系统解码器 vs 打包 ffmpeg） | 先出方案与许可评估再实现 |
| R9 | 补原型里有但未实现的设置（识别语言、整理方式、保留录音、诊断）；“已保留原话”的原因改为按界面语言显示 | 每项有 VM 支持与测试；不新增无效入口 |
| R10 | 评估系统 Acrylic / Mica（圆角伪影） | 有结论再决定 |
| R7 | macOS 签名与公证；Windows 安装包含 WebView2 运行时（Evergreen Bootstrapper）；随包附 Apache-2.0 许可全文；`LICENSE` 文件 | 干净机器安装可用 |
| R11 | 删除旧界面后重测覆盖率与变异得分，更新 README / CHANGELOG / 进度表 | 门禁仍达标 |

## 6. 需要负责人决定

1. ~~确认采用 Web UI 方案，以及 Node 进入构建工具链~~ **已决定（2026-11-01）：采用 Web UI；不引入 Node，Web 源文件作为嵌入资源，Preact + htm 随源码提交（D20）。**
2. 仓库没有 `LICENSE`（1.x README 的 MIT 徽章无依据）：选哪种许可。
3. Windows 10 的 WebView2 运行时：随安装包带 Bootstrapper，还是首次运行检测。
4. 默认翻译档位在 M2 / 16 GB 数据出来后确认（D13）。
5. 原型中“写入并发送”已取消（D12）；是否需要作为显式可选项加回。

## 7. 通用验收

每个任务结束：`./build.sh Build`（0 警告）→ `./build.sh Test`（全绿，覆盖率门禁）→ 本地提交（英文信息 + Co-authored-by）。里程碑结束：在用户终端 `./build.sh Run`，对照手工清单操作，并把结果与数据写入 [实施计划](./2026-10-31-composer-implementation.md) 的进度表。
