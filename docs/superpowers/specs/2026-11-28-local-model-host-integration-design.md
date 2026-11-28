# LiveLingo · 本机模型宿主集成设计（Bionic 优先 + 主流 Local Model API 自发现与复用）

日期：2026-11-28。状态：**已实施并通过单元、浏览器及本机 Bionic 1.2.2+3 实机探针验证**。相关：[Composer 设计](./2026-10-31-livelingo-composer-design.md)、[模型预热设计](./2026-11-02-model-warmup-design.md)、[Qwen-Audio-3.1-ASR 接入设计](./2026-11-22-qwen-audio-3.1-asr-integration-design.md)、[多场景润色设计](./2026-11-27-multi-scenario-polish-design.md)。

---

## 1. 背景、核心痛点与优化收益

随着 LiveLingo 基线硬件升级至 32 GB 统一内存并引入旗舰共享模型（[ModelCatalog.cs](../../../src/LiveLingo.Core/Models/ModelCatalog.cs)），本地文本大模型体积与常驻内存已显著增长：

- 统一旗舰 `Qwen3.6 35B-A3B MoE`：磁盘 **17.73 GB**，常驻内存约 **18.8 GB**；
- 分阶段高质量组合 `Qwen3.5 9B`（5.68 GB / ~6.17 GB RAM）+ `Index-Translate 9B`（5.78 GB / ~6.27 GB RAM）：磁盘合计 **11.46 GB**，双驻留约 **12.44 GB**。

与此同时，LiveLingo 的目标用户本机通常已在运行 **Bionic（LM Studio Bionic）**、**LM Studio / `llmster`**、**Ollama** 或独立本地推理服务（如 `llama-server`、`mlx_lm.server`、vLLM、Jan），并已下载甚至正在内存中常驻 15–20+ GB 的高性能模型（例如本机 Bionic 实测中正在 `omlx` 引擎上常驻的 `qwen/qwen3.8-27b` MLX 4-bit，占 **16.08 GB** 统一内存）。

若 LiveLingo 仅依赖私有目录下载并拉起独立 `llama-server`，会引发四类直接问题：

1. **磁盘重复占用**：同一量级模型在 Bionic / Ollama 与 LiveLingo 数据目录各存一份，白白多占 **11–20+ GB** 磁盘及对应下载流量。
2. **内存双份驻留与抖动**：在 32 GB 设备上，若 Bionic 已加载 16–18 GB 模型，LiveLingo 再独立启动 `llama-server` 加载另一份权重，会迅速触发系统内存压缩与 Swap 抖动，甚至挤掉前台应用。
3. **错失宿主硬件引擎优化**：Bionic / LM Studio 在 Apple Silicon 上通过内置 `omlx` 引擎原生运行 **MLX** 量化模型并支持多槽并发（实测 `parallel: 4`），而 LiveLingo 随包分发的 `llama-server` 仅支持单槽 GGUF。
4. **现有「云端端点（`CloudSettings`）」无法替代本机宿主**：
   - [CloudRoute.cs](../../../src/LiveLingo.Core/Cloud/CloudRoute.cs) 强制要求非空 `ApiKey`，而 Bionic / LM Studio / Ollama 本机服务默认免鉴权；
   - [SettingsCloudRoutes.cs](../../../src/LiveLingo.Desktop/Services/Cloud/SettingsCloudRoutes.cs) 将端点一律视为**云端外发**，会弹出隐私外发授权确认框、把隐私状态条标为「部分云端」；
   - `CloudSettings` 不具备本机宿主自发现、可用模型与热驻留状态识别、缺失模型下载引导，也无法传递宿主原生的思考链关闭参数（如 Bionic `/api/v1/chat` 的 `"reasoning": "off"` 或 Ollama `/api/chat` 的 `"think": false`）——实测 Bionic 中 `qwen/qwen3.8-27b` 默认开启 `reasoning = "xhigh"`，若直接走普通 OpenAI 端点不关闭思考，每次润色都会白白耗费数十秒！

### 1.1 优化收益量化（32 GB Apple Silicon · 运行 Bionic 实测基线）

| 指标 | 全内置模式（与 Bionic 并行运行） | 接入本机宿主共享模式（复用 Bionic 热模型） | 收益 |
|---|---|---|---|
| 文本 LLM 磁盘占用（LiveLingo 私有目录） | 11.5–17.7 GB + `llama-server` | **0 GB**（直接复用 `~/.lmstudio/models` 共享库） | 节省 **11.5–17.7 GB** 磁盘与下载时间 |
| 文本 LLM 新增驻留内存 | +6.2–18.8 GB（独立 `llama-server` 进程） | **0 GB**（直接复用 Bionic 已驻留的 16.08 GB `qwen/qwen3.8-27b` `omlx` 实例） | 彻底消除双份驻留与 Swap 抖动 |
| 首次整理/翻译冷启动延迟 | 5–12 s（拉起进程 + 载入权重） | **< 100 ms**（命中热实例 + `parallel: 4` 多槽并发） | 首句响应接近瞬时 |
| 语音阶段本地内存余量 | 与自管 LLM 争抢 `CoResidentBudgetBytes` | 释放自管 LLM 预算，轻松常驻旗舰 `Qwen3-ASR 1.7B`（~3.0 GB） | 语音识别质量同步受益 |
| 隐私属性 | 100% 本机处理 | **100% 本机处理**（严格限定回环地址，数据不出设备） | 零隐私退让 |

---

## 2. Bionic 实机调研事实（第一手已验证依据）

在设计落地前，对本机运行的 **Bionic 1.2.2+3**（`/Applications/Bionic.app`，未同时安装 `LM Studio.app`）进行了只读实机核查，以下 **6 项实测事实**直接决定了集成方案的关键细节，避免了多处致命踩坑：

1. **Bionic 默认公开 HTTP API 端口为 `1235`（而非 LM Studio 经典的 `1234`）**：
   - 配置文件位于 **`~/.lmstudio/apps/bionic/.internal/http-server-config.json`**（而非 `~/.lmstudio/.internal/`），实测内容包含 `{"autoStartOnLaunch": true, "port": 1235, "networkInterface": "127.0.0.1", "justInTimeModelLoading": true}`；
   - 安装路径自报文件位于 `~/.lmstudio/apps/bionic/.internal/app-install-location.json`；
   - Bionic 同时随装 `~/.lmstudio/bin/lms`，执行 `lms server status --json` 直接返回 `{"running":true,"port":1235}`。
2. **必须区分公开 API 端口与内部控制端口（严防误连 `http-server.json`）**：
   - `~/.lmstudio/apps/bionic/.internal/http-server.json` 记录的是内部 RPC/SDK 控制端口（实测 `127.0.0.1:52173`）；
   - 该内部端口对 `GET /lmstudio-greeting` **同样返回 `200 OK {"lmstudio":true}`**，但对 `/api/v1/models` 与 `/v1/models` 返回 **`404 Not Found`**；
   - **设计结论**：探测时必须读取 `http-server-config.json`（不是 `http-server.json`），且**必须以 `GET /api/v1/models` 返回 `200 OK` 为最终有效凭证**，绝不能仅凭 `/lmstudio-greeting` 判定端口可用。
