# LiveLingo 2.0 · Web UI 外壳设计

状态：**负责人已确认采用（2026-11-01，W0 在 macOS 通过）**。前置：[Composer 设计](./2026-10-31-livelingo-composer-design.md)、[原型 v3](../prototypes/livelingo-ux-v3/README.md)。实施见 [交接计划](../plans/2026-11-01-web-ui-and-release-handoff.md)。

## 1. 结论

**保留 Avalonia 作为桌面外壳，界面（浮窗与设置）改用 Web 技术绘制，托管在 Avalonia 12 的 `NativeWebView` 里（macOS：WKWebView；Windows：WebView2）。** 状态与业务仍全部在 C#（`ComposerViewModel`、`SettingsViewModel`、Core），Web 只是状态的渲染层与输入采集层。

理由：

1. **原型就是 Web。** `prototypes/livelingo-ux-v3` 的 `style.css`（238 行）、`index.html`、28 个 Playwright 场景已经定义了视觉与交互。现在用 XAML 重画一遍，需要逐项对照截图才能一致（上一轮就是这样做的），仍有差距（无背景模糊、悬停、动画、字体渲染）。直接复用 CSS 令牌与标记，一致性由浏览器保证。
2. **中文输入法。** 听写工具的使用者会在原话框里打中文。WKWebView / WebView2 的输入法候选窗定位、组合态与回车确认由系统 Web 引擎处理，比 Avalonia `TextBox` 成熟；原型也有“输入法组合中不触发回车”的用例。
3. **迭代成本。** 布局、状态样式、动画、深浅色在 CSS 中改动是分钟级；有热重载；可以用 Playwright（WebKit 与 Chromium 两个引擎）自动截图对比。
4. **无障碍。** 原生 ARIA、读屏与键盘焦点模型，由引擎映射到 VoiceOver / Narrator。
5. **不引入第二个运行时。** 所有能力（热键、窗口跟踪、注入、麦克风、模型、llama-server）已在 .NET；外壳不换。

## 2. 备选与取舍

**不随包带浏览器内核。** `NativeWebView` 使用系统自带的引擎：macOS 的 WKWebView（随系统）、Windows 的 WebView2（Windows 11 预装；Windows 10 可能缺，见 §11）。应用只增加约 1.5 MB 的 NuGet 包。代价是引擎版本由用户系统决定，因此：

- Web 代码的特性基线以 WebKit（macOS 最低支持版本自带的 Safari 内核）与 Chromium（WebView2）的**交集**为准；用到较新的 CSS / JS 特性（`:has()`、`backdrop-filter`、容器查询等）前，先在两个引擎的 Playwright 项目里验证。
- 前端测试同时跑 WebKit 与 Chromium（§7），分别对应 macOS 与 Windows。

| 方案 | 做法 | 结论 | 原因 |
|---|---|---|---|
| **A（推荐）Avalonia 外壳 + NativeWebView** | 一个透明无边框 Avalonia 窗口承载 WebView；JSON 消息桥 | **采用** | 重用全部 C#；MIT 许可，随 Avalonia 12 开源（Avalonia.Controls.WebView 12.1.1，net10.0）；不带 Chromium |
| B 继续 Avalonia XAML | 现状 | 否决 | 视觉一致性靠手工对照；输入法与文本渲染不如 Web；每次改版都要改 XAML 与代码后置（`ComposerWindow.axaml.cs` 495 行都是“状态→控件”映射） |
| C Tauri 2 外壳 + .NET 边车 | Rust 壳 + 系统 WebView，.NET 作子进程 | 否决 | 要用 Rust 重写热键、窗口跟踪、注入、托盘，并设计进程间协议；“同一功能一个实现”被破坏 |
| D Electron + .NET 边车 | 同上，带 Chromium | 否决 | 额外约 150 MB 与一份 Chromium 常驻内存，与模型争 16 GB；同样要重写平台层 |
| E Photino / 直接嵌 WebView2 与 WKWebView | 轻壳 | 否决 | 要自己做透明窗口、置顶、非激活、托盘；Avalonia 已有且已验证 |
| F 本地 HTTP 服务 + 系统浏览器窗口 | Kestrel + 浏览器 | 否决 | 开出监听端口，窗口不可控，无法做透明浮窗与回填焦点 |

## 3. 已验证与未验证

在 macOS（Apple Silicon，Avalonia 12.1.4 + Avalonia.Controls.WebView 12.1.1）上运行 [`tools/WebViewSpike`](../../../tools/WebViewSpike)（`--auto`）：

