(function () {
  "use strict";
  const { Draft, splitSentences, routeFor, checkFactAnchors } = window.LiveLingoDraft;
  const $ = id => document.getElementById(id);
  const esc = text => String(text ?? "").replace(/[&<>"']/g, ch => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[ch]);
  const icon = name => `<svg class="ic" aria-hidden="true"><use href="#i-${name}"/></svg>`;

  /* ---------- target apps & context ---------- */
  const TARGETS = {
    agent: {
      name: "Cursor",
      menubar: "Cursor",
      title: "Cursor — Agent",
      chip: "Agent",
      smartPreset: "prompt",
      placeholder: "给 Agent 发指令…",
      context: `<p class="bubble me">我们继续看登录重试和缓存超时这块。</p><p class="bubble ai">代码已读完。请直接说出你的目标、不能碰的接口边界以及是否直接改代码。</p>`
    },
    linear: {
      name: "Linear",
      menubar: "Linear",
      title: "Linear — 新建任务清单",
      chip: "Linear",
      smartPreset: "todo",
      placeholder: "粘贴待办清单（支持 - [ ] Markdown）…",
      context: `<div class="context-sheet"><b>Sprint 24 · 稳定性专项</b><p>识别到任务管理工具：默认落位「润色 · 待办与派单」，自动过滤口述废案并提取 - [ ] 清单。</p></div>`
    },
    "report-doc": {
      name: "飞书文档",
      menubar: "飞书文档",
      title: "飞书文档 — 架构评审周报",
      chip: "飞书文档",
      smartPreset: "report",
      placeholder: "写入评审结论、关键决策与后续行动…",
      context: `<div class="context-sheet"><b>本周技术评审纪要</b><p>识别到文档协同宿主：默认落位「润色 · 汇报与纪要」，按结论先行（BLUF）重组流水账。</p></div>`
    },
    notes: {
      name: "Slack",
      menubar: "Slack",
      title: "Slack — #eng-backend",
      chip: "Slack",
      smartPreset: null,
      placeholder: "发消息到 #eng-backend…",
      context: `<p class="bubble ai">评审刚结束，麻烦把刚才定的 Redis 和 PR 处理结论同步一下？</p>`
    }
  };

  /* ---------- built-in & custom polish presets ---------- */
  const BUILTIN_PRESETS = [
    {
      id: "prompt",
      name: "AI 任务指令",
      pill: "润色 · AI 任务指令",
      key: "1",
      builtIn: true,
      enabled: true,
      gate: "fact-anchor",
      forbidExecution: true,
      desc: "倒叙归位、剪除改口废案、分离硬性护栏（短话免套壳）",
      defaultPrompt: "你是 AI 任务指令编译编辑。将发散口述提炼为高信噪比、无歧义的下游 AI 指令：\n1. 废案剪枝：说话人中途自我否定的旧方案与旧数值彻底剔除；\n2. 目标置顶：把段尾才说出的真实动作提炼到最前；\n3. 正负护栏分离：所有「不要改/禁止/只改」独立列出；\n4. 自适应篇幅：1–2 句短纠偏直接输出精炼段落，严禁硬套空标题；复杂任务按实际要素选用 ### 任务目标 / ### 上下文与现状 / ### 硬性约束 / ### 交付要求；严禁回答问题、严禁编写实现代码、严禁注入角色扮演套话。"
    },
    {
      id: "natural",
      name: "口语顺句",
      pill: "口语顺句",
      key: "2",
      builtIn: true,
      enabled: true,
      gate: "verbatim",
      forbidExecution: false,
      desc: "接碎句、去口头语与改口残片，保留真人语气",
      defaultPrompt: "1. 把停顿切碎的半句接成完整句，删去口头语（呃、嗯、那个、就是）、重复与自我改口前的废话；\n2. 结合上下文修正同音字与技术名词误识别；\n3. 保持说话人自然口吻，不加标题列表，不改成公文腔。"
    },
    {
      id: "formal",
      name: "得体书面",
      pill: "润色 · 得体书面",
      key: "3",
      builtIn: true,
      enabled: true,
      gate: "verbatim",
      forbidExecution: false,
      desc: "邮件、PR 评论、克制书面段落（零公文黑话）",
      defaultPrompt: "1. 去除口语助词与松散句式，改写为简洁、克制、得体的书面段落；\n2. 完整保留第一人称立场、条件限定、生效数字与代码标识符；\n3. 严禁使用「赋能、抓手、闭环、综上所述」等公文套话。"
    },
    {
      id: "todo",
      name: "待办与派单",
      pill: "润色 · 待办与派单",
      key: "4",
      builtIn: true,
      enabled: true,
      gate: "fact-anchor",
      forbidExecution: false,
      desc: "过滤闲聊废案，提取 - [ ] 行动项、负责人与条件",
      defaultPrompt: "从口述中剥离背景铺垫与已否决设想，只提取明确可执行的行动项：\n- 格式：- [ ] 动词开头事项（如有明确提及负责人或前置条件，在括号内注明）\n- 原话未提及截止时间或负责人时严禁编造。"
    },
    {
      id: "report",
      name: "汇报与纪要",
      pill: "润色 · 汇报与纪要",
      key: "5",
      builtIn: true,
      enabled: true,
      gate: "fact-anchor",
      forbidExecution: false,
      desc: "结论先行（BLUF），归纳关键决策与下一步",
      defaultPrompt: "按金字塔结论先行（BLUF）将流水账重组为决策简报：\n- ### 核心结论（首句直出当前状态或拍板结果）\n- ### 关键决策与技术边界（保留全部生效数字、组件名与正反条件）\n- ### 下一步行动（明确谁在什么条件下跟进）"
    }
  ];

  const STARTER_TEMPLATES = [
    {
      name: "Bug 缺陷与复现",
      desc: "提炼复现步骤、预期行为与实际报错",
      gate: "fact-anchor",
      prompt: "将口述整理为结构化缺陷报告：\n- ### 复现步骤（按顺序列出触发操作与参数）\n- ### 预期行为\n- ### 实际现象与影响范围\n完整保留所有数值、错误码与代码组件名，未提及的环境信息严禁编造。"
    },
    {
      name: "需求规格（Spec/AC）",
      desc: "提炼背景问题、非目标边界与验收标准",
      gate: "fact-anchor",
      prompt: "面向 Spec-Driven AI 研发，将口述整理为轻量规格说明：\n- ### 解决的问题\n- ### 变更范围与非目标（Out of Scope）\n- ### 验收标准（仅基于原话已声明的条件，不凭空扩写功能）。"
    },
    {
      name: "Commit 说明",
      desc: "生成简明提交标题与核心改动要点",
      gate: "fact-anchor",
      prompt: "将口述提炼为规范的代码提交说明：\n- 首行：简明概括本次改动意图（不超过 50 字）\n- 空一行后用 `- ` 列出核心变更点与兼容边界（保留精确接口名与数值）。"
    }
  ];

  const presets = BUILTIN_PRESETS.map(item => ({ ...item, prompt: item.defaultPrompt }));
  let customSeq = 0;

  /* ---------- preset corpus: the prototype runs no external model ---------- */
  const ZH_EN = [
    {
      raw: "嗯，这个 PR 先不要合并，等 CI 跑完再说。",
      clean: "这个 PR 先不要合并，等 CI 跑完再说。",
      formal: "当前 PR 请暂缓合并，待 CI 流水线全部执行通过后再行处理。",
      todoItem: "- [ ] 等待 CI 检查全部跑完后再合并当前 PR",
      translation: "Do not merge this PR yet. Wait for CI to finish."
    },
    {
      raw: "Redis 的超时设置为 30 秒，不是 30 分钟。",
      clean: "Redis 的超时设置为 30 秒，不是 30 分钟。",
      formal: "将 Redis 超时时间明确设定为 30 秒（切勿误配为 30 分钟）。",
      todoItem: "- [ ] 将 Redis 超时配置设为 30 秒（确认单位不是 30 分钟）",
      translation: "Set the Redis timeout to 30 seconds, not 30 minutes.",
      gate: { candidate: "Redis 的超时设置为 30 分钟。", reason: "整理稿把「30 秒」改成了「30 分钟」，已保留原话" }
    },
    {
      raw: "就是，保持 `CacheService` 的 API 不变，只修改错误处理。",
      clean: "保持 `CacheService` 的 API 不变，只修改错误处理。",
      formal: "在保持 `CacheService` 公共 API 签名不变的前提下，仅调整内部错误处理逻辑。",
      todoItem: "- [ ] 修改 `CacheService` 内部错误处理逻辑（保持公共 API 不变）",
      translation: "Keep the API of `CacheService` unchanged. Only modify error handling."
    },
    {
      raw: "今天主要讨论缓存穿透的处理方案。",
      clean: "今天主要讨论缓存穿透的处理方案。",
      formal: "本次重点对齐缓存穿透场景的兜底处理方案。",
      todoItem: "- [ ] 落实缓存穿透处理方案评审结论",
      translation: "Today we mainly discuss how to handle cache penetration."
    },
    {
      raw: "会后请小王整理纪要，并同步给所有人。",
      clean: "会后请小王整理纪要，并同步给所有人。",
      formal: "会后烦请小王整理完整纪要并同步团队全员。",
      todoItem: "- [ ] 整理评审纪要并同步给所有人（负责人：小王 · 会后）",
      translation: "After the meeting, please ask Xiao Wang to write the minutes and share them with everyone."
    }
  ].map(item => ({ ...item, dir: "zh-en" }));

  const EN_ZH = [
    { raw: "Please do not merge this PR until CI finishes.", clean: "Please do not merge this PR until CI finishes.", translation: "在 CI 跑完前，请不要合并这个 PR。" },
    { raw: "Set the Redis timeout to 30 seconds, not 30 minutes.", clean: "Set the Redis timeout to 30 seconds, not 30 minutes.", translation: "将 Redis 的超时设为 30 秒，不是 30 分钟。" },
    { raw: "Keep the API of `CacheService` unchanged.", clean: "Keep the API of `CacheService` unchanged.", translation: "保持 `CacheService` 的 API 不变。" }
  ].map(item => ({ ...item, dir: "en-zh" }));

  const DOC_RAW = '# 部署约束\n\n- 不要更改 `CacheService` 的 API。\n- 超时为 30 秒，日志不包含凭据。\n\n```json\n{\n  "timeout": 30,\n  "api_key": "${API_KEY}"\n}\n```\n';
  const DOC = {
    raw: DOC_RAW,
    clean: DOC_RAW,
    dir: "zh-en",
    translation: '# Deployment constraints\n\n- Do not change the API of `CacheService`.\n- The timeout is 30 seconds. Logs must not contain credentials.\n\n```json\n{\n  "timeout": 30,\n  "api_key": "${API_KEY}"\n}\n```\n'
  };

  const AI_DELEGATE = [
    {
      raw: "呃我想改一下登录和缓存这块，先把客户端重试设成 3 次吧……哦不对，网关层已经有重试了，客户端干脆把自动重试关掉，只保留 401 刷新 token 的逻辑。",
      clean: "我想改一下登录和缓存这块：网关层已经有重试了，客户端干脆把自动重试关掉，只保留 401 刷新 token 的逻辑。",
      formal: "关于登录与缓存模块的调整，鉴于网关层已具备重试机制，客户端请关闭自动重试，仅保留 401 状态码刷新 Token 的流程。",
      todoItem: "- [ ] 关闭客户端自动重试逻辑，仅保留 `401` 刷新 Token 流程"
    },
    {
      raw: "另外 Redis 的超时设置为 30 秒，不是 30 分钟。",
      clean: "另外 Redis 的超时设置为 30 秒，不是 30 分钟。",
      formal: "同时将 Redis 超时时间明确设定为 30 秒（切勿误配为 30 分钟）。",
      todoItem: "- [ ] 将 Redis 超时配置设为 30 秒（确认单位不是 30 分钟）"
    },
    {
      raw: "就是，保持 `CacheService` 的 API 不变，只修改错误处理。",
      clean: "保持 `CacheService` 的 API 不变，只修改错误处理。",
      formal: "在保持 `CacheService` 公共 API 签名不变的前提下，仅调整内部错误处理逻辑。",
      todoItem: "- [ ] 修改 `CacheService` 内部错误处理逻辑（保持公共 API 不变）"
    },
    {
      raw: "会后请小王整理纪要，并同步给所有人。",
      clean: "会后请小王整理纪要，并同步给所有人。",
      formal: "会后烦请小王整理完整纪要并同步团队全员。",
      todoItem: "- [ ] 整理本次评审与排查纪要并同步全员（负责人：小王 · 会后）"
    },
    {
      raw: "你先帮我查一下 `AuthInterceptor` 还有没有别的地方在悄悄发重试，列出来给我看，先千万别直接改代码。",
      clean: "你先帮我查一下 `AuthInterceptor` 还有没有别的地方在悄悄发重试，列出来给我看，先千万别直接改代码。",
      formal: "请先排查 `AuthInterceptor` 中是否存在其他隐式重试调用并输出清单，当前阶段切勿直接修改业务代码。",
      todoItem: "- [ ] 排查 `AuthInterceptor` 隐式重试位置并输出改造方案（前置约束：暂不修改代码）"
    }
  ].map(item => ({ ...item, dir: "zh-en" }));

  const AI_STEER = [
    {
      raw: "呃不对不对，不要去改 `CacheService` 的公共 API 签名，外部好多地方调了。",
      clean: "不要去改 `CacheService` 的公共 API 签名，外部好多地方调了。",
      formal: "请勿修改 `CacheService` 的公共 API 签名，以免影响外部多处调用方。",
      todoItem: "- [ ] 保持 `CacheService` 公共 API 签名不变"
    },
    {
      raw: "你直接在它内部方法里加一层空值兜底就行，现有单测先别动。",
      clean: "你直接在它内部方法里加一层空值兜底就行，现有单测先别动。",
      formal: "仅需在其内部方法中补充空值兜底处理，暂不改动现有单元测试。",
      todoItem: "- [ ] 在 `CacheService` 内部方法补充空值兜底（暂不改动现有单测）"
    }
  ].map(item => ({ ...item, dir: "zh-en" }));

  const FIXTURES = [...ZH_EN, ...AI_DELEGATE, ...AI_STEER, ...EN_ZH, DOC];
  const SAMPLES = { "zh-en": ZH_EN, "en-zh": EN_ZH };
  const LANG = { "zh-en": { src: "中文", dst: "English" }, "en-zh": { src: "English", dst: "中文" } };
  const NO_MODEL = "演示语料之外的文本没有译文（原型未接入模型）";
  const REASONS = {
    recording: "请先结束录音。",
    empty: "草稿是空的。",
    processing: "选定的内容还在处理中。",
    stale: "原话已更新，当前人工修改稿已过期：请先点「重新生成」或「编辑全文」确认。",
    unavailable: "选定的内容未通过校验或未完成：请重试、手动编辑，或改用「口语整理 / 原话」。",
    "target-lost": "原输入框已失效，请重新选择目标或复制。"
  };

  /* ---------- state ---------- */
  const draft = new Draft("agent");
  const ui = {
    direction: "zh-en",
    defaultPolish: "natural",
    cleanerModel: "Qwen3.5 4B",
    rememberApps: true,
    rememberedFrom: null,
    srcExpanded: false,
    lastCompositionEndAt: null,
    editing: null, // unit.id or "doc"
    editValue: "",
    wide: false,
    forceWhole: false,
    armed: false,
    timeline: false,
    fileName: "",
    slow: false,
    gate: false,
    docEdge: null, // "hijack" | "fact-anchor" | null
    handsFree: false,
    composing: false,
    cloud: { translation: false, cleanup: false },
    cleanup: "on",
    notice: null,
    micBlock: null,
    modelPhase: null,
    used: { speech: false, clean: false, translation: false },
    history: [],
    secs: 0,
    tail: "",
    theme: "auto",
    binding: "Ctrl+Alt+T",
    commitKey: "enter",
    platform: "mac"
  };
  // Per-app remembered choice: Map<targetId, { output: "clean" | "translation", polishMode: string, direction: string, time: string }>
  const appMemory = new Map();
  const rec = { id: 0, timer: null, index: 0 };
  const inflight = new Set();
  const terms = [{ source: "缓存穿透", target: "cache penetration" }, { source: "CacheService", target: "CacheService" }];
  const endpoints = [
    { id: "ep1", name: "Foundry · 东亚", url: "https://livelingo-eastasia.openai.azure.com/openai/v1", model: "gpt-4o-mini", hasKey: true, enabled: true },
    { id: "ep2", name: "OpenAI", url: "https://api.openai.com/v1", model: "gpt-4.1-mini", hasKey: true, enabled: false },
    { id: "ep3", name: "本机 llama.cpp", url: "http://localhost:8080/v1", model: "qwen3", hasKey: true, enabled: false }
  ];
  let endpointSeq = endpoints.length;
  let syncTimer = null, toastTimer = null, copyTimer = null, lastToast = "", lastStruct = "", lastData = "", lastPointerUp = 0;

  /* ---------- helpers ---------- */
  function formatStructuredPreview(text) {
    return String(text ?? "").split("\n").map(line => {
      if (/^\s*#{1,6}\s/.test(line)) return `<span class="md-h">${esc(line)}</span>`;
      const taskMatch = line.match(/^(\s*-\s*\[[ xX]\])(.*)$/);
      const renderInline = str => esc(str).replace(/`([^`\n]+)`/g, '<span class="md-code">`$1`</span>');
      if (taskMatch) return `<span class="md-box">${esc(taskMatch[1])}</span>${renderInline(taskMatch[2])}`;
      return renderInline(line);
    }).join("\n");
  }

  const isImeComposing = event =>
    event.isComposing
    || event.keyCode === 229
    || ui.composing
    || (ui.lastCompositionEndAt !== null && Math.abs(event.timeStamp - ui.lastCompositionEndAt) < 50);
  const presetById = id => presets.find(item => item.id === id) ?? presets[0];
  const offeredPresets = () => presets.filter(item => item.enabled || item.id === ui.defaultPolish);
  const fixtureFor = raw => FIXTURES.find(item => item.raw === raw);
  const mmss = total => `${Math.floor(total / 60)}:${String(total % 60).padStart(2, "0")}`;
  const stamp = total => `${String(Math.floor(total / 60)).padStart(2, "0")}:${String(total % 60).padStart(2, "0")}`;
  const SYMBOLS = { ctrl: "⌃", alt: "⌥", shift: "⇧", meta: "⌘", cmd: "⌘" };
  const modCap = key => ui.platform === "mac" ? `⌘${key}` : `Ctrl+${key}`;
  const keyLabel = binding => {
    const parts = binding.split("+").map(part => part.trim()).filter(Boolean);
    return ui.platform === "mac" ? parts.map(part => SYMBOLS[part.toLowerCase()] ?? part.toUpperCase()).join("") : parts.join("+");
  };
  function matches(event, binding) {
    const parts = binding.toLowerCase().split("+").map(part => part.trim()).filter(Boolean);
    if (!parts.length) return false;
    const key = parts.at(-1), need = new Set(parts.slice(0, -1));
    const has = { ctrl: event.ctrlKey, alt: event.altKey, shift: event.shiftKey, meta: event.metaKey };
    const keyMatch = event.key.toLowerCase() === key || event.code.toLowerCase() === `key${key}` || event.code.toLowerCase() === key;
    return keyMatch && Object.keys(has).every(mod => need.has(mod) === has[mod]);
  }
  const commitKeyLabel = () => ui.commitKey === "enter" ? "↵" : ui.platform === "mac" ? "⌃↵" : "Ctrl+↵";
  const docSaveKeyLabel = () => ui.platform === "mac" ? "⌘↵" : "Ctrl+↵";
  const isWide = () => innerWidth >= 900 && (ui.wide || draft.route === "whole" || ui.timeline);
  const popoverOpen = () => document.querySelector(".menu:popover-open");
  const dialogOpen = () => $("dlg").open;
  const targetMeta = id => TARGETS[id] ?? TARGETS.agent;
  const memoryKey = target => target ?? "·clipboard";
  const memoryTargetName = target => target === null ? "仅复制" : targetMeta(target).name;

  function choiceSummary(output, polishMode, direction) {
    if (output === "translation") return `翻译成 ${LANG[direction].dst}`;
    if (output === "raw") return "原话";
    return presetById(polishMode).pill;
  }

  function rememberCurrentChoice() {
    if (!ui.rememberApps || draft.output === "raw") return;
    const key = memoryKey(draft.target);
    appMemory.set(key, {
      target: draft.target,
      name: memoryTargetName(draft.target),
      output: draft.output,
      polishMode: draft.polishMode,
      direction: ui.direction,
      time: "刚刚"
    });
    ui.rememberedFrom = null;
    renderAppsMemory();
  }

  function applyStartChoice(target, preferGlobalDefault = false) {
    const fallbackMode = ui.cleanup === "off" ? "natural" : ui.defaultPolish;
    const defaultOut = ui.cleanup === "off" ? "raw" : "clean";
    ui.rememberedFrom = null;
    if (ui.rememberApps) {
      const saved = appMemory.get(memoryKey(target));
      if (saved && !(saved.output === "clean" && ui.cleanup === "off")) {
        const modeOk = saved.output !== "clean" || offeredPresets().some(p => p.id === saved.polishMode);
        const resolvedMode = modeOk ? saved.polishMode : fallbackMode;
        draft.output = saved.output;
        draft.polishMode = resolvedMode;
        ui.direction = saved.direction;
        const differs = saved.output !== defaultOut || (saved.output === "clean" && resolvedMode !== fallbackMode);
        if (differs) ui.rememberedFrom = { name: saved.name, summary: choiceSummary(saved.output, resolvedMode, saved.direction) };
        return;
      }
    }
    const smart = !preferGlobalDefault && target !== null ? targetMeta(target).smartPreset : null;
    const chosenMode = smart && offeredPresets().some(p => p.id === smart) ? smart : fallbackMode;
    draft.output = defaultOut;
    draft.polishMode = chosenMode;
  }

  function toast(message) {
    let text = String(message ?? "");
    if (text === lastToast) text += "\u00A0";
    lastToast = text;
    $("toast").textContent = text;
    $("toast").hidden = false;
    clearTimeout(toastTimer);
    toastTimer = setTimeout(() => { $("toast").hidden = true; }, 3600);
  }
  function dialog(title, body, closeMode = "closerequest") {
    const dlg = $("dlg");
    dlg.setAttribute("closedby", closeMode);
    $("dlg-title").textContent = title;
    $("dlg-body").innerHTML = body;
    if (!dlg.open) dlg.showModal();
  }
  function showPage(name) {
    document.querySelectorAll(".page").forEach(page => { page.hidden = page.id !== `page-${name}`; });
    document.querySelectorAll(".tab").forEach(tab => {
      if (tab.dataset.page === name) tab.setAttribute("aria-current", "page");
      else tab.removeAttribute("aria-current");
    });
  }
  function showSettings(section = "general", focusId = null) {
    showPage("settings");
    document.querySelectorAll("[data-panel]").forEach(panel => {
      panel.hidden = panel.dataset.panel === section ? false : "until-found";
    });
    document.querySelectorAll("[data-sec]").forEach(button => {
      const selected = button.dataset.sec === section;
      button.setAttribute("aria-selected", String(selected));
      button.toggleAttribute("focusgroupstart", selected);
    });
    if (focusId) $(focusId)?.scrollIntoView({ block: "nearest" });
  }
  function applyTheme() {
    const theme = ui.theme === "auto" ? (matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light") : ui.theme;
    document.documentElement.dataset.theme = theme;
  }

  /* ---------- structured polish synthesis ---------- */
  function buildStructuredCandidate(modeId, units) {
    const preset = presetById(modeId);
    const raws = units.map(u => u.raw);
    const cleanedLines = units.map(u => fixtureFor(u.raw)?.clean ?? u.raw.replace(/^(嗯，|呃，|就是，)/, ""));

    if (ui.docEdge === "hijack" && modeId === "prompt") {
      return "好的，下面直接为你写出修改后的 CacheService 实现代码：\n```csharp\npublic sealed class CacheService {\n  public int TimeoutSeconds => 30;\n}\n```";
    }
    if (ui.docEdge === "slop" && modeId === "prompt") {
      return "你是一位拥有十年经验的资深系统架构师，请排查 `CacheService` 的 30 秒超时与错误处理问题。";
    }
    if (ui.docEdge === "fact-anchor" && modeId === "todo") {
      return "- [ ] 等待 CI 跑完后再合并当前 PR\n- [ ] 将 Redis 超时设置为 60 秒\n- [ ] 保持 `CacheService` 的 API 不变，只改错误处理";
    }

    if (raws.length === 1 && raws[0] === DOC_RAW) return DOC_RAW;

    if (modeId === "formal") {
      const formalParts = units.map(u => fixtureFor(u.raw)?.formal ?? (fixtureFor(u.raw)?.clean ?? u.raw));
      return formalParts.join("");
    }

    if (modeId === "prompt") {
      const joinedRaw = raws.join("");
      // Adaptive Granularity: short 1-2 sentence mid-turn steering emits crisp direct instruction without bloated headers
      if (units.length <= 2 && joinedRaw.length <= 95) {
        if (joinedRaw.includes("公共 API 签名")) {
          return "不要修改 `CacheService` 的公共 API 签名（外部存在多处调用）；请仅在其内部方法中补充空值兜底逻辑，且暂不改动现有单元测试。";
        }
        return cleanedLines.join(" ");
      }

      const hasAuth = raws.some(r => r.includes("AuthInterceptor"));
      const hasCache = raws.some(r => r.includes("CacheService") || r.includes("缓存穿透"));
      const contextItems = [];
      const guardrails = [];
      raws.forEach((raw, idx) => {
        if (raw.includes("401")) {
          contextItems.push("- 网关层已具备重试机制，客户端需关闭自动重试，仅保留 `401` 状态码刷新 Token 流程");
        } else if (raw.includes("Redis")) {
          contextItems.push("- 将 Redis 超时时间设定为 30 秒（严格区分单位，切勿误设为 30 分钟）");
        } else if (raw.includes("CacheService")) {
          guardrails.push("- 保持 `CacheService` 公共 API 签名不变，仅修改内部错误处理逻辑");
        } else if (raw.includes("PR")) {
          guardrails.push("- 当前 PR 需等待 CI 检查全部通过后再合并");
        } else if (raw.includes("千万别直接改代码")) {
          guardrails.push("- 当前阶段仅输出排查清单与修改方案，禁止直接改动业务代码");
        } else if (!raw.includes("缓存穿透") && !raw.includes("小王")) {
          contextItems.push(`- ${cleanedLines[idx]}`);
        }
      });

      const goal = hasAuth
        ? "排查 `AuthInterceptor` 中是否存在隐式重试调用，并结合 `CacheService` 与超时边界给出改造方案（先列方案，不要直接改代码）。"
        : hasCache
          ? "针对缓存穿透场景，排查并优化 `CacheService` 的错误处理与 Redis 超时配置。"
          : cleanedLines[0];

      const sections = ["### 任务目标", goal];
      if (contextItems.length) sections.push("", "### 上下文与目标行为", ...contextItems);
      if (guardrails.length) sections.push("", "### 硬性约束（Do / Don't）", ...guardrails);
      if (raws.some(r => r.includes("列出来") || r.includes("小王"))) {
        sections.push("", "### 交付要求", "- 输出清晰的调用点排查清单与改动边界说明，便于会后由小王整理归档并同步全员");
      }
      return sections.join("\n");
    }

    if (modeId === "todo") {
      const items = units.map((u, idx) => fixtureFor(u.raw)?.todoItem ?? `- [ ] ${cleanedLines[idx].replace(/[。！？.!?]+$/, "")}`);
      return items.join("\n");
    }

    if (modeId === "report") {
      const hasAuth = raws.some(r => r.includes("AuthInterceptor"));
      const lead = hasAuth
        ? "已明确登录重试收敛与缓存改造边界：客户端关闭自动重试（仅保留 `401` 刷新 Token），先完成 `AuthInterceptor` 隐式重试排查再动代码。"
        : raws.some(r => r.includes("缓存穿透"))
          ? "本次讨论明确了缓存穿透的处理方案与边界；当前 PR 暂缓合并，待 CI 执行完成后推进。"
          : cleanedLines[0];
      const decisions = [];
      const followups = [];
      units.forEach((u, idx) => {
        if (u.raw.includes("小王")) followups.push("- 会后由小王整理完整纪要并同步团队全员");
        else if (u.raw.includes("AuthInterceptor")) followups.push("- 先排查 `AuthInterceptor` 隐式重试点并输出方案（暂不直接改代码）");
        else if (u.raw.includes("PR")) followups.push("- 待 CI 检查通过后合入当前 PR");
        else decisions.push(`- ${cleanedLines[idx]}`);
      });
      return [
        "### 核心结论",
        lead,
        ...(decisions.length ? ["", "### 关键决策与技术边界", ...decisions] : []),
        ...(followups.length ? ["", "### 下一步行动", ...followups] : [])
      ].join("\n");
    }

    // Custom user-created polish preset: structured synthesis preserving every active number & inline literal
    return [
      `### ${preset.name}`,
      ...cleanedLines.map(line => `- ${line}`)
    ].join("\n");
  }

  /* ---------- processing simulation ---------- */
  function runUnit(unit, kind) {
    const key = `u:${unit.id}:${kind}:${draft.translationRevision}`;
    if (ui.slow || inflight.has(key)) return;
    inflight.add(key);
    const ticket = draft.ticketFor(unit);
    const fixture = fixtureFor(unit.raw);
    let text, warning = "";
    if (kind === "clean") {
      if (ui.cleanup === "off") text = unit.raw;
      else if (ui.gate && fixture?.gate) { text = fixture.gate.candidate; warning = fixture.gate.reason; }
      else text = fixture ? fixture.clean : unit.raw;
    } else {
      text = fixture && fixture.dir === ui.direction ? fixture.translation : null;
    }
    setTimeout(() => {
      inflight.delete(key);
      if (text == null) draft.fail(ticket, kind, NO_MODEL);
      else draft.complete(ticket, kind, text, warning);
      render();
    }, kind === "clean" ? 260 : 520);
  }

  function runDoc(modeId) {
    const ticket = draft.docTicket(modeId);
    const key = `d:${ticket.epoch}:${modeId}:${ticket.sourceRevision}:${ticket.modeRevision}`;
    if (ui.slow || inflight.has(key) || !draft.units.length) return;
    inflight.add(key);
    const preset = presetById(modeId);
    const unitsSnap = draft.units.map(u => ({ raw: u.raw }));
    const rawJoined = draft.rawText();
    setTimeout(() => {
      inflight.delete(key);
      const candidate = buildStructuredCandidate(modeId, unitsSnap);
      if (preset.gate === "fact-anchor") {
        const check = checkFactAnchors(rawJoined, candidate, { forbidExecution: preset.forbidExecution });
        if (!check.ok) {
          draft.failDoc(ticket, `${check.message}（已阻止自动写回，原话未丢失）`);
          render();
          return;
        }
      }
      draft.completeDoc(ticket, candidate);
      render();
    }, 320);
  }

  function ensure() {
    if (!draft.units.length) return;
    if (draft.output === "raw") return;
    if (draft.output === "translation") {
      draft.units.forEach(unit => {
        if (unit.translation === null && !unit.translationError) runUnit(unit, "translation");
      });
      return;
    }
    // output === "clean": keep unit clean ready for live preview & natural mode, and run doc polish when settled
    draft.units.forEach(unit => {
      if (unit.clean === null && !unit.cleanError) runUnit(unit, "clean");
    });
    if (draft.usesDocumentPolish()) {
      const st = draft.docState(draft.polishMode);
      if (st === "pending") runDoc(draft.polishMode);
    }
  }

  /* ---------- source <-> draft ---------- */
  function autosize() {
    const field = $("src");
    if (field.hidden || CSS.supports("field-sizing", "content")) return;
    const cap = $("composer").classList.contains("has-res") ? 96 : 160;
    field.style.height = "auto";
    field.style.height = `${Math.min(field.scrollHeight, cap)}px`;
  }
  function setSource() {
    $("src").value = draft.units.map(unit => unit.raw).join(draft.route === "whole" ? "" : "\n");
    autosize();
  }
  function syncFromSource() {
    syncTimer = null;
    if (draft.snapshot || draft.recording || ui.timeline) return;
    const text = $("src").value;
    const route = ui.forceWhole ? "whole" : routeFor(text);
    draft.syncSource(route === "whole" ? (text.trim() ? [text] : []) : splitSentences(text), route);
    if (text.trim()) ui.rememberedFrom = null;
    ensure();
    render();
  }
  function flushSource() {
    if (syncTimer === null) return;
    clearTimeout(syncTimer);
    syncFromSource();
  }

  /* ---------- recording ---------- */
  function startRecording(free) {
    if (draft.recording) return;
    if (ui.micBlock) {
      toast(ui.micBlock === "permission" ? "麦克风权限未开启 · 仍可直接打字" : "语音模型还在下载 · 现在可以直接打字");
      $("src").focus();
      return;
    }
    if (draft.snapshot) fresh();
    if (ui.editing !== null) saveEdit();
    flushSource();
    ui.notice = null;
    ui.rememberedFrom = null;
    draft.recording = true;
    ui.handsFree = free;
    ui.secs = 0;
    ui.tail = "";
    const run = ++rec.id;
    const samples = SAMPLES[ui.direction];
    let position = 0, pause = 0, ticks = 0;
    rec.timer = setInterval(() => {
      if (run !== rec.id) return;
      if (++ticks % 10 === 0) ui.secs++;
      if (pause > 0) { pause--; render(); return; }
      const sample = samples[rec.index % samples.length];
      position = Math.min(position + 3, sample.raw.length);
      ui.tail = sample.raw.slice(0, position);
      if (position === sample.raw.length) {
        draft.append(sample.raw);
        rec.index++;
        position = 0;
        ui.tail = "";
        pause = 10;
        ensure();
      }
      render();
    }, 100);
    render();
  }

  function stopRecording() {
    if (!draft.recording) return;
    clearInterval(rec.timer);
    rec.id++;
    const tail = ui.tail.trim();
    draft.recording = false;
    ui.handsFree = false;
    ui.tail = "";
    if (tail) draft.append(tail);
    if (!ui.timeline) setSource();
    ensure();
    render();
  }

  /* ---------- draft lifecycle ---------- */
  function saveHistory() {
    if (!draft.units.length) return;
    ui.history = [{
      time: new Date().toLocaleTimeString(),
      units: structuredClone(draft.units),
      output: draft.output,
      polishMode: draft.polishMode,
      docs: structuredClone(draft.exportDocs()),
      route: draft.route,
      direction: ui.direction,
      timeline: ui.timeline,
      fileName: ui.fileName
    }, ...ui.history].slice(0, 5);
  }

  function fresh(keepTarget = draft.target) {
    clearInterval(rec.timer);
    rec.id++;
    rec.index = 0;
    clearTimeout(syncTimer);
    syncTimer = null;
    saveHistory();
    inflight.clear();
    draft.reset(keepTarget);
    draft.timeline = false;
    Object.assign(ui, {
      editing: null,
      srcExpanded: false,
      armed: false,
      timeline: false,
      fileName: "",
      slow: false,
      gate: false,
      docEdge: null,
      handsFree: false,
      tail: "",
      notice: null,
      modelPhase: null
    });
    applyStartChoice(keepTarget);
    $("src").value = "";
    autosize();
    $("composer").hidden = false;
    lastStruct = "";
    render();
  }

  function restore(index) {
    const entry = ui.history[index];
    if (!entry) return;
    clearInterval(rec.timer);
    rec.id++;
    inflight.clear();
    draft.reset(draft.target);
    draft.timeline = entry.timeline;
    draft.restore(structuredClone(entry.units), entry.output, entry.route, entry.polishMode, structuredClone(entry.docs));
    Object.assign(ui, {
      direction: entry.direction,
      timeline: entry.timeline,
      fileName: entry.fileName,
      slow: false,
      editing: null,
      armed: false,
      notice: null,
      docEdge: null,
      rememberedFrom: null
    });
    if (!ui.timeline) setSource();
    $("composer").hidden = false;
    lastStruct = "";
    ensure();
    render();
  }

  function chooseOutput(kind, polishMode = draft.polishMode, fromUser = true) {
    if (draft.snapshot) { toast("已写入的内容已冻结 · 新草稿后可继续"); return; }
    if (kind === "clean") {
      draft.setPolishMode(polishMode);
    } else {
      draft.output = kind;
    }
    ui.editing = null;
    ui.armed = false;
    lastStruct = "";
    if (fromUser) rememberCurrentChoice();
    ensure();
    render();
  }

  function swapDirection(fromUser = true) {
    if (draft.snapshot) return;
    ui.direction = ui.direction === "zh-en" ? "en-zh" : "zh-en";
    $("direction-setting").value = ui.direction;
    draft.invalidateTranslations();
    if (fromUser && draft.output === "translation") rememberCurrentChoice();
    ensure();
    render();
  }

  function switchTarget(nextTarget) {
    const wasEmpty = !draft.units.length && !draft.recording && $("src").value.trim() === "";
    draft.retarget(nextTarget);
    if (wasEmpty && !draft.snapshot) {
      applyStartChoice(nextTarget);
    }
    lastStruct = "";
    render();
  }

  function loadTranscript(name, fromPicker) {
    fresh();
    ui.timeline = true;
    draft.timeline = true;
    ui.fileName = name;
    ZH_EN.forEach(item => draft.append(item.raw));
    ensure();
    render();
    if (fromPicker) toast("已载入示例转录（不会读取所选文件的音频）");
  }

  function showComposer() {
    showPage("play");
    if (draft.snapshot) fresh(draft.target);
    $("composer").hidden = false;
    render();
    if (draft.units.length === 0 || !(draft.recording || ui.timeline)) $("src").focus();
    else $("mic").focus();
  }

  function hideComposer() {
    if (draft.recording) stopRecording();
    flushSource();
    ui.armed = false;
    saveHistory();
    $("composer").hidden = true;
    $("target-text").focus();
  }

  /* ---------- edit / commit ---------- */
  function startEdit(id) {
    if (draft.snapshot) return;
    if (id === "doc") {
      ui.editing = "doc";
      ui.editValue = draft.docEntry(draft.polishMode)?.text ?? draft.rawText();
      ui.armed = false;
      lastStruct = "";
      render();
      $("edit-doc")?.focus();
      return;
    }
    const unit = draft.units.find(item => item.id === id);
    if (!unit) return;
    ui.editing = id;
    ui.editValue = unit[draft.output] ?? "";
    ui.armed = false;
    lastStruct = "";
    render();
    $(`edit-${id}`)?.focus();
  }

  function saveEdit() {
    if (ui.editing === null) return;
    if (ui.editing === "doc") {
      const field = $("edit-doc");
      if (field) draft.editDoc(draft.polishMode, field.value);
      ui.editing = null;
      lastStruct = "";
      render();
      toast("已保存整篇润色稿 · 切换其他润色方式不会丢失这一版");
      return;
    }
    const field = $(`edit-${ui.editing}`);
    if (field) draft.edit(draft.indexOf(ui.editing), draft.output, field.value);
    ui.editing = null;
    lastStruct = "";
    render();
    toast("已保存 · 之后的模型结果不会覆盖这一版");
  }

  function cancelEdit() {
    ui.editing = null;
    lastStruct = "";
    render();
  }

  function rowAction(action, id) {
    if (draft.snapshot) return;
    if (id === "doc") {
      if (action === "edit") startEdit("doc");
      if (action === "retry") {
        ui.docEdge = null;
        draft.retryDoc(draft.polishMode);
        ui.armed = false;
        lastStruct = "";
        ensure();
        render();
      }
      if (action === "to-natural") chooseOutput("clean", "natural", false);
      if (action === "to-raw") chooseOutput("raw", draft.polishMode, false);
      if (action === "save") saveEdit();
      if (action === "cancel") cancelEdit();
      return;
    }
    const index = draft.indexOf(id);
    if (index < 0) return;
    if (action === "edit") startEdit(id);
    if (action === "raw") { draft.useRaw(index); ui.armed = false; lastStruct = ""; render(); }
    if (action === "retry") {
      const unit = draft.units[index];
      unit[draft.output + "Error"] = "";
      unit[draft.output] = null;
      ensure();
      render();
    }
    if (action === "save") saveEdit();
    if (action === "cancel") cancelEdit();
  }

  function commit(fromKey) {
    if (draft.snapshot) return;
    if (ui.armed) {
      if (!fromKey) { ui.armed = false; render(); toast("已取消自动写入"); }
      return;
    }
    if (ui.editing !== null) saveEdit();
    flushSource();
    const ready = draft.readiness();
    if (!ready.ok && ready.reason === "processing") { ui.armed = true; render(); return; }
    if (!ready.ok) { toast(REASONS[ready.reason]); return; }
    submit();
  }

  function submit() {
    const result = draft.requestCommit();
    if (!result.ok) return;
    saveHistory();
    ui.armed = false;
    if (result.action === "insert") {
      const field = $("target-text");
      field.value += (field.value ? "\n" : "") + result.text;
      $("composer").hidden = true;
      toast(`已写入 ${targetMeta(result.target).name} · 未发送`);
      field.focus();
    } else {
      copyText(result.text);
    }
    render();
  }

  async function copyText(text, fromCopyBtn = false) {
    try {
      await navigator.clipboard.writeText(text);
      if (fromCopyBtn) {
        const btn = $("copy"), useEl = $("copy-ic");
        clearTimeout(copyTimer);
        btn.setAttribute("data-copied", "");
        useEl?.setAttribute("href", "#i-check");
        copyTimer = setTimeout(() => {
          btn.removeAttribute("data-copied");
          useEl?.setAttribute("href", "#i-copy");
        }, 1800);
      }
      toast("已复制");
    } catch {
      dialog("请手动复制", `<p>浏览器未允许写入剪贴板，内容没有丢失。</p><pre tabindex="0">${esc(text)}</pre>`, "any");
    }
  }

  function copyCurrent() {
    const ready = draft.recording ? { ok: false, reason: "recording" } : draft.contentReadiness();
    if (!ready.ok) { toast(REASONS[ready.reason]); return; }
    copyText(draft.text(), true);
  }

  function download(format) {
    const ready = draft.recording ? { ok: false, reason: "recording" } : draft.contentReadiness();
    if (!ready.ok) { toast("选定的内容未完成，不能导出半截结果"); return; }
    const srt = draft.units.map((unit, i) => `${i + 1}\n00:${stamp(i * 3)},000 --> 00:${stamp(i * 3 + 3)},000\n${unit[draft.output === "translation" ? "translation" : draft.output === "raw" ? "raw" : "clean"] ?? ""}\n`).join("\n");
    const body = format === "srt" ? srt : format === "md" ? `# LiveLingo 草稿（${choiceSummary(draft.output, draft.polishMode, ui.direction)}）\n\n${draft.text()}` : draft.text();
    const url = URL.createObjectURL(new Blob([body], { type: "text/plain;charset=utf-8" }));
    const link = document.createElement("a");
    link.href = url;
    link.download = `livelingo-prototype.${format}`;
    link.click();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
    toast(format === "srt" ? "已导出 SRT · 时间为示例，非真实对齐" : "已导出");
  }

  /* ---------- rendering ---------- */
  let lastMenuSig = "";
  function renderOutputMenu() {
    const lang = LANG[ui.direction];
    const offered = offeredPresets();
    const sig = JSON.stringify([
      ui.cleanup, ui.direction, ui.defaultPolish, ui.rememberApps,
      draft.target, draft.output, draft.polishMode, ui.platform,
      offered.map(p => [p.id, p.name, p.desc, p.key])
    ]);
    if (sig === lastMenuSig) return;
    lastMenuSig = sig;
    const items = [];
    if (ui.cleanup !== "off") {
      items.push(`<div class="menu-group-label">整理与场景润色（保持${esc(lang.src)}）</div>`);
      offeredPresets().forEach(preset => {
        const checked = draft.output === "clean" && draft.polishMode === preset.id;
        items.push(`<button type="button" role="menuitemradio" aria-checked="${checked}" data-polish="${esc(preset.id)}">
          <span class="menu-item-main">
            <span class="menu-item-title">${esc(preset.name)}${preset.id === ui.defaultPolish ? "（默认）" : ""}</span>
            <span class="menu-item-desc">${esc(preset.desc)}</span>
          </span>
          <span class="menu-item-right">${preset.key ? `<kbd>${esc(modCap(preset.key))}</kbd>` : ""}</span>
        </button>`);
      });
      items.push("<hr>");
    }
    const transChecked = draft.output === "translation";
    const rawChecked = draft.output === "raw";
    items.push(`<button type="button" role="menuitemradio" aria-checked="${transChecked}" data-out="translation">
      <span class="menu-item-main"><span class="menu-item-title">翻译成 ${esc(lang.dst)}</span></span>
      <span class="menu-item-right"><kbd>${esc(modCap("6"))}</kbd></span>
    </button>`);
    items.push(`<button type="button" role="menuitemradio" aria-checked="${rawChecked}" data-out="raw">
      <span class="menu-item-main"><span class="menu-item-title">原话（不整理）</span></span>
      <span class="menu-item-right"><kbd>${esc(modCap("0"))}</kbd></span>
    </button>`);
    items.push("<hr>");
    items.push(`<button type="button" data-act="swap"><span>语言：${esc(lang.src)} → ${esc(lang.dst)}　⇄ 互换</span></button>`);
    items.push(`<button type="button" data-act="polish-settings"><span>管理润色方式…</span></button>`);
    if (ui.rememberApps) {
      items.push(`<p class="menu-note">选择会为「${esc(memoryTargetName(draft.target))}」自动记住</p>`);
    }
    const container = $("output-items");
    container.innerHTML = items.join("");
    if (!("focusGroup" in HTMLElement.prototype)) {
      container.querySelectorAll("button:not([role])").forEach(btn => btn.setAttribute("role", "menuitem"));
    }
  }

  function unitHTML(unit, index) {
    const kind = draft.output, state = draft.state(unit, kind), owned = unit[kind + "Owned"], editing = ui.editing === unit.id;
    const warn = kind === "clean" && unit.warning;
    let body;
    if (editing) {
      body = `<label class="sr-only" for="edit-${unit.id}">编辑第 ${index + 1} 句</label><textarea class="edit" id="edit-${unit.id}" rows="3">${esc(ui.editValue)}</textarea><div class="edit-actions"><button type="button" data-row="save" data-id="${unit.id}">保存 <kbd>${commitKeyLabel()}</kbd></button><button type="button" data-row="cancel" data-id="${unit.id}">取消 <kbd>Esc</kbd></button></div>`;
    } else if (state === "pending") {
      body = `<div class="skel" aria-hidden="true"></div><div class="skel short" aria-hidden="true"></div><span class="sr-only">处理中</span>`;
    } else if (state === "error") {
      body = `<p class="row-error">${esc(unit[kind + "Error"])}</p><div class="inline-actions"><button type="button" data-row="retry" data-id="${unit.id}">重试</button><button type="button" data-row="edit" data-id="${unit.id}">手动输入</button></div>`;
    } else if (state === "empty") {
      body = `<p class="row-empty">（空）</p>`;
    } else {
      body = `<p class="row-text">${esc(unit[kind])}</p>${warn ? `<p class="row-note">${esc(unit.warning)}</p>` : owned ? `<p class="row-note owned">已手动修改</p>` : ""}`;
    }
    const tools = draft.snapshot || draft.recording || editing
      ? ""
      : `<div class="row-tools">${kind === "clean" ? `<button type="button" class="icon-btn" data-row="raw" data-id="${unit.id}" aria-label="第 ${index + 1} 句改用原话" title="改用原话">${icon("undo")}</button>` : ""}<button type="button" class="icon-btn" data-row="edit" data-id="${unit.id}" aria-label="编辑第 ${index + 1} 句" title="编辑">${icon("edit")}</button></div>`;
    return `<article class="unit ${warn ? "warn" : ""} ${owned ? "owned" : ""}" data-id="${unit.id}">${body}${tools}</article>`;
  }

  function docHTML() {
    const modeId = draft.polishMode;
    const preset = presetById(modeId);
    const st = draft.docState(modeId);
    const entry = draft.docEntry(modeId);
    const editing = ui.editing === "doc";

    if (editing) {
      return `<div class="doc-card"><label class="sr-only" for="edit-doc">编辑${esc(preset.name)}全文</label>
        <textarea class="edit doc-edit" id="edit-doc" rows="6">${esc(ui.editValue)}</textarea>
        <p class="edit-hint">Enter 换行 · ${esc(docSaveKeyLabel())} 保存全文（不会直接写入目标应用）</p>
        <div class="edit-actions">
          <button type="button" data-row="save" data-id="doc">保存 <kbd>${esc(docSaveKeyLabel())}</kbd></button>
          <button type="button" data-row="cancel" data-id="doc">取消 <kbd>Esc</kbd></button>
        </div></div>`;
    }
    if (st === "pending") {
      return `<div class="doc-card"><div class="skel" aria-hidden="true"></div><div class="skel" aria-hidden="true"></div><div class="skel short" aria-hidden="true"></div><span class="sr-only">正在按「${esc(preset.name)}」重组全文</span></div>`;
    }
    if (st === "error") {
      return `<div class="doc-card bad"><p class="row-error">${esc(entry?.error || "生成失败")}</p>
        <div class="inline-actions">
          <button type="button" data-row="retry" data-id="doc">重试提炼</button>
          <button type="button" data-row="edit" data-id="doc">手动编辑</button>
          <button type="button" data-row="to-natural" data-id="doc">切换口语整理</button>
          <button type="button" data-row="to-raw" data-id="doc">改用原话</button>
        </div></div>`;
    }
    if (st === "stale") {
      return `<div class="doc-card warn"><p class="doc-text">${formatStructuredPreview(entry?.text ?? "")}</p>
        <p class="row-note">原话已更新，当前人工修改稿已过期且不会自动写入。请重新生成或编辑确认。</p>
        <div class="inline-actions">
          <button type="button" data-row="retry" data-id="doc">重新生成（替换当前稿）</button>
          <button type="button" data-row="edit" data-id="doc">编辑确认</button>
        </div></div>`;
    }
    const tools = draft.snapshot
      ? ""
      : `<div class="doc-tools">
          <button type="button" class="icon-btn" data-row="to-raw" data-id="doc" aria-label="改用原话" title="改用原话">${icon("undo")}</button>
          <button type="button" class="icon-btn" data-row="edit" data-id="doc" aria-label="编辑全文" title="编辑全文">${icon("edit")}</button>
        </div>`;
    return `<div class="doc-card ${entry?.owned ? "owned" : ""}">
      <p class="doc-text">${formatStructuredPreview(entry?.text ?? "")}</p>
      ${entry?.owned ? `<p class="row-note owned">已手动修改 · 切换其他润色方式再切回仍保留</p>` : ""}
      ${tools}
    </div>`;
  }

  function bannerState() {
    if (draft.targetLost) {
      return { tone: "bad", text: "原输入框已失效，草稿已保留。为避免写进错误的窗口，不会自动写入。", actions: [["重新选择", "retarget"], ["复制", "copy"]] };
    }
    return ui.notice;
  }

  function statusText() {
    if (draft.snapshot) return "已写入";
    if (draft.recording) return ui.handsFree ? `正在听 · ${mmss(ui.secs)} · 点击结束` : `按住说话 · ${mmss(ui.secs)} · 松开结束`;
    if (ui.armed) return "处理完成后自动写入 · Esc 取消";
    if (!draft.units.length) return "";
    const ready = draft.contentReadiness();
    if (ready.reason === "processing") {
      if (draft.output === "translation") return "正在翻译…";
      return draft.usesDocumentPolish() ? `正在按「${presetById(draft.polishMode).name}」重组全文…` : "正在整理…";
    }
    if (ready.reason === "stale") return "原话已变 · 请重新生成或编辑确认";
    if (ready.reason === "unavailable") return "未通过安全校验 · 可重试、编辑或切换方式";
    const where = ui.cloud.translation || ui.cloud.cleanup ? "含云端处理" : "本地处理";
    if (draft.usesDocumentPolish()) return `${presetById(draft.polishMode).name}已就绪 · ${where}`;
    return `${draft.units.length} 句 · ${where}`;
  }

  /* ---------- model line ---------- */
  function modelMeta(stage) {
    if (stage === "speech") return ["识别", "X-ASR + Qwen3-ASR", "X-ASR 480 ms streaming + Qwen3-ASR 0.6B"];
    if (stage === "clean") return [draft.polishMode !== "natural" ? "润色" : "整理", ui.cleanerModel, `${ui.cleanerModel} (Q4_K_M)`];
    return ["翻译", "Hy-MT2-1.8B", "Hy-MT2 1.8B"];
  }
  function modelLine() {
    const stage = draft.recording ? "speech" : { clean: "clean", translation: "translation" }[draft.output];
    if (!stage || (stage !== "speech" && !draft.units.length)) return null;
    const [stageLabel, local] = modelMeta(stage);
    const phase = ui.modelPhase?.stage === stage ? ui.modelPhase : null;
    if (phase?.kind === "preparing") return { tone: "info", text: `${stageLabel} · 正在准备 ${local}` };
    if (phase?.kind === "switching") return { tone: "info", text: `${stageLabel} · 正在切换到 ${local}（上次 ${phase.from}）` };
    const inCloud = stage === "translation" ? ui.cloud.translation : stage === "clean" ? ui.cloud.cleanup : false;
    const endpoint = activeEndpoint();
    if (inCloud && endpoint) return { tone: "cloud", text: `${stageLabel} · ${endpoint.model} · 云端（${endpoint.name}）` };
    return { tone: "", text: `${stageLabel} · ${local} · 本地` };
  }

  function renderModelCard() {
    if (draft.recording) ui.used.speech = true;
    if (draft.units.length && draft.output === "clean") ui.used.clean = true;
    if (draft.units.length && draft.output === "translation") ui.used.translation = true;
    const endpoint = activeEndpoint();
    const stages = [
      ["speech", "语音识别", modelMeta("speech")[2]],
      ["clean", "整理与润色", modelMeta("clean")[2]],
      ["translation", "翻译", modelMeta("translation")[2]]
    ];
    $("models-list").innerHTML = stages.map(([stage, label, full]) => {
      const phase = ui.modelPhase?.stage === stage ? ui.modelPhase : null;
      const inCloud = stage === "translation" ? ui.cloud.translation : stage === "clean" ? ui.cloud.cleanup : false;
      let text = "本次窗口尚未使用";
      if (phase?.kind === "preparing") text = `正在准备 ${full}`;
      else if (phase?.kind === "switching") text = `正在切换到 ${full}；上次实际使用 ${phase.from}`;
      else if (ui.used[stage]) text = inCloud && endpoint ? `${endpoint.model}（云端服务返回的模型名）` : `${full}（本地，已实际加载）`;
      return `<div><dt>${label}</dt><dd>${esc(text)}</dd></div>`;
    }).join("");
  }

  function render() {
    const lang = LANG[ui.direction], output = draft.output, preset = presetById(draft.polishMode);
    const composer = $("composer");
    const docMode = draft.usesDocumentPolish();
    composer.classList.toggle("wide", isWide());
    composer.classList.toggle("has-res", draft.units.length > 0 && output !== "raw");

    // simulated foreground app context
    const meta = targetMeta(draft.target);
    $("menubar-title").textContent = meta.menubar;
    $("target-title").textContent = meta.title;
    $("target-text").placeholder = meta.placeholder;
    if ($("app-context").dataset.target !== String(draft.target ?? "agent")) {
      $("app-context").dataset.target = String(draft.target ?? "agent");
      $("app-context").innerHTML = meta.context;
    }

    // header
    const dest = $("dest");
    dest.className = `chip dest${draft.targetLost ? " bad" : draft.target === null ? " warn" : ""}`;
    dest.innerHTML = draft.targetLost ? `${icon("alert")}原输入框已失效` : draft.target === null ? "没有输入框 · 仅复制" : `${icon("target")}${esc(meta.chip)}`;
    const cloud = [ui.cloud.translation && "翻译", ui.cloud.cleanup && "整理"].filter(Boolean);
    $("privacy").className = `chip privacy ${cloud.length ? "warn" : "ok"}`;
    $("privacy").innerHTML = `<i class="dot"></i><span class="lbl">${cloud.length ? `云端 · ${cloud.join("、")}` : "本地"}</span>`;
    $("privacy").setAttribute("aria-label", `${cloud.length ? `云端处理：${cloud.join("、")}，发送到${activeEndpoint()?.name ?? "未知端点"}` : "全部本地处理"} · 查看本次实际使用的模型`);
    $("privacy").title = "查看本次实际使用的模型";

    const pillLabel = output === "translation"
      ? `→ ${lang.dst}`
      : output === "raw"
        ? "原话"
        : preset.id === "natural"
          ? `${lang.src} · 口语顺句`
          : preset.pill;
    const pillIcon = output === "translation" ? icon("globe") : output === "clean" && preset.id !== "natural" ? icon("sparkle") : "";
    $("out-pill").innerHTML = `${pillIcon}${esc(pillLabel)}${icon("chevron")}`;
    renderOutputMenu();

    document.querySelectorAll("[data-target]").forEach(item => {
      item.setAttribute("aria-checked", String((item.dataset.target === "none" ? null : item.dataset.target) === draft.target));
    });
    document.querySelector('[data-act="wide"]').setAttribute("aria-checked", String(ui.wide));
    document.querySelector('[data-act="whole"]').setAttribute("aria-checked", String(ui.forceWhole));
    document.querySelectorAll(".k-summon").forEach(el => { el.textContent = keyLabel(ui.binding); });
    document.querySelectorAll(".k-commit").forEach(el => { el.textContent = commitKeyLabel(); });
    document.querySelectorAll(".k-mod-range").forEach(el => { el.textContent = ui.platform === "mac" ? "⌘1–5" : "Ctrl+1–5"; });

    // banner
    const banner = bannerState();
    $("banner").hidden = !banner;
    if (banner) {
      $("banner").className = `banner ${banner.tone}`;
      $("banner").innerHTML = `${icon(banner.tone === "info" ? "info" : "alert")}<p>${esc(banner.text)}</p>${banner.progress ? `<progress max="100" value="${banner.progress}" aria-label="下载进度"></progress>` : ""}<span class="actions">${(banner.actions || []).map(([label, act]) => `<button type="button" data-banner="${act}">${esc(label)}</button>`).join("")}</span>`;
    }

    // source (auto-collapses to a 1-line summary when a long structured whole-document result is ready in narrow layout)
    const live = draft.recording || ui.timeline;
    const empty = draft.units.length === 0 && !draft.recording && $("src").value.trim() === "";
    const canCollapseSrc = !live && docMode && !isWide() && draft.docState(draft.polishMode) === "ready" && draft.units.length >= 4;
    const srcCollapsed = canCollapseSrc && !ui.srcExpanded;
    $("src-toggle").hidden = !canCollapseSrc;
    if (canCollapseSrc) {
      $("src-toggle").setAttribute("aria-expanded", String(!srcCollapsed));
      $("src-toggle").textContent = srcCollapsed ? `展开原话（${draft.units.length} 句）` : "收起原话";
    }
    $("src-collapsed").hidden = !srcCollapsed;
    if (srcCollapsed) {
      $("src-collapsed").textContent = `${draft.units[0]?.raw ?? ""} …（共 ${draft.units.length} 句识别原话）`;
    }
    $("src").hidden = live || srcCollapsed;
    $("src-live").hidden = !live;
    if (!live && !srcCollapsed) autosize();
    $("src").readOnly = !!draft.snapshot;
    if (live) {
      const lines = draft.units.map((unit, i) => `<div class="line">${ui.timeline ? `<time>${stamp(i * 3)}</time>` : ""}<span>${esc(unit.raw)}</span></div>`);
      if (ui.tail) lines.push(`<div class="line tail"><span>${esc(ui.tail)}</span><i class="caret"></i></div>`);
      $("src-live").innerHTML = lines.join("");
      $("src-live").scrollTop = $("src-live").scrollHeight;
    }
    $("src-label").textContent = ui.timeline ? `转录 · ${ui.fileName}` : draft.recording ? "正在听…" : "原话";
    $("route-chip").hidden = draft.route !== "whole";
    $("hint").hidden = !empty;
    const showRemembered = empty && !!ui.rememberedFrom;
    $("remembered").hidden = !showRemembered;
    if (showRemembered) {
      $("remembered-text").textContent = `已沿用「${ui.rememberedFrom.name}」上次的选择：${ui.rememberedFrom.summary}`;
    }

    // result pane: unit rows vs structured whole-document polish
    const showRes = draft.units.length > 0 && output !== "raw";
    $("res").hidden = !showRes;
    if (showRes) {
      if (output === "translation") {
        $("res-label").textContent = `将写入 · ${lang.dst}`;
      } else if (draft.recording && preset.id !== "natural") {
        $("res-label").textContent = `实时识别预览 · 停麦后生成「${preset.name}」`;
      } else if (docMode) {
        $("res-label").textContent = `将写入 · ${preset.name}`;
      } else {
        $("res-label").textContent = "将写入 · 口语顺句";
      }

      const gateChip = $("gate-chip");
      if (docMode) {
        const st = draft.docState(draft.polishMode);
        gateChip.hidden = st === "pending";
        if (st === "error") {
          gateChip.className = "chip mini bad";
          gateChip.textContent = preset.gate === "fact-anchor" ? "事实锚点已拦截" : "保真门禁已拦截";
        } else if (st === "stale") {
          gateChip.className = "chip mini warn";
          gateChip.textContent = "待重新确认";
        } else if (st === "ready") {
          gateChip.className = "chip mini ok";
          gateChip.textContent = preset.gate === "fact-anchor" ? "事实锚点已校验" : "保真校验通过";
        }
      } else {
        gateChip.hidden = true;
      }

      $("rows").hidden = docMode;
      $("doc-view").hidden = !docMode;

      const docEntry = docMode ? draft.docEntry(draft.polishMode) : null;
      const struct = JSON.stringify([output, draft.polishMode, docMode, ui.editing, draft.snapshot !== null, draft.recording]);
      const data = docMode
        ? JSON.stringify([draft.docState(draft.polishMode), docEntry?.text, docEntry?.error, docEntry?.owned, docEntry?.stale])
        : JSON.stringify(draft.units.map(unit => [unit.id, unit.clean, unit.translation, unit.cleanError, unit.translationError, unit.warning, unit.cleanOwned, unit.translationOwned]));

      if (struct !== lastStruct || (data !== lastData && ui.editing === null)) {
        const scroller = $("composer").querySelector(".c-body");
        const wasBottom = scroller.scrollTop;
        if (docMode) {
          $("doc-view").innerHTML = docHTML();
        } else {
          $("rows").innerHTML = draft.units.map(unitHTML).join("");
        }
        lastStruct = struct;
        lastData = data;
        scroller.scrollTop = draft.recording ? 1e6 : wasBottom;
      }
    }

    // footer
    const mic = $("mic");
    mic.classList.toggle("recording", draft.recording);
    mic.setAttribute("aria-label", draft.recording ? "结束听写" : "开始听写，长按为按住说话");
    const status = $("status");
    status.textContent = statusText();
    const ready = draft.contentReadiness();
    status.className = `status${!draft.recording && draft.units.length && (ready.reason === "unavailable" || ready.reason === "stale") ? " warn" : ""}`;
    const model = modelLine(), modelEl = $("model-line");
    modelEl.hidden = !model;
    if (model) {
      modelEl.className = `model-line ${model.tone}`;
      modelEl.innerHTML = `<i class="dot" aria-hidden="true"></i><span>${esc(model.text)}</span>`;
      modelEl.title = model.text;
    }
    renderModelCard();
    $("copy").disabled = draft.recording || !ready.ok;
    const commitButton = $("commit");
    const liveReady = draft.readiness();
    commitButton.disabled = !!draft.snapshot || (!ui.armed && !liveReady.ok && liveReady.reason !== "processing");
    commitButton.innerHTML = ui.armed
      ? `<span class="spinner" aria-hidden="true"></span>完成后写入`
      : draft.snapshot
        ? "已写入"
        : `${draft.target === null ? "复制" : `写入 ${meta.chip}`} <kbd>${commitKeyLabel()}</kbd>`;
    commitButton.setAttribute("aria-label", ui.armed ? "取消完成后自动写入" : `${draft.target === null ? "复制" : "写入"}`);

    if (ui.armed) {
      const state = draft.readiness();
      if (state.ok) { ui.armed = false; submit(); }
      else if (state.reason !== "processing") { ui.armed = false; toast(REASONS[state.reason]); render(); }
    }
  }

  /* ---------- scenarios ---------- */
  function loadCorpus(targetId, polishMode, corpus) {
    fresh(targetId);
    draft.retarget(targetId);
    ui.direction = "zh-en";
    corpus.forEach(item => draft.append(item.raw));
    setSource();
    chooseOutput("clean", polishMode, true);
  }

  function scenario(name) {
    showPage("play");
    ui.micBlock = null;
    if (name === "compare" || name === "prompt") {
      loadCorpus("agent", "prompt", AI_DELEGATE);
      return;
    }
    if (name === "ai-steer") {
      loadCorpus("agent", "prompt", AI_STEER);
      return;
    }
    if (name === "todo") {
      loadCorpus("linear", "todo", AI_DELEGATE);
      return;
    }
    if (name === "report") {
      loadCorpus("report-doc", "report", AI_DELEGATE);
      return;
    }
    fresh(name === "no-target" ? null : "agent");
    draft.retarget(name === "no-target" ? null : "agent");
    ui.direction = "zh-en";
    if (name === "pair") {
      draft.setPolishMode("natural");
      startRecording(true);
    } else if (name === "model-preparing") {
      startRecording(true);
      ui.modelPhase = { stage: "speech", kind: "preparing" };
      render();
    } else if (name === "translate") {
      draft.output = "translation";
      startRecording(true);
    } else if (name === "document") {
      draft.output = "translation";
      $("src").value = DOC_RAW;
      autosize();
      syncFromSource();
    } else if (name === "meeting") {
      loadTranscript("会议录音（示例）.m4a");
    } else if (name === "prompt-hijack") {
      ZH_EN.slice(0, 3).forEach(item => draft.append(item.raw));
      setSource();
      ui.docEdge = "hijack";
      draft.setPolishMode("prompt");
      ensure();
      render();
    } else if (name === "prompt-slop") {
      ZH_EN.slice(0, 3).forEach(item => draft.append(item.raw));
      setSource();
      ui.docEdge = "slop";
      draft.setPolishMode("prompt");
      ensure();
      render();
    } else if (name === "fact-anchor") {
      ZH_EN.slice(0, 3).forEach(item => draft.append(item.raw));
      setSource();
      ui.docEdge = "fact-anchor";
      draft.retarget("linear");
      draft.setPolishMode("todo");
      ensure();
      render();
    } else {
      ZH_EN.slice(0, 3).forEach(item => draft.append(item.raw));
      setSource();
      draft.setPolishMode("natural");
      if (name === "gate") ui.gate = true;
      if (name === "slow") {
        ui.slow = true;
        ui.notice = { tone: "info", text: "处理比平时慢。你可以继续等待，或改用原话。", actions: [["改用原话", "use-raw"], ["继续等待", "keep-waiting"]] };
      }
      if (name === "failure") draft.output = "translation";
      if (name === "model-switch") {
        draft.output = "translation";
        ui.modelPhase = { stage: "translation", kind: "switching", from: "Hy-MT2-7B" };
      }
      if (name === "lost") draft.targetLost = true;
      if (name === "no-audio") ui.notice = { tone: "warn", text: "没有检测到声音，已停止录音，已有内容保留。", actions: [["检查输入设备", "mic-settings"], ["知道了", "dismiss"]] };
      if (name === "permission") {
        ui.micBlock = "permission";
        ui.notice = { tone: "warn", text: "麦克风权限未开启 · 仍可直接打字。授权后无需重启（示意）。", actions: [["打开系统设置", "open-os"], ["知道了", "dismiss"]] };
      }
      if (name === "download") {
        ui.micBlock = "download";
        ui.notice = { tone: "info", text: "语音模型下载中 · 现在可以直接打字。", progress: 46, actions: [["后台下载", "dismiss"]] };
      }
      ensure();
      if (name === "failure") {
        draft.invalidateTranslations();
        draft.units.forEach((unit, i) => draft.fail(draft.ticket(i), "translation", "示例连接超时 · 原话和整理稿未丢失"));
      }
      render();
    }
  }

  /* ---------- settings: polish presets & remembered apps ---------- */
  function renderDefaultPolishSelect() {
    const sel = $("default-polish");
    sel.innerHTML = presets.filter(p => p.enabled || p.id === ui.defaultPolish)
      .map(p => `<option value="${esc(p.id)}"${p.id === ui.defaultPolish ? " selected" : ""}>${esc(p.name)}</option>`)
      .join("");
  }

  function renderPresetsSettings() {
    renderDefaultPolishSelect();
    $("presets-list").innerHTML = presets.map(item => {
      const isDefault = item.id === ui.defaultPolish;
      const customized = item.builtIn && item.prompt !== item.defaultPrompt;
      const gateText = item.gate === "fact-anchor" ? "事实锚点校验" : "字级保真门禁";
      return `<li class="preset-card${isDefault ? " default" : ""}" data-preset-card="${esc(item.id)}">
        <div class="ep-main">
          <div class="ep-title">
            <b>${esc(item.name)}</b>
            <span class="chip mini">${item.builtIn ? "内置" : "自定义"}</span>
            <span class="chip mini accent">${esc(gateText)}</span>
            ${isDefault ? '<span class="chip mini ok">默认起点</span>' : ""}
            ${customized ? '<span class="chip mini warn">已微调</span>' : ""}
          </div>
          <p>${esc(item.desc)}</p>
        </div>
        <div class="ep-actions">
          <button type="button" class="link-btn" data-preset-edit="${esc(item.id)}">${item.builtIn ? "查看 / 微调" : "编辑"}</button>
          ${item.builtIn && customized ? `<button type="button" class="link-btn" data-preset-reset="${esc(item.id)}">恢复默认</button>` : ""}
          ${!item.builtIn ? `<button type="button" class="link-btn danger" data-preset-del="${esc(item.id)}" aria-label="删除 ${esc(item.name)}">删除</button>` : ""}
          <input type="checkbox" role="switch" data-preset-on="${esc(item.id)}" aria-label="在浮窗菜单显示 ${esc(item.name)}"${item.enabled || isDefault ? " checked" : ""}${isDefault ? " disabled" : ""}>
        </div>
      </li>`;
    }).join("");
  }

  function renderAppsMemory() {
    $("clear-apps").disabled = appMemory.size === 0;
    const knownKeys = ["agent", "linear", "report-doc", "notes"];
    const extraKeys = [...appMemory.keys()].filter(k => !knownKeys.includes(k));
    const allKeys = [...knownKeys, ...extraKeys];
    const modeOptions = [
      ["default", `跟随全局默认（${presetById(ui.defaultPolish).name}）`],
      ...offeredPresets().map(p => [`clean:${p.id}`, p.pill]),
      ["translation:zh-en", "翻译成 English"],
      ["translation:en-zh", "翻译成 中文"]
    ];
    $("apps-list").innerHTML = allKeys.map(key => {
      const saved = appMemory.get(key);
      const appName = saved?.name ?? (key === "·clipboard" ? "仅复制" : targetMeta(key).name);
      const currentVal = !saved
        ? "default"
        : saved.output === "translation"
          ? `translation:${saved.direction}`
          : `clean:${saved.polishMode}`;
      return `<li class="endpoint${saved ? " on" : ""}" data-app-row="${esc(key)}">
        <div class="ep-main">
          <div class="ep-title">
            <b>${esc(appName)}</b>
            ${saved ? `<span class="chip mini accent">${esc(choiceSummary(saved.output, saved.polishMode, saved.direction))}</span>` : '<span class="chip mini">跟随默认</span>'}
          </div>
          <p>${saved ? `上次更新：${esc(saved.time)} · 唤起时自动沿用` : "尚未单独指定，唤起时使用默认润色方式"}</p>
        </div>
        <div class="ep-actions">
          <select class="app-mode-select" data-app-mode="${esc(key)}" aria-label="${esc(appName)} 的润色或输出方式">
            ${modeOptions.map(([val, label]) => `<option value="${esc(val)}"${val === currentVal ? " selected" : ""}>${esc(label)}</option>`).join("")}
          </select>
          ${saved ? `<button type="button" class="link-btn danger" data-forget-app="${esc(key)}" aria-label="忘记 ${esc(appName)}">忘记</button>` : ""}
        </div>
      </li>`;
    }).join("");
  }

  function presetForm(existing) {
    const v = existing ?? { name: "", desc: "", gate: "fact-anchor", prompt: "" };
    const builtIn = !!existing?.builtIn;
    const starters = !existing
      ? `<div class="starter-row"><span>一键填入模板：</span>${STARTER_TEMPLATES.map((tpl, idx) => `<button type="button" class="starter-chip" data-starter="${idx}">${esc(tpl.name)}</button>`).join("")}</div>`
      : "";
    dialog(existing ? (builtIn ? `查看 / 微调「${existing.name}」` : `编辑「${existing.name}」`) : "添加润色方式", `${starters}<form id="preset-form" novalidate class="ep-form">
      <label>方式名称<input id="pr-name" value="${esc(v.name)}" maxlength="20" placeholder="例如 Bug 缺陷单 / Commit 说明"${builtIn ? " readonly" : " autofocus"} autocomplete="off"></label>
      <label>适用场景一句话说明<input id="pr-desc" value="${esc(v.desc)}" maxlength="40" placeholder="显示在浮窗输出菜单副标题中" autocomplete="off"></label>
      <label>安全校验门禁
        <select id="pr-gate"${builtIn ? " disabled" : ""}>
          <option value="fact-anchor"${v.gate === "fact-anchor" ? " selected" : ""}>结构化成稿 · 事实锚点校验（推荐：允许分点与标题，锁死数字和代码词）</option>
          <option value="verbatim"${v.gate === "verbatim" ? " selected" : ""}>逐句顺句 · 严格字级保真（仅去口头语，禁止大幅浓缩或加标题）</option>
        </select>
      </label>
      <label>润色要求与目标格式（10–1500 字）
        <textarea id="pr-prompt" rows="5"${builtIn ? " autofocus" : ""} placeholder="例如：提取【复现步骤】【预期行为】【实际现象】三段，保留错误码与接口名…">${esc(v.prompt)}</textarea>
        <small>「不编造事实、不篡改数字、不丢代码标识符」底线由系统固定保障，不可关闭。</small>
      </label>
      <p id="pr-error" class="ep-error" role="alert" hidden></p>
      <div class="dlg-actions">
        <button type="button" class="btn" data-dlg="close">取消</button>
        <button type="submit" class="btn primary">${existing ? "保存" : "添加"}</button>
      </div>
    </form>`);
    $("dlg-body").querySelectorAll("[data-starter]").forEach(btn => btn.addEventListener("click", () => {
      const tpl = STARTER_TEMPLATES[Number(btn.dataset.starter)];
      if (!tpl) return;
      $("pr-name").value = tpl.name;
      $("pr-desc").value = tpl.desc;
      $("pr-gate").value = tpl.gate;
      $("pr-prompt").value = tpl.prompt;
      $("pr-error").hidden = true;
      $("pr-name").focus();
    }));
    $("preset-form").addEventListener("input", () => { $("pr-error").hidden = true; });
    $("preset-form").addEventListener("submit", event => {
      event.preventDefault();
      const name = $("pr-name").value.trim();
      const desc = $("pr-desc").value.trim() || "自定义场景结构化润色";
      const gate = $("pr-gate").value;
      const prompt = $("pr-prompt").value.trim();
      const fail = (msg, field) => { $("pr-error").textContent = msg; $("pr-error").hidden = false; $(field).focus(); };
      if (!name) return fail("请填写方式名称。", "pr-name");
      if (!builtIn && presets.some(p => p !== existing && p.name.toLowerCase() === name.toLowerCase())) {
        return fail("已有同名润色方式，请换一个名称。", "pr-name");
      }
      if (prompt.length < 10 || prompt.length > 1500) {
        return fail("润色要求需要 10–1500 个字符。", "pr-prompt");
      }
      if (existing) {
        existing.desc = desc;
        existing.prompt = prompt;
        if (!builtIn) {
          existing.name = name;
          existing.pill = `润色 · ${name}`;
          existing.gate = gate;
        }
        draft.invalidatePolishMode(existing.id);
        ensure();
      } else {
        const id = `custom${++customSeq}`;
        presets.push({
          id,
          name,
          pill: `润色 · ${name}`,
          key: "",
          builtIn: false,
          enabled: true,
          gate,
          forbidExecution: false,
          desc,
          defaultPrompt: prompt,
          prompt
        });
      }
      $("dlg").close();
      renderPresetsSettings();
      render();
      toast(existing ? `已保存「${name}」` : `已添加「${name}」· 可在浮窗胶囊中直接选用`);
    });
  }

  function confirmDeletePreset(item) {
    dialog(`删除「${item.name}」？`, `<p>删除后浮窗菜单不再提供该润色方式；已记住该方式的应用将自动回退到默认「${esc(presetById(ui.defaultPolish).name)}」。</p>
      <div class="dlg-actions"><button type="button" class="btn" data-dlg="close" autofocus>保留</button><button type="button" class="btn primary danger" id="delete-preset">删除</button></div>`);
    $("delete-preset").addEventListener("click", () => {
      presets.splice(presets.indexOf(item), 1);
      if (ui.defaultPolish === item.id) ui.defaultPolish = "natural";
      if (draft.polishMode === item.id) draft.setPolishMode(ui.defaultPolish);
      for (const [k, mem] of appMemory.entries()) {
        if (mem.polishMode === item.id) appMemory.delete(k);
      }
      $("dlg").close();
      renderPresetsSettings();
      renderAppsMemory();
      ensure();
      render();
      toast(`已删除「${item.name}」`);
    });
  }

  /* ---------- events ---------- */
  document.addEventListener("click", event => {
    const el = event.target.closest("button, [data-sec]");
    if (!el) return;
    const pop = el.closest("[popover]");
    if (el.classList.contains("tab")) showPage(el.dataset.page);
    if (el.dataset.scenario) scenario(el.dataset.scenario);
    if (el.dataset.sec) showSettings(el.dataset.sec);
    if (el.dataset.row) rowAction(el.dataset.row, el.dataset.id === "doc" ? "doc" : Number(el.dataset.id));
    if (el.dataset.banner) bannerAction(el.dataset.banner);
    if (el.dataset.polish) {
      pop?.hidePopover();
      chooseOutput("clean", el.dataset.polish, true);
    }
    if (el.dataset.out) {
      pop?.hidePopover();
      chooseOutput(el.dataset.out, draft.polishMode, true);
    }
    if (el.dataset.target) {
      pop?.hidePopover();
      switchTarget(el.dataset.target === "none" ? null : el.dataset.target);
    }
    if (el.dataset.platform) {
      ui.platform = el.dataset.platform;
      document.documentElement.dataset.platform = ui.platform;
      document.querySelectorAll("[data-platform]").forEach(b => {
        if (b.getAttribute("role") === "radio") b.setAttribute("aria-checked", String(b.dataset.platform === ui.platform));
      });
      render();
    }
    if (el.dataset.act) {
      pop?.hidePopover();
      menuAction(el.dataset.act);
    }
    if (el.dataset.restore !== undefined) { $("dlg").close(); restore(Number(el.dataset.restore)); }
    if (el.dataset.export) { $("dlg").close(); download(el.dataset.export); }
    if (el.dataset.dlg === "close") $("dlg").close();
    if (el.dataset.dlg === "clipboard") { $("dlg").close(); copyCurrent(); }
  });

  $("restore-defaults").addEventListener("click", () => {
    appMemory.delete(memoryKey(draft.target));
    ui.rememberedFrom = null;
    applyStartChoice(draft.target, true);
    renderAppsMemory();
    ensure();
    render();
    toast("已恢复默认润色方式");
  });
  $("src-toggle").addEventListener("click", () => {
    ui.srcExpanded = !ui.srcExpanded;
    render();
    if (ui.srcExpanded) $("src").focus();
  });
  $("src-collapsed").addEventListener("click", () => {
    ui.srcExpanded = true;
    render();
    $("src").focus();
  });

  function bannerAction(action) {
    if (action === "retarget") $("dest").click();
    if (action === "copy") copyCurrent();
    if (action === "dismiss") { ui.notice = null; render(); }
    if (action === "use-raw") { ui.notice = null; ui.slow = false; chooseOutput("raw", draft.polishMode, false); }
    if (action === "keep-waiting") { ui.notice = null; ui.slow = false; inflight.clear(); ensure(); render(); }
    if (action === "mic-settings") { ui.notice = null; showSettings("voice"); }
    if (action === "open-os") toast("（演示）将打开系统隐私设置");
  }

  function menuAction(act) {
    if (act === "summon") showComposer();
    if (act === "hide") hideComposer();
    if (act === "settings") showSettings("general");
    if (act === "polish-settings") showSettings("voice", "presets-section");
    if (act === "new") { fresh(draft.target); $("src").focus(); }
    if (act === "swap") swapDirection(true);
    if (act === "import") $("file").click();
    if (act === "wide") { ui.wide = !ui.wide; render(); }
    if (act === "whole") { ui.forceWhole = !ui.forceWhole; if (!draft.recording && !ui.timeline) syncFromSource(); render(); }
    if (act === "history") {
      dialog("最近草稿", ui.history.length
        ? ui.history.map((entry, i) => `<div class="hist"><span>${esc(entry.time)} · ${esc(choiceSummary(entry.output, entry.polishMode, entry.direction))} · ${esc(entry.units[0]?.raw.slice(0, 28))}</span><button type="button" class="btn" data-restore="${i}"${i === 0 ? " autofocus" : ""}>恢复</button></div>`).join("")
        : "<p>还没有草稿。关闭或写入后会出现在这里；刷新页面会清除。</p>", "any");
    }
    if (act === "export") {
      dialog("导出当前内容", `<p>导出当前选定的内容（${esc(choiceSummary(draft.output, draft.polishMode, ui.direction))}）。SRT 使用示例时间轴。</p><div class="dlg-actions"><button type="button" class="btn" data-export="txt" autofocus>TXT</button><button type="button" class="btn" data-export="md">Markdown</button><button type="button" class="btn" data-export="srt">SRT</button></div>`, "any");
    }
  }

  $("summon").addEventListener("click", showComposer);
  $("close").addEventListener("click", hideComposer);
  $("commit").addEventListener("click", () => commit(false));
  $("copy").addEventListener("click", copyCurrent);
  $("dlg-close").addEventListener("click", () => $("dlg").close());
  $("dlg").addEventListener("close", () => { $("dlg").returnValue = ""; });
  if (!("closedBy" in HTMLDialogElement.prototype)) {
    const dlg = $("dlg");
    const outside = e => {
      const r = dlg.getBoundingClientRect();
      return e.clientX < r.left || e.clientX > r.right || e.clientY < r.top || e.clientY > r.bottom;
    };
    let pressedOut = false;
    dlg.addEventListener("pointerdown", e => { pressedOut = dlg.getAttribute("closedby") === "any" && outside(e); });
    dlg.addEventListener("pointerup", e => { if (pressedOut && outside(e)) dlg.close(); pressedOut = false; });
  }
  $("file").addEventListener("change", () => {
    const file = $("file").files[0];
    if (file) loadTranscript(file.name, true);
    $("file").value = "";
  });
  $("edge").addEventListener("change", () => {
    if ($("edge").value) scenario($("edge").value);
    $("edge").selectedIndex = 0;
  });
  $("theme-btn").addEventListener("click", () => {
    ui.theme = document.documentElement.dataset.theme === "dark" ? "light" : "dark";
    $("theme").value = ui.theme;
    applyTheme();
  });

  // microphone: tap = hands-free, press and hold = push-to-talk
  const mic = $("mic");
  let pressed = null;
  mic.addEventListener("pointerdown", event => {
    if (event.button !== 0) return;
    pressed = { at: performance.now(), wasRecording: draft.recording };
    if (!draft.recording) startRecording(false);
    mic.setPointerCapture(event.pointerId);
  });
  mic.addEventListener("pointerup", () => {
    if (!pressed) return;
    const { at, wasRecording } = pressed;
    pressed = null;
    lastPointerUp = performance.now();
    if (wasRecording) stopRecording();
    else if (!draft.recording) return;
    else if (performance.now() - at >= 350) stopRecording();
    else { ui.handsFree = true; render(); }
  });
  mic.addEventListener("pointercancel", () => { pressed = null; if (draft.recording && !ui.handsFree) stopRecording(); });
  mic.addEventListener("click", () => {
    if (performance.now() - lastPointerUp < 400) return;
    if (draft.recording) stopRecording();
    else startRecording(true);
  });

  // source input
  const src = $("src");
  src.addEventListener("input", () => {
    if (ui.editing !== null) saveEdit();
    autosize();
    ui.armed = false;
    clearTimeout(syncTimer);
    syncTimer = null;
    if (!ui.composing) syncTimer = setTimeout(syncFromSource, 320);
    render();
  });
  document.addEventListener("compositionstart", () => { ui.composing = true; });
  document.addEventListener("compositionend", event => {
    ui.composing = false;
    ui.lastCompositionEndAt = event.timeStamp;
    if (event.target === src) {
      clearTimeout(syncTimer);
      syncTimer = setTimeout(syncFromSource, 320);
    }
  });

  // modern-web-guidance menu: anchor pairing + @supports fallback + focusgroup fallback + aria-expanded in toggle
  let pointerPressedTrigger = null;
  const enterOnOpen = new WeakSet();
  document.querySelectorAll("[popovertarget]").forEach((trigger, i) => {
    const menu = $(trigger.getAttribute("popovertarget"));
    if (!menu) return;
    const anchorName = `--ll-anchor-${i}`;
    trigger.style.anchorName = anchorName;
    menu.style.positionAnchor = anchorName;
    trigger.addEventListener("pointerdown", () => { pointerPressedTrigger = trigger; });
    trigger.addEventListener("click", () => {
      if (pointerPressedTrigger !== trigger) enterOnOpen.add(menu);
      pointerPressedTrigger = null;
    });
    trigger.addEventListener("keydown", event => {
      if (event.key === "ArrowDown" && !menu.matches(":popover-open")) {
        event.preventDefault();
        enterOnOpen.add(menu);
        trigger.click();
      }
    });
    menu.addEventListener("toggle", event => {
      const open = event.newState === "open";
      trigger.setAttribute("aria-expanded", String(open));
      if (!open) return;
      if (!CSS.supports("position-area", "block-end")) {
        const rect = trigger.getBoundingClientRect(), width = menu.offsetWidth, height = menu.offsetHeight;
        let top = rect.bottom + 6;
        if (top + height > innerHeight - 8) top = Math.max(8, rect.top - height - 6);
        const left = menu.id === "menu-target"
          ? Math.min(Math.max(8, rect.left), innerWidth - width - 8)
          : Math.min(Math.max(8, rect.right - width), innerWidth - width - 8);
        menu.style.left = `${left}px`;
        menu.style.top = `${top}px`;
      }
      if (enterOnOpen.has(menu)) {
        enterOnOpen.delete(menu);
        (menu.querySelector('[aria-checked="true"]:not(:disabled)') || menu.querySelector("button:not(:disabled)"))?.focus({ preventScroll: true });
      }
    });
  });

  if (!("focusGroup" in HTMLElement.prototype)) {
    document.querySelectorAll('[focusgroup~="menu"]').forEach(group => {
      group.setAttribute("role", "menu");
      group.querySelectorAll("button:not([role])").forEach(item => item.setAttribute("role", "menuitem"));
      group.addEventListener("keydown", event => {
        if (event.key === "Tab") {
          group.closest("[popover]")?.hidePopover();
          return;
        }
        if (!["ArrowDown", "ArrowUp", "Home", "End"].includes(event.key)) return;
        const items = [...group.querySelectorAll("button")].filter(item => !item.disabled);
        if (!items.length) return;
        const current = items.indexOf(document.activeElement);
        const next = event.key === "Home" ? 0
          : event.key === "End" ? items.length - 1
          : current < 0 ? 0
          : (current + (event.key === "ArrowUp" ? -1 : 1) + items.length) % items.length;
        event.preventDefault();
        event.stopImmediatePropagation();
        items[next].focus({ preventScroll: true });
      }, true);
    });
    document.querySelectorAll('[focusgroup~="tablist"]').forEach(tablist => {
      tablist.setAttribute("role", "tablist");
      const tabs = [...tablist.querySelectorAll("button")];
      tabs.forEach(tab => tab.setAttribute("role", "tab"));
      tablist.addEventListener("keydown", event => {
        if (!["ArrowDown", "ArrowUp", "ArrowRight", "ArrowLeft", "Home", "End"].includes(event.key)) return;
        const current = tabs.indexOf(document.activeElement);
        if (current < 0) return;
        const forward = event.key === "ArrowDown" || event.key === "ArrowRight";
        const next = event.key === "Home" ? 0
          : event.key === "End" ? tabs.length - 1
          : (current + (forward ? 1 : -1) + tabs.length) % tabs.length;
        event.preventDefault();
        tabs[next].focus({ preventScroll: true });
        showSettings(tabs[next].dataset.sec);
      });
    });
  }

  document.querySelectorAll("[data-panel]").forEach(panel => {
    panel.addEventListener("beforematch", () => showSettings(panel.dataset.panel));
  });

  // Also support ArrowDown from trigger after pointer-click open
  document.addEventListener("keydown", event => {
    const openMenu = popoverOpen();
    if (!openMenu) return;
    if (["ArrowDown", "ArrowUp"].includes(event.key) && !openMenu.contains(document.activeElement)) {
      const first = openMenu.querySelector('[aria-checked="true"]:not(:disabled)') || openMenu.querySelector("button:not(:disabled)");
      if (first) {
        event.preventDefault();
        first.focus({ preventScroll: true });
      }
    }
  }, true);

  // global & composer keyboard shortcuts
  document.addEventListener("keydown", event => {
    if (isImeComposing(event)) return;
    if (matches(event, ui.binding)) { event.preventDefault(); showComposer(); return; }
    if (dialogOpen() || popoverOpen() || $("page-play").hidden || $("composer").hidden) return;

    // Direct polish / output mode shortcuts: Cmd/Ctrl + 1..5, 6 (translate), 0 (raw)
    const primaryMod = ui.platform === "mac" ? event.metaKey && !event.ctrlKey : event.ctrlKey && !event.metaKey;
    if (primaryMod && !event.altKey && !event.shiftKey) {
      if (["1", "2", "3", "4", "5"].includes(event.key) && ui.cleanup !== "off") {
        const hit = offeredPresets().find(p => p.key === event.key);
        if (hit) { event.preventDefault(); chooseOutput("clean", hit.id, true); return; }
      }
      if (event.key === "6") { event.preventDefault(); chooseOutput("translation", draft.polishMode, true); return; }
      if (event.key === "0") { event.preventDefault(); chooseOutput("raw", draft.polishMode, true); return; }
    }

    const target = event.target;
    if (event.key === "Escape") {
      event.preventDefault();
      if (ui.editing !== null) cancelEdit();
      else if (ui.armed) { ui.armed = false; render(); }
      else if (draft.recording) stopRecording();
      else hideComposer();
      return;
    }
    if (event.key !== "Enter" || event.shiftKey) return;
    if (target.classList?.contains("edit")) {
      if (target.classList.contains("doc-edit") && !(event.ctrlKey || event.metaKey)) return;
      event.preventDefault();
      saveEdit();
      return;
    }
    if (!(target === src || target === document.body || target === mic)) return;
    const chord = ui.commitKey === "ctrl-enter" ? event.ctrlKey || event.metaKey : !event.ctrlKey && !event.metaKey && !event.altKey;
    if (chord) { event.preventDefault(); commit(true); }
  });

  $("rows").addEventListener("input", event => { if (event.target.classList.contains("edit")) ui.editValue = event.target.value; });
  $("doc-view").addEventListener("input", event => { if (event.target.classList.contains("edit")) ui.editValue = event.target.value; });
  window.addEventListener("blur", () => { if (draft.recording && !ui.handsFree) stopRecording(); });
  window.addEventListener("resize", render);

  // settings listeners
  $("summon-binding").addEventListener("input", () => { ui.binding = $("summon-binding").value; render(); });
  $("commit-binding").addEventListener("change", () => { ui.commitKey = $("commit-binding").value; render(); });
  $("theme").addEventListener("change", () => { ui.theme = $("theme").value; applyTheme(); });
  $("reduce-motion").addEventListener("change", () => { document.documentElement.dataset.motion = $("reduce-motion").checked ? "off" : ""; });
  $("remember-apps").addEventListener("change", () => {
    ui.rememberApps = $("remember-apps").checked;
    if (!ui.rememberApps) ui.rememberedFrom = null;
    render();
  });
  $("clear-apps").addEventListener("click", () => {
    appMemory.clear();
    ui.rememberedFrom = null;
    renderAppsMemory();
    render();
    toast("已清除所有应用偏好记忆");
  });
  $("apps-list").addEventListener("click", event => {
    const forget = event.target.closest("[data-forget-app]");
    if (!forget) return;
    appMemory.delete(forget.dataset.forgetApp);
    if (memoryKey(draft.target) === forget.dataset.forgetApp) {
      ui.rememberedFrom = null;
      if (!draft.units.length) applyStartChoice(draft.target);
    }
    renderAppsMemory();
    render();
  });
  $("apps-list").addEventListener("change", event => {
    const sel = event.target.closest("[data-app-mode]");
    if (!sel) return;
    const key = sel.dataset.appMode;
    const val = sel.value;
    if (val === "default") {
      appMemory.delete(key);
    } else if (val.startsWith("translation:")) {
      const dir = val.split(":")[1];
      appMemory.set(key, {
        target: key === "·clipboard" ? null : key,
        name: key === "·clipboard" ? "仅复制" : targetMeta(key).name,
        output: "translation",
        polishMode: ui.defaultPolish,
        direction: dir,
        time: "设置中指定"
      });
    } else if (val.startsWith("clean:")) {
      const modeId = val.split(":")[1];
      appMemory.set(key, {
        target: key === "·clipboard" ? null : key,
        name: key === "·clipboard" ? "仅复制" : targetMeta(key).name,
        output: "clean",
        polishMode: modeId,
        direction: ui.direction,
        time: "设置中指定"
      });
    }
    if (memoryKey(draft.target) === key && !draft.units.length) {
      applyStartChoice(draft.target);
    }
    renderAppsMemory();
    render();
  });

  $("cleanup").addEventListener("change", () => {
    ui.cleanup = $("cleanup").value;
    if (ui.cleanup === "off" && draft.output === "clean") draft.output = "raw";
    render();
  });
  $("default-polish").addEventListener("change", () => {
    ui.defaultPolish = $("default-polish").value;
    const targetPreset = presetById(ui.defaultPolish);
    targetPreset.enabled = true;
    if (!draft.units.length && !ui.rememberedFrom && draft.output === "clean") {
      draft.setPolishMode(ui.defaultPolish);
    }
    renderPresetsSettings();
    render();
  });
  $("direction-setting").addEventListener("change", () => { if ($("direction-setting").value !== ui.direction) swapDirection(false); });
  $("cleaner-model").addEventListener("change", () => {
    ui.cleanerModel = $("cleaner-model").value;
    $("cleaner-chip").textContent = `整理与润色 · ${ui.cleanerModel}`;
    $("cleaner-quality-hint").hidden = ui.cleanerModel !== "Qwen3.5 2B";
    render();
  });
  $("clear-history").addEventListener("click", () => { ui.history = []; toast("最近草稿已清除"); });
  $("show-diagnostics").addEventListener("click", () => {
    dialog("本次状态（不含正文）", `<pre>${esc(JSON.stringify({
      output: draft.output,
      polishMode: draft.polishMode,
      route: draft.route,
      recording: draft.recording,
      target: draft.target,
      targetLost: draft.targetLost,
      units: draft.units.length,
      submitted: !!draft.snapshot,
      rememberedApps: appMemory.size,
      cloud: ui.cloud
    }, null, 2))}</pre>`, "any");
  });

  $("add-preset").addEventListener("click", () => presetForm(null));
  $("presets-list").addEventListener("click", event => {
    const edit = event.target.closest("[data-preset-edit]");
    const reset = event.target.closest("[data-preset-reset]");
    const del = event.target.closest("[data-preset-del]");
    if (edit) presetForm(presetById(edit.dataset.presetEdit));
    if (reset) {
      const p = presetById(reset.dataset.presetReset);
      p.prompt = p.defaultPrompt;
      draft.invalidatePolishMode(p.id);
      renderPresetsSettings();
      ensure();
      render();
      toast(`「${p.name}」已恢复内置默认要求`);
    }
    if (del) confirmDeletePreset(presetById(del.dataset.presetDel));
  });
  $("presets-list").addEventListener("change", event => {
    const sw = event.target.closest("[data-preset-on]");
    if (!sw) return;
    const p = presetById(sw.dataset.presetOn);
    p.enabled = sw.checked;
    if (!p.enabled && draft.polishMode === p.id) {
      draft.setPolishMode(ui.defaultPolish);
      ensure();
    }
    renderPresetsSettings();
    render();
  });

  ["cloud-translation", "cloud-cleanup"].forEach(id => $(id).addEventListener("change", () => {
    const kind = id === "cloud-translation" ? "translation" : "cleanup";
    if (!$(id).checked) { ui.cloud[kind] = false; render(); return; }
    $(id).checked = false;
    const ep = activeEndpoint();
    if (!ep) { toast("先在下方启用一个云端端点"); return; }
    dialog(`同意云端${kind === "translation" ? "翻译" : "整理与润色"}？`, `<p>开启后，相应文本会发送到「${esc(ep.name)}」（${esc(hostOf(ep.url))}）；语音不会上传。本演示不发送任何请求。</p><div class="dlg-actions"><button type="button" class="btn" data-dlg="close" autofocus>保持本地</button><button type="button" class="btn primary" id="allow-cloud">同意</button></div>`);
    $("allow-cloud").addEventListener("click", () => { ui.cloud[kind] = true; $(id).checked = true; $("dlg").close(); render(); });
  }));

  // cloud endpoints: any number can be saved, at most one is enabled
  const hostOf = url => { try { return new URL(url).host; } catch { return url; } };
  const activeEndpoint = () => endpoints.find(ep => ep.enabled);
  const cloudOn = () => ui.cloud.translation || ui.cloud.cleanup;
  const cloudSteps = () => [ui.cloud.translation && "云端翻译", ui.cloud.cleanup && "云端整理"].filter(Boolean).join("、");
  function syncCloudSwitches() { $("cloud-translation").checked = ui.cloud.translation; $("cloud-cleanup").checked = ui.cloud.cleanup; }
  function cloudOff(reason) {
    const steps = cloudSteps();
    ui.cloud.translation = ui.cloud.cleanup = false;
    syncCloudSwitches();
    if (steps) toast(`${reason}，${steps}已关闭，需要时重新同意`);
  }
  function renderEndpoints() {
    const ep = activeEndpoint();
    $("endpoints-sub").textContent = ep ? `云端处理只会发送到「${ep.name}」；同一时间只能启用一个` : "没有启用的端点，所有处理都在本机";
    $("endpoints-list").innerHTML = endpoints.length ? endpoints.map(item => `<li class="endpoint${item.enabled ? " on" : ""}">
      <div class="ep-main"><div class="ep-title"><b>${esc(item.name)}</b>${item.enabled ? '<span class="chip mini ok">已启用</span>' : ""}</div>
        <p>${esc(hostOf(item.url))} · ${esc(item.model)} · 密钥${item.hasKey ? "已保存" : "未保存"}</p></div>
      <div class="ep-actions"><button type="button" class="link-btn" data-ep-edit="${item.id}" aria-label="编辑 ${esc(item.name)}">编辑</button><button type="button" class="link-btn danger" data-ep-del="${item.id}" aria-label="删除 ${esc(item.name)}">删除</button>
        <input type="checkbox" role="switch" data-ep-on="${item.id}" aria-label="启用 ${esc(item.name)}"${item.enabled ? " checked" : ""}></div></li>`).join("")
      : '<li class="ep-empty">还没有端点。添加后默认不启用，不会改变数据去向。</li>';
  }
  function enableEndpoint(id) {
    const next = endpoints.find(item => item.id === id);
    const apply = () => { endpoints.forEach(item => { item.enabled = item.id === id; }); renderEndpoints(); render(); };
    if (!cloudOn()) { apply(); return; }
    renderEndpoints();
    dialog("切换云端端点？", `<p>${esc(cloudSteps())}正在使用「${esc(activeEndpoint().name)}」。切换后，相应文本将改为发送到「${esc(next.name)}」（${esc(hostOf(next.url))}）。</p><div class="dlg-actions"><button type="button" class="btn" data-dlg="close" autofocus>保持不变</button><button type="button" class="btn primary" id="switch-ep">切换</button></div>`);
    $("switch-ep").addEventListener("click", () => { apply(); $("dlg").close(); toast(`已切换到「${next.name}」`); });
  }
  function disableEndpoint(id) {
    endpoints.find(item => item.id === id).enabled = false;
    cloudOff("端点已停用");
    renderEndpoints();
    render();
  }
  function endpointForm(existing) {
    const v = existing ?? { name: "", url: "", model: "", hasKey: false };
    dialog(existing ? "编辑端点" : "添加端点", `<form id="ep-form" novalidate class="ep-form">
      <label>名称<input id="ep-name" value="${esc(v.name)}" maxlength="40" placeholder="例如 Foundry · 北美" autocomplete="off" autofocus></label>
      <label>连接地址<input id="ep-url" type="url" value="${esc(v.url)}" placeholder="https://your-endpoint.example/v1" autocomplete="off"><small>只发送文本，不发送音频；必须是 https（本机可用 http）</small></label>
      <label>模型或部署名<input id="ep-model" value="${esc(v.model)}" placeholder="例如 gpt-4o-mini" autocomplete="off"></label>
      <label>API 密钥<input id="ep-key" type="password" placeholder="${v.hasKey ? "已保存；留空则保持不变" : "sk-…"}" autocomplete="off"><small>实际产品存入系统钥匙串；原型不保存，请勿输入真实密钥</small></label>
      <p id="ep-error" class="ep-error" role="alert" hidden></p>
      <div class="dlg-actions"><button type="button" class="btn" data-dlg="close">取消</button><button type="submit" class="btn primary">${existing ? "保存" : "添加"}</button></div></form>`);
    $("ep-name").focus();
    $("ep-form").addEventListener("submit", event => {
      event.preventDefault();
      const name = $("ep-name").value.trim(), url = $("ep-url").value.trim().replace(/\/+$/, ""), model = $("ep-model").value.trim(), key = $("ep-key").value.trim();
      const fail = (message, field) => { $("ep-error").textContent = message; $("ep-error").hidden = false; $(field).focus(); };
      let parsed = null;
      try { parsed = new URL(url); } catch { /* reported below */ }
      const local = parsed && ["localhost", "127.0.0.1", "[::1]"].includes(parsed.hostname);
      if (!name) return fail("请填写名称。", "ep-name");
      if (endpoints.some(item => item !== existing && item.name.toLowerCase() === name.toLowerCase())) return fail("已有同名端点，请换一个名称。", "ep-name");
      if (!parsed || !(parsed.protocol === "https:" || (parsed.protocol === "http:" && local))) return fail("连接地址必须是 https（仅本机可用 http）。", "ep-url");
      if (!model) return fail("请填写模型或部署名。", "ep-model");
      if (!key && !v.hasKey) return fail("请填写 API 密钥。", "ep-key");
      if (existing) {
        const redirected = existing.enabled && cloudOn() && hostOf(existing.url) !== hostOf(url);
        Object.assign(existing, { name, url, model, hasKey: true });
        if (redirected) cloudOff("该端点的地址已更改");
      } else endpoints.push({ id: `ep${++endpointSeq}`, name, url, model, hasKey: true, enabled: false });
      $("dlg").close();
      renderEndpoints();
      render();
      toast(existing ? `已保存「${name}」` : `已添加「${name}」· 尚未启用`);
    });
  }
  function confirmDelete(item) {
    dialog(`删除「${item.name}」？`, `<p>同时删除它保存的 API 密钥，无法恢复。${item.enabled && cloudOn() ? `${esc(cloudSteps())}将随之关闭。` : ""}</p><div class="dlg-actions"><button type="button" class="btn" data-dlg="close" autofocus>取消</button><button type="button" class="btn primary danger" id="delete-ep">删除</button></div>`);
    $("delete-ep").addEventListener("click", () => {
      if (item.enabled) cloudOff("端点已删除");
      endpoints.splice(endpoints.indexOf(item), 1);
      $("dlg").close();
      renderEndpoints();
      render();
    });
  }
  $("add-endpoint").addEventListener("click", () => endpointForm(null));
  $("endpoints-list").addEventListener("click", event => {
    const edit = event.target.closest("[data-ep-edit]"), del = event.target.closest("[data-ep-del]");
    if (edit) endpointForm(endpoints.find(item => item.id === edit.dataset.epEdit));
    if (del) confirmDelete(endpoints.find(item => item.id === del.dataset.epDel));
  });
  $("endpoints-list").addEventListener("change", event => {
    const sw = event.target.closest("[data-ep-on]");
    if (!sw) return;
    if (sw.checked) enableEndpoint(sw.dataset.epOn);
    else disableEndpoint(sw.dataset.epOn);
  });

  function renderTerms() {
    $("terms-list").innerHTML = terms.map((term, i) => `<li class="term-row"><span>${esc(term.source)}</span><span aria-hidden="true">→</span><span>${esc(term.target)}</span><button type="button" data-term="${i}" aria-label="删除术语 ${esc(term.source)}">删除</button></li>`).join("");
  }
  $("terms-list").addEventListener("click", event => {
    const del = event.target.closest("[data-term]");
    if (del) { terms.splice(Number(del.dataset.term), 1); renderTerms(); }
  });
  $("term-form").addEventListener("submit", event => {
    event.preventDefault();
    const source = $("term-source").value.trim(), target = $("term-target").value.trim();
    if (!source || !target || terms.some(term => term.source.toLowerCase() === source.toLowerCase())) {
      toast("原词不能为空，也不能重复");
      return;
    }
    terms.push({ source, target });
    event.target.reset();
    renderTerms();
    toast("已添加 · 原型的预设译文不会应用术语表");
  });

  matchMedia("(prefers-color-scheme: dark)").addEventListener("change", () => { if (ui.theme === "auto") applyTheme(); });
  applyTheme();
  applyStartChoice("agent");
  renderTerms();
  renderEndpoints();
  renderPresetsSettings();
  renderAppsMemory();
  autosize();
  render();
})();