3. **Bionic 内部托管 `omlx-server` 子进程（随机高位端口），绝不可绕过宿主直连**：
   - 实测 Bionic 会在本机拉起子进程 `omlx-server`（例如 `127.0.0.1:64168`）作为 Apple Silicon MLX 推理后端，并管理 `~/.lmstudio/.internal/prompt-cache/omlx` 缓存；
   - **设计结论**：LiveLingo 只通过 Bionic 公开 API（`1235`）接入，绝不扫描高位随机端口，避免绕过 Bionic 的并发调度与缓存管理。
4. **`GET /api/v1/models` 与 `GET /v1/models` 返回集合存在本质差异（必须首选 `/api/v1/models`）**：
   - 实测 Bionic 的 **`GET /v1/models`** 混入了非本地/非文本模型且不带任何类型标记：
     - 本地 TTS：`phonon-40m-multilingual`；
     - 本地 ASR：`voxtral-realtime-et`；
     - Bionic+ 云端模型：`deepseek-v4.1-flash-1017`（**若用户误选会破坏“100% 本机隐私”承诺！**）；
     - 向量模型：`text-embedding-nomic-embed-text-v1.5`。
   - 而 **`GET /api/v1/models`** 精准只列出本机磁盘模型，且提供完整元数据：`type`（`"llm"` / `"embedding"`）、`format`（`"mlx"` / `"gguf"`）、`quantization`、`size_bytes`、`params_string`、`max_context_length`、`loaded_instances[]`（含 `engine: "omlx"`、`config.context_length`、`config.parallel`）及 `capabilities.reasoning`。
5. **`type == "llm"` 中仍可能混入非文本模态模型（需语义过滤）**：
   - 实测 Bionic 的 `/api/v1/models` 中音乐生成模型 `inspiremusic-1.5b-long` 也被归类为 `type: "llm", architecture: "qwen2"`；
   - **设计结论**：模型匹配器必须对 `key` / `display_name` 做非文本模态黑名单过滤（`music`、`audio`、`tts`、`asr`、`whisper`、`voxtral`、`phonon`、`rerank`、`embed`、`diffusion` 等），避免向用户推荐不可用于文本整理的模型。
6. **热加载旗舰模型默认开启重度思考（`default: "xhigh"`），必须按能力显式关闭**：
   - 实测 Bionic 热加载的 `qwen/qwen3.8-27b`（16.08 GB MLX 4-bit，`omlx`，`parallel: 4`，`context_length: 262144`）其 `capabilities.reasoning` 为 `{"allowed_options": ["off","low","medium","xhigh","on"], "default": "xhigh"}`；
   - 其他已下载的 `qwen3.8-flash-next`、`google/gemma-4-31b`、`google/gemma-4-26b-a4b-qat`、`google/gemma-4-12b-qat` 的默认 `reasoning` 也全为 `"on"`，且全部支持 `"off"`；
   - **设计结论**：通过 `POST /api/v1/chat` 在每条 LiveLingo 请求中显式传入 `"reasoning": "off"` 与 `"store": false`，即可**零修改 Bionic 自身配置、零影响 Bionic Agent 的深度思考会话**，同时让 LiveLingo 享有亚秒级直出响应！

---

## 3. 适用范围、架构不变量与可扩展多宿主发现体系

### 3.1 管道阶段边界与四条工程不变量

- **适用阶段**：仅覆盖文本 LLM 阶段——**整理与多场景润色（`ModelStage.Cleaner` / `InferenceRole.Cleaner`）** 与 **翻译（`ModelStage.Translator` / `InferenceRole.Translator`）**，两阶段可**独立绑定**到同一宿主热模型或不同宿主/模型。**语音识别（`ModelStage.Speech`）** 保持独立（继续走本地 `SherpaSpeechEngine` 或可选云端 `Qwen-Audio-3.1-ASR`）。
- **四条不可违反的不变量**：
  1. **三源互斥，显式选择，绝不静默降级**（沿袭 [Composer 设计](./2026-10-31-livelingo-composer-design.md) D2 与 [RoutedChatClient.cs](../../../src/LiveLingo.Core/Cloud/RoutedChatClient.cs) 原则）：每阶段在 `BuiltInLocal` / `LocalHost` / `Cloud` 间严格三选一；绑定的本机宿主不可达或模型失踪时，明确报错并提供一步恢复动作，**绝不**静默拉起第二个 `llama-server` 或偷发云端。
  2. **严格回环约束（Strict Loopback = 100% 本机隐私）**：`LocalHostRoute` 仅接受 `127.0.0.1` / `localhost` / `[::1]`，免云端隐私授权弹窗，隐私状态条保持绿色「全部本机处理」；跨机局域网 / LM Link 远端地址统一归入 `Cloud` 自定义端点走显式授权。
  3. **共享宿主礼仪（Co-Tenancy Courtesy）**：绝不调用 `unload` 强卸宿主模型、绝不关闭宿主进程、绝不修改宿主全局配置；内存回收交由宿主原生 TTL / `keep_alive` / Auto-Evict 仲裁。
  4. **面向扩展的 Provider 抽象（零堆砌 `if-else`）**：所有本机宿主统一通过 `ILocalHostProvider` + `LocalHostRegistry` 接入，新增任意本地模型宿主仅需添加一个小型 Provider 适配器。

### 3.2 主流 Local Model API 自发现与复用架构（`ILocalHostProvider`）

LiveLingo 以 **Bionic 为一等公民默认优化目标**，同时通过统一的 `LocalHostRegistry` 自动发现并复用本机上运行的全部主流 Local Model API 宿主：

```text
                    ┌──────────────────────────────────────────┐
                    │      LocalHostRegistry (并行自发现)       │
                    │  单探针超时 800ms · 按 host:port 去重     │
                    └────┬──────────────┬─────────────────┬────┘
                         │              │                 │
                         ▼              ▼                 ▼
          ┌────────────────────┐ ┌─────────────┐ ┌─────────────────────────┐
          │ LmStudioFamily     │ │ Ollama      │ │ GenericOpenAiLocal      │
          │ Provider (P0)      │ │ Provider    │ │ Provider (P1)           │
          │ • Bionic  (:1235)  │ │ (:11434)    │ │ • llama-server (:8080)  │
          │ • LM Studio(:1234) │ │ /api/tags   │ │ • vLLM / MLX  (:8000)   │
          │ • llmster / lms    │ │ /api/ps     │ │ • Jan         (:1337)   │
          │ /api/v1/models     │ │ /api/chat   │ │ • 自定义回环 URL        │
          │ /api/v1/chat       │ │ /api/pull   │ │ /v1/models + /props     │
          └────────────────────┘ └─────────────┘ └─────────────────────────┘
```

#### 主流宿主协议能力对照表