| 项 | 结果 |
|---|---|
| 适配器 | `WkWebView`，场景 `NativeControlHost`，无需额外安装 |
| `NavigateToString(html, baseUri)` 加载本地页面 | ✅ |
| C# → JS：`InvokeScript` 返回基本类型 | ✅；**返回 Promise 会抛 `NSErrorException`**，脚本必须用 `void (...)` 包裹 |
| JS → C#：页面里的 `invokeCSharpAction(string)` → `WebMessageReceived` | ✅（只传字符串，用 JSON） |
| CSP 生效（`securitypolicyviolation` 触发） | ✅ |

**未验证，必须由 S8 关口确认，任何一项失败都回到第 8 节的退路：**

| 编号 | 问题 | 通过标准 |
|---|---|---|
| S8a | 透明无边框窗口里，WebView 背景是否真透明，圆角与阴影不出白底 / 黑角（macOS、Windows） | 肉眼：窗口外侧只有面板的圆角与阴影；浅色与深色桌面壁纸上都一样 |
| S8b | 焦点：热键唤起后窗口激活，`textarea` 自动获得焦点并可直接打字；失焦（`Deactivated`）事件仍到达 C#；关闭后焦点回到原应用，回填成功 | 手工 10 次，均成功 |
| S8c | 中文输入法：拼音输入、候选窗位置、回车确认候选不触发“写入” | macOS 系统拼音与 Windows 微软拼音各测 |
| S8d | 预热：窗口隐藏时保持 WebView 存活，再唤起的延迟 | 热键到首帧可输入 p95 ≤ 150 ms（M2 级别）；冷启动首次 ≤ 600 ms |
| S8e | 内存与进程：WebView 常驻增加的内存 | macOS ≤ 120 MB，Windows ≤ 200 MB（含渲染进程）；联合内存仍在 S1 的 16 GB 余量内 |
| S8f | Windows 透明：WebView2 作为子窗口时分层窗口是否合成 alpha；不行则用 `WindowsWebView2EnvironmentRequestedEventArgs.ExperimentalOffscreen = true`（由 Avalonia 合成，场景 `OffscreenRenderer`） | 同 S8a |
| S8g | `AdapterType.Headless` 是否能让 C# 侧集成测试在无界面环境跑 | 能则加入 CI；不能则桥的测试用假 `IWebHost` |

### W0 实测记录（2026-11-01，macOS 通过；Windows 推迟到 R3）

状态：**macOS 全部通过，负责人已确认采用 Avalonia + WebView 方案。** 环境：Apple M3 Pro / 36 GB（高于基线 M2 / 16 GB，绝对数偏乐观，用来判断趋势），macOS，Avalonia 12.1.4，WkWebView。Agent 进程无屏幕录制与辅助功能权限，肉眼项与 S8d 最终数据由用户在自己的终端取得。命令：`dotnet run --project tools/WebViewSpike -- [--auto | --bench [--park] [--hide-ms=N] | --headless]`。

