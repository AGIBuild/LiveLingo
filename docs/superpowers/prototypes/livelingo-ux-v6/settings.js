(function () {
  "use strict";

  const esc = s => String(s ?? "").replace(/[&<>"']/g, ch => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[ch]);
  const ic = name => `<svg class="ic" aria-hidden="true"><use href="#i-${name}"/></svg>`;

  const BUILTIN_PRESETS = [
    {
      id: "ai-task",
      name: "AI 任务指令",
      pill: "润色 · AI 任务指令",
      icon: "terminal",
      key: "1",
      builtIn: true,
      enabled: true,
      gate: "FactAnchor",
      desc: "倒叙归位、剪除改口废案、分离硬性护栏（短话免套壳）",
      defaultInstructions: "你是 AI 任务指令编译编辑。将发散口述提炼为高信噪比、无歧义的下游 AI 指令：\n1. 废案剪枝：说话人中途自我否定的旧方案与旧数值彻底剔除；\n2. 目标置顶：把段尾才说出的真实动作提炼到最前；\n3. 正负护栏分离：所有「不要改/禁止/只改」独立列出；\n4. 自适应篇幅：1–2 句短纠偏直接输出精炼段落，严禁硬套空标题。"
    },
    {
      id: "natural",
      name: "口语顺句",
      pill: "整理原话",
      icon: "chat",
      key: "2",
      builtIn: true,
      enabled: true,
      gate: "Verbatim",
      desc: "接碎句、去口头语与改口残片，保留真人说话语气",
      defaultInstructions: "1. 把停顿切碎的半句接成完整句，删去口头语（呃、嗯、那个、就是）、重复与自我改口前的废话；\n2. 结合上下文修正同音字与技术名词误识别；\n3. 保持说话人自然口吻，不加标题列表，不改成公文腔。"
    },
    {
      id: "formal",
      name: "得体书面",
      pill: "润色 · 得体书面",
      icon: "briefcase",
      key: "3",
      builtIn: true,
      enabled: true,
      gate: "Verbatim",
      desc: "邮件、PR 评论、克制书面段落（零公文黑话）",
      defaultInstructions: "1. 去除口语助词与松散句式，改写为简洁、克制、得体的书面段落；\n2. 完整保留第一人称立场、条件限定、生效数字与代码标识符；\n3. 严禁使用「赋能、抓手、闭环、综上所述」等公文套话。"
    },
    {
      id: "todo",
      name: "待办与派单",
      pill: "润色 · 待办与派单",
      icon: "list-check",
      key: "4",
      builtIn: true,
      enabled: true,
      gate: "FactAnchor",
      desc: "过滤闲聊废案，提取 - [ ] 行动项、负责人与条件",
      defaultInstructions: "从口述中剥离背景铺垫与已否决设想，只提取明确可执行的行动项：\n- 格式：- [ ] 动词开头事项（如有明确提及负责人或前置条件，在括号内注明）\n- 原话未提及截止时间或负责人时严禁编造。"
    },
    {
      id: "report",
      name: "汇报与纪要",
      pill: "润色 · 汇报与纪要",
      icon: "file-text",
      key: "5",
      builtIn: true,
      enabled: true,
      gate: "FactAnchor",
      desc: "结论先行（BLUF），归纳关键决策与下一步行动",
      defaultInstructions: "按金字塔结论先行（BLUF）将流水账重组为决策简报：\n- ### 核心结论（首句直出当前状态或拍板结果）\n- ### 关键决策与技术边界（保留全部生效数字、组件名与正反条件）\n- ### 下一步行动（明确谁在什么条件下跟进）"
    }
  ];

  const STARTER_TEMPLATES = [
    {
      id: "bug-report",
      name: "Bug 缺陷与复现",
      desc: "提炼复现步骤、预期行为与实际报错",
      gate: "FactAnchor",
      instructions: "将口述整理为结构化缺陷报告：\n- ### 复现步骤（按顺序列出触发操作与参数）\n- ### 预期行为\n- ### 实际现象与影响范围\n完整保留所有数值、错误码与代码组件名，未提及的环境信息严禁编造。"
    },
    {
      id: "spec-ac",
      name: "需求规格（Spec/AC）",
      desc: "提炼解决的问题、非目标边界与验收标准",
      gate: "FactAnchor",
      instructions: "面向 Spec-Driven AI 研发，将口述整理为轻量规格说明：\n- ### 解决的问题\n- ### 变更范围与非目标（Out of Scope）\n- ### 验收标准（仅基于原话已声明的条件，不凭空扩写功能）。"
    },
    {
      id: "git-commit",
      name: "Commit 说明",
      desc: "生成简明提交首行与核心改动要点",
      gate: "FactAnchor",
      instructions: "将口述提炼为规范的代码提交说明：\n- 首行：简明概括本次改动意图（不超过 50 字）\n- 空一行后用 `- ` 列出核心变更点与兼容边界（保留精确接口名与数值）。"
    }
  ];

  const BUILTIN_DEV_TERMS = [
    "Agent", "Skill", "Prompt", "API", "PR", "VS Code", "Cursor", "Claude Code",
    "MCP", "Docker", "Kubernetes", "Redis", "PostgreSQL", "TypeScript", "GraphQL", "CI/CD"
  ];

  const DEFAULT_POLISH_PROMPT = "1. 把停顿切碎的半句接成完整句，去掉口头语（呃、嗯、那个、就是）和重复，修正常见同音技术词。\n2. 保持说话人的第一人称语气、逻辑否定、条件限定与全部数字，不扩写事实，不改成套话。";

  /* ==================== CENTRAL SETTINGS STORE ==================== */
  const state = {
    section: "general",
    platform: "mac",
    general: {
      hotkey: "Ctrl+Alt+T",
      hotkeyRecording: false,
      commitKey: "Enter",
      language: "zh-CN",
      theme: "Light",
      reduceMotion: false,
      rememberChoices: true,
      recentCount: 3,
      apps: [
        { id: "cursor", name: "Cursor", abbr: "Cu", choice: "polish:ai-task", used: "2 分钟前" },
        { id: "linear", name: "Linear", abbr: "Li", choice: "polish:todo", used: "18 分钟前" },
        { id: "feishu", name: "飞书文档", abbr: "飞", choice: "polish:report", used: "1 小时前" },
        { id: "slack", name: "Slack", abbr: "Sl", choice: "default", used: "昨天" }
      ]
    },
    voice: {
      microphone: "MacBook Pro 麦克风（内置）",
      microphones: ["系统默认", "MacBook Pro 麦克风（内置）", "AirPods Pro（免提）", "Shure MV7 USB"],
      testingMic: false,
      autoDictate: true,
      preloadSpeech: true,
      cleanup: true,
      defaultOutput: "Clean",
      direction: "English"
    },
    polish: {
      defaultPolishMode: "natural",
      smartHostPolish: true,
      polishPrompt: DEFAULT_POLISH_PROMPT,
      polishPromptInvalid: false,
      presets: BUILTIN_PRESETS.map(p => ({ ...p, instructions: p.defaultInstructions }))
    },
    glossary: {
      tab: "corrections", // "corrections" | "terms"
      builtInGlossary: true,
      corrections: [
        { id: "c1", spoken: "Scale", canonical: "Skill" },
        { id: "c2", spoken: "六零九", canonical: "LiveLingo" },
        { id: "c3", spoken: "卡夫卡", canonical: "Kafka" }
      ],
      terms: [
        { id: "t1", source: "缓存穿透", target: "cache penetration" },
        { id: "t2", source: "CacheService", target: "CacheService" },
        { id: "t3", source: "熔断降级", target: "circuit breaking" }
      ]
    },
    models: {
      stageTab: "speech", // "speech" | "cleaner" | "translator" | "storage"
      sourceTab: { speech: "local", cleaner: "local", translator: "local" }, // "local" | "host" | "cloud"
      hardware: {
        summary: "Apple M3 Max · 36 GB 统一内存 · Metal GPU 加速",
        budgetGb: 20.0
      },
      hotHostDismissed: false,
      localHostRefreshing: false,
      addingCustomHost: false,
      downloads: [], // { id, stage, name, category, percent, totalGb }
      active: {
        speech: { source: "local", id: "qwen3-asr-1.7b" },
        cleaner: { source: "local", id: "qwen3.5-9b" },
        translator: { source: "local", id: "index-translate-9b" }
      },
      localCatalog: {
        speech: [
          {
            id: "qwen3-asr-1.7b",
            name: "Qwen3-ASR 1.7B · 旗舰本地识别",
            summary: "Qwen 开源最强本地 ASR（30 语种 + 22 方言），支持长从句语境消歧与中英热词偏置，免费离线",
            quality: 3, speed: 2, diskGb: 1.9, ramGb: 2.3,
            fit: "recommended", fitBadge: "★ 本机最佳匹配", installed: true
          },
          {
            id: "qwen3-asr-0.6b",
            name: "Qwen3-ASR 0.6B · 均衡高精度识别",
            summary: "支持研发术语与口音纠错热词偏置，低内存负载下依然稳健",
            quality: 2, speed: 3, diskGb: 0.9, ramGb: 1.2,
            fit: "supported", fitBadge: "可流畅运行", installed: true
          },
          {
            id: "x-asr-offline",
            name: "X-ASR Offline · 轻快识别",
            summary: "非自回归轻量架构，常驻省约 0.6 GB 内存，适合重度后台任务场景降级使用",
            quality: 2, speed: 3, diskGb: 0.5, ramGb: 0.7,
            fit: "supported", fitBadge: "可流畅运行", installed: false
          }
        ],
        cleaner: [
          {
            id: "qwen3.6-35b-a3b-cleaner",
            name: "Qwen3.6 35B-A3B · 旗舰全能合一（MoE）",
            summary: "每 token 仅激活 3B 参数兼顾高速与 35B 级语义理解；与翻译同选时共用单实例、仅占单份内存",
            quality: 3, speed: 3, diskGb: 19.2, ramGb: 20.5, sharedKey: "qwen3.6-35b",
            fit: "heavy", fitBadge: "高负载 · 共用单实例", fitDetail: "与翻译共用权重时驻留约 20.5 GB，建议 36GB+ 或独占使用", installed: false
          },
          {
            id: "qwen3.5-9b",
            name: "Qwen3.5 9B · 旗舰深层润色",
            summary: "32 GB 默认推荐：长篇多段口述的跨句逻辑缝合、改口废案剪枝与同音技术词还原能力强",
            quality: 3, speed: 2, diskGb: 5.7, ramGb: 6.4,
            fit: "recommended", fitBadge: "★ 本机最佳匹配", installed: true
          },
          {
            id: "qwen3.5-4b",
            name: "Qwen3.5 4B · 标准均衡整理",
            summary: "准确执行去口头语、自我纠正与全文润色，内存与速度均衡",
            quality: 2, speed: 3, diskGb: 2.8, ramGb: 3.3,
            fit: "supported", fitBadge: "可流畅运行", installed: true
          },
          {
            id: "qwen3.5-2b",
            name: "Qwen3.5 2B · 轻快基础整理",
            summary: "低内存与 CPU 友好，提供快速断句标点与基础语气词清理（结构化多场景建议 4B/9B）",
            quality: 1, speed: 3, diskGb: 1.5, ramGb: 1.9,
            fit: "supported", fitBadge: "可流畅运行", installed: false
          },
          {
            id: "qwen3.5-72b-heavy",
            name: "Qwen3.5 72B · 超大参数科研版",
            summary: "面向 64 GB+ 工作站的专业长文精校模型",
            quality: 3, speed: 1, diskGb: 42.0, ramGb: 46.0,
            fit: "unsupported", fitBadge: "当前设备不支持", fitDetail: "需要至少 64 GB 内存（当前 36 GB）", installed: false
          }
        ],
        translator: [
          {
            id: "qwen3.6-35b-a3b-translator",
            name: "Qwen3.6 35B-A3B · 旗舰全能合一（MoE）",
            summary: "35B 级复杂语境中英互译且每 token 仅激活 3B 参数；与整理同选时共用单实例、切换零等待",
            quality: 3, speed: 3, diskGb: 19.2, ramGb: 20.5, sharedKey: "qwen3.6-35b",
            fit: "heavy", fitBadge: "高负载 · 共用单实例", fitDetail: "与整理共用权重时仅占单份磁盘与内存", installed: false
          },
          {
            id: "index-translate-9b",
            name: "Index-Translate 9B · 旗舰专精翻译",
            summary: "32 GB 默认推荐：基于 Qwen3.5 深度专训，术语遵循、代码版式与长文连贯性全面领先",
            quality: 3, speed: 2, diskGb: 5.6, ramGb: 6.3,
            fit: "recommended", fitBadge: "★ 本机最佳匹配", installed: true
          },
          {
            id: "hy-mt2-7b",
            name: "Hy-MT2 7B · 高质量翻译",
            summary: "腾讯混元专用翻译模型，技术长句语义准确，代码与结构化格式通过率达 94%",
            quality: 3, speed: 2, diskGb: 4.6, ramGb: 5.2,
            fit: "supported", fitBadge: "可流畅运行", installed: false
          },
          {
            id: "index-translate-2b",
            name: "Index-Translate 2B · 轻快高质翻译",
            summary: "新一代 2B 专用翻译模型，兼顾 0.1 秒级响应与优于 1.8B 的术语约束能力",
            quality: 2, speed: 3, diskGb: 1.5, ramGb: 1.9,
            fit: "supported", fitBadge: "可流畅运行", installed: true
          },
          {
            id: "hy-mt2-1.8b",
            name: "Hy-MT2 1.8B · 极速轻量翻译",
            summary: "首字约 0.1 秒，最低内存占用，适合高负载场景手动降级使用",
            quality: 1, speed: 3, diskGb: 1.2, ramGb: 1.5,
            fit: "supported", fitBadge: "可流畅运行", installed: false
          }
        ]
      },
      localHosts: [
        {
          hostId: "lmstudio",
          name: "LM Studio",
          address: "127.0.0.1:1234",
          status: "running",
          statusBadge: "已连接",
          isCustom: false,
          models: [
            { key: "qwen3.5-9b-instruct", name: "qwen3.5-9b-instruct", meta: "Q4_K_M · 32K ctx", suitability: "verified", suitabilityBadge: "★ 评测推荐", isLoaded: true, loadedBadge: "已驻留 · 0 额外内存" },
            { key: "qwen3.6-35b-a3b", name: "qwen3.6-35b-a3b-iq4xs", meta: "MoE · 64K ctx", suitability: "flagship", suitabilityBadge: "★ 推荐复用", isLoaded: false, loadedBadge: "已下载 · 按需加载" }
          ]
        },
        {
          hostId: "ollama",
          name: "Ollama",
          address: "127.0.0.1:11434",
          status: "running",
          statusBadge: "已连接",
          isCustom: false,
          models: [
            { key: "qwen3:8b", name: "qwen3:8b", meta: "Q4_K_M · 16K ctx", suitability: "compatible", suitabilityBadge: "兼容可用", isLoaded: false, loadedBadge: "已下载 · 按需加载" }
          ]
        },
        {
          hostId: "bionic",
          name: "Bionic",
          address: "127.0.0.1:1235",
          status: "stopped",
          statusBadge: "已安装 · 服务未启动",
          isCustom: false,
          models: []
        }
      ],
      cloudEndpoints: {
        speech: [
          {
            id: "cloud-qwen-asr",
            name: "通义百炼 · Qwen-Audio-3.1-ASR",
            url: "https://dashscope.aliyuncs.com/api/v1",
            host: "dashscope.aliyuncs.com",
            model: "qwen-audio-3.1-asr-flash-message",
            hasKey: true
          }
        ],
        text: [
          {
            id: "cloud-foundry",
            name: "Foundry · 东亚",
            url: "https://livelingo-eastasia.openai.azure.com/openai/v1",
            host: "livelingo-eastasia.openai.azure.com",
            model: "gpt-4o-mini",
            hasKey: true
          },
          {
            id: "cloud-openai",
            name: "OpenAI",
            url: "https://api.openai.com/v1",
            host: "api.openai.com",
            model: "gpt-4.1-mini",
            hasKey: true
          }
        ]
      },
      legacyModels: { count: 1, sizeGb: 1.4 }
    },
    about: {
      version: "v2.6.0",
      updateState: "idle", // "idle" | "checking" | "latest"
      updateCheckedAt: ""
    }
  };

  let seq = 100;
  const listeners = new Set();
  const notify = () => listeners.forEach(fn => fn());

  /* ==================== HELPERS & COMPUTED ==================== */
  function presetById(id) {
    return state.polish.presets.find(p => p.id === id) ?? state.polish.presets[0];
  }

  function activeModelMeta(stage) {
    const sel = state.models.active[stage];
    if (sel.source === "local") {
      const m = state.models.localCatalog[stage].find(x => x.id === sel.id) ?? state.models.localCatalog[stage][0];
      return { name: m.name.split(" · ")[0], fullName: m.name, source: "local", sourceLabel: "本机离线", ramGb: m.ramGb, raw: m };
    }
    if (sel.source === "host") {
      const host = state.models.localHosts.find(h => h.hostId === sel.hostId);
      const model = host?.models.find(m => m.key === sel.modelKey);
      return {
        name: model?.name ?? sel.modelKey,
        fullName: `${model?.name ?? sel.modelKey}（${host?.name ?? "本机宿主"}）`,
        source: "host",
        sourceLabel: `${host?.name ?? "宿主"} · 本机`,
        ramGb: 0,
        hostName: host?.name ?? "本机宿主"
      };
    }
    const list = stage === "speech" ? state.models.cloudEndpoints.speech : state.models.cloudEndpoints.text;
    const ep = list.find(e => e.id === sel.id) ?? list[0];
    return {
      name: ep?.model ?? "云端模型",
      fullName: `${ep?.name ?? "云端"} · ${ep?.model ?? ""}`,
      source: "cloud",
      sourceLabel: `云端 · ${ep?.name ?? ""}`,
      ramGb: 0,
      endpoint: ep
    };
  }

  function computeResidencyGb() {
    const seenShared = new Set();
    let total = 0;
    for (const stage of ["speech", "cleaner", "translator"]) {
      const sel = state.models.active[stage];
      if (sel.source !== "local") continue;
      const m = state.models.localCatalog[stage].find(x => x.id === sel.id);
      if (!m) continue;
      if (m.sharedKey) {
        if (seenShared.has(m.sharedKey)) continue;
        seenShared.add(m.sharedKey);
      }
      total += m.ramGb;
    }
    return Math.round(total * 10) / 10;
  }

  function computeCloudStages() {
    const names = [];
    if (state.models.active.speech.source === "cloud") names.push("语音识别");
    if (state.models.active.cleaner.source === "cloud") names.push("整理与润色");
    if (state.models.active.translator.source === "cloud") names.push("翻译");
    return names;
  }

  function computeHostStages() {
    const names = [];
    if (state.models.active.cleaner.source === "host") names.push("整理与润色");
    if (state.models.active.translator.source === "host") names.push("翻译");
    return names;
  }

  function computeStorageStats() {
    const seenShared = new Set();
    let speechGb = 0, cleanerGb = 0, translatorGb = 0, idleGb = 0;
    const items = [];
    const stageLabels = { speech: "语音识别", cleaner: "整理与润色", translator: "翻译" };
    for (const stage of ["speech", "cleaner", "translator"]) {
      for (const m of state.models.localCatalog[stage]) {
        if (!m.installed) continue;
        if (m.sharedKey && seenShared.has(m.sharedKey)) continue;
        if (m.sharedKey) seenShared.add(m.sharedKey);
        const inUse = state.models.active[stage].source === "local" && state.models.active[stage].id === m.id;
        items.push({ stage, category: stageLabels[stage], id: m.id, name: m.name, diskGb: m.diskGb, inUse });
        if (!inUse) idleGb += m.diskGb;
        else if (stage === "speech") speechGb += m.diskGb;
        else if (stage === "cleaner") cleanerGb += m.diskGb;
        else translatorGb += m.diskGb;
      }
    }
    const legacyGb = state.models.legacyModels.count > 0 ? state.models.legacyModels.sizeGb : 0;
    const totalGb = Math.round((speechGb + cleanerGb + translatorGb + idleGb + legacyGb) * 10) / 10;
    return { items, speechGb, cleanerGb, translatorGb, idleGb, legacyGb, totalGb };
  }

  function formatHotkeyDisplay(hotkey) {
    if (state.platform === "mac") {
      return hotkey
        .replace(/Win\+/gi, "⌘")
        .replace(/Cmd\+/gi, "⌘")
        .replace(/Ctrl\+/gi, "⌃")
        .replace(/Alt\+/gi, "⌥")
        .replace(/Shift\+/gi, "⇧")
        .replace(/\+/g, "");
    }
    return hotkey;
  }

  function isUsingRecommended() {
    return state.models.active.speech.source === "local" && state.models.active.speech.id === "qwen3-asr-1.7b"
      && state.models.active.cleaner.source === "local" && state.models.active.cleaner.id === "qwen3.5-9b"
      && state.models.active.translator.source === "local" && state.models.active.translator.id === "index-translate-9b";
  }

  function renderDots(count) {
    return "●".repeat(count) + "○".repeat(Math.max(0, 3 - count));
  }

  /* ==================== TOAST & MODAL ==================== */
  let toastTimer = null;
  function showToast(msg) {
    const el = document.getElementById("toast");
    if (!el) return;
    el.textContent = msg;
    el.hidden = false;
    clearTimeout(toastTimer);
    toastTimer = setTimeout(() => { el.hidden = true; }, 3000);
  }

  function openDialog(title, bodyHtml, { onOpen, closeMode = "closerequest" } = {}) {
    const dlg = document.getElementById("dlg");
    dlg.setAttribute("closedby", closeMode);
    document.getElementById("dlg-title").textContent = title;
    const body = document.getElementById("dlg-body");
    body.innerHTML = bodyHtml;
    if (!dlg.open) dlg.showModal();
    onOpen?.(body, dlg);
  }

  function closeDialog() {
    const dlg = document.getElementById("dlg");
    if (dlg.open) dlg.close();
  }

  /* ==================== SEARCH INDEX ==================== */
  const SEARCH_INDEX = [
    { sec: "general", target: "row-hotkey", title: "呼出快捷键", kw: "快捷键 唤起 hotkey shortcut" },
    { sec: "general", target: "row-commit", title: "回填快捷键", kw: "回车 enter ctrl+enter 回填 写入" },
    { sec: "general", target: "row-theme", title: "外观主题", kw: "深色 浅色 暗黑 dark light auto theme 主题" },
    { sec: "general", target: "row-language", title: "界面语言", kw: "中文 english 语言 language" },
    { sec: "general", target: "row-motion", title: "减少动画", kw: "动画 motion 动效" },
    { sec: "general", target: "row-remember", title: "应用偏好记忆", kw: "记住 cursor linear 飞书 slack 应用 偏好" },
    { sec: "general", target: "row-recent", title: "最近草稿", kw: "清除 草稿 历史 recent" },
    { sec: "voice", target: "row-mic", title: "麦克风与音量测试", kw: "麦克风 音频 输入 设备 mic" },
    { sec: "voice", target: "row-autodictate", title: "唤起后自动开始听写", kw: "自动 听写 录音" },
    { sec: "voice", target: "row-preload", title: "启动时预热语音模型", kw: "预热 内存 启动 加载" },
    { sec: "voice", target: "row-cleanup", title: "整理句子与默认输出", kw: "整理 翻译 原话 默认输出" },
    { sec: "voice", target: "row-direction", title: "默认翻译方向", kw: "中文 英文 互译 direction 翻译" },
    { sec: "polish", target: "row-polish-default", title: "默认润色起点与宿主智能匹配", kw: "润色 默认 宿主 cursor 智能匹配" },
    { sec: "polish", target: "row-polish-presets", title: "润色方式预设（AI 任务指令 / 待办 / 汇报）", kw: "预设 prompt ai 任务 待办 汇报 书面 顺句 bug spec commit" },
    { sec: "polish", target: "row-polish-advanced", title: "全文默认润色提示词", kw: "高级 提示词 prompt 全文" },
    { sec: "glossary", target: "row-builtin-terms", title: "内置中英研发术语库", kw: "内置 研发 术语 agent skill api" },
    { sec: "glossary", target: "row-lexicon", title: "口音误识别纠错与术语表", kw: "口音 纠错 同音 热词 术语 译法" },
    { sec: "models", target: "models-hw-card", title: "硬件内存预算与隐私状态", kw: "内存 gpu metal 预算 隐私 离线 云端" },
    { sec: "models", target: "models-pipeline", title: "语音识别 / 整理 / 翻译模型切换", kw: "qwen asr index translate hy-mt2 35b 9b 4b 2b" },
    { sec: "models", target: "models-source-bar", title: "本机共享宿主（LM Studio / Ollama / Bionic）", kw: "lm studio ollama bionic 宿主 端口 回环" },
    { sec: "models", target: "models-source-bar", title: "云端端点与 API 密钥", kw: "云端 cloud api key foundry openai 阿里云 百炼" },
    { sec: "models", target: "models-pipeline", title: "本地存储占用与清理", kw: "存储 磁盘 删除 释放 空间 旧版本" },
    { sec: "about", target: "row-update", title: "软件更新与诊断信息", kw: "更新 版本 诊断 排查 about" }
  ];

  /* ==================== SECTION RENDERERS ==================== */
  function renderGeneral() {
    const g = state.general;
    const appChoiceOpts = [
      { value: "default", label: "跟随全局默认" },
      ...state.polish.presets.filter(p => p.enabled).map(p => ({ value: `polish:${p.id}`, label: p.pill })),
      { value: "translate:English", label: "翻译成 English" },
      { value: "translate:Chinese", label: "翻译成 中文" }
    ];

    const appListHtml = g.apps.length === 0
      ? `<div class="lex-empty">还没有记住任何应用。唤起浮窗并切换输出方式后，将自动记录在此处。</div>`
      : `<ul class="app-list">${g.apps.map(app => {
          const isCustom = app.choice !== "default";
          return `<li class="app-item">
            <span class="app-avatar" aria-hidden="true">${esc(app.abbr)}</span>
            <div class="app-info">
              <div class="app-name-row">
                <b>${esc(app.name)}</b>
                ${isCustom ? `<span class="chip mini accent">已固定</span>` : `<span class="chip mini">跟随默认</span>`}
              </div>
              <span class="app-sub">上次使用：${esc(app.used)}</span>
            </div>
            <div class="app-ctl">
              <select aria-label="${esc(app.name)} 的输出与润色方式" data-app-choice="${esc(app.id)}">
                ${appChoiceOpts.map(opt => `<option value="${esc(opt.value)}" ${opt.value === app.choice ? "selected" : ""}>${esc(opt.label)}</option>`).join("")}
              </select>
              <button type="button" class="link-btn danger" data-forget-app="${esc(app.id)}" aria-label="忘记 ${esc(app.name)}">忘记</button>
            </div>
          </li>`;
        }).join("")}</ul>`;

    document.getElementById("mount-general").innerHTML = `
      <header class="spage-head">
        <div>
          <h2>通用</h2>
          <p>配置全局快捷键、界面外观与每个应用的记忆策略。</p>
        </div>
      </header>

      <section class="sgroup" aria-labelledby="g-keys-h">
        <div class="sgroup-head"><h3 id="g-keys-h" class="sgroup-title">快捷键与输入行为</h3></div>
        <div class="scard">
          <div class="srow" id="row-hotkey">
            <div class="srow-text">
              <label for="hotkey-btn">呼出快捷键</label>
              <p>在任意应用中一键唤起浮窗；点击按钮后按下新的组合键即可修改。</p>
            </div>
            <div class="srow-ctl">
              <button type="button" id="hotkey-btn" class="hotkey-btn ${g.hotkeyRecording ? "recording" : ""}">
                <span>${g.hotkeyRecording ? "请按下组合键…" : esc(formatHotkeyDisplay(g.hotkey))}</span>
                <span class="hotkey-hint">${g.hotkeyRecording ? "Esc 取消" : "点击录制"}</span>
              </button>
            </div>
          </div>

          <div class="srow" id="row-commit">
            <div class="srow-text">
              <span class="srow-title">回填快捷键</span>
              <p>Shift+Enter 始终换行，输入法候选确认优先级高于回填。</p>
            </div>
            <div class="srow-ctl">
              <div class="seg-radio" role="radiogroup" aria-label="回填快捷键">
                <label><input type="radio" name="commitKey" value="Enter" ${g.commitKey === "Enter" ? "checked" : ""}>Enter ↵</label>
                <label><input type="radio" name="commitKey" value="CtrlEnter" ${g.commitKey === "CtrlEnter" ? "checked" : ""}>${state.platform === "mac" ? "⌃ / ⌘+↵" : "Ctrl + Enter"}</label>
              </div>
            </div>
          </div>
        </div>
      </section>

      <section class="sgroup" aria-labelledby="g-look-h">
        <div class="sgroup-head"><h3 id="g-look-h" class="sgroup-title">外观与语言</h3></div>
        <div class="scard">
          <div class="srow stacked" id="row-theme">
            <div class="srow-text">
              <span class="srow-title">界面外观</span>
              <p>切换浅色、深色或跟随系统明暗变化，立即预览效果。</p>
            </div>
            <div class="theme-cards" role="radiogroup" aria-label="界面外观">
              <label class="choice-card">
                <input type="radio" name="theme" value="Auto" ${g.theme === "Auto" ? "checked" : ""}>
                <div class="theme-preview tp-auto" aria-hidden="true">
                  <div class="tp-half-l"><i></i><i></i></div>
                  <div class="tp-half-r"><i></i><i></i></div>
                </div>
                <div class="choice-card-top">
                  <span class="choice-card-title">跟随系统</span>
                  <span class="choice-check">${ic("check")}</span>
                </div>
              </label>
              <label class="choice-card">
                <input type="radio" name="theme" value="Light" ${g.theme === "Light" ? "checked" : ""}>
                <div class="theme-preview tp-light" aria-hidden="true">
                  <div class="tp-side"></div>
                  <div class="tp-main"><div class="tp-bar accent"></div><div class="tp-bar"></div></div>
                </div>
                <div class="choice-card-top">
                  <span class="choice-card-title">浅色模式</span>
                  <span class="choice-check">${ic("check")}</span>
                </div>
              </label>
              <label class="choice-card">
                <input type="radio" name="theme" value="Dark" ${g.theme === "Dark" ? "checked" : ""}>
                <div class="theme-preview tp-dark" aria-hidden="true">
                  <div class="tp-side"></div>
                  <div class="tp-main"><div class="tp-bar accent"></div><div class="tp-bar"></div></div>
                </div>
                <div class="choice-card-top">
                  <span class="choice-card-title">深色模式</span>
                  <span class="choice-check">${ic("check")}</span>
                </div>
              </label>
            </div>
          </div>

          <div class="srow" id="row-language">
            <div class="srow-text">
              <span class="srow-title">界面语言</span>
              <p>切换菜单与设置项显示语言（重新打开窗口后完整生效）。</p>
            </div>
            <div class="srow-ctl">
              <div class="seg-radio" role="radiogroup" aria-label="界面语言">
                <label><input type="radio" name="language" value="zh-CN" ${g.language === "zh-CN" ? "checked" : ""}>简体中文</label>
                <label><input type="radio" name="language" value="en-US" ${g.language === "en-US" ? "checked" : ""}>English</label>
              </div>
            </div>
          </div>

          <div class="srow" id="row-motion">
            <div class="srow-text">
              <label for="opt-motion">减少动画</label>
              <p>关闭浮窗弹出、波形与折叠过渡动画，同时遵守系统「减少动态效果」。</p>
            </div>
            <div class="srow-ctl">
              <input id="opt-motion" type="checkbox" switch role="switch" class="toggle-switch" ${g.reduceMotion ? "checked" : ""}>
            </div>
          </div>
        </div>
      </section>

      <section class="sgroup" aria-labelledby="g-apps-h">
        <div class="sgroup-head">
          <h3 id="g-apps-h" class="sgroup-title">应用偏好记忆</h3>
          <button type="button" id="apps-clear-btn" class="link-btn" ${g.apps.length === 0 ? "disabled" : ""}>清除全部记忆</button>
        </div>
        <div class="scard">
          <div class="srow" id="row-remember">
            <div class="srow-text">
              <label for="opt-remember">记住每个应用的上次选择</label>
              <p>仅在本机记住应用标识与输出方式（如 Cursor → AI 任务指令），绝不记录窗口标题与正文。</p>
            </div>
            <div class="srow-ctl">
              <input id="opt-remember" type="checkbox" switch role="switch" class="toggle-switch" ${g.rememberChoices ? "checked" : ""}>
            </div>
          </div>
          ${g.rememberChoices ? appListHtml : `<div class="lex-empty">已关闭应用记忆：每次唤起浮窗都将使用全局默认输出。</div>`}
        </div>
      </section>

      <section class="sgroup" aria-labelledby="g-data-h">
        <div class="sgroup-head"><h3 id="g-data-h" class="sgroup-title">临时草稿</h3></div>
        <div class="scard">
          <div class="srow" id="row-recent">
            <div class="srow-text">
              <span class="srow-title">最近草稿缓存 <span class="chip mini">${g.recentCount} 份</span></span>
              <p>仅在内存中保留最近 5 份草稿，退出应用自动清空。</p>
            </div>
            <div class="srow-ctl">
              <button type="button" id="clear-recent-btn" class="btn compact" ${g.recentCount === 0 ? "disabled" : ""}>立即清除</button>
            </div>
          </div>
        </div>
      </section>
    `;
  }

  function renderVoice() {
    const v = state.voice;
    document.getElementById("mount-voice").innerHTML = `
      <header class="spage-head">
        <div>
          <h2>听写与翻译</h2>
          <p>配置麦克风输入、听写启动习惯以及没有应用记忆时的默认输出形态。</p>
        </div>
      </header>

      <section class="sgroup" aria-labelledby="v-input-h">
        <div class="sgroup-head"><h3 id="v-input-h" class="sgroup-title">麦克风与听写启动</h3></div>
        <div class="scard">
          <div class="srow" id="row-mic">
            <div class="srow-text">
              <label for="opt-mic">麦克风设备</label>
              <p>听写时使用的音频输入设备，可快速测试收音电平。</p>
            </div>
            <div class="srow-ctl mic-ctl-wrap">
              <select id="opt-mic">
                ${v.microphones.map(m => `<option value="${esc(m)}" ${m === v.microphone ? "selected" : ""}>${esc(m)}</option>`).join("")}
              </select>
              <div class="mic-meter ${v.testingMic ? "testing" : ""}" aria-hidden="true" title="收音电平">
                <i class="${v.testingMic ? "lit" : ""}"></i>
                <i class="${v.testingMic ? "lit" : ""}"></i>
                <i class="${v.testingMic ? "lit" : ""}"></i>
                <i></i>
                <i></i>
              </div>
              <button type="button" id="test-mic-btn" class="btn compact outline">${v.testingMic ? "停止测试" : "测音"}</button>
            </div>
          </div>

          <div class="srow" id="row-autodictate">
            <div class="srow-text">
              <label for="opt-autodictate">唤起后自动开始听写</label>
              <p>按下呼出快捷键后立即收音，无需再点一次麦克风按钮。</p>
            </div>
            <div class="srow-ctl">
              <input id="opt-autodictate" type="checkbox" switch role="switch" class="toggle-switch" ${v.autoDictate ? "checked" : ""}>
            </div>
          </div>

          <div class="srow" id="row-preload">
            <div class="srow-text">
              <label for="opt-preload">启动时预热语音模型</label>
              <p>后台提前加载本地 ASR（约占 1.5–2.3 GB 内存，闲置 30 分钟自动释放），首次听写零等待。</p>
            </div>
            <div class="srow-ctl">
              <input id="opt-preload" type="checkbox" switch role="switch" class="toggle-switch" ${v.preloadSpeech ? "checked" : ""}>
            </div>
          </div>
        </div>
      </section>

      <section class="sgroup" aria-labelledby="v-output-h">
        <div class="sgroup-head"><h3 id="v-output-h" class="sgroup-title">默认输出与翻译方向</h3></div>
        <div class="scard">
          <div class="srow" id="row-cleanup">
            <div class="srow-text">
              <label for="opt-cleanup">启用整理与场景润色</label>
              <p>关闭后浮窗仅保留「原话」与「翻译」，不再调用本地或云端整理模型。</p>
            </div>
            <div class="srow-ctl">
              <input id="opt-cleanup" type="checkbox" switch role="switch" class="toggle-switch" ${v.cleanup ? "checked" : ""}>
            </div>
          </div>

          <div class="srow stacked">
            <div class="srow-text">
              <span class="srow-title">默认输出形态</span>
              <p>首次在未记住的应用中唤起浮窗时的起点模式。</p>
            </div>
            <div class="output-cards" role="radiogroup" aria-label="默认输出形态">
              <label class="choice-card">
                <input type="radio" name="defaultOutput" value="Clean" ${v.defaultOutput === "Clean" ? "checked" : ""} ${!v.cleanup ? "disabled" : ""}>
                <div class="choice-card-top">
                  <span class="choice-card-title">整理 / 润色</span>
                  <span class="choice-check">${ic("check")}</span>
                </div>
                <span class="choice-card-desc">保持原语言，去口头语或按场景整理成稿。</span>
              </label>
              <label class="choice-card">
                <input type="radio" name="defaultOutput" value="Translate" ${v.defaultOutput === "Translate" ? "checked" : ""}>
                <div class="choice-card-top">
                  <span class="choice-card-title">实时翻译</span>
                  <span class="choice-check">${ic("check")}</span>
                </div>
                <span class="choice-card-desc">边说边按下方目标方向输出双语译文。</span>
              </label>
              <label class="choice-card">
                <input type="radio" name="defaultOutput" value="Original" ${v.defaultOutput === "Original" ? "checked" : ""}>
                <div class="choice-card-top">
                  <span class="choice-card-title">原话直出</span>
                  <span class="choice-check">${ic("check")}</span>
                </div>
                <span class="choice-card-desc">不做任何改写，完整保留识别出的原始文本。</span>
              </label>
            </div>
          </div>

          <div class="srow" id="row-direction">
            <div class="srow-text">
              <span class="srow-title">默认翻译方向</span>
              <p>中文与英文互译方向；在浮窗内也可随时一键反转。</p>
            </div>
            <div class="srow-ctl">
              <div class="seg-radio" role="radiogroup" aria-label="默认翻译方向">
                <label><input type="radio" name="direction" value="English" ${v.direction === "English" ? "checked" : ""}>中文 → English</label>
                <label><input type="radio" name="direction" value="Chinese" ${v.direction === "Chinese" ? "checked" : ""}>English → 中文</label>
              </div>
            </div>
          </div>
        </div>
      </section>

      <div class="s-callout info">
        ${ic("sparkle")}
        <div class="s-callout-body">
          想要自定义 AI 任务指令、待办或汇报格式？前往<b>「润色场景」</b>；想要修正固定口音或专业词汇，前往<b>「词库与纠错」</b>。
        </div>
        <div class="s-callout-actions">
          <button type="button" class="btn compact outline" data-jump-sec="polish">去配置润色 →</button>
        </div>
      </div>
    `;
  }

  function renderPolish() {
    const p = state.polish;
    const enabledPresets = p.presets.filter(item => item.enabled);
    const cleanerMeta = activeModelMeta("cleaner");
    const is2b = state.models.active.cleaner.source === "local" && state.models.active.cleaner.id === "qwen3.5-2b";
    const isDefaultPrompt = p.polishPrompt.trim() === DEFAULT_POLISH_PROMPT.trim();

    document.getElementById("mount-polish").innerHTML = `
      <header class="spage-head">
        <div>
          <h2>润色场景</h2>
          <p>管理浮窗菜单里的多场景润色方式与宿主应用智能匹配策略。</p>
        </div>
        <div class="spage-head-actions">
          <button type="button" id="add-preset-btn" class="btn primary">${ic("plus")} 新建润色方式</button>
        </div>
      </header>

      ${is2b ? `<div class="s-callout warn">
        ${ic("alert")}
        <div class="s-callout-body">当前整理模型为 <b>Qwen3.5 2B</b>（轻量版），仅建议用于「口语顺句」；使用「AI 任务指令 / 待办 / 汇报」时建议升级至 4B / 9B 或本机宿主模型以保证结构化重组质量。</div>
        <div class="s-callout-actions"><button type="button" class="btn compact" data-jump-stage="cleaner">去切换模型</button></div>
      </div>` : ""}

      <section class="sgroup" aria-labelledby="p-route-h" id="row-polish-default">
        <div class="sgroup-head"><h3 id="p-route-h" class="sgroup-title">默认落位与宿主感知</h3></div>
        <div class="scard">
          <div class="srow">
            <div class="srow-text">
              <label for="opt-default-polish">默认润色起点</label>
              <p>未记住应用且无宿主智能推荐时，优先使用的润色方式。</p>
            </div>
            <div class="srow-ctl">
              <select id="opt-default-polish">
                ${enabledPresets.map(item => `<option value="${esc(item.id)}" ${item.id === p.defaultPolishMode ? "selected" : ""}>${esc(item.name)}</option>`).join("")}
              </select>
            </div>
          </div>

          <div class="srow">
            <div class="srow-text">
              <label for="opt-smart-host">按目标应用智能匹配初始润色</label>
              <p>首次在 Cursor / VS Code / 终端自动落位「AI 任务指令」，Linear 落位「待办与派单」，文档应用落位「汇报与纪要」。</p>
            </div>
            <div class="srow-ctl">
              <input id="opt-smart-host" type="checkbox" switch role="switch" class="toggle-switch" ${p.smartHostPolish ? "checked" : ""}>
            </div>
          </div>
        </div>
      </section>

      <section class="sgroup" aria-labelledby="p-cards-h" id="row-polish-presets">
        <div class="sgroup-head">
          <h3 id="p-cards-h" class="sgroup-title">润色方式预设（${enabledPresets.length} / ${p.presets.length} 已启用）</h3>
          <span class="sgroup-meta">当前引擎：${esc(cleanerMeta.name)} · ${esc(cleanerMeta.sourceLabel)}</span>
        </div>
        <div class="preset-grid">
          ${p.presets.map(item => {
            const isDef = item.id === p.defaultPolishMode;
            const customized = item.builtIn && item.instructions.trim() !== item.defaultInstructions.trim();
            const canToggle = !isDef && (enabledPresets.length > 1 || !item.enabled);
            return `<article class="pcard ${isDef ? "is-default" : ""} ${!item.enabled ? "is-disabled" : ""}">
              <div class="pcard-head">
                <div class="pcard-title-wrap">
                  <span class="pcard-ic">${ic(item.icon || "sparkle")}</span>
                  <div>
                    <div class="pcard-name">${esc(item.name)}</div>
                    <div class="pcard-tags">
                      <span class="chip mini ${item.builtIn ? "" : "accent"}">${item.builtIn ? "内置" : "自定义"}</span>
                      <span class="chip mini ${item.gate === "FactAnchor" ? "info" : ""}">${item.gate === "FactAnchor" ? "事实锚点校验" : "字级保真门禁"}</span>
                      ${isDef ? `<span class="chip mini ok">默认起点</span>` : ""}
                      ${customized ? `<span class="chip mini warn">已微调</span>` : ""}
                    </div>
                  </div>
                </div>
                <input type="checkbox" switch role="switch" class="toggle-switch"
                  aria-label="在浮窗菜单显示 ${esc(item.name)}"
                  data-preset-toggle="${esc(item.id)}"
                  ${item.enabled ? "checked" : ""} ${!canToggle ? "disabled" : ""}>
              </div>
              <p class="pcard-desc">${esc(item.desc || "自定义润色方式")}</p>
              <div class="pcard-foot">
                <div class="pcard-foot-left">
                  <button type="button" class="btn compact" data-preset-edit="${esc(item.id)}">${item.builtIn ? "查看 / 微调" : "编辑"}</button>
                  ${customized ? `<button type="button" class="link-btn" data-preset-reset="${esc(item.id)}">恢复默认</button>` : ""}
                  ${!item.builtIn ? `<button type="button" class="link-btn danger" data-preset-delete="${esc(item.id)}">删除</button>` : ""}
                </div>
                ${item.key ? `<kbd>${state.platform === "mac" ? "⌘" : "Ctrl+"}${item.key}</kbd>` : ""}
              </div>
            </article>`;
          }).join("")}
        </div>
      </section>

      <details class="s-disclosure" id="row-polish-advanced">
        <summary>
          <span>高级：基础整理全局提示词</span>
          <span class="s-disclosure-sub">${isDefaultPrompt ? "使用内置默认" : "已自定义 · 点击展开编辑"}</span>
        </summary>
        <div class="s-disclosure-body">
          <p class="srow-text" style="font-size:13px;color:var(--muted)">
            仅用于调整默认顺句整理时的语气偏好。「不编造事实、不篡改生效数字、不丢失代码标识符」等底线门禁由系统固定保障，不会被此文本覆盖。
          </p>
          <textarea id="global-polish-prompt" class="prompt-editor" spellcheck="false" aria-label="基础整理全局提示词">${esc(p.polishPrompt)}</textarea>
          <div class="prompt-editor-foot">
            ${p.polishPromptInvalid
              ? `<span class="err-txt" role="alert">要求需要 10–1500 个字符，当前更改未保存。</span>`
              : `<span class="hint-txt">离开输入框自动保存（10–1500 字符）</span>`}
            <button type="button" id="reset-global-prompt" class="btn compact" ${isDefaultPrompt ? "disabled" : ""}>恢复内置默认</button>
          </div>
        </div>
      </details>
    `;
  }

  function renderGlossary() {
    const gl = state.glossary;
    const isCorr = gl.tab === "corrections";

    document.getElementById("mount-glossary").innerHTML = `
      <header class="spage-head">
        <div>
          <h2>词库与纠错</h2>
          <p>用「口音纠错」把容易听错的词修回正确写法，用「术语表」固定专业词的翻译或原样保留。</p>
        </div>
      </header>

      <section class="sgroup" aria-labelledby="gl-builtin-h" id="row-builtin-terms">
        <div class="sgroup-head"><h3 id="gl-builtin-h" class="sgroup-title">系统内置热词</h3></div>
        <div class="scard">
          <div class="srow">
            <div class="srow-text">
              <label for="opt-builtin-glossary">内置中英研发术语库</label>
              <p>自动注入语音识别热词与保真词表，显著提升中英混说研发名词准确度；日常非技术口述可关闭。</p>
              ${gl.builtInGlossary ? `<div class="builtin-terms-preview" aria-label="包含示例术语">${BUILTIN_DEV_TERMS.map(t => `<span class="term-pill">${esc(t)}</span>`).join("")}</div>` : ""}
            </div>
            <div class="srow-ctl">
              <input id="opt-builtin-glossary" type="checkbox" switch role="switch" class="toggle-switch" ${gl.builtInGlossary ? "checked" : ""}>
            </div>
          </div>
        </div>
      </section>

      <section class="sgroup" aria-labelledby="gl-custom-h" id="row-lexicon">
        <div class="subtabs-bar">
          <div class="subtab-list" role="tablist" aria-label="自定义词库类型">
            <button type="button" role="tab" class="subtab-btn" data-lex-tab="corrections" aria-selected="${isCorr}">
              口音与误识别纠错 <span class="chip mini">${gl.corrections.length}</span>
            </button>
            <button type="button" role="tab" class="subtab-btn" data-lex-tab="terms" aria-selected="${!isCorr}">
              术语与翻译词表 <span class="chip mini">${gl.terms.length}</span>
            </button>
          </div>
          <span class="sgroup-meta">${isCorr ? "右侧词会注入 ASR 热词，整理时自动把左侧修回右侧" : "原词与译法填相同时表示翻译时原样保留"}</span>
        </div>

        <div class="scard">
          ${isCorr ? `
            <form id="corr-add-form" class="inline-add-form" novalidate>
              <label>常被听成（误识别）<input id="corr-spoken" placeholder="例如 Scale / 六零九" autocomplete="off" spellcheck="false"></label>
              <span class="inline-arrow" aria-hidden="true">→</span>
              <label>实际想说（正确写法）<input id="corr-canonical" placeholder="例如 Skill / LiveLingo" autocomplete="off" spellcheck="false"></label>
              <button type="submit" class="btn primary">添加纠错</button>
            </form>
            ${gl.corrections.length === 0
              ? `<div class="lex-empty">暂无口音纠错条目。在浮窗手动修改错词时也可一键记入此处。</div>`
              : `<ul class="lex-list">${gl.corrections.map(c => `<li class="lex-item">
                  <span class="lex-from">${esc(c.spoken)}</span>
                  <span class="lex-arrow" aria-hidden="true">⇒</span>
                  <span class="lex-to">${esc(c.canonical)}</span>
                  <span class="chip mini ok lex-tag">热词偏置</span>
                  <button type="button" class="link-btn danger" data-del-corr="${esc(c.id)}" aria-label="删除 ${esc(c.spoken)}">删除</button>
                </li>`).join("")}</ul>`}
          ` : `
            <form id="term-add-form" class="inline-add-form" novalidate>
              <label>原词<input id="term-source" placeholder="例如 缓存穿透 / CacheService" autocomplete="off" spellcheck="false"></label>
              <span class="inline-arrow" aria-hidden="true">→</span>
              <label>译法（留空或同名则原样保留）<input id="term-target" placeholder="例如 cache penetration" autocomplete="off" spellcheck="false"></label>
              <button type="submit" class="btn primary">添加术语</button>
            </form>
            ${gl.terms.length === 0
              ? `<div class="lex-empty">暂无自定义术语。</div>`
              : `<ul class="lex-list">${gl.terms.map(t => {
                  const keepSame = t.source === t.target;
                  return `<li class="lex-item">
                    <span class="lex-from">${esc(t.source)}</span>
                    <span class="lex-arrow" aria-hidden="true">→</span>
                    <span class="lex-to">${esc(t.target)}</span>
                    <span class="chip mini ${keepSame ? "info" : ""}" style="margin-inline-start:4px">${keepSame ? "原样保留" : "指定译法"}</span>
                    <button type="button" class="link-btn danger" data-del-term="${esc(t.id)}" aria-label="删除 ${esc(t.source)}">删除</button>
                  </li>`;
                }).join("")}</ul>`}
          `}
        </div>
      </section>
    `;
  }

  function renderModels() {
    const m = state.models;
    const residency = computeResidencyGb();
    const budgetPct = Math.min(100, Math.round((residency / m.hardware.budgetGb) * 100));
    const overBudget = residency > m.hardware.budgetGb;
    const cloudStages = computeCloudStages();
    const hostStages = computeHostStages();
    const anyCloud = cloudStages.length > 0;
    const recommended = isUsingRecommended();
    const storage = computeStorageStats();

    const speechMeta = activeModelMeta("speech");
    const cleanerMeta = activeModelMeta("cleaner");
    const translatorMeta = activeModelMeta("translator");

    const showHotBanner = !m.hotHostDismissed && m.active.cleaner.source === "local" && m.active.translator.source === "local";

    const downloadsHtml = m.downloads.length === 0 ? "" : `
      <section class="dl-panel" aria-label="下载任务">
        <div class="dl-head">
          <div style="display:flex;align-items:center;gap:8px">
            <b>正在下载模型</b>
            <span class="chip mini accent">${m.downloads.length} 项</span>
          </div>
          <span class="sgroup-meta">下载完成后将自动切换启用，当前对话不受打断</span>
        </div>
        ${m.downloads.map(d => `<div class="dl-item">
          <div class="dl-row">
            <div style="display:flex;align-items:center;gap:8px">
              <span class="chip mini">${esc(d.category)}</span>
              <b>${esc(d.name)}</b>
            </div>
            <div style="display:flex;align-items:center;gap:10px">
              <span class="hw-bar-label">${d.percent}% · ${d.totalGb} GB</span>
              <button type="button" class="btn compact" data-cancel-dl="${esc(d.id)}">取消</button>
            </div>
          </div>
          <div class="progress" role="progressbar" aria-valuemin="0" aria-valuemax="100" aria-valuenow="${d.percent}">
            <span style="width:${d.percent}%"></span>
          </div>
        </div>`).join("")}
      </section>
    `;

    const tab = m.stageTab;
    const stageContentHtml = tab === "storage"
      ? renderStorageStage(storage)
      : renderPipelineStage(tab);

    document.getElementById("mount-models").innerHTML = `
      <header class="spage-head">
        <div>
          <h2>模型与服务</h2>
          <p>按阶段独立选择「本机内置」「本机共享宿主」或「云端服务」，隐私与内存占用一目了然。</p>
        </div>
      </header>

      <section class="hw-overview" id="models-hw-card" aria-label="硬件预算与隐私总览">
        <div class="hw-col">
          <div class="hw-title-row">
            <span class="sgroup-title">本机硬件与并发内存</span>
            ${recommended
              ? `<span class="chip mini ok">✓ 本机最佳匹配</span>`
              : `<button type="button" id="apply-recommended-btn" class="link-btn accent">恢复本机最佳匹配</button>`}
          </div>
          <div class="hw-spec">${esc(m.hardware.summary)}</div>
          <div class="hw-bar-row">
            <div class="progress ${overBudget ? "warn" : "ok"}" role="progressbar" aria-label="并发驻留内存占比" aria-valuemin="0" aria-valuemax="100" aria-valuenow="${budgetPct}">
              <span style="width:${budgetPct}%"></span>
            </div>
            <span class="hw-bar-label">${residency} / ${m.hardware.budgetGb} GB</span>
          </div>
          <div class="hw-sub">${overBudget
            ? "当前配置高于双模型并发预算，整理与翻译切换时将自动释放对端并按需加载。"
            : hostStages.length > 0
              ? `已复用本机宿主承载${hostStages.join("、")}，LiveLingo 驻留内存显著降低。`
              : "识别、整理与翻译可在安全预算内同时预热，切换零等待。"}</div>
        </div>

        <div class="hw-col">
          <div class="hw-title-row">
            <span class="sgroup-title">数据流向与隐私</span>
            <span class="chip mini ${anyCloud ? "warn" : "ok"}">${anyCloud ? "含云端处理" : "全部本机离线"}</span>
          </div>
          <div class="hw-sub">${anyCloud
            ? `已授权云端：<b>${cloudStages.join("、")}</b>。未授权阶段仍在本地运行。`
            : "语音识别、整理与翻译均在当前设备完成，音频与文本绝不离开本机。"}</div>
          <div class="hw-actions-row">
            ${anyCloud ? `<button type="button" id="disable-all-cloud-btn" class="btn compact outline">全部切回本机</button>` : ""}
            <button type="button" class="link-btn accent" data-model-tab="storage">已占用磁盘 ${storage.totalGb} GB →</button>
          </div>
        </div>
      </section>

      ${showHotBanner ? `<div class="hot-host-bar" role="status">
        <div class="hot-host-left">
          <span class="chip mini host">零额外内存</span>
          <span>检测到本机 <b>LM Studio</b> 已常驻 <code>qwen3.5-9b-instruct</code>，可直接复用完成整理与翻译，免重复占内存。</span>
        </div>
        <div style="display:flex;align-items:center;gap:6px;flex:none">
          <button type="button" id="apply-hot-host-btn" class="btn compact primary">一键复用</button>
          <button type="button" id="dismiss-hot-host-btn" class="icon-btn" aria-label="关闭提示">${ic("close")}</button>
        </div>
      </div>` : ""}

      ${downloadsHtml}

      <div class="stage-tabs" id="models-pipeline" role="tablist" aria-label="处理阶段与存储">
        <button type="button" role="tab" class="stage-tab" data-model-tab="speech" aria-selected="${tab === "speech"}">
          <div class="stage-tab-top">
            <span class="stage-tab-name">${ic("mic")} 语音识别</span>
            <span class="dot" style="color:${speechMeta.source === "cloud" ? "var(--warn)" : "var(--ok)"}"></span>
          </div>
          <span class="stage-tab-model">${esc(speechMeta.name)}</span>
          <span class="stage-tab-src">${esc(speechMeta.sourceLabel)}</span>
        </button>

        <button type="button" role="tab" class="stage-tab" data-model-tab="cleaner" aria-selected="${tab === "cleaner"}">
          <div class="stage-tab-top">
            <span class="stage-tab-name">${ic("sparkle")} 整理与润色</span>
            <span class="dot" style="color:${cleanerMeta.source === "cloud" ? "var(--warn)" : cleanerMeta.source === "host" ? "var(--host)" : "var(--ok)"}"></span>
          </div>
          <span class="stage-tab-model">${esc(cleanerMeta.name)}</span>
          <span class="stage-tab-src">${esc(cleanerMeta.sourceLabel)}</span>
        </button>

        <button type="button" role="tab" class="stage-tab" data-model-tab="translator" aria-selected="${tab === "translator"}">
          <div class="stage-tab-top">
            <span class="stage-tab-name">${ic("globe")} 翻译</span>
            <span class="dot" style="color:${translatorMeta.source === "cloud" ? "var(--warn)" : translatorMeta.source === "host" ? "var(--host)" : "var(--ok)"}"></span>
          </div>
          <span class="stage-tab-model">${esc(translatorMeta.name)}</span>
          <span class="stage-tab-src">${esc(translatorMeta.sourceLabel)}</span>
        </button>

        <button type="button" role="tab" class="stage-tab" data-model-tab="storage" aria-selected="${tab === "storage"}">
          <div class="stage-tab-top">
            <span class="stage-tab-name">${ic("drive")} 本地存储</span>
          </div>
          <span class="stage-tab-model">${storage.totalGb} GB</span>
          <span class="stage-tab-src">${storage.items.length} 个模型${storage.idleGb + storage.legacyGb > 0 ? ` · 可清理 ${(storage.idleGb + storage.legacyGb).toFixed(1)} GB` : ""}</span>
        </button>
      </div>

      ${stageContentHtml}
    `;
  }

  function renderPipelineStage(stage) {
    const m = state.models;
    const meta = activeModelMeta(stage);
    const activeSrc = m.active[stage].source;
    const currentSourceTab = m.sourceTab[stage];
    const supportsHost = stage !== "speech";
    const localList = m.localCatalog[stage];
    const compatibleLocals = localList.filter(x => x.fit !== "unsupported");
    const unsupportedLocals = localList.filter(x => x.fit === "unsupported");
    const cloudList = stage === "speech" ? m.cloudEndpoints.speech : m.cloudEndpoints.text;
    const runningHostCount = m.localHosts.filter(h => h.status === "running").length;

    const stageCopy = {
      speech: { title: "语音识别模型", desc: "决定听写成句与录音转录的字准率，支持中英混说热词偏置。" },
      cleaner: { title: "整理与润色模型", desc: "负责顺句去口头语、AI 任务指令重组、改口剪枝与多场景成稿。" },
      translator: { title: "翻译模型", desc: "负责中英双向翻译、术语对齐与代码/Markdown 结构保真。" }
    }[stage];

    let sourceBodyHtml = "";
    if (currentSourceTab === "local") {
      sourceBodyHtml = `
        <ul class="model-list">
          ${compatibleLocals.map(opt => renderLocalModelCard(stage, opt)).join("")}
        </ul>
        ${unsupportedLocals.length > 0 ? `
          <details class="s-disclosure">
            <summary>
              <span>查看不适合当前设备的模型（${unsupportedLocals.length}）</span>
              <span class="s-disclosure-sub">内存需求高于本机 36 GB</span>
            </summary>
            <div class="s-disclosure-body">
              <ul class="model-list">
                ${unsupportedLocals.map(opt => renderLocalModelCard(stage, opt)).join("")}
              </ul>
            </div>
          </details>
        ` : ""}
      `;
    } else if (currentSourceTab === "host") {
      sourceBodyHtml = renderLocalHostsPanel(stage);
    } else {
      sourceBodyHtml = renderCloudPanel(stage, cloudList);
    }

    const speechNativeNotice = stage === "cleaner" && m.active.speech.source === "cloud"
      ? `<div class="s-callout info">${ic("zap")}<div class="s-callout-body">当前语音识别已启用 <b>Qwen-Audio-3.1-ASR</b> 原生润色：默认口语顺句在识别中一步完成；仅手打文字或结构化场景（AI 指令 / 待办 / 汇报）时调用此整理模型。</div></div>`
      : "";

    const cleanupOffNotice = stage === "cleaner" && !state.voice.cleanup
      ? `<div class="s-callout warn">${ic("alert")}<div class="s-callout-body">当前已在「听写与翻译」中关闭整理与润色，此环节暂不运行。</div></div>`
      : "";

    return `
      <div class="stage-panel" role="tabpanel">
        ${speechNativeNotice}
        ${cleanupOffNotice}

        <div class="source-bar" id="models-source-bar">
          <div class="source-seg" role="group" aria-label="选择运行来源">
            <button type="button" data-source-tab="local" data-stage="${stage}" aria-pressed="${currentSourceTab === "local"}">
              ${ic("cpu")} 本机内置
              <span class="src-count">${compatibleLocals.length}</span>
              ${activeSrc === "local" ? `<span class="src-active-dot" title="当前生效"></span>` : ""}
            </button>
            ${supportsHost ? `<button type="button" data-source-tab="host" data-stage="${stage}" aria-pressed="${currentSourceTab === "host"}">
              ${ic("server")} 本机宿主
              <span class="src-count">${runningHostCount} 在线</span>
              ${activeSrc === "host" ? `<span class="src-active-dot" title="当前生效"></span>` : ""}
            </button>` : ""}
            <button type="button" data-source-tab="cloud" data-stage="${stage}" aria-pressed="${currentSourceTab === "cloud"}">
              ${ic("cloud")} 云端服务
              <span class="src-count">${cloudList.length}</span>
              ${activeSrc === "cloud" ? `<span class="src-active-dot" title="当前生效"></span>` : ""}
            </button>
          </div>

          <div style="display:flex;align-items:center;gap:6px">
            ${activeSrc !== "local" ? `<button type="button" class="btn compact outline" data-back-local="${stage}">切回本机内置</button>` : ""}
            ${currentSourceTab === "host" ? `
              <button type="button" class="btn compact outline" id="refresh-hosts-btn" ${m.localHostRefreshing ? "disabled" : ""}>${ic("refresh")} ${m.localHostRefreshing ? "扫描中…" : "重新扫描"}</button>
              <button type="button" class="btn compact" id="toggle-custom-host-btn">${ic("plus")} 添加本机端口</button>
            ` : ""}
            ${currentSourceTab === "cloud" ? `
              <button type="button" class="btn compact primary" data-open-cloud-form="add" data-stage="${stage}">${ic("plus")} 添加云端服务</button>
            ` : ""}
          </div>
        </div>

        <p class="hw-sub" style="margin-top:-6px">${stageCopy.desc}（当前生效：<b>${esc(meta.fullName)}</b> · ${esc(meta.sourceLabel)}）</p>

        ${sourceBodyHtml}
      </div>
    `;
  }

  function renderLocalModelCard(stage, opt) {
    const sel = state.models.active[stage];
    const isActive = sel.source === "local" && sel.id === opt.id;
    const dl = state.models.downloads.find(d => d.id === opt.id);
    const canSelect = opt.installed && !isActive && opt.fit !== "unsupported";
    const fitCls = opt.fit === "recommended" ? "chip mini accent"
      : opt.fit === "heavy" ? "chip mini warn"
      : opt.fit === "unsupported" ? "chip mini bad"
      : "chip mini";

    return `<li class="mcard ${isActive ? "active" : ""} ${canSelect ? "selectable" : ""} ${opt.fit === "unsupported" ? "unsupported" : ""}" data-select-local="${canSelect ? `${stage}:${opt.id}` : ""}">
      <div class="mcard-top">
        <input type="radio" class="mradio" name="stage-model-${stage}" aria-label="${esc(opt.name)}" ${isActive ? "checked" : ""} ${!canSelect && !isActive ? "disabled" : ""}>
        <div class="mcard-body">
          <div class="mcard-head">
            <span class="mname">${esc(opt.name)}</span>
            <span class="${fitCls}">${esc(opt.fitBadge)}</span>
            ${isActive ? `<span class="chip mini ok">正在使用</span>` : opt.installed ? `<span class="chip mini">已下载</span>` : ""}
          </div>
          <p class="mcard-desc">${esc(opt.summary)}</p>
          <div class="mcard-metrics">
            <span class="mmetric">质量 <span class="mdots" aria-hidden="true">${renderDots(opt.quality)}</span></span>
            <span class="mmetric">速度 <span class="mdots" aria-hidden="true">${renderDots(opt.speed)}</span></span>
            <span>磁盘 ${opt.diskGb} GB · 驻留约 ${opt.ramGb} GB</span>
            ${opt.fitDetail ? `<span style="color:${opt.fit === "unsupported" ? "var(--danger)" : "var(--warn)"}">· ${esc(opt.fitDetail)}</span>` : ""}
          </div>
        </div>
        <div class="mcard-actions">
          ${dl ? `<button type="button" class="btn compact" data-cancel-dl="${esc(opt.id)}">取消下载 (${dl.percent}%)</button>`
            : !opt.installed && opt.fit !== "unsupported"
              ? `<button type="button" class="btn compact outline" data-download-model="${stage}:${esc(opt.id)}">${ic("download")} 下载并启用（${opt.diskGb} GB）</button>`
            : canSelect
              ? `<button type="button" class="btn compact" data-select-local-btn="${stage}:${esc(opt.id)}">启用</button>`
            : ""}
          ${opt.installed && !isActive ? `<button type="button" class="link-btn danger" data-delete-model="${stage}:${esc(opt.id)}">删除</button>` : ""}
        </div>
      </div>
    </li>`;
  }

  function renderLocalHostsPanel(stage) {
    const m = state.models;
    const sel = m.active[stage];
    const customForm = m.addingCustomHost ? `
      <form id="custom-host-form" class="host-custom-form">
        <input id="ch-name" placeholder="名称（如 本机 llama.cpp）" autocomplete="off">
        <input id="ch-url" type="url" value="http://127.0.0.1:8080" placeholder="仅限本机回环 http://127.0.0.1:端口" autocomplete="off">
        <button type="submit" class="btn compact primary">添加并扫描</button>
      </form>
    ` : "";

    return `
      ${customForm}
      <div class="s-callout info">
        ${ic("shield")}
        <div class="s-callout-body">仅连接本机回环地址（<code>127.0.0.1</code> / <code>localhost</code>），数据绝不出设备；复用已驻留模型可做到 <b>0 额外内存</b>。</div>
      </div>
      ${m.localHosts.map(host => `
        <div class="host-box">
          <div class="host-head">
            <div class="host-title">
              <b>${esc(host.name)}</b>
              <code class="host-addr">${esc(host.address)}</code>
              <span class="chip mini ${host.status === "running" ? "ok" : ""}">${esc(host.statusBadge)}</span>
            </div>
            <div style="display:flex;gap:6px">
              ${host.status === "stopped" ? `<button type="button" class="btn compact primary" data-start-host="${esc(host.hostId)}">启动本地服务</button>` : ""}
              ${host.isCustom ? `<button type="button" class="link-btn danger" data-remove-host="${esc(host.hostId)}">移除</button>` : ""}
            </div>
          </div>
          ${host.status === "stopped"
            ? `<p class="hw-sub">检测到本机已安装 ${esc(host.name)}，但 HTTP 服务尚未监听。点击「启动本地服务」后即可共享复用模型库。</p>`
            : `<ul class="model-list">${host.models.map(hm => {
                const isActive = sel.source === "host" && sel.hostId === host.hostId && sel.modelKey === hm.key;
                return `<li class="mcard ${isActive ? "active" : "selectable"}" data-select-host="${!isActive ? `${stage}:${host.hostId}:${hm.key}` : ""}">
                  <div class="mcard-top">
                    <input type="radio" class="mradio" name="stage-model-${stage}" aria-label="${esc(host.name)} ${esc(hm.name)}" ${isActive ? "checked" : ""}>
                    <div class="mcard-body">
                      <div class="mcard-head">
                        <span class="chip mini host">宿主</span>
                        <span class="mname">${esc(hm.name)}</span>
                        <span class="chip mini ${hm.suitability === "verified" || hm.suitability === "flagship" ? "accent" : ""}">${esc(hm.suitabilityBadge)}</span>
                        <span class="chip mini ${hm.isLoaded ? "ok" : ""}">${esc(hm.loadedBadge)}</span>
                        ${isActive ? `<span class="chip mini ok">正在使用</span>` : ""}
                      </div>
                      <p class="mcard-desc">${esc(hm.meta)}</p>
                    </div>
                    <div class="mcard-actions">
                      ${!isActive ? `<button type="button" class="btn compact" data-select-host-btn="${stage}:${host.hostId}:${hm.key}">启用复用</button>` : ""}
                    </div>
                  </div>
                </li>`;
              }).join("")}</ul>`}
        </div>
      `).join("")}
    `;
  }

  function renderCloudPanel(stage, cloudList) {
    const sel = state.models.active[stage];
    const isSpeech = stage === "speech";
    const privacyHint = isSpeech
      ? "启用云端识别时，麦克风音频与导入录音将发送至你授权的服务端点；首次启用需弹窗确认。"
      : "启用云端整理或翻译时，仅发送该环节的文字内容，麦克风原始音频绝不上传；首次启用需弹窗确认。";

    return `
      <div class="s-callout warn">
        ${ic("cloud")}
        <div class="s-callout-body">${privacyHint}</div>
      </div>
      ${cloudList.length === 0 ? `
        <div class="scard padded dashed" style="display:flex;align-items:center;justify-content:space-between;gap:14px;flex-wrap:wrap">
          <div>
            <b>尚未配置${isSpeech ? "云端语音" : "云端文本"}服务</b>
            <p class="hw-sub">${isSpeech ? "推荐接入通义百炼 Qwen-Audio-3.1-ASR（支持流式上屏与原生润色）。" : "可接入 Microsoft Foundry 或 OpenAI 兼容端点，零本地内存占用。"}</p>
          </div>
          <button type="button" class="btn primary compact" data-open-cloud-form="add" data-stage="${stage}">添加云端服务</button>
        </div>
      ` : `<ul class="model-list">${cloudList.map(ep => {
        const isActive = sel.source === "cloud" && sel.id === ep.id;
        return `<li class="mcard ${isActive ? "active" : "selectable"}" data-select-cloud="${!isActive ? `${stage}:${ep.id}` : ""}">
          <div class="mcard-top">
            <input type="radio" class="mradio" name="stage-model-${stage}" aria-label="${esc(ep.name)}" ${isActive ? "checked" : ""}>
            <div class="mcard-body">
              <div class="mcard-head">
                <span class="chip mini info">云端</span>
                <span class="mname">${esc(ep.name)}</span>
                <span class="chip mini">${esc(ep.model)}</span>
                ${isActive ? `<span class="chip mini ok">正在使用</span>` : ""}
              </div>
              <p class="mcard-desc">${esc(ep.host)} · ${ep.hasKey ? "API 密钥已存于系统钥匙串" : "未保存密钥"}</p>
              <div class="mcard-metrics"><span>0 磁盘 · 0 本地内存占用</span></div>
            </div>
            <div class="mcard-actions">
              ${isActive
                ? `<button type="button" class="btn compact" data-back-local="${stage}">切回本机</button>`
                : `<button type="button" class="btn compact" data-select-cloud-btn="${stage}:${esc(ep.id)}">授权并启用</button>`}
              <div class="mcard-links">
                <button type="button" class="link-btn" data-open-cloud-form="edit:${esc(ep.id)}" data-stage="${stage}">编辑</button>
                <button type="button" class="link-btn danger" data-delete-cloud="${stage}:${esc(ep.id)}">删除</button>
              </div>
            </div>
          </div>
        </li>`;
      }).join("")}</ul>`}
    `;
  }

  function renderStorageStage(storage) {
    const t = Math.max(0.1, storage.totalGb);
    const pct = v => Math.max(v > 0 ? 4 : 0, Math.round((v / t) * 100));
    const leg = state.models.legacyModels;

    return `
      <div class="stage-panel" role="tabpanel">
        <div class="storage-bar-card">
          <div class="storage-top">
            <div>
              <span class="stage-active-kicker">本地模型总占用</span>
              <div class="storage-total">${storage.totalGb} GB</div>
            </div>
            <span class="chip ${storage.idleGb + storage.legacyGb > 0 ? "warn" : "ok"}">
              ${storage.idleGb + storage.legacyGb > 0
                ? `可安全释放 ${(storage.idleGb + storage.legacyGb).toFixed(1)} GB`
                : "全部处于使用中"}
            </span>
          </div>

          <div class="storage-stacked" aria-label="模型存储分类占比">
            ${storage.speechGb > 0 ? `<div class="storage-seg seg-speech" style="width:${pct(storage.speechGb)}%" title="语音识别 ${storage.speechGb} GB"></div>` : ""}
            ${storage.cleanerGb > 0 ? `<div class="storage-seg seg-cleaner" style="width:${pct(storage.cleanerGb)}%" title="整理与润色 ${storage.cleanerGb} GB"></div>` : ""}
            ${storage.translatorGb > 0 ? `<div class="storage-seg seg-translator" style="width:${pct(storage.translatorGb)}%" title="翻译 ${storage.translatorGb} GB"></div>` : ""}
            ${storage.idleGb + storage.legacyGb > 0 ? `<div class="storage-seg seg-idle" style="width:${pct(storage.idleGb + storage.legacyGb)}%" title="闲置 / 旧版 ${(storage.idleGb + storage.legacyGb).toFixed(1)} GB"></div>` : ""}
          </div>

          <div class="storage-legend">
            <span class="legend-item"><i class="legend-dot seg-speech"></i>语音识别 ${storage.speechGb.toFixed(1)} GB</span>
            <span class="legend-item"><i class="legend-dot seg-cleaner"></i>整理与润色 ${storage.cleanerGb.toFixed(1)} GB</span>
            <span class="legend-item"><i class="legend-dot seg-translator"></i>翻译 ${storage.translatorGb.toFixed(1)} GB</span>
            <span class="legend-item"><i class="legend-dot seg-idle"></i>闲置 / 旧版 ${(storage.idleGb + storage.legacyGb).toFixed(1)} GB</span>
          </div>

          <div class="storage-path-row">
            <code>~/Library/Application Support/LiveLingo/models</code>
            <button type="button" id="copy-model-path-btn" class="link-btn accent">${ic("copy")} 复制路径</button>
          </div>
        </div>

        ${leg.count > 0 ? `<div class="s-callout warn">
          ${ic("trash")}
          <div class="s-callout-body">检测到旧版本遗留模型（${leg.count} 个 · ${leg.sizeGb} GB），不再被当前版本使用；不点击不会自动删除。</div>
          <div class="s-callout-actions"><button type="button" id="remove-legacy-btn" class="btn compact">清理释放 ${leg.sizeGb} GB</button></div>
        </div>` : ""}

        <section class="sgroup">
          <div class="sgroup-head"><h3 class="sgroup-title">已安装明细（${storage.items.length}）</h3></div>
          <div class="scard">
            ${storage.items.map(item => `<div class="srow">
              <div class="srow-text">
                <span class="srow-title">
                  <span class="chip mini">${esc(item.category)}</span>
                  ${esc(item.name)}
                  <span class="chip mini ${item.inUse ? "ok" : ""}">${item.inUse ? "正在使用" : "闲置"}</span>
                </span>
              </div>
              <div class="srow-ctl">
                <span class="hw-bar-label" style="font-weight:600">${item.diskGb} GB</span>
                ${!item.inUse ? `<button type="button" class="btn compact" data-select-local-btn="${item.stage}:${esc(item.id)}">启用</button>` : ""}
                ${!item.inUse ? `<button type="button" class="link-btn danger" data-delete-model="${item.stage}:${esc(item.id)}">删除</button>` : ""}
              </div>
            </div>`).join("")}
          </div>
        </section>
      </div>
    `;
  }

  function renderAbout() {
    const a = state.about;
    const speech = activeModelMeta("speech");
    const cleaner = activeModelMeta("cleaner");
    const translator = activeModelMeta("translator");
    const updateText = a.updateState === "checking"
      ? "正在检查最新版本…"
      : a.updateState === "latest"
        ? `已是最新版本（${a.version}），检查于 ${a.updateCheckedAt}。`
        : "启动时自动检查更新并在后台静默准备。";

    document.getElementById("mount-about").innerHTML = `
      <header class="spage-head">
        <div>
          <h2>关于与诊断</h2>
          <p>查看软件版本、更新状态以及脱敏运行诊断信息。</p>
        </div>
      </header>

      <section class="about-hero">
        <div class="about-brand">
          <span class="about-logo" aria-hidden="true">li</span>
          <div>
            <h3>LiveLingo</h3>
            <p>默认本机离线的跨应用中英语音听写、多场景润色与翻译工具</p>
          </div>
        </div>
        <span class="chip accent">${esc(a.version)} · Apple Silicon</span>
      </section>

      <section class="sgroup">
        <div class="sgroup-head"><h3 class="sgroup-title">更新与运行诊断</h3></div>
        <div class="scard">
          <div class="srow" id="row-update">
            <div class="srow-text">
              <span class="srow-title">软件更新</span>
              <p role="status" aria-live="polite">${esc(updateText)}</p>
            </div>
            <div class="srow-ctl">
              <button type="button" id="check-update-btn" class="btn compact" ${a.updateState === "checking" ? "disabled" : ""}>${a.updateState === "checking" ? "检查中…" : "检查更新"}</button>
            </div>
          </div>

          <div class="srow">
            <div class="srow-text">
              <span class="srow-title">脱敏运行状态</span>
              <p>仅包含平台、推理后端与当前模型名，绝不含正文、术语、地址或密钥。</p>
            </div>
            <div class="srow-ctl">
              <button type="button" id="copy-diag-btn" class="btn compact outline">${ic("copy")} 复制诊断信息</button>
            </div>
          </div>
        </div>
      </section>

      <details class="s-disclosure" open>
        <summary>
          <span>当前运行快照（只读）</span>
          <span class="s-disclosure-sub">便于提交 Issue 时附带</span>
        </summary>
        <div class="s-disclosure-body">
          <dl class="diag-dl" id="diag-snapshot">
            <dt>版本</dt><dd>${esc(a.version)} (macOS arm64)</dd>
            <dt>推理后端</dt><dd>Metal GPU · 36 GB 统一内存</dd>
            <dt>语音识别</dt><dd>${esc(speech.fullName)} [${esc(speech.sourceLabel)}]</dd>
            <dt>整理与润色</dt><dd>${esc(cleaner.fullName)} [${esc(cleaner.sourceLabel)}]</dd>
            <dt>翻译</dt><dd>${esc(translator.fullName)} [${esc(translator.sourceLabel)}]</dd>
            <dt>隐私状态</dt><dd>${computeCloudStages().length === 0 ? "全部本机处理（0 云端流量）" : `含云端：${computeCloudStages().join("、")}`}</dd>
          </dl>
        </div>
      </details>
    `;
  }

  /* ==================== SIDEBAR BADGES & HEADER STATUS ==================== */
  function updateChromeIndicators() {
    const enabledPresets = state.polish.presets.filter(p => p.enabled).length;
    const lexCount = state.glossary.corrections.length + state.glossary.terms.length;
    document.getElementById("nav-polish-count").textContent = String(enabledPresets);
    document.getElementById("nav-glossary-count").textContent = String(lexCount);

    const dlBadge = document.getElementById("nav-models-badge");
    const mDot = document.getElementById("nav-models-dot");
    const cloudStages = computeCloudStages();
    if (state.models.downloads.length > 0) {
      dlBadge.hidden = false;
      dlBadge.textContent = `${state.models.downloads[0].percent}%`;
      mDot.hidden = true;
    } else {
      dlBadge.hidden = true;
      mDot.hidden = false;
      mDot.className = `s-nav-dot ${cloudStages.length > 0 ? "warn" : "ok"}`;
      mDot.title = cloudStages.length > 0 ? `含云端：${cloudStages.join("、")}` : "全部本机离线";
    }

    const statusPill = document.getElementById("shell-status-pill");
    const residency = computeResidencyGb();
    statusPill.innerHTML = `
      <span class="chip ${cloudStages.length > 0 ? "warn" : "ok"}">
        <span class="dot"></span>
        ${cloudStages.length > 0 ? `含云端 · ${cloudStages.join("、")}` : "全部本机离线"}
      </span>
      <span class="chip">常驻预估 ${residency} / ${state.models.hardware.budgetGb} GB</span>
    `;

    document.getElementById("search-kbd").textContent = state.platform === "mac" ? "⌘K" : "Ctrl+K";
  }

  function applyLanguage(lang) {
    document.documentElement.lang = lang;
    const isEn = lang === "en-US";
    const labels = isEn
      ? { general: "General", voice: "Voice & Translation", polish: "Polish Modes", glossary: "Glossary & Corrections", models: "Models & Services", about: "About", search: "Search settings…", title: "Settings" }
      : { general: "通用", voice: "听写与翻译", polish: "润色场景", glossary: "词库与纠错", models: "模型与服务", about: "关于与诊断", search: "搜索设置项…", title: "设置" };
    for (const [k, txt] of Object.entries(labels)) {
      const navLabel = document.querySelector(`#stab-${k} .s-nav-label`);
      if (navLabel) navLabel.textContent = txt;
    }
    const searchEl = document.getElementById("settings-search");
    if (searchEl) searchEl.placeholder = labels.search;
    const h1 = document.querySelector("#page-settings h1");
    if (h1) h1.textContent = labels.title;
  }

  function applyThemeAndMotion() {
    const t = state.general.theme;
    const effective = t === "Auto"
      ? (matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light")
      : t.toLowerCase();
    document.documentElement.dataset.theme = effective;
    document.documentElement.dataset.motion = state.general.reduceMotion ? "off" : "on";
  }

  function renderAll() {
    applyThemeAndMotion();
    updateChromeIndicators();
    renderGeneral();
    renderVoice();
    renderPolish();
    renderGlossary();
    renderModels();
    renderAbout();
    notify();
  }

  /* ==================== SECTION SWITCH & SEARCH ==================== */
  function showSection(sec, focusSelector) {
    state.section = sec;
    document.querySelectorAll("#settings-nav [data-sec]").forEach(btn => {
      const selected = btn.dataset.sec === sec;
      btn.setAttribute("aria-selected", String(selected));
      btn.toggleAttribute("focusgroupstart", selected);
    });
    document.querySelectorAll("#settings-content [data-panel]").forEach(panel => {
      panel.hidden = panel.dataset.panel === sec ? false : "until-found";
    });
    document.getElementById("settings-content").scrollTop = 0;

    if (focusSelector) {
      requestAnimationFrame(() => {
        const el = document.getElementById(focusSelector);
        if (el) {
          el.scrollIntoView({ behavior: state.general.reduceMotion ? "auto" : "smooth", block: "center" });
          el.classList.add("flash");
          setTimeout(() => el.classList.remove("flash"), 1100);
        }
      });
    }
  }

  function renderSearchResults(query) {
    const box = document.getElementById("settings-search-results");
    const q = query.trim().toLowerCase();
    if (!q) {
      box.hidden = true;
      box.innerHTML = "";
      return;
    }
    const secNames = { general: "通用", voice: "听写与翻译", polish: "润色场景", glossary: "词库与纠错", models: "模型与服务", about: "关于与诊断" };
    const hits = SEARCH_INDEX.filter(item =>
      item.title.toLowerCase().includes(q) || item.kw.toLowerCase().includes(q)
    ).slice(0, 7);

    box.hidden = false;
    if (hits.length === 0) {
      box.innerHTML = `<div class="s-search-empty">未找到包含「${esc(query)}」的设置项</div>`;
      return;
    }
    box.innerHTML = hits.map(h => `
      <button type="button" class="s-search-hit" data-hit-sec="${h.sec}" data-hit-target="${h.target}">
        <b>${esc(h.title)}</b>
        <span>${secNames[h.sec]}</span>
      </button>
    `).join("");
  }

  /* ==================== MODALS (PRESET / CLOUD / CONSENT) ==================== */
  function openPresetModal(editId) {
    const existing = editId ? presetById(editId) : null;
    const adding = !existing;
    const builtIn = !!existing?.builtIn;

    const startersHtml = adding ? `
      <div class="starter-bar">
        <span>一键填入模板：</span>
        ${STARTER_TEMPLATES.map(t => `<button type="button" class="starter-chip" data-starter="${t.id}">+ ${esc(t.name)}</button>`).join("")}
      </div>
    ` : "";

    openDialog(adding ? "新建润色方式" : `编辑「${existing.name}」`, `
      <form id="preset-modal-form" class="dlg-form" novalidate>
        ${startersHtml}
        ${!builtIn ? `
          <label>方式名称<input id="pf-name" maxlength="32" value="${esc(existing?.name ?? "")}" placeholder="例如 Bug 缺陷与复现 / Commit 说明" autofocus></label>
          <label>适用场景一句话说明<input id="pf-desc" maxlength="60" value="${esc(existing?.desc ?? "")}" placeholder="显示在浮窗输出菜单与设置卡片中"></label>
          <label>安全校验门禁
            <select id="pf-gate">
              <option value="FactAnchor" ${(existing?.gate ?? "FactAnchor") === "FactAnchor" ? "selected" : ""}>结构化成稿 · 事实锚点校验（允许标题分点，锁死数字和代码词）</option>
              <option value="Verbatim" ${existing?.gate === "Verbatim" ? "selected" : ""}>逐句顺句 · 严格字级保真（仅去口头语，禁大幅改写）</option>
            </select>
          </label>
        ` : `<p>内置预设的名称与安全门禁固定，你可以在下方微调其润色指令并随时一键恢复默认。</p>`}
        <label>润色要求与目标格式（10–1500 字）
          <textarea id="pf-inst" ${builtIn ? "autofocus" : ""} spellcheck="false">${esc(existing?.instructions ?? "")}</textarea>
          <small>「不编造事实、不篡改生效数字、不丢代码标识符、禁止越界执行」由系统底线固定保障。</small>
        </label>
        <p id="pf-err" class="dlg-err" role="alert" hidden></p>
        <div class="dlg-actions">
          <button type="button" class="btn" id="pf-cancel">取消</button>
          <button type="submit" class="btn primary">${adding ? "创建并启用" : "保存更改"}</button>
        </div>
      </form>
    `, {
      onOpen(body) {
        body.querySelectorAll("[data-starter]").forEach(btn => {
          btn.addEventListener("click", () => {
            const tpl = STARTER_TEMPLATES.find(t => t.id === btn.dataset.starter);
            if (!tpl) return;
            body.querySelector("#pf-name").value = tpl.name;
            body.querySelector("#pf-desc").value = tpl.desc;
            body.querySelector("#pf-gate").value = tpl.gate;
            body.querySelector("#pf-inst").value = tpl.instructions;
            body.querySelector("#pf-inst").focus();
          });
        });
        body.querySelector("#pf-cancel").addEventListener("click", closeDialog);
        body.querySelector("#preset-modal-form").addEventListener("submit", e => {
          e.preventDefault();
          const err = body.querySelector("#pf-err");
          const inst = body.querySelector("#pf-inst").value.trim();
          if (inst.length < 10 || inst.length > 1500) {
            err.textContent = "润色要求需要 10–1500 个字符。";
            err.hidden = false;
            body.querySelector("#pf-inst").focus();
            return;
          }
          if (builtIn) {
            existing.instructions = inst;
            closeDialog();
            renderAll();
            showToast(`已保存「${existing.name}」微调要求`);
            return;
          }
          const name = body.querySelector("#pf-name").value.trim();
          const desc = body.querySelector("#pf-desc").value.trim();
          const gate = body.querySelector("#pf-gate").value;
          if (!name) {
            err.textContent = "请填写方式名称。";
            err.hidden = false;
            body.querySelector("#pf-name").focus();
            return;
          }
          if (adding) {
            state.polish.presets.push({
              id: `custom-${++seq}`,
              name,
              pill: `润色 · ${name}`,
              icon: "sparkle",
              builtIn: false,
              enabled: true,
              gate,
              desc,
              defaultInstructions: inst,
              instructions: inst
            });
            showToast(`已添加「${name}」并显示在浮窗菜单`);
          } else {
            existing.name = name;
            existing.pill = `润色 · ${name}`;
            existing.desc = desc;
            existing.gate = gate;
            existing.instructions = inst;
            showToast(`已更新「${name}」`);
          }
          closeDialog();
          renderAll();
        });
      }
    });
  }

  function openCloudConsent(stage, endpoint) {
    const isSpeech = stage === "speech";
    const stageLabel = { speech: "语音识别", cleaner: "整理与润色", translator: "翻译" }[stage];
    openDialog(`同意云端${stageLabel}？`, `
      <p>${isSpeech
        ? `开启后，麦克风语音和导入的录音将发送至「<b>${esc(endpoint.name)}</b>」（<code>${esc(endpoint.host)}</code>）进行识别。`
        : `开启后，该环节的文本内容将发送至「<b>${esc(endpoint.name)}</b>」（<code>${esc(endpoint.host)}</code>）；麦克风原始音频绝不上传。`}</p>
      <div class="dlg-actions">
        <button type="button" class="btn" id="consent-cancel" autofocus>保持本地</button>
        <button type="button" class="btn primary" id="consent-ok">同意并启用</button>
      </div>
    `, {
      onOpen(body) {
        body.querySelector("#consent-cancel").addEventListener("click", closeDialog);
        body.querySelector("#consent-ok").addEventListener("click", () => {
          state.models.active[stage] = { source: "cloud", id: endpoint.id };
          closeDialog();
          renderAll();
          showToast(`${stageLabel}已切换至云端：${endpoint.name}`);
        });
      }
    });
  }

  function openCloudEndpointModal(stage, editId) {
    const isSpeech = stage === "speech";
    const list = isSpeech ? state.models.cloudEndpoints.speech : state.models.cloudEndpoints.text;
    const existing = editId ? list.find(e => e.id === editId) : null;
    const adding = !existing;

    openDialog(adding ? `添加${isSpeech ? "云端语音" : "云端文本"}服务` : `编辑「${existing.name}」`, `
      <form id="ep-modal-form" class="dlg-form" novalidate>
        ${adding && isSpeech ? `<div class="starter-bar">
          <span>推荐快速填入：</span>
          <button type="button" class="starter-chip" id="fill-qwen-asr">+ 通义百炼 Qwen-Audio-3.1-ASR</button>
        </div>` : ""}
        <label>服务名称<input id="ep-name" value="${esc(existing?.name ?? "")}" placeholder="${isSpeech ? "例如 通义百炼 · Qwen-Audio-3.1-ASR" : "例如 Foundry · 东亚"}" autofocus></label>
        <label>连接地址（Base URL）<input id="ep-url" type="url" value="${esc(existing?.url ?? "")}" placeholder="${isSpeech ? "https://dashscope.aliyuncs.com/api/v1" : "https://.../v1"}">
          <small>必须使用 https（或本机回环 http）</small>
        </label>
        <label>模型 / 部署名称<input id="ep-model" value="${esc(existing?.model ?? "")}" placeholder="${isSpeech ? "qwen-audio-3.1-asr-flash-message" : "gpt-4o-mini"}"></label>
        <label>API 密钥<input id="ep-key" type="password" placeholder="${existing?.hasKey ? "已保存在钥匙串，留空则沿用原密钥" : "sk-..."}">
          <small>仅存入系统钥匙串，不写入配置文件。</small>
        </label>
        <p id="ep-err" class="dlg-err" role="alert" hidden></p>
        <div class="dlg-actions">
          <button type="button" class="btn" id="ep-cancel">取消</button>
          <button type="submit" class="btn primary">${adding ? "添加" : "保存"}</button>
        </div>
      </form>
    `, {
      onOpen(body) {
        body.querySelector("#fill-qwen-asr")?.addEventListener("click", () => {
          body.querySelector("#ep-name").value = "通义百炼 · Qwen-Audio-3.1-ASR";
          body.querySelector("#ep-url").value = "https://dashscope.aliyuncs.com/api/v1";
          body.querySelector("#ep-model").value = "qwen-audio-3.1-asr-flash-message";
          body.querySelector("#ep-key").focus();
        });
        body.querySelector("#ep-cancel").addEventListener("click", closeDialog);
        body.querySelector("#ep-modal-form").addEventListener("submit", e => {
          e.preventDefault();
          const name = body.querySelector("#ep-name").value.trim();
          const url = body.querySelector("#ep-url").value.trim();
          const model = body.querySelector("#ep-model").value.trim();
          const key = body.querySelector("#ep-key").value.trim();
          const err = body.querySelector("#ep-err");
          let host = "";
          try {
            const parsed = new URL(url);
            if (parsed.protocol !== "https:" && parsed.protocol !== "wss:" && parsed.hostname !== "127.0.0.1" && parsed.hostname !== "localhost") {
              throw new Error("protocol");
            }
            host = parsed.host;
          } catch {
            err.textContent = "请填写有效的 https:// 连接地址。";
            err.hidden = false;
            return;
          }
          if (!name || !model || (adding && !key)) {
            err.textContent = "请完整填写名称、模型名与 API 密钥。";
            err.hidden = false;
            return;
          }
          if (adding) {
            list.push({ id: `ep-${++seq}`, name, url, host, model, hasKey: true });
            showToast(`已添加云端服务「${name}」`);
          } else {
            existing.name = name;
            existing.url = url;
            existing.host = host;
            existing.model = model;
            if (key) existing.hasKey = true;
            showToast(`已更新云端服务「${name}」`);
          }
          closeDialog();
          renderAll();
        });
      }
    });
  }

  /* ==================== SIMULATED MODEL DOWNLOAD ==================== */
  function startDownload(stage, modelId) {
    const opt = state.models.localCatalog[stage].find(x => x.id === modelId);
    if (!opt || state.models.downloads.some(d => d.id === modelId)) return;
    const stageLabels = { speech: "语音识别", cleaner: "整理与润色", translator: "翻译" };
    const task = { id: modelId, stage, name: opt.name.split(" · ")[0], category: stageLabels[stage], percent: 8, totalGb: opt.diskGb };
    state.models.downloads.push(task);
    renderAll();
    showToast(`开始后台下载 ${task.name}，完成后将自动启用`);

    task.timer = setInterval(() => {
      task.percent = Math.min(100, task.percent + 23);
      if (task.percent >= 100) {
        clearInterval(task.timer);
        state.models.downloads = state.models.downloads.filter(d => d.id !== modelId);
        opt.installed = true;
        state.models.active[stage] = { source: "local", id: modelId };
        renderAll();
        showToast(`${task.name} 下载完成并已自动启用`);
      } else {
        renderAll();
      }
    }, 500);
  }

  function cancelDownload(modelId) {
    const task = state.models.downloads.find(d => d.id === modelId);
    if (task?.timer) clearInterval(task.timer);
    state.models.downloads = state.models.downloads.filter(d => d.id !== modelId);
    renderAll();
    showToast("已取消模型下载");
  }

  /* ==================== EVENT WIRING ==================== */
  function bindEvents() {
    // Sidebar tablist clicks & keyboard arrows
    const nav = document.getElementById("settings-nav");
    nav.addEventListener("click", e => {
      const btn = e.target.closest("[data-sec]");
      if (!btn) return;
      showSection(btn.dataset.sec);
    });
    nav.addEventListener("keydown", e => {
      const tabs = [...nav.querySelectorAll("[data-sec]")];
      const idx = tabs.indexOf(document.activeElement);
      if (idx < 0) return;
      let next = -1;
      if (e.key === "ArrowDown" || e.key === "ArrowRight") next = (idx + 1) % tabs.length;
      else if (e.key === "ArrowUp" || e.key === "ArrowLeft") next = (idx - 1 + tabs.length) % tabs.length;
      else if (e.key === "Home") next = 0;
      else if (e.key === "End") next = tabs.length - 1;
      if (next >= 0) {
        e.preventDefault();
        tabs[next].focus();
        showSection(tabs[next].dataset.sec);
      }
    });

    // Global Cmd/Ctrl+K search shortcut
    const searchInput = document.getElementById("settings-search");
    document.addEventListener("keydown", e => {
      if ((e.metaKey || e.ctrlKey) && e.key.toLowerCase() === "k" && !document.getElementById("page-settings").hidden) {
        e.preventDefault();
        searchInput.focus();
        searchInput.select();
      }
    });
    searchInput.addEventListener("input", () => renderSearchResults(searchInput.value));
    searchInput.addEventListener("keydown", e => {
      if (e.key === "Escape") {
        searchInput.value = "";
        renderSearchResults("");
        searchInput.blur();
      }
    });
    document.getElementById("settings-search-results").addEventListener("click", e => {
      const hit = e.target.closest("[data-hit-sec]");
      if (!hit) return;
      const sec = hit.dataset.hitSec;
      const target = hit.dataset.hitTarget;
      searchInput.value = "";
      renderSearchResults("");
      showSection(sec, target);
    });

    // Find-in-page auto-reveal for hidden="until-found" panels
    document.getElementById("settings-content").addEventListener("beforematch", e => {
      const panel = e.target.closest("[data-panel]");
      if (panel) showSection(panel.dataset.panel);
    });

    // Delegated interactions inside settings content
    const content = document.getElementById("settings-content");

    content.addEventListener("click", e => {
      const target = e.target;

      // Hotkey recording
      const hk = target.closest("#hotkey-btn");
      if (hk) {
        state.general.hotkeyRecording = !state.general.hotkeyRecording;
        renderGeneral();
        document.getElementById("hotkey-btn")?.focus();
        return;
      }

      // Apps clear / forget
      if (target.closest("#apps-clear-btn")) {
        state.general.apps = [];
        renderAll();
        showToast("已清除全部应用偏好记忆");
        return;
      }
      const forgetBtn = target.closest("[data-forget-app]");
      if (forgetBtn) {
        const id = forgetBtn.dataset.forgetApp;
        state.general.apps = state.general.apps.filter(a => a.id !== id);
        renderAll();
        showToast("已忘记该应用的偏好");
        return;
      }
      if (target.closest("#clear-recent-btn")) {
        state.general.recentCount = 0;
        window.dispatchEvent(new CustomEvent("ll:clear-recent"));
        renderAll();
        showToast("已清除内存中保留的最近草稿");
        return;
      }

      // Mic test toggle
      if (target.closest("#test-mic-btn")) {
        state.voice.testingMic = !state.voice.testingMic;
        renderVoice();
        return;
      }

      // Cross-section jump buttons
      const jumpSec = target.closest("[data-jump-sec]");
      if (jumpSec) {
        showSection(jumpSec.dataset.jumpSec);
        return;
      }
      const jumpStage = target.closest("[data-jump-stage]");
      if (jumpStage) {
        state.models.stageTab = jumpStage.dataset.jumpStage;
        renderModels();
        showSection("models");
        return;
      }

      // Polish preset actions
      if (target.closest("#add-preset-btn")) {
        openPresetModal(null);
        return;
      }
      const editPreset = target.closest("[data-preset-edit]");
      if (editPreset) {
        openPresetModal(editPreset.dataset.presetEdit);
        return;
      }
      const resetPreset = target.closest("[data-preset-reset]");
      if (resetPreset) {
        const p = presetById(resetPreset.dataset.presetReset);
        p.instructions = p.defaultInstructions;
        renderAll();
        showToast(`已恢复「${p.name}」默认要求`);
        return;
      }
      const delPreset = target.closest("[data-preset-delete]");
      if (delPreset) {
        const p = presetById(delPreset.dataset.presetDelete);
        openDialog(`删除「${p.name}」？`, `
          <p>删除后浮窗菜单将不再提供该润色方式，记住该方式的应用会自动回退到默认起点。</p>
          <div class="dlg-actions">
            <button type="button" class="btn" id="del-keep" autofocus>保留</button>
            <button type="button" class="btn danger" id="del-go">确认删除</button>
          </div>
        `, {
          onOpen(body) {
            body.querySelector("#del-keep").addEventListener("click", closeDialog);
            body.querySelector("#del-go").addEventListener("click", () => {
              state.polish.presets = state.polish.presets.filter(x => x.id !== p.id);
              if (state.polish.defaultPolishMode === p.id) state.polish.defaultPolishMode = "natural";
              closeDialog();
              renderAll();
              showToast(`已删除「${p.name}」`);
            });
          }
        });
        return;
      }
      if (target.closest("#reset-global-prompt")) {
        state.polish.polishPrompt = DEFAULT_POLISH_PROMPT;
        state.polish.polishPromptInvalid = false;
        renderAll();
        showToast("已恢复基础整理默认提示词");
        return;
      }

      // Glossary sub-tabs & deletes
      const lexTab = target.closest("[data-lex-tab]");
      if (lexTab) {
        state.glossary.tab = lexTab.dataset.lexTab;
        renderGlossary();
        return;
      }
      const delCorr = target.closest("[data-del-corr]");
      if (delCorr) {
        state.glossary.corrections = state.glossary.corrections.filter(c => c.id !== delCorr.dataset.delCorr);
        renderAll();
        return;
      }
      const delTerm = target.closest("[data-del-term]");
      if (delTerm) {
        state.glossary.terms = state.glossary.terms.filter(t => t.id !== delTerm.dataset.delTerm);
        renderAll();
        return;
      }

      // Models: stage tabs & source tabs
      const modelTab = target.closest("[data-model-tab]");
      if (modelTab) {
        state.models.stageTab = modelTab.dataset.modelTab;
        renderModels();
        return;
      }
      const srcTab = target.closest("[data-source-tab]");
      if (srcTab) {
        state.models.sourceTab[srcTab.dataset.stage] = srcTab.dataset.sourceTab;
        renderModels();
        return;
      }

      // Models: recommended / disable all cloud / hot host
      if (target.closest("#apply-recommended-btn")) {
        state.models.active.speech = { source: "local", id: "qwen3-asr-1.7b" };
        state.models.active.cleaner = { source: "local", id: "qwen3.5-9b" };
        state.models.active.translator = { source: "local", id: "index-translate-9b" };
        renderAll();
        showToast("已恢复本机 32GB+ 最佳匹配方案");
        return;
      }
      if (target.closest("#disable-all-cloud-btn")) {
        if (state.models.active.speech.source === "cloud") state.models.active.speech = { source: "local", id: "qwen3-asr-1.7b" };
        if (state.models.active.cleaner.source === "cloud") state.models.active.cleaner = { source: "local", id: "qwen3.5-9b" };
        if (state.models.active.translator.source === "cloud") state.models.active.translator = { source: "local", id: "index-translate-9b" };
        renderAll();
        showToast("已将全部阶段切回本机离线运行");
        return;
      }
      if (target.closest("#apply-hot-host-btn")) {
        state.models.active.cleaner = { source: "host", hostId: "lmstudio", modelKey: "qwen3.5-9b-instruct" };
        state.models.active.translator = { source: "host", hostId: "lmstudio", modelKey: "qwen3.5-9b-instruct" };
        state.models.hotHostDismissed = true;
        renderAll();
        showToast("整理与翻译已一键复用 LM Studio 常驻模型（0 额外内存）");
        return;
      }
      if (target.closest("#dismiss-hot-host-btn")) {
        state.models.hotHostDismissed = true;
        renderModels();
        return;
      }

      // Models: back to local
      const backLocal = target.closest("[data-back-local]");
      if (backLocal) {
        const st = backLocal.dataset.backLocal;
        const firstInstalled = state.models.localCatalog[st].find(x => x.installed) ?? state.models.localCatalog[st][0];
        state.models.active[st] = { source: "local", id: firstInstalled.id };
        state.models.sourceTab[st] = "local";
        renderAll();
        showToast(`已切回本机模型：${firstInstalled.name.split(" · ")[0]}`);
        return;
      }

      // Models: download / cancel / delete / select local
      const dlBtn = target.closest("[data-download-model]");
      if (dlBtn) {
        const [st, id] = dlBtn.dataset.downloadModel.split(":");
        startDownload(st, id);
        return;
      }
      const cancelDl = target.closest("[data-cancel-dl]");
      if (cancelDl) {
        cancelDownload(cancelDl.dataset.cancelDl);
        return;
      }
      const delModel = target.closest("[data-delete-model]");
      if (delModel) {
        const [st, id] = delModel.dataset.deleteModel.split(":");
        const opt = state.models.localCatalog[st].find(x => x.id === id);
        if (opt) {
          opt.installed = false;
          renderAll();
          showToast(`已删除 ${opt.name.split(" · ")[0]}，释放 ${opt.diskGb} GB`);
        }
        return;
      }
      const selectLocalBtn = target.closest("[data-select-local-btn]");
      if (selectLocalBtn) {
        const [st, id] = selectLocalBtn.dataset.selectLocalBtn.split(":");
        state.models.active[st] = { source: "local", id };
        renderAll();
        showToast("已切换启用本机模型");
        return;
      }
      const selectLocalCard = !target.closest("button, a") && target.closest("[data-select-local]");
      if (selectLocalCard && selectLocalCard.dataset.selectLocal) {
        const [st, id] = selectLocalCard.dataset.selectLocal.split(":");
        state.models.active[st] = { source: "local", id };
        renderAll();
        showToast("已切换启用本机模型");
        return;
      }

      // Local host actions
      if (target.closest("#refresh-hosts-btn")) {
        state.models.localHostRefreshing = true;
        renderModels();
        setTimeout(() => {
          state.models.localHostRefreshing = false;
          renderModels();
          showToast("已刷新本机宿主状态（检测到 2 个运行中）");
        }, 500);
        return;
      }
      if (target.closest("#toggle-custom-host-btn")) {
        state.models.addingCustomHost = !state.models.addingCustomHost;
        renderModels();
        return;
      }
      const startHost = target.closest("[data-start-host]");
      if (startHost) {
        const h = state.models.localHosts.find(x => x.hostId === startHost.dataset.startHost);
        if (h) {
          h.status = "running";
          h.statusBadge = "已连接";
          h.models = [{ key: "qwen3.5-9b-bionic", name: "qwen3.5-9b-q4km", meta: "Q4_K_M · Bionic 共享池", suitability: "verified", suitabilityBadge: "★ 评测推荐", isLoaded: true, loadedBadge: "已驻留 · 0 额外内存" }];
          renderAll();
          showToast(`已启动 ${h.name} 本地服务`);
        }
        return;
      }
      const removeHost = target.closest("[data-remove-host]");
      if (removeHost) {
        state.models.localHosts = state.models.localHosts.filter(x => x.hostId !== removeHost.dataset.removeHost);
        renderAll();
        return;
      }
      const hostSelect = target.closest("[data-select-host-btn]") || (!target.closest("button") && target.closest("[data-select-host]"));
      if (hostSelect) {
        const raw = hostSelect.dataset.selectHostBtn || hostSelect.dataset.selectHost;
        if (raw) {
          const [st, hostId, modelKey] = raw.split(":");
          state.models.active[st] = { source: "host", hostId, modelKey };
          renderAll();
          showToast(`已切换至本机宿主模型：${modelKey}`);
          return;
        }
      }

      // Cloud actions
      const cloudFormBtn = target.closest("[data-open-cloud-form]");
      if (cloudFormBtn) {
        const mode = cloudFormBtn.dataset.openCloudForm;
        const st = cloudFormBtn.dataset.stage;
        openCloudEndpointModal(st, mode.startsWith("edit:") ? mode.slice(5) : null);
        return;
      }
      const cloudDel = target.closest("[data-delete-cloud]");
      if (cloudDel) {
        const [st, id] = cloudDel.dataset.deleteCloud.split(":");
        const list = st === "speech" ? state.models.cloudEndpoints.speech : state.models.cloudEndpoints.text;
        const ep = list.find(e => e.id === id);
        if (!ep) return;
        openDialog(`删除「${ep.name}」？`, `
          <p>同时移除钥匙串中的 API 密钥；正在使用该端点的阶段将自动切回本机模型。</p>
          <div class="dlg-actions">
            <button type="button" class="btn" id="ep-keep" autofocus>保留</button>
            <button type="button" class="btn danger" id="ep-del-ok">确认删除</button>
          </div>
        `, {
          onOpen(body) {
            body.querySelector("#ep-keep").addEventListener("click", closeDialog);
            body.querySelector("#ep-del-ok").addEventListener("click", () => {
              const targetKey = st === "speech" ? "speech" : "text";
              state.models.cloudEndpoints[targetKey] = list.filter(x => x.id !== id);
              for (const s of ["speech", "cleaner", "translator"]) {
                if (state.models.active[s].source === "cloud" && state.models.active[s].id === id) {
                  state.models.active[s] = { source: "local", id: state.models.localCatalog[s][0].id };
                }
              }
              closeDialog();
              renderAll();
              showToast(`已删除云端服务「${ep.name}」`);
            });
          }
        });
        return;
      }
      const cloudSelect = target.closest("[data-select-cloud-btn]") || (!target.closest("button") && target.closest("[data-select-cloud]"));
      if (cloudSelect) {
        const raw = cloudSelect.dataset.selectCloudBtn || cloudSelect.dataset.selectCloud;
        if (raw) {
          const [st, id] = raw.split(":");
          const list = st === "speech" ? state.models.cloudEndpoints.speech : state.models.cloudEndpoints.text;
          const ep = list.find(x => x.id === id);
          if (ep) openCloudConsent(st, ep);
          return;
        }
      }

      // Storage actions
      if (target.closest("#remove-legacy-btn")) {
        const freed = state.models.legacyModels.sizeGb;
        state.models.legacyModels = { count: 0, sizeGb: 0 };
        renderAll();
        showToast(`已清理旧版本模型，释放 ${freed} GB`);
        return;
      }
      if (target.closest("#copy-model-path-btn")) {
        navigator.clipboard?.writeText("~/Library/Application Support/LiveLingo/models").catch(() => {});
        showToast("已复制模型存储目录路径");
        return;
      }

      // About actions
      if (target.closest("#check-update-btn")) {
        state.about.updateState = "checking";
        renderAbout();
        setTimeout(() => {
          state.about.updateState = "latest";
          const d = new Date();
          state.about.updateCheckedAt = `${String(d.getHours()).padStart(2, "0")}:${String(d.getMinutes()).padStart(2, "0")}`;
          renderAbout();
          showToast("当前已是最新版本");
        }, 600);
        return;
      }
      if (target.closest("#copy-diag-btn")) {
        const text = document.getElementById("diag-snapshot")?.innerText ?? "";
        navigator.clipboard?.writeText(text).catch(() => {});
        showToast("已复制脱敏运行状态");
      }
    });

    // Hotkey capture when recording
    content.addEventListener("keydown", e => {
      if (!state.general.hotkeyRecording) return;
      if (e.key === "Tab") { state.general.hotkeyRecording = false; renderGeneral(); return; }
      e.preventDefault();
      if (e.key === "Escape") { state.general.hotkeyRecording = false; renderGeneral(); return; }
      const mods = [e.ctrlKey && "Ctrl", e.altKey && "Alt", e.shiftKey && "Shift", e.metaKey && "Cmd"].filter(Boolean);
      let key = "";
      if (/^Key[A-Z]$/.test(e.code)) key = e.code.slice(3);
      else if (/^Digit[0-9]$/.test(e.code)) key = e.code.slice(5);
      else if (e.code === "Space") key = "Space";
      if (!key || mods.length === 0) return;
      state.general.hotkey = [...mods, key].join("+");
      state.general.hotkeyRecording = false;
      renderAll();
      showToast(`呼出快捷键已更新为 ${formatHotkeyDisplay(state.general.hotkey)}`);
    });

    // Input & change delegation
    content.addEventListener("change", e => {
      const t = e.target;
      if (t.name === "commitKey") { state.general.commitKey = t.value; renderAll(); return; }
      if (t.name === "theme") { state.general.theme = t.value; renderAll(); return; }
      if (t.name === "language") {
        state.general.language = t.value;
        applyLanguage(t.value);
        renderAll();
        showToast(t.value === "en-US" ? "Switched UI language to English" : "已切换界面语言为简体中文");
        return;
      }
      if (t.id === "opt-motion") { state.general.reduceMotion = t.checked; renderAll(); return; }
      if (t.id === "opt-remember") { state.general.rememberChoices = t.checked; renderAll(); return; }
      if (t.dataset.appChoice) {
        const app = state.general.apps.find(a => a.id === t.dataset.appChoice);
        if (app) { app.choice = t.value; app.used = "刚刚"; renderAll(); }
        return;
      }
      if (t.id === "opt-mic") { state.voice.microphone = t.value; renderAll(); return; }
      if (t.id === "opt-autodictate") { state.voice.autoDictate = t.checked; renderAll(); return; }
      if (t.id === "opt-preload") { state.voice.preloadSpeech = t.checked; renderAll(); return; }
      if (t.id === "opt-cleanup") {
        state.voice.cleanup = t.checked;
        if (!t.checked && state.voice.defaultOutput === "Clean") state.voice.defaultOutput = "Original";
        renderAll();
        return;
      }
      if (t.name === "defaultOutput") { state.voice.defaultOutput = t.value; renderAll(); return; }
      if (t.name === "direction") { state.voice.direction = t.value; renderAll(); return; }
      if (t.id === "opt-default-polish") { state.polish.defaultPolishMode = t.value; renderAll(); return; }
      if (t.id === "opt-smart-host") { state.polish.smartHostPolish = t.checked; renderAll(); return; }
      if (t.dataset.presetToggle) {
        const p = presetById(t.dataset.presetToggle);
        p.enabled = t.checked;
        renderAll();
        return;
      }
      if (t.id === "opt-builtin-glossary") { state.glossary.builtInGlossary = t.checked; renderAll(); return; }
    });

    // Global polish prompt save on blur
    content.addEventListener("focusout", e => {
      if (e.target.id === "global-polish-prompt") {
        const val = e.target.value.trim();
        if (val.length < 10 || val.length > 1500) {
          state.polish.polishPromptInvalid = true;
          renderPolish();
        } else {
          state.polish.polishPrompt = val;
          state.polish.polishPromptInvalid = false;
          renderPolish();
          showToast("基础整理全局提示词已保存");
        }
      }
    });

    // Inline form submissions (glossary & custom local host)
    content.addEventListener("submit", e => {
      if (e.target.id === "corr-add-form") {
        e.preventDefault();
        const spoken = document.getElementById("corr-spoken").value.trim();
        const canonical = document.getElementById("corr-canonical").value.trim();
        if (!spoken || !canonical || spoken === canonical) {
          showToast("请填写不同的「误识别词」与「正确写法」");
          return;
        }
        state.glossary.corrections.unshift({ id: `c-${++seq}`, spoken, canonical });
        renderAll();
        showToast(`已添加口音纠错：${spoken} ⇒ ${canonical}`);
        return;
      }
      if (e.target.id === "term-add-form") {
        e.preventDefault();
        const source = document.getElementById("term-source").value.trim();
        const rawTarget = document.getElementById("term-target").value.trim();
        if (!source) {
          showToast("请填写原词");
          return;
        }
        const target = rawTarget || source;
        state.glossary.terms.unshift({ id: `t-${++seq}`, source, target });
        renderAll();
        showToast(`已添加术语：${source} → ${target}`);
        return;
      }
      if (e.target.id === "custom-host-form") {
        e.preventDefault();
        const name = document.getElementById("ch-name").value.trim() || "本机 OpenAI 服务";
        const url = document.getElementById("ch-url").value.trim();
        try {
          const parsed = new URL(url);
          if (parsed.hostname !== "127.0.0.1" && parsed.hostname !== "localhost") throw new Error("loopback");
          state.models.localHosts.push({
            hostId: `custom-${++seq}`,
            name,
            address: parsed.host,
            status: "running",
            statusBadge: "已连接",
            isCustom: true,
            models: [{ key: "local-qwen3.5", name: "local-qwen3.5", meta: "自定义回环端口", suitability: "compatible", suitabilityBadge: "兼容可用", isLoaded: true, loadedBadge: "已驻留 · 0 额外内存" }]
          });
          state.models.addingCustomHost = false;
          renderAll();
          showToast(`已连接本机端口：${parsed.host}`);
        } catch {
          showToast("安全限制：本机宿主仅允许 127.0.0.1 或 localhost 回环地址");
        }
      }
    });

    // Dialog close button & click-outside fallback when closedby is unsupported
    document.getElementById("dlg-close").addEventListener("click", closeDialog);
    const dlgEl = document.getElementById("dlg");
    let pressedOutside = false;
    const isOutside = e => {
      const r = dlgEl.getBoundingClientRect();
      return e.clientX < r.left || e.clientX > r.right || e.clientY < r.top || e.clientY > r.bottom;
    };
    dlgEl.addEventListener("pointerdown", e => { pressedOutside = isOutside(e); });
    dlgEl.addEventListener("pointerup", e => {
      if (pressedOutside && isOutside(e) && dlgEl.getAttribute("closedby") === "any") closeDialog();
    });
  }

  window.LiveLingoSettings = {
    state,
    presetById,
    activeModelMeta,
    computeCloudStages,
    formatHotkeyDisplay,
    showSection,
    renderAll,
    showToast,
    openDialog,
    closeDialog,
    subscribe(fn) { listeners.add(fn); }
  };

  function applyUrlState() {
    const params = new URLSearchParams(location.search);
    const theme = params.get("theme");
    if (theme === "dark" || theme === "light" || theme === "auto") {
      state.general.theme = theme[0].toUpperCase() + theme.slice(1);
    }
    const stage = params.get("stage");
    if (stage && ["speech", "cleaner", "translator", "storage"].includes(stage)) {
      state.models.stageTab = stage;
    }
    const src = params.get("source");
    if (src && ["local", "host", "cloud"].includes(src) && state.models.stageTab !== "storage") {
      state.models.sourceTab[state.models.stageTab] = src;
    }
    const lex = params.get("lex");
    if (lex === "terms" || lex === "corrections") {
      state.glossary.tab = lex;
    }
    const sec = params.get("sec");
    if (sec && ["general", "voice", "polish", "glossary", "models", "about"].includes(sec)) {
      state.section = sec;
    }
  }

  document.addEventListener("DOMContentLoaded", () => {
    applyUrlState();
    bindEvents();
    renderAll();
    showSection(state.section);
  });
})();