| 优先级 | 宿主类型 (`HostKind`) | 自动发现策略（均限回环） | 模型发现与热驻留识别 | 推理协议与思考链关闭 | 缺失模型下载能力 |
|---|---|---|---|---|---|
| **P0 主力** | **Bionic** (`bionic`) | 1. `~/.lmstudio/apps/bionic/.internal/http-server-config.json`（实测默认 **`1235`**，跳过 `http-server.json`）<br>2. `~/.lmstudio/bin/lms server status --json`<br>3. 默认候选端口 `1235` | `GET /api/v1/models`（严选本机 `type=="llm"`，天然屏蔽云模型/ASR/TTS）；`loaded_instances[]` 精确识别 `engine: "omlx"`、`parallel`、`context_length` | `POST /api/v1/chat`（`store: false`，当 `allowed_options` 含 `"off"` 时强制 `"reasoning": "off"`，仅消费 `message.delta`） | 实现 `ILocalHostModelDownloader`：`POST /api/v1/models/download` + 状态轮询；兜底打开 Bionic（`Settings → Local Models → Explore`）或复制 `lms get` |
| **P0 主力** | **LM Studio / `llmster`** (`lmstudio`) | 1. `~/.lmstudio/.internal/http-server-config.json` 与 `~/.lmstudio/apps/lmstudio/.internal/http-server-config.json`（默认 **`1234`**）<br>2. `lms server status --json`<br>3. 默认候选端口 `1234` | 同上 `GET /api/v1/models`（兼容 `/api/v0/models`、`/v1/models`）；若与 Bionic 共用同一端口自动合并去重 | 同上 `LmStudioChatClient`（原生 `/api/v1/chat` + 旧版 `/v1/chat/completions` `ttl: 600` 兼容） | `POST /api/v1/models/download`；兜底打开 LM Studio Discover 或 `lms get` |
| **P1 原生** | **Ollama** (`ollama`) | 1. 环境变量 `OLLAMA_HOST`（仅当为回环地址时采纳）<br>2. 默认 `http://127.0.0.1:11434` | **`GET /api/tags`**（磁盘模型、量化、参数规模）+ **`GET /api/ps`**（**内存/VRAM 实时热驻留模型**与 `size_vram`） | `OllamaChatClient`：`POST /api/chat`（原生 **`"think": false`** 关思考 + `keep_alive: "10m"` + 完整 `options` 采样参数） | 实现 `ILocalHostModelDownloader`：`POST /api/pull` 流式拉取进度；兜底复制 `ollama pull <model>` |
| **P1 通用** | **通用 OpenAI 兼容本地服务** (`openai-local`：独立 `llama-server` / `mlx_lm.server` / vLLM / Jan / LocalAI / 自定义端口) | 1. 轻探测常用固定端口：`8080`（`llama-server`，附探 `/props` 读 `n_ctx`）、`1337`（Jan）、`8000`（vLLM / MLX）—跳过已被 P0/Ollama 占用的端口；<br>2. **绝不扫描高位随机端口**（防误连 Bionic 内部 `omlx-server`）；<br>3. 支持用户手动添加任意本机回环 URL | `GET /v1/models`（过滤已知 embedding/asr/tts 关键词；若存在 `/props` 则补充上下文与活跃模型信息） | `OpenAiChatClient` + 安全白名单采样参数（`top_p`, `top_k`, `repeat_penalty`, `seed`）+ 可选 `chat_template_kwargs: { enable_thinking: false }`（若返回 400 自动降级不带该扩展字段重试一次） | 仅提供推荐模型名与手动启动指引（不实现自动下载接口） |

### 3.3 四态宿主探测状态机（`LocalHostStatus`）

对每个已发现或已配置的宿主实例，维护统一的四态状态机（探测单次超时 **`800 ms`**，内存缓存 **15 秒**，不阻塞启动与 UI）：

```text
                   ┌──────────────────────────────┐
                   │   Parallel Provider Probes   │
                   │  (Bionic / LM Studio / Ollama│
                   │   / OpenAI-Local / Custom)   │
                   └──────────────┬───────────────┘
                                  │
          ┌───────────────────────┼───────────────────────┐
          ▼                       ▼                       ▼
     HTTP 200                 HTTP 401              连接失败 / 超时
  ┌───────────────┐       ┌──────────────┐       ┌─────────────────────┐
  │    Running    │       │ AuthRequired │       │ 检测本机安装痕迹     │
  │ 列出模型+热状态│       │ 需配置 Token │       └──────────┬──────────┘
  └───────────────┘       └──────────────┘                  │
                                                 ┌──────────┴──────────┐
                                                 ▼                     ▼
                                            检测到应用/目录         无安装痕迹
                                       ┌─────────────────────┐ ┌──────────────┐
                                       │ InstalledNotServing │ │ NotInstalled │
                                       │ 已安装，服务未启动   │ │  未检测到    │
                                       └─────────────────────┘ └──────────────┘
```