| 编号 | 状态 | 结论与数据 |
|---|---|---|
| 桥 / CSP（`--auto`） | ✅ macOS | 4 项 PASS；Windows 未跑 |
| S8a 透明 | ✅ macOS | 用户肉眼确认：浅色与深色壁纸上都没有白底或黑角 |
| S8b 焦点 | ✅ macOS | 用户确认：窗口出现后不点击即可直接打字；用户终端实测 `activeElement = textarea`、`hasFocus = true`。“关闭后焦点回到原应用并回填”属于 W4 步骤 2 |
| S8c 输入法 | ✅ macOS 系统拼音（需一处特判） | 用户实测日志：组合中的按键 `isComposing:true`、`keyCode:229`；候选窗正常，能上屏中文。**但确认候选的回车到达时 `compositionend` 已先触发，该 `keydown` 是 `key:"Enter"`、`keyCode:229`、`isComposing:false`**；用户随后有意按的回车是 `keyCode:13`。只判 `isComposing` 会把确认候选误当成“写入”。**结论：Web 的回车处理必须同时忽略 `e.isComposing` 与 `e.keyCode === 229`（WebKit 的已知行为），只在这一处实现；W3 用 WebKit 项目加用例。** Windows 微软拼音未测 |
| S8d 预热延迟 | ✅ macOS 采用“常驻 + 停放”（`Hide()` 不采用） | **用户终端实测（`--bench --park --hide-ms=2000`，n = 30）：冷启动首帧 24 ms；“脚本已执行、textarea 已聚焦”4–10 ms；首帧 p50 = 42 ms、p95 = 116 ms、max = 119 ms（≤ 150 ms）；结束时 `activeElement = textarea`、`hasFocus = true`。** 结论：W4 的预热窗口不要 `Hide()`，而是保持显示、透明、移出屏幕，唤起时移回。下面是 Agent 进程里较早的数据（应用不在前台、屏幕状态不明），尾部偏高，保留作对照，真实热键流程在 R2 复测。早期数据（首帧 = `requestAnimationFrame` 回调）：**`Hide()` 再 `Show()`**，n = 30：隐藏 0.4 s 时 p50 = 44 ms、p95 = 610 ms（4 次落在 323–650 ms）；隐藏 2 s 时 p50 = 58 ms、p95 = 618 ms（约 1/3 落在 360–650 ms）。**窗口常驻、只置透明并移出屏幕（`--park`）**：隐藏 0.4 s 时 p50 = 29 ms、p95 = 34 ms；隐藏 2 s 时 p50 = 44 ms、p95 = 358 ms（2/30 落在 358–613 ms）。尾部集中在 330 ms 与 650 ms 附近，像 WebKit 对不可见页面的节流恢复。`Hide()` 的路径没有在用户终端复测，因为已决定不用 |
| S8e 内存 | ✅ macOS | 新增 WebKit 进程（用户终端实测）：WebContent 31 MB、GPU 25 MB、Networking 10 MB，合计约 66 MB（≤ 120 MB）。应用进程 184 MB 是整个 Avalonia 运行时，不能归给 WebView |
| S8f Windows 透明 | ⏳ 推迟到 R3 | 负责人决定：现在没有 Windows 机器，W0 按 macOS 通过；Windows 的 `--auto`、透明（含 `ExperimentalOffscreen` 退路）、微软拼音与 WebView2 内存在 R3 实测。若 R3 失败，按 §8 退路处理 |
| S8g Headless | ❌ 不可用，退路成立 | `Avalonia.Headless` 平台下 `NativeWebView` 不创建适配器（无 `AdapterCreated`，`InvokeScript` 抛“Unable to invoke script before any page was loaded”）；`AdapterPreference = [Headless]` 同样无效。`Headless` 适配器本身只是桩，由调用方提供 `ScriptEngine` 与 `HttpHandler`，不会运行页面。结论：桥的 C# 测试用假 `IWebHost`（§7 已规定） |

## 4. 架构

```
┌────────────────────────── Avalonia 外壳（C#）─────────────────────────┐
│ ComposerWindow（透明、置顶、无边框；位置、激活、失焦、尺寸）            │
│   └ WebSurface ── NativeWebView ── 页面 dist/index.html（Preact）       │
│        │  ▲                                                             │
│  intents │ │ state / strings / toast / focus                            │
│        ▼  │                                                             │
│ WebBridge（JSON、校验、合并推送）                                       │
│   ├ ComposerIntents ──► ComposerViewModel ──► DraftSession / Core       │
│   └ ComposerViewState ◄── ComposerViewModel + ComposerTexts（本地化）   │
│ SettingsWindow（同一 WebSurface，另一入口）…… SettingsViewModel          │
│ 平台层（热键、窗口跟踪、注入、麦克风、密钥库）与托盘：不变               │
└────────────────────────────────────────────────────────────────────────┘
```

原则：

1. **单向数据流。** Web → C#：只发“意图”（用户做了什么）。C# → Web：只发“整份状态快照”。Web 不计算业务状态：没有草稿状态机、没有调度、没有计时（原型的 `draft.js` 与模拟管线**不进产品**，只留在原型目录）。
2. **一份本地化来源。** `Resources/i18n/*.json` 仍是唯一来源。静态文案（标签、提示、可访问名）作为一份 `strings` 在语言确定时发给 Web 一次；带参数的文案（状态行、横幅、行内说明）由 `ComposerTexts` 在 C# 格式化后放进快照。Web 里没有 i18n 库、没有文案常量。
3. **一份契约来源。** 消息类型只在 C# 里定义（`record` + `System.Text.Json` 多态），由 Nuke 目标 `WebContract` 用 `JsonSchemaExporter` 生成 `contract.generated.ts`；CI 校验生成物与提交一致。Web 不手写消息类型。
4. **视图映射可测。** 今天 `ComposerWindow.axaml.cs` 里“状态→控件”的 495 行不可测；它变成纯函数 `ComposerViewState.From(vm, texts)`，由 C# 单测覆盖。

## 5. 契约（v1）

所有消息是 JSON 字符串，带 `"v":1`。

### 5.1 Web → C#（意图，封闭集合）

C# 用多态反序列化为封闭的 `WebIntent` 联合；未知 `name`、缺字段、超长（正文 > 1 MiB）、类型不符一律丢弃并记一条不含正文的日志。意图不返回数据，结果通过下一份状态快照到达。

| 域 | 意图（`name`；字段） | 对应 VM |
|---|---|---|
| 生命周期 | `ready`；`size{width,height}`（整数 px，CSS 像素，含 18 px 阴影边距） | 首次推送；窗口高度 |
| 原话 | `source{text,rev}` | `SourceText`（见 5.3） |
| 麦克风 | `micDown`；`micUp` | `PressMicAsync` / `ReleaseMicAsync`（350 ms 阈值仍由 C# 计时） |
| 输出 | `output{value:"clean"\|"translate"\|"original"}`；`swapLanguage` | `Output`；`SwapLanguage` |
| 写入 | `commit{fromKey}`；`copy`；`cancel`（Esc 逐级）；`hide` | `CommitAsync`；`Copy`；`Cancel`；`Hide` |
| 行 | `retry{i}`；`edit{i}`；`editText{text}`；`saveEdit`；`cancelEdit`；`useOriginal{i}` | 同名命令 |
| 草稿 | `newDraft`；`restoreRecent{index}`；`clearRecent`；`import`；`export{format}` | C# 负责文件选择器与对话框 |
| 布局 | `wide{on}`；`whole{on}`；`copyOnly{on}` | `ForceWide`、`ForceWhole`、`CopyOnly` |
| 横幅 | `banner{action}` | `RunBannerActionAsync` |
| 导航 | `openSettings{section}` | `OpenSettings` |
| 设置 | `setting{key,value}`；`addTerm{source,target}`；`removeTerm{index}`；`cloudToggle{step,on}`（C# 决定是否弹同意）；`saveKey{value}`；`forgetKey`；`clearRecent`；`removeLegacyModels` | `SettingsViewModel` |

秘钥规则：`saveKey` 的值只在这一条消息里出现，C# 立即写入密钥库；快照里只有 `hasStoredKey`；日志不得记录消息体。

### 5.2 C# → Web

| 消息 | 内容 | 时机 |
|---|---|---|
| `strings` | `{locale, dict}`：所有静态文案 | 页面 `ready` 后、语言变更时 |
| `composer` | `ComposerViewState`（整份） | 每次 VM 变化；C# 侧用 16 ms 定时器合并，保证最多 60 次/s |
| `settings` | `SettingsViewState`（整份） | 同上 |
| `toast` | `{kind,text}` | `ToastRaised` |
| `focus` | `{target:"source"\|"mic"\|"edit"}` | 窗口显示后；横幅操作后 |
| `reset` | 无 | 重新唤起时清掉 Web 里的瞬时状态（悬停、菜单、对话框） |

`ComposerViewState` 字段（由现有 `ComposerViewModel` 与 `ComposerTexts` 直接得出）：`rev`；`dest{text,kind:"target"|"copyOnly"|"lost",icon}`；`privacy{text,cloud,aria}`；`output{value,pill,menu[],swap}`；`banner{kind,tone,message,progress,actions[{id,label}]}`；`source{mode:"edit"|"live",text,sourceRev,editable,placeholder,label,routeChip,lines[{time,text,tail}],hint?}`；`result{visible,label,rows[{i,state,text,note,time,warn,owned,editing,canEdit,canUseOriginal,error}],editText}`；`footer{mic{recording,aria},status{text,tone},copyEnabled,commit{label,enabled,armed,written,keyCap,aria}}`；`layout{wide}`；`options{animations,commitKey}`；`menus{target[],more[]}`。

### 5.3 原话框的双向同步

用户在 Web 的 `textarea` 里打字时，Web 拥有该文本；C# 在语音、导入、新草稿时才改写文本。每份 `source{text,rev}` 意图带递增 `rev`；快照里的 `source.sourceRev` 是 C# 已接受的最新 `rev`。Web 仅当 `sourceRev` 大于自己最后发送的 `rev` 时才用快照文本覆盖 `textarea`（并保持光标）。这样输入法组合与快速打字不会被回显打断。