| 状态 | 判定依据 | 用户提示与可执行动作 |
|---|---|---|
| **`Running`** | 宿主 API 返回 `200 OK` 且模型列表解析成功（Bionic/LM Studio 必须校验 `/api/v1/models` 或 `/api/v0/models` 而非仅 `/lmstudio-greeting`） | 按宿主分组展示可用模型、格式（`MLX`/`GGUF`）、热驻留与并发槽状态；若无合适模型进入第 5 节缺失引导 |
| **`AuthRequired`** | 回环 API 返回 `HTTP 401`（例如 LM Studio / Bionic 开启了 Require Authentication） | 提示「该本机宿主已开启 API Token 鉴权」，提供输入框按 `HostId` 存入系统密钥库（`ISecretStore`），保存后自动重探 |
| **`InstalledNotServing`** | 端口未监听，但检测到本地安装痕迹：<br>• Bionic：`~/.lmstudio/apps/bionic/.internal/app-install-location.json`、`/Applications/Bionic.app`、`%LOCALAPPDATA%\Programs\Bionic\`<br>• LM Studio：`/Applications/LM Studio.app`、`%LOCALAPPDATA%\Programs\LM Studio\`、`~/.lmstudio/bin/lms`<br>• Ollama：`which ollama`、`/Applications/Ollama.app`、`~/.ollama/` | 提供非侵入式一步操作（仅在用户手动点击后执行，绝不后台自启）：<br>• **「打开 Bionic」**（Bionic 默认 `autoStartOnLaunch: true`，打开后即自动恢复 `1235` 服务）<br>• 或检测到 `lms` 时提供 **「启动本地 API 服务（`lms server start`）」** / Ollama 提供 **「打开 Ollama」** |
| **`NotInstalled`** | 无任何运行中的本机宿主，也无安装痕迹 | 保持界面安静不打扰，内置本地模型为默认体验；在「设置 → 模型」保留折叠入口供手动输入自定义本机回环地址 |

---

## 4. 模型发现、协议分层与适配度匹配

### 4.1 Bionic / LM Studio 多版本 API 发现协议分层

`LmStudioFamilyProvider` 按能力从高到低探测并记录 `LocalHostApiTier`（`NativeV1` / `LegacyV0` / `OpenAiCompat`）：

1. **首选：`GET /api/v1/models`（Bionic 1.2+ / LM Studio 0.4.0+ 原生 REST API，已实机验证）**
   - 天然仅返回本机模型（彻底隔离 Bionic+ 云端模型与本地 ASR/TTS 管线模型）；
   - 仅保留 `type == "llm"`，提取 `key`、`display_name`、`publisher`、`architecture`、`format`（`"mlx"` / `"gguf"`）、`quantization.name`、`size_bytes`、`params_string`、`max_context_length`、`loaded_instances[]`（含 `engine`、`config.context_length`、`config.parallel`）、`capabilities.reasoning`（`allowed_options`、`default`）。
2. **兼容降级：`GET /api/v0/models`（LM Studio 0.3.x）**
   - 仅保留 `type == "llm" || type == "vlm"`，通过 `state == "loaded"` 识别热实例。
3. **兜底降级：`GET /v1/models`**
   - 仅在既无 v1 也无 v0 的旧版或通用本地服务上使用；同时对返回 ID 应用非文本/已知云模型过滤规则。

### 4.2 统一模型适配度分级与热实例优先排序（`LocalHostModelMatcher`）

所有宿主发现的模型统一归一化为 `LocalHostModelInfo`，由纯函数 `LocalHostModelMatcher` 评估是否适合 LiveLingo 的低延迟口语整理（含 `FactAnchorGate` 保真）与中英技术翻译：

1. **前置排除过滤（不进入候选列表）**：
   - 非文本对话类型（`embedding`、`decision`）；
   - `key` / `display_name` 命中非文本模态关键词（`music`、`inspiremusic`、`audio`、`tts`、`asr`、`whisper`、`voxtral`、`phonon`、`rerank`、`embed`、`diffusion`、`flux`、`sdxl`）或纯基座后缀（`-base` / `-pt`）。
2. **四档适配度分级**：

| 适配度等级 | 判定规则 | 计入“已有合适模型” | UI 徽章与交互 |
|---|---|---|---|
| **`CatalogVerified`（评测推荐）** | 归一化名称匹配 LiveLingo 已评测目录家族（含 MLX / GGUF 变体）：<br>• 整理：`qwen3.6-35b-a3b`、`qwen3.5-9b/4b/2b`<br>• 翻译：`qwen3.6-35b-a3b`、`index-translate-9b/2b`、`hy-mt2-7b/1.8b` | **是** | 绿色 `★ 评测推荐`；若已加载叠加 `已驻留 · 0 额外内存` |
| **`FlagshipBilingual`（双语旗舰优选）** | 匹配公认高性能中英双语指令模型家族（如 `qwen3.8`、`qwen3`、`gemma-4`、`glm-4`/`glm-5`、`deepseek-v3`/`v4`），且满足 `max_context_length` 未知或 `≥ 4096`、非强制思考（`reasoning.allowed_options` 含 `"off"` 或无强制思考）、参数 `≥ 4B`（如本机 Bionic 已加载的 `qwen/qwen3.8-27b`、`google/gemma-4-26b-a4b-qat`、`qwen3.8-flash-next`） | **是** | 绿色 `★ 推荐复用`；避免强迫已有 27B Qwen3.8 热模型的用户重复下载另一份 18 GB 模型！ |
| **`Compatible`（通用兼容可用）** | 其他指令对话 LLM，满足 `max_context_length ≥ 4096`（或未知）、非强制思考、参数 `≥ 1.5B` | **是** | 中性 `兼容可用`，可直接单选启用 |
| **`SuboptimalWarning`（不推荐但可强选）** | 命中任一风险项：强制思考无法关闭（`allowed_options == ["on"]`，如纯 `deepseek-r1`）、`max_context_length < 4096`、加载实例 `config.context_length < 4096` 或参数 `< 1.5B` | **否** | 琥珀色警告 + 具体原因；允许手动强选试用 |

3. **列表排序原则（零额外内存与低延迟第一）**：
   1. 当前已绑定模型置顶；
   2. **热实例强加权**：在 `CatalogVerified` / `FlagshipBilingual` / `Compatible` 合格模型中，**当前已驻留内存（`IsLoaded == true`）的模型优先置顶**（例如本机打开设置页，第一眼即看到 Bionic 已加载的 `Qwen3.8 27B · MLX · omlx · 已驻留 0 额外内存`）；
   3. 其次按适配度等级（`CatalogVerified` → `FlagshipBilingual` → `Compatible` → `SuboptimalWarning`）；
   4. 同级内优先 Apple Silicon `MLX` 变体与 `parallel >= 2` 多并发实例，再按参数与质量档位排序。

---

## 5. 缺失模型处理与多宿主下载引导

当出现以下任一场景时，触发**缺失模型处理流程**：

- **场景 M1（空库）**：宿主处于 `Running`，但过滤后无任何文本对话 LLM；
- **场景 M2（无合适模型）**：宿主虽有模型，但均为被排除的非文本模型（如仅含 `embedding` / `inspiremusic`）或全为 `SuboptimalWarning`，无可用合格模型；
- **场景 M3（专项获取）**：用户已有通用模型，但想额外拉取 LiveLingo 评测最佳的专用翻译/整理模型（折叠入口）；
- **场景 M4（已绑定模型失踪）**：原先绑定的 `ModelKey` 在宿主中被删除，设置页标红警示并在 Composer 浮窗弹出一步修复横幅。

### 5.1 缺失推荐清单（按本机 `HardwareProfile` 动态生成）

| 本机内存档位 | 整理 + 翻译统一推荐（仅需 1 个模型，零切换开销） | 分阶段备选推荐 |
|---|---|---|
| **≥ 32 GB（产品基线）** | `Qwen3.6 35B-A3B`（约 17.7 GB，Apple Silicon 优先 MLX 4-bit 或 GGUF `UD-IQ4_XS`） | 整理：`Qwen3.5 9B`（5.7 GB）<br>翻译：`Index-Translate 9B`（5.8 GB）或 `Hy-MT2 7B`（4.6 GB） |
| **16 GB（轻量降级档）** | `Qwen3.5 4B`（2.7 GB，兼顾整理与日常双语翻译） | 整理：`Qwen3.5 4B`（2.7 GB）<br>翻译：`Index-Translate 2B`（1.3 GB）或 `Hy-MT2 1.8B`（1.1 GB） |

### 5.2 按宿主能力自适应的三级下载引导

1. **首选（支持原生下载 API 的宿主：Bionic / LM Studio / Ollama）**：
   - **Bionic / LM Studio（`NativeV1`）**：点击 **「在 Bionic / LM Studio 中直接下载」**，调用 `POST /api/v1/models/download`（传入 [ModelCatalog.cs](../../../src/LiveLingo.Core/Models/ModelCatalog.cs) 对应 Hugging Face 仓库 URL 与量化），轮询 `GET /api/v1/models/download/status/{job_id}` 在设置页内平滑展示进度；模型直接落盘至 `~/.lmstudio/models` 共享库，LiveLingo 私有目录 **0 字节占用**；若返回 `already_downloaded` 直接刷新就绪。
   - **Ollama**：点击 **「通过 Ollama 拉取」**，调用 `POST /api/pull` 消费流式进度，落盘至 Ollama 共享模型库。
2. **通用手动引导（支持挑 MLX 变体 / 旧版宿主 / 下载 API 受限时）**：
   - **Bionic**：展示一步路径 *“在 Bionic 中打开 Settings → Local Models → Explore，搜索并下载推荐模型（Apple Silicon 推荐 MLX 4-bit），完成后点击刷新”*，提供 **「打开 Bionic」** + **「复制 `lms get` 命令」** + **「刷新模型列表」**。
   - **LM Studio**：提供 **「打开 LM Studio」**（Discover 页面）+ **「复制 `lms get` 命令」** + **「刷新」**。
   - **Ollama / 通用本地服务**：提供 **「复制 `ollama pull` 命令」** 或目标服务拉取说明 + **「刷新」**。
3. **随时可逆的旁路选项**：提供次要链接「改用 LiveLingo 内置本机模型」，随时可切回内置闭环。

---

## 6. 推理协议、思考链抑制与并发感知预热

### 6.1 按宿主分派的原生推理客户端

1. **Bionic / LM Studio 主路径：`LmStudioChatClient`（`POST /api/v1/chat`，`ApiTier == NativeV1`）**
   ```json
   {
     "model": "qwen/qwen3.8-27b",
     "system_prompt": "<LiveLingo Cleaner/Polish/Translator System Prompt>",
     "input": "<User Message>",
     "stream": true,
     "store": false,
     "temperature": 0.0,
     "top_p": 0.6,
     "top_k": 20,
     "repeat_penalty": 1.05,
     "max_output_tokens": 2200,
     "reasoning": "off"
   }
   ```
   - **精准关闭思考链（实测关键）**：仅当 `/api/v1/models` 中该模型的 `capabilities.reasoning.allowed_options` 包含 `"off"` 时传入 `"reasoning": "off"`（针对实测默认 `xhigh` / `on` 的 Qwen3.8 / Gemma 4 立即生效；对无 reasoning 配置的模型绝不传该字段以免 400 报错）；
   - **`store: false`**：防止高频整理/翻译污染 Bionic 或 LM Studio 的对话历史；
   - **原生 SSE 分流**：仅消费 `message.delta` 送入 `request.OnDelta`（喂给 [DocumentResult.cs](../../../src/LiveLingo.Core/Drafting/DocumentResult.cs) 的 `StreamingText`），彻底忽略 `reasoning.delta`；消费 `model_load.start/progress/end` 反馈冷加载进度；从 `chat.end` 提取 `model_instance_id` 与 `stats.total_output_tokens`。
   - **旧版降级**：当仅探测到 v0 / OpenAI 时，退回 `/v1/chat/completions`（带 `top_p`、`top_k`、`repeat_penalty`、`seed`、`ttl: 600`）。

2. **Ollama 原生路径：`OllamaChatClient`（`POST /api/chat`）**
   - 传入 `"think": false`（Ollama 官方思考抑制开关）、`"stream": true`、`"keep_alive": "10m"` 及 `options`（`temperature`、`top_p`、`top_k`、`repeat_penalty`、`seed`、`num_predict`）；仅消费 `message.content`（忽略 `message.thinking`）。

3. **通用 OpenAI 本地路径：`OpenAiChatClient`（`POST /v1/chat/completions`）**
   - 透传白名单采样参数，必要时附带 `chat_template_kwargs: { enable_thinking: false }`（若服务端不识别报 400 则无状态退回纯 OpenAI 负载并缓存该宿主的协议能力）；`LlmTextCleaner.Tidy` / `TidyStreaming` 的 `<think>` 剥离始终作为最后一道安全网。

### 6.2 并发感知预热与驻留协同（不抢占 Bionic 正在执行的 Agent 会话）

对接 [ModelWarmup.cs](../../../src/LiveLingo.Desktop/Services/Warmup/ModelWarmup.cs)、[LlamaWarmup.cs](../../../src/LiveLingo.Core/Inference/LlamaWarmup.cs) 与 [PolishScheduler.cs](../../../src/LiveLingo.Core/Cleanup/PolishScheduler.cs)，并利用 Bionic 实测暴露的 `loaded_instances[].config.parallel` 并发槽信息：

| 时机 | 目标阶段为 `LocalHost` 时的行为 | 设计理由 |
|---|---|---|
| **应用启动（`AtStartup`）** | **不加载**宿主 LLM（仅按原规则预热本地语音模型） | 开机零额外内存占用 |
| **浮窗唤起（`OnSummon` / `OnOutputChanged`）** | 1. 绝不启动内置 `LlamaServerHost`；<br>2. 检查宿主实时状态：<br>　• **目标模型已加载** → 零开销直接复用；<br>　• **宿主当前无任何已加载模型** → 支持显式加载的宿主（Bionic/LM Studio）可静默调用 `POST /api/v1/models/load` 消除首字等待；<br>　• **宿主已加载了其他模型（例如 Bionic 正在运行另一模型）** → **唤起阶段绝不抢载**，推迟到真正发起整理/翻译请求时由 JIT 按需加载 | 防止用户仅唤起浮窗或走「原话」输出就触发宿主 Auto-Evict 挤掉 Bionic 正在用的模型 |
| **录音期间 KV 预热（`PolishScheduler` → `WarmPromptAsync`）** | **并发安全守卫**：仅当目标模型**已驻留且 `parallel >= 2`**（如实测 Bionic `omlx` 的 `parallel: 4`）时，才在录音期间发送 1-token 轻量系统提示词预热（`store: false`）；若未加载或单槽（`parallel == 1`）则跳过 | 多槽时充分利用 `omlx` Prompt Cache 加速停麦首字；单槽时不与 Bionic 正在进行的推理争抢唯一槽位 |
| **空闲超时 / LiveLingo 退出** | **绝不卸载**宿主模型、不终止宿主进程 | 遵守共享宿主礼仪，内存回收交由宿主 TTL / `keep_alive` / Auto-Evict |

---

## 7. 与现有系统架构的无缝集成

### 7.1 三路路由（`RoutedChatClient`）

扩展 [RoutedChatClient.cs](../../../src/LiveLingo.Core/Cloud/RoutedChatClient.cs) 为清晰、单一入口的三路分发（不引入备选回退链）：

```text
IChatCompletionClient (Cleaner / Translator)
  └─ RoutedChatClient
       ├── 1. ICloudRoutes.GetAsync(role) != null     ──► OpenAiChatClient (Cloud, ModelOrigin.Cloud)
       ├── 2. ILocalHostRoutes.GetAsync(role) != null ──► LocalHostRegistry.CreateChatClient(route)
       │                                                  ├─ Bionic / LM Studio ──► LmStudioChatClient (/api/v1/chat)
       │                                                  ├─ Ollama             ──► OllamaChatClient (/api/chat)
       │                                                  └─ Generic OpenAI     ──► OpenAiChatClient (/v1/chat/completions)
       └── 3. 否则                                     ──► LocalChatClient → LlamaServerHost (ModelOrigin.BuiltInLocal)