### 5.4 窗口尺寸

C# 控制宽度（窄 620 / 宽 940 + 36 px 边距，来自 `layout.wide`）；Web 用 `ResizeObserver` 在 `requestAnimationFrame` 里上报内容高度，C# 设置 `Window.Height` 并按 `ComposerPlacement` 重新定位（向上生长，锚定目标窗口）。**所有弹出菜单与对话框都在面板内**（原型本就如此），因此窗口高度总是包含它们；不依赖窗口外的弹出层。

## 6. 安全

1. 页面只来自内嵌资源（`NavigateToString`，单文件 `index.html`）；`NavigationStarting` 一律取消，`NewWindowRequested` 一律取消。
2. CSP：`default-src 'none'; script-src 'nonce-…'; style-src 'nonce-…'; img-src data:; font-src 'none'; connect-src 'none'; base-uri 'none'; form-action 'none'`。nonce 由 C# 在每次加载时写入模板。页面**没有任何网络请求**；`connect-src 'none'` 使这一点由引擎强制，并与“默认没有出站请求”的隐私承诺一致。
3. 数据存储不落盘：macOS 设置 `NonPersistentDataStore = true`；Windows 用 `IsInPrivateModeEnabled = true` 并把 `UserDataFolder` 放在临时目录并在退出时清除。
4. **模型输出与用户文本是不可信输入。** Web 只用文本节点渲染；禁止 `innerHTML`、`dangerouslySetInnerHTML`、`eval`、`new Function`、动态 `<script>`；ESLint 规则强制，CI 失败即拒。
5. 发布版禁用开发者工具；Debug 构建才允许 `LIVELINGO_WEB_DEV_URL` 指向 Vite 开发服务器（仅回环地址）。
6. 消息桥只接受第 5 节的封闭意图；Web 无法调用任意 C# 方法；也没有文件、剪贴板、热键 API——这些都经 C# 意图完成。
7. 日志不含正文、录音、密钥；消息体不入日志。

## 7. Web 工程

```
src/LiveLingo.Web/
  package.json  package-lock.json  .nvmrc  tsconfig.json  vite.config.ts  eslint.config.js
  index.html                       入口（模板；含 nonce 占位）
  src/
    main.tsx                       读取入口（composer | settings），启动 store 与桥
    bridge.ts                      唯一与宿主通信的模块；Debug 下可换成 mock
    store.ts                       持有最新快照与瞬时 UI 状态（菜单开合、悬停）
    contract.generated.ts          由 WebContract 生成，禁止手改
    views/composer/*               头部、横幅、原话区、结果行、页脚、菜单、对话框
    views/settings/*               四组设置
    styles/tokens.css  base.css    由原型 style.css 拆分；令牌名不变
  tests/                           Vitest（store、键盘语义）与 Playwright（场景、截图、a11y）
```

- 技术：Vite + TypeScript（`strict`）+ **Preact**（运行时依赖只有这一个，约 4 KB）。备选 Svelte / Solid / 原生 DOM：Svelte 需要编译器运行时与更多概念；原生 DOM 重复原型 `app.js` 的命令式写法，状态与视图易漂移；Preact 的“快照 → 视图”函数最贴近本设计。
- 产物：`vite-plugin-singlefile` 打成一个 `index.html`，嵌入 `LiveLingo.Desktop` 的 `EmbeddedResource`。预算：≤ 150 KB（gzip 前 ≤ 400 KB）。
- 字体：系统字体栈，不打包字体（与原型一致）。
- Nuke 新目标：`WebRestore`（`npm ci`）、`WebContract`（生成并校验契约）、`WebBuild`、`WebTest`；`Build` 依赖 `WebBuild`；`Run` 在 Debug 下可附带 `--WebDev`。CI 安装固定的 Node（`.nvmrc`）。
- 测试金字塔：
  1. C#：`ComposerViewState`、`WebIntent` 解析与拒绝、`WebBridge`（合并、丢弃、校验）、`ComposerIntents` → VM 的映射；沿用现有 VM 测试。
  2. Web 单元：Vitest（`store`、`bridge` 的 rev 规则、键盘语义：Enter / Shift+Enter / Esc / 输入法组合）。
  3. Web 场景：Playwright（**WebKit 与 Chromium**，对应 WKWebView 与 WebView2），宿主用 mock 桥回放“快照序列”；把原型 `browser.test.cjs` 的 28 个场景改写为“给定快照 → 断言 DOM 与发出的意图”。
  4. 视觉：对原型截图（`translation-live`、`long-document`、`dark`、`settings` 等）做像素差阈值回归；axe 无障碍零违规；全页无小于 12 px 的文字；零控制台错误与零网络请求。
  5. 真机：每个 OS 一份手工清单（S8 与 Composer 验收），因为 `NativeWebView` 不能无界面运行（S8g 另测）。
- 门禁：Web 行覆盖 ≥ 80%；`tsc --noEmit`、`eslint`、`npm audit --omit=dev` 零问题；C# 门禁不变。

## 8. 退路（S8 失败时）

| 失败项 | 退路 | 代价 |
|---|---|---|
| S8a / S8f 透明不成立 | 面板不透明（去掉窗口圆角阴影，用不透明矩形窗口与系统阴影）；Windows 试 `ExperimentalOffscreen`；macOS 试 `drawsBackground = false`（经 `IAppleWKWebViewPlatformHandle`） | 视觉略偏离原型的悬浮感 |
| S8b 焦点 | 窗口激活后由 C# `web.Focus()` 再 `InvokeScript("void document.getElementById('source').focus()")` | 多一步 |
| S8c 输入法有缺陷 | 针对问题特判（例如 `compositionend` 之后再处理回车） | 只在 Web 一处处理 |
| S8d 延迟过高 | 保持 WebView 常驻（预热）；仍不够则不销毁窗口只隐藏 | 常驻内存 |
| 全部不可行 | 保持现有 Avalonia 界面（已提交，`7b5b375`） | 无需回退，因为迁移期间不删除旧界面，直到 W4 通过验收 |

## 9. 将被删除（W4 / W5 验收后，同一个提交内完成）

`Composer/ComposerWindow.axaml(.cs)`、`Composer/ComposerDialogs.cs`、`Settings/SettingsWindow.axaml(.cs)`、`Controls/Icon.cs`、`Controls/HotkeyRecorder.cs`（热键录制改在 Web，由 `event.code` 映射到 `HotkeyParser` 的 token，C# 校验）、`Styles/AppTheme.axaml` 中除托盘与系统对话框所需之外的全部样式、各 `*Window*` 测试。保留：`ComposerViewModel*`、`ComposerTexts`、`ComposerPlacement`、`RecentDrafts`、`SettingsViewModel`、平台层、托盘。不保留两套界面：“同一功能只有一个实现”。

## 10. 关键决策

| 编号 | 决策 | 备选 | 取舍 |
|---|---|---|---|
| D14 | Web 只做渲染与输入，状态留在 C# | 状态机放 Web（直接用 `draft.js`） | 两处状态机会漂移；C# 已有 359 个测试保护 |
| D15 | 整份快照，不做增量补丁 | JSON Patch | 快照约数 KB，合并后 ≤ 60 次/s，足够；长文档超过预算再按行 `rev` 做增量（先测量） |
| D16 | 契约由 C# 生成 TS 类型 | 手写两份 / 用 OpenAPI | 单一来源；漂移由 CI 发现 |
| D17 | `NavigateToString` 单文件 + CSP `connect-src 'none'` | 本地 HTTP 服务；`avares://` 自定义协议 | 无监听端口、无网络；已在 macOS 验证 `NavigateToString` 可用 |
| D18 | Preact | Svelte / Solid / React / 原生 | 体积、概念少、快照渲染最直接 |
| D19 | 设置也用 Web，同一包不同入口 | 设置保持 XAML | 一套设计令牌与一种技术；省掉 HotkeyRecorder 等控件 |
| D20 | Node 进入构建工具链 | 提交 `dist` 产物 | 提交产物会漂移且不可审；代价是开发者需装 Node（固定版本，`.nvmrc`），CI 缓存 |
| D21 | 不做背景模糊（沿用 alpha 玻璃） | 系统 Acrylic / Mica | 圆角窗口 + 系统模糊在 Windows 上会有直角伪影；先不承诺 |

## 11. 开放问题

1. Node 作为构建依赖是否可接受？（D20）
2. Windows 10 上 WebView2 运行时：安装包随带 Evergreen Bootstrapper，还是首次运行检测并以横幅提示下载？建议随带（离线可装），决定权在发布负责人。
3. `LICENSE` 文件缺失（见交接计划 §6）。