```

三者由设置层保证严格互斥；任一选中路径失败均直接抛出对应异常并呈现在浮窗/设置页。

### 7.2 硬件顾问、下载计划与隐私展示

1. **`ComposerModelPlan`**（[ComposerModelPlan.cs](../../../src/LiveLingo.Desktop/Composer/ComposerModelPlan.cs)）：
   - `Create(...)` 增加 `cleanOnLocalHost` 与 `translateOnLocalHost`。
   - 当某阶段走 `LocalHost` 时，对应计划列表返回空集合 `[]`，Composer **不会**提示下载内置 GGUF 模型或 `llama-server`。
2. **`ModelAdvisor`**（[ModelAdvisor.cs](../../../src/LiveLingo.Core/Models/ModelAdvisor.cs)）：
   - `AssessPlan(...)` 与 `Recommend(...)` 接收 `cleanOnLocalHost` / `translateOnLocalHost`。
   - `LocalHost` 阶段在 LiveLingo 自身的内存仪表盘中计为 **`0 B` 自管驻留**，并在说明行明确展示：*“整理/翻译由本机 Bionic（`127.0.0.1:1235`）共享托管，LiveLingo 自身仅占语音模型 ~3.0 GB”*。
   - 释放自管 LLM 预算后，16 GB 或 32 GB 设备在语音阶段均能更从容推荐高质量本地语音模型（如 `Qwen3-ASR 1.7B`）。
3. **`ModelUsage` & `ModelReport`**（[ModelUsage.cs](../../../src/LiveLingo.Core/Inference/ModelUsage.cs)）：
   - 新增枚举 `ModelOrigin { BuiltInLocal, LocalHost, Cloud }`；`ModelUse` 携带 `Origin`（保留只读计算属性 `bool Cloud => Origin == ModelOrigin.Cloud`）。
   - 浮窗底部状态栏明确显示实际宿主与来源，例如 `Qwen3.8 27B · Bionic (本机)` 或 `Qwen3.5 9B · Ollama (本机)`，不与云端混淆。
4. **`ServiceFailure`**（[ServiceFailure.cs](../../../src/LiveLingo.Core/Inference/ServiceFailure.cs)）：
   - 新增 `LocalHostUnavailableException`（结构化 `LocalHostFailureKind`：`ServerNotRunning` / `AuthRequired` / `ModelNotFound` / `ContextTooSmall` / `HostError`，附带 `HostId` 与 `HostDisplayName`），纳入 `ServiceFailure.IsUnavailable`，确保调度器将其按服务级不可用收敛并在浮窗弹出精准恢复按钮。
5. **私有目录磁盘释放提示**：
   - 当用户的 `Cleaner` 与 `Translator` 均已切换至本机共享宿主（或云端），而 LiveLingo 私有存储目录仍留有未使用的内置 GGUF 模型或 `llama-server` 时，在「设置 → 模型 → 存储空间」提示可一键安全删除未使用的内置副本以立即回收 10+ GB 磁盘空间。

---

## 8. 多宿主配置 Schema 与凭据安全

在 [SettingsModel.cs](../../../src/LiveLingo.Desktop/Services/Configuration/SettingsModel.cs) 中新增与 `CloudSettings` 并列的 `LocalHostSettings`（Schema 纯增量扩展，完全向后兼容老版本 `settings.json`）：

```csharp
/// <summary>One stage's binding to a discovered or custom loopback local model host.</summary>
public sealed class LocalHostStageBinding
{
    /// <summary>Stable host identifier: "bionic", "lmstudio", "ollama", or "custom:&lt;id&gt;".</summary>
    public string HostId { get; set; } = "";

    /// <summary>Last known loopback base URL (e.g. http://127.0.0.1:1235); re-resolved dynamically via HostId when auto-detect is on.</summary>
    public string BaseUrl { get; set; } = "";

    /// <summary>Model key served by the host (e.g. "qwen/qwen3.8-27b").</summary>
    public string ModelKey { get; set; } = "";

    [JsonIgnore] public bool IsBound => !string.IsNullOrEmpty(HostId) && !string.IsNullOrEmpty(ModelKey);

    public LocalHostStageBinding DeepClone() => new() { HostId = HostId, BaseUrl = BaseUrl, ModelKey = ModelKey };
}

/// <summary>User-added custom loopback local OpenAI-compatible server (e.g. llama-server or mlx_lm.server on a custom port).</summary>
public sealed class CustomLocalHostEntry
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string BaseUrl { get; set; } = ""; // Strictly loopback only

    public CustomLocalHostEntry DeepClone() => new() { Id = Id, Name = Name, BaseUrl = BaseUrl };
}

public partial class LocalHostSettings : ObservableObject
{
    /// <summary>Per-host optional API token stored in OS secret store under "local-host-token:{hostId}".</summary>
    public static string TokenSecretIdFor(string hostId) => $"local-host-token:{hostId}";

    /// <summary>Automatically discover running local model hosts (Bionic, LM Studio, Ollama, etc.).</summary>
    [ObservableProperty] private bool _autoDetect = true;

    /// <summary>Optional loopback URL overrides keyed by well-known HostId ("bionic", "lmstudio", "ollama").</summary>
    [ObservableProperty] private Dictionary<string, string> _portOverrides = new(StringComparer.Ordinal);

    /// <summary>User-added custom loopback local servers.</summary>
    [ObservableProperty] private List<CustomLocalHostEntry> _customHosts = [];

    /// <summary>Binding for cleanup/polish stage when routed to a local host.</summary>
    [ObservableProperty] private LocalHostStageBinding _cleaner = new();

    /// <summary>Binding for translation stage when routed to a local host.</summary>
    [ObservableProperty] private LocalHostStageBinding _translator = new();

    [JsonIgnore] public bool Clean => Cleaner.IsBound;
    [JsonIgnore] public bool Translate => Translator.IsBound;
}
```

**路由互斥写入规则（集中于 `SettingsViewModel`）**：
- 为某阶段选择 `LocalHost` 模型 → 清空该阶段的 `Cloud.*EndpointId`，写入 `LocalHost.Cleaner` / `LocalHost.Translator`；
- 为某阶段选择 `Cloud` 端点（通过隐私确认后）→ 清空该阶段的 `LocalHost` 绑定，写入 `Cloud.*EndpointId`；
- 为某阶段选择 `BuiltInLocal` 模型 → 同时清空该阶段的 `Cloud.*EndpointId` 与 `LocalHost` 绑定，更新 `Composer.*Model`。

**端口动态重解析**：当 `AutoDetect == true` 且 `HostId == "bionic"` 时，即使 Bionic 日后修改了 `http-server-config.json` 中的监听端口，`SettingsLocalHostRoutes` 会优先采用 `LocalHostCoordinator` 最新发现的 Bionic 回环地址，避免端口变更导致失效。

**安全规范**：
- 所有 `BaseUrl`（含 `PortOverrides` 与 `CustomHosts`）强制校验 `uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback`，拒绝任何非回环主机名或 IP；
- 可选的本机 API Token 仅通过 `ISecretStore`（macOS Keychain / Windows Credential Manager）以 `LocalHostSettings.TokenSecretIdFor(hostId)` 持久化，绝不落盘到 `settings.json` 或日志。

---

## 9. 模块划分与界面交互设计

### 9.1 新增与变更组件清单

| 层级 | 文件 / 组件 | 职责 |
|---|---|---|
| **Core** | `src/LiveLingo.Core/LocalHost/LocalHostContracts.cs` | `LocalHostKind`（`Bionic` / `LmStudio` / `Ollama` / `OpenAiLocal`）、`LocalHostStatus`、`LocalHostApiTier`、`LocalHostModelInfo`（含 `Engine`、`Parallel`、`Format`、`ReasoningOptions`）、`HostModelSuitability`、`LocalHostDownloadJob`、`LocalHostUnavailableException`、`LocalHostInstance` |
| **Core** | `src/LiveLingo.Core/LocalHost/LocalHostRoute.cs` | 严格回环路由值对象（`HostId`、`Kind`、`Endpoint`、`ModelKey`、`ApiTier`、`Capabilities`、`Parallel`、`BearerToken`） |
| **Core** | `src/LiveLingo.Core/LocalHost/ILocalHostRoutes.cs` | 阶段路由解析接口与 `NoLocalHostRoutes` 空实现 |
| **Core** | `src/LiveLingo.Core/LocalHost/ILocalHostProvider.cs` & `LocalHostRegistry.cs` | 统一多宿主自发现注册表（并行 800 ms 探测、按 `host:port` 去重、按 `HostKind` 分派聊天客户端与可选下载器 `ILocalHostModelDownloader`） |
| **Core** | `src/LiveLingo.Core/LocalHost/Providers/LmStudioFamilyProvider.cs` & `LmStudioChatClient.cs` | **P0 主力**：Bionic（`1235`）/ LM Studio（`1234`）/ `llmster` 发现、`/api/v1/models`（严拒 `http-server.json` 内部端口与 `/v1/models` 混杂项）、`/api/v1/chat` SSE（`store: false` + 条件 `reasoning: "off"` + `message.delta` 分流）、`/api/v1/models/load` 与 `/api/v1/models/download` |
| **Core** | `src/LiveLingo.Core/LocalHost/Providers/OllamaHostProvider.cs` & `OllamaChatClient.cs` | **P1 原生**：Ollama `11434` 双接口发现（`/api/tags` + `/api/ps` 热实例）、`/api/chat`（`"think": false` + `keep_alive: "10m"`）与 `/api/pull` 流式拉取 |
| **Core** | `src/LiveLingo.Core/LocalHost/Providers/GenericOpenAiLocalProvider.cs` | **P1 通用**：常用固定端口（`8080` + `/props`、`1337`、`8000`）与用户自定义回环 URL 发现及 OpenAI 兼容推理 |
| **Core** | `src/LiveLingo.Core/LocalHost/LocalHostModelMatcher.cs` | 纯函数模块：非文本模态黑名单过滤、四档适配度评分、热实例 + `MLX` + 多并发槽排序、按 `HardwareProfile` 生成缺失推荐清单 |
| **Core** | `RoutedChatClient.cs` / `LlamaWarmup.cs` / `ModelUsage.cs` / `ModelAdvisor.cs` / `ServiceFailure.cs` | 最小增量对接三路路由、并发感知礼貌预热、`ModelOrigin`、自管内存扣减与故障归类 |
| **Desktop** | `src/LiveLingo.Desktop/Services/LocalHost/LocalHostEnvironmentProbe.cs` | 跨平台（macOS / Windows）读取 Bionic / LM Studio 配置文件（`apps/bionic/.internal/http-server-config.json`、`app-install-location.json` 等）、探测 `lms` / `ollama` 安装痕迹与安全唤起应用 |
| **Desktop** | `src/LiveLingo.Desktop/Services/LocalHost/SettingsLocalHostRoutes.cs` | `ILocalHostRoutes` 实现，组合 `ISettingsService` + `ISecretStore` + `LocalHostCoordinator` 实时快照 |
| **Desktop** | `src/LiveLingo.Desktop/Services/LocalHost/LocalHostCoordinator.cs` | 探测缓存（15 s TTL）、多宿主下载进度轮询、状态变更事件推送 |
| **Desktop** | `SettingsModel.cs` / `SettingsViewModel.cs` / `ComposerModelPlan.cs` / `ModelWarmup.cs` | 持久化、三源互斥切换、下载计划豁免与并发感知预热 |
| **Desktop Web** | `SettingsViewState.cs` / `WebIntent.cs` / `SettingsIntents.cs` / `Assets/app/31-settings.js` / `i18n/*.json` | 设置页「本机共享宿主」卡片组（支持多宿主切换/折叠）、缺失引导卡、自定义回环端口表单与 Composer 故障恢复横幅 |

### 9.2 「设置 → 模型」界面结构（以本机实测 Bionic 为例）

延续现有统一单选列表范式（[31-settings.js](../../../src/LiveLingo.Desktop/Web/Assets/app/31-settings.js)），在 **整理** 与 **翻译** 标签页内自然分为三组（单选按钮跨组互斥）：

```text
┌─────────────────────────────────────────────────────────────────────────┐
│ 硬件与隐私横幅                                                          │
│ • [全部本机处理] 数据不离开本机（整理/翻译已接入本机 Bionic 共享宿主）  │
│ • 首次发现引导条（未绑定时显示）：                                       │
│   “检测到本机 Bionic 已常驻 Qwen3.8 27B (16.1 GB, MLX)，可直接零额外内存 │
│    复用，省去重复下载”                              [一键复用] [稍后再说] │
└─────────────────────────────────────────────────────────────────────────┘

┌─ 1. 本机共享宿主（自动发现）                     [刷新] [+ 添加本机端口] ─┐
│  ▼ Bionic · 127.0.0.1:1235  [● 已连接] [omlx · 1 个已驻留]  [端口/Token] │
│    ● Qwen3.8 27B       [★ 推荐复用] [MLX] [已驻留 · 0 额外内存] [4 并发] │
│      16.1 GB · 4bit · 262K ctx · 推理时自动关闭思考 · 与 Bionic 共享内存 │
│    ○ Gemma 4 26B A4B   [★ 推荐复用] [MLX] [已下载 · 按需加载]   [启用]   │
│      15.6 GB · 4bit · 262K ctx · 免重复下载                              │
│    ○ Qwen3.8 Flash Next [★ 推荐复用] [GGUF] [已下载 · 按需加载] [启用]   │
│      5.0 GB · Q8_0 · 262K ctx · 轻量高速                                 │
│    ▸ 展开其余 2 个模型（Gemma 4 31B、Gemma 4 12B QAT）                   │
│                                                                          │
│  （若本机同时运行 Ollama :11434 等其他宿主，自动以第二个折叠分组并列展示）│
│  ▸ Ollama · 127.0.0.1:11434  [● 已连接] [2 个可用模型]                   │
│                                                                          │
│  （当已连接宿主无合适模型时展示缺失引导卡片）                             │
│  ┌────────────────────────────────────────────────────────────────────┐  │
│  │ Bionic 中暂无适合「翻译」的模型                                    │  │
│  │ 建议在 Bionic 中下载以下模型，多软件共享、不重复占磁盘与内存：      │  │
│  │  • Qwen3.6 35B-A3B（统一旗舰，17.7 GB）       [在 Bionic 中下载]   │  │
│  │  • Index-Translate 9B（旗舰翻译，5.8 GB）     [在 Bionic 中下载]   │  │
│  │  [打开 Bionic 手动选择]  [复制 lms get 命令]  [刷新列表]           │  │
│  └────────────────────────────────────────────────────────────────────┘  │
└──────────────────────────────────────────────────────────────────────────┘

┌─ 2. 本机内置模型（LiveLingo 独立托管） ──────────────────────────────────┐
│  ○ #1 Qwen3.6 35B-A3B · 统一旗舰   ...（现有内置模型卡片保持不变）        │
└──────────────────────────────────────────────────────────────────────────┘

┌─ 3. 云端模型（需隐私授权） ──────────────────────────────────────────────┐
│  ...（现有云端端点卡片保持不变）                                          │
└──────────────────────────────────────────────────────────────────────────┘
```

**新增 `WebIntent` 契约**（严格受控闭集，沿用 [WebIntent.cs](../../../src/LiveLingo.Desktop/Web/WebIntent.cs) 校验规范）：
- `refreshLocalHosts`：并行重新探测所有本机宿主与模型列表；
- `configureLocalHost`（`hostId`, `baseUrl`, `apiToken`, `clearToken`）：配置回环地址覆盖或保存/清除 Token；
- `addCustomLocalHost` / `removeCustomLocalHost`：增删自定义回环本机服务（严格校验 loopback）；
- `selectLocalHostModel`（`stage`, `hostId`, `modelKey`）：绑定整理或翻译阶段到指定宿主的指定模型；
- `applyHotLocalHostModel`（`hostId`, `modelKey`）：首次发现引导条的「一键复用」（将整理与翻译双阶段同时绑定到宿主已加载热模型，立即生效）；
- `downloadLocalHostModel`（`hostId`, `catalogModelId`）：触发宿主原生下载 API（仅允许 `ModelCatalog` 白名单内推荐模型，防止任意 URL 注入）；
- `startLocalHostServer`（`hostId`）：用户显式点击后调用本地 `lms server start`；
- `openLocalHostApp`（`hostId`）：拉起已安装的 Bionic / LM Studio / Ollama 应用。

---

## 10. 分阶段实施路线与验证计划

| 阶段 | 交付内容 | 验证标准 |
|---|---|---|
| **Phase 1：Core 多宿主协议、匹配与三路路由** | 实现 `LiveLingo.Core/LocalHost/*`（`LocalHostRegistry`、`LmStudioFamilyProvider` + `LmStudioChatClient`、`OllamaHostProvider` + `OllamaChatClient`、`GenericOpenAiLocalProvider`、`LocalHostModelMatcher`），扩展 `RoutedChatClient` / `LlamaWarmup` / `ModelUsage` / `ModelAdvisor` / `ServiceFailure` | 新增 `tests/LiveLingo.Core.Tests/LocalHost/` 单元测试：覆盖回环安全校验、忽略 `http-server.json` 内部端口、`/api/v1/models` 过滤 `embedding`/`inspiremusic` 及隔离云模型、`FlagshipBilingual` 热模型排序、`reasoning: "off"` 条件发送与 SSE `reasoning.delta` 隔离、Ollama `/api/ps` 热识别与 `"think": false`、多宿主并行发现与 `host:port` 去重、三路路由互斥且无静默回退 |
| **Phase 2：Desktop 环境探测、配置与并发感知预热** | 实现 `LocalHostEnvironmentProbe`（读取 `apps/bionic/.internal/http-server-config.json` 与 `app-install-location.json`）、`SettingsLocalHostRoutes`、`LocalHostCoordinator`、`LocalHostSettings`、`ComposerModelPlan` 与 `ModelWarmup` 并发感知预热 | 单元测试覆盖四态状态机、Token 仅存 `ISecretStore`、端口变更自动重解析、单槽/他模占槽时不抢载、多槽热模型允许 KV 预热、`ComposerModelPlan` 免触发内置下载 |
| **Phase 3：Settings & Composer Web UI** | 更新 `SettingsViewState`、`WebIntent`、`SettingsIntents`、`31-settings.js`、`i18n/zh-CN.json` & `en-US.json`、首次发现引导条、自定义本机端口表单与 Composer 故障恢复横幅 | WebIntent 契约单测 + 双引擎浏览器测试新增用例（Bionic 热模型一键复用、多宿主切换、缺模委托下载进度条、401 Token 输入、宿主未启动提示、隐私徽章保持全本机） |
| **Phase 4：真机联调探针（优先 Bionic 1.2.2+3）** | 在 `tests/LiveLingo.Core.Tests/Probes/` 新增环境门控探针（默认跳过，真机运行 Bionic / LM Studio / Ollama 时手动触发） | 在本机 Bionic (`127.0.0.1:1235`) 上实测 `qwen/qwen3.8-27b`（`omlx`、`parallel: 4`）：验证 `"reasoning": "off"` 真实生效（`reasoning_output_tokens == 0`）、TTFT 与流式润色、`FactAnchorGate` / `FidelityGate` 通过率、LiveLingo 内存增量 ≈ 0 GB，以及与 Bionic 并发对话互不干扰 |

---

## 11. 剩余待验证项与已知风险

> 注：本机 macOS 上 Bionic 1.2.2+3 的端口配置（`1235`）、内部 RPC 端口隔离（`http-server.json`）、`/api/v1/models` 字段结构、`omlx` 引擎与 `parallel: 4`、`reasoning.default: "xhigh"` 及 `lms server status --json` 已于 2026-11-28 完成真机实测验证。以下为实施阶段仍需补测的边界项：

1. **Windows 平台 Bionic 配置文件路径**：需在 Windows 真机确认 Bionic 的 `http-server-config.json` 与 `app-install-location.json` 是否同样位于 `%USERPROFILE%\.lmstudio\apps\bionic\.internal\`，以及 `Bionic.exe` 默认安装目录。
2. **Bionic 上 `POST /api/v1/chat` 对 `omlx` 引擎的参数兼容性与实际延迟**：需在 Phase 4 探针中实测 `qwen/qwen3.8-27b`（`omlx` MLX 4-bit）对 `"reasoning": "off"`、`"store": false`、`top_k`、`repeat_penalty` 的实际支持情况，确认不存在忽略或报错字段；并验证 `FactAnchorGate` / `FidelityGate` 在 Qwen3.8 / Gemma 4 上的通过率。
3. **Bionic 上 `POST /api/v1/models/download` 的权限与国内网络表现**：需实测 Bionic `:1235` 是否开放 `/api/v1/models/download` 以及对非标准量化名（如 `UD-IQ4_XS`）和 MLX 仓库的匹配行为；若不可用或网络受限，确保平滑退化为「打开 Bionic 手动下载 / 复制 `lms get`」。
4. **单槽或 Auto-Evict 开启时的双模型切换抖动**：若用户为 `Cleaner` 和 `Translator` 绑定了宿主中**两个不同**的模型且宿主开启了 `Only Keep Last JIT Loaded Model`，频繁在整理与翻译间切换会引起反复换入换出。**缓解策略**：UI 优先推荐两阶段复用同一个热模型（例如 Bionic 已常驻的 `qwen/qwen3.8-27b`），并在检测到双阶段绑定同一宿主的不同模型时展示轻提示。
