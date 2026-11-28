(function () {
  "use strict";

  const $ = id => document.getElementById(id);
  const esc = s => String(s ?? "").replace(/[&<>"']/g, ch => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[ch]);
  const ic = name => `<svg class="ic" aria-hidden="true"><use href="#i-${name}"/></svg>`;

  const TARGETS = {
    agent: {
      name: "Cursor", menubar: "Cursor", title: "Cursor — Agent", chip: "Agent", smartPreset: "ai-task",
      placeholder: "给 Agent 发指令…",
      context: `<p class="bubble me">我们继续看登录重试和缓存超时这块。</p><p class="bubble ai">代码已读完。请直接说出你的目标、不能碰的接口边界以及是否直接改代码。</p>`
    },
    linear: {
      name: "Linear", menubar: "Linear", title: "Linear — 新建任务清单", chip: "Linear", smartPreset: "todo",
      placeholder: "粘贴待办清单（支持 - [ ] Markdown）…",
      context: `<div class="context-sheet"><b>Sprint 24 · 稳定性专项</b><p>识别到任务管理工具：默认落位「润色 · 待办与派单」，自动过滤口述废案并提取 - [ ] 清单。</p></div>`
    },
    "report-doc": {
      name: "飞书文档", menubar: "飞书文档", title: "飞书文档 — 架构评审周报", chip: "飞书文档", smartPreset: "report",
      placeholder: "写入评审结论、关键决策与后续行动…",
      context: `<div class="context-sheet"><b>本周技术评审纪要</b><p>识别到文档协同宿主：默认落位「润色 · 汇报与纪要」，按结论先行（BLUF）重组流水账。</p></div>`
    },
    notes: {
      name: "Slack", menubar: "Slack", title: "Slack — #eng-backend", chip: "Slack", smartPreset: "natural",
      placeholder: "发消息到 #eng-backend…",
      context: `<p class="bubble ai">评审刚结束，麻烦把刚才定的 Redis 和 PR 处理结论同步一下？</p>`
    }
  };

  const SCENARIOS = {
    compare: {
      target: "agent",
      output: "clean",
      mode: "ai-task",
      raw: "呃我想改一下登录和缓存这块，先把客户端重试设成 3 次吧……哦不对，网关层已经有重试了，客户端干脆把自动重试关掉，只保留 401 刷新 token 的逻辑。另外 Redis 超时设成 30 秒不是 30 分钟。保持 `CacheService` 公共 API 不变，你先帮我查一下 `AuthInterceptor` 还有没有隐式重试，列出来给我看，先千万别直接改代码。",
      doc: "### 任务目标\n排查 `AuthInterceptor` 中是否存在隐式重试调用，并结合 `CacheService` 与超时边界给出改造方案（先列方案，不要直接改代码）。\n\n### 上下文与目标行为\n- 网关层已具备重试机制，客户端关闭自动重试，仅保留 `401` 刷新 Token 流程\n- Redis 超时时间设定为 30 秒（非 30 分钟）\n\n### 硬性约束（Do / Don't）\n- 保持 `CacheService` 公共 API 签名不变\n- 当前阶段禁止直接改动业务代码",
      gate: "事实锚点已校验"
    },
    todo: {
      target: "linear",
      output: "clean",
      mode: "todo",
      raw: "今天先别急着合这个 PR，等 CI 全绿再合。Redis 超时确认改成 30 秒，会后麻烦小王把纪要同步到群里。",
      doc: "- [ ] 等待 CI 检查全部通过后再合并当前 PR\n- [ ] 将 Redis 超时配置确认为 30 秒\n- [ ] 整理本次评审纪要并同步团队全员（负责人：小王 · 会后）",
      gate: "事实锚点已校验"
    },
    translate: {
      target: "notes",
      output: "translation",
      mode: "natural",
      raw: "这个 PR 先不要合并，等 CI 跑完再说。保持 `CacheService` 的 API 不变。",
      doc: "Do not merge this PR yet. Wait for CI to finish. Keep the API of `CacheService` unchanged.",
      gate: "保真校验通过"
    },
    pair: {
      target: "notes",
      output: "clean",
      mode: "natural",
      raw: "嗯，就是那个 Redis 超时我们刚刚对齐好了，设成 30 秒不是 30 分钟，然后 `CacheService` 的接口先不动。",
      doc: "Redis 超时我们刚刚对齐好了，设成 30 秒而不是 30 分钟，`CacheService` 的接口先不动。",
      gate: "保真校验通过"
    }
  };

  /* Mirrors src/LiveLingo.Desktop/Composer/ComposerShortcuts.cs 1:1 */
  const SHORTCUTS = [
    { id: "dictate", code: "KeyD", key: "D", shift: false },
    { id: "polish:ai-task", code: "Digit1", key: "1", shift: false },
    { id: "polish:natural", code: "Digit2", key: "2", shift: false },
    { id: "polish:formal", code: "Digit3", key: "3", shift: false },
    { id: "polish:todo", code: "Digit4", key: "4", shift: false },
    { id: "polish:report", code: "Digit5", key: "5", shift: false },
    { id: "translate", code: "KeyT", key: "T", shift: false },
    { id: "original", code: "Digit0", key: "0", shift: false },
    { id: "swap", code: "KeyL", key: "L", shift: false },
    { id: "copy", code: "KeyC", key: "C", shift: true },
    { id: "new", code: "KeyN", key: "N", shift: false },
    { id: "recent", code: "KeyR", key: "R", shift: false },
    { id: "import", code: "KeyO", key: "O", shift: false },
    { id: "export", code: "KeyS", key: "S", shift: false },
    { id: "zoom", code: "KeyE", key: "E", shift: false },
    { id: "settings", code: "Comma", key: ",", shift: false }
  ];

  function capOf(id) {
    const sc = SHORTCUTS.find(s => s.id === id);
    if (!sc) return "";
    const mac = (window.LiveLingoSettings?.state.platform ?? "mac") === "mac";
    return mac
      ? (sc.shift ? "⇧⌘" : "⌘") + sc.key
      : (sc.shift ? "Ctrl+Shift+" : "Ctrl+") + sc.key;
  }

  const composerState = {
    target: "agent",
    output: "clean",
    mode: "ai-task",
    wide: true,
    whole: false,
    zoomed: false,
    hidden: false,
    recording: false,
    recordTimer: null,
    recordStep: 0,
    recordSecs: 0,
    raw: SCENARIOS.compare.raw,
    doc: SCENARIOS.compare.doc,
    gate: SCENARIOS.compare.gate,
    recentDrafts: [
      { id: "d1", title: "排查 AuthInterceptor 隐式重试边界", raw: SCENARIOS.compare.raw, mode: "ai-task", output: "clean", time: "2 分钟前" },
      { id: "d2", title: "Sprint 24 Redis 超时与 PR 合并待办", raw: SCENARIOS.todo.raw, mode: "todo", output: "clean", time: "18 分钟前" },
      { id: "d3", title: "CacheService API 兼容声明（英文）", raw: SCENARIOS.translate.raw, mode: "natural", output: "translation", time: "1 小时前" }
    ],
    lastExportFormat: "markdown"
  };

  /* Apply glossary corrections & dynamically synthesize output for any mode/output/raw text */
  function applyCorrections(text, api) {
    let out = String(text ?? "");
    for (const c of api.state.glossary.corrections) {
      if (c.spoken && c.canonical) out = out.split(c.spoken).join(c.canonical);
    }
    return out;
  }

  function synthesizeOutput(raw, output, modeId, api) {
    const cleaned = applyCorrections(raw, api).trim();
    if (!cleaned) return { text: "", gate: "" };
    if (output === "raw") return { text: cleaned, gate: "" };

    const dir = api.state.voice.direction;
    if (output === "translation") {
      // Check built-in preset pairs first
      if (cleaned.includes("AuthInterceptor")) {
        return {
          text: dir === "English"
            ? "Check whether `AuthInterceptor` still triggers implicit retries and propose a plan without modifying production code. Disable client auto-retry (keep `401` token refresh only), set Redis timeout to 30 seconds instead of 30 minutes, and keep `CacheService` public API unchanged."
            : "排查 `AuthInterceptor` 是否仍有隐式重试并先给出方案、不要直接改代码；关闭客户端自动重试（仅保留 `401` 刷新 Token），将 Redis 超时设为 30 秒而非 30 分钟，并保持 `CacheService` 公共 API 不变。",
          gate: "保真校验通过"
        };
      }
      if (cleaned.includes("PR") && cleaned.includes("CI")) {
        return {
          text: dir === "English"
            ? "Do not merge this PR yet. Wait for CI to finish. Keep the API of `CacheService` unchanged."
            : "当前 PR 请先不要合并，等待 CI 跑完再处理，同时保持 `CacheService` 的 API 不变。",
          gate: "保真校验通过"
        };
      }
      // Apply user glossary terms when translating arbitrary user input
      let translated = cleaned;
      for (const t of api.state.glossary.terms) {
        if (t.source && t.target) translated = translated.split(t.source).join(t.target);
      }
      return {
        text: dir === "English"
          ? `[EN · ${api.activeModelMeta("translator").name}] ${translated}`
          : `[中文 · ${api.activeModelMeta("translator").name}] ${translated}`,
        gate: "保真校验通过"
      };
    }

    // Polish modes
    const stripFillers = s => s
      .replace(/(?:^|[，,])\s*(?:呃|嗯|那个|就是|然后)[，,]?\s*/g, "")
      .replace(/先把客户端重试设成 3 次吧……哦不对，/g, "")
      .trim();
    const sentences = cleaned.split(/(?<=[。！？.!?\n])/).map(s => stripFillers(s)).filter(Boolean);

    if (modeId === "ai-task") {
      if (cleaned.includes("AuthInterceptor")) return { text: SCENARIOS.compare.doc, gate: "事实锚点已校验" };
      if (sentences.length <= 2) {
        return { text: sentences.join(" "), gate: "事实锚点已校验" };
      }
      return {
        text: `### 任务目标\n${sentences[sentences.length - 1]}\n\n### 上下文与约束\n${sentences.slice(0, -1).map(s => `- ${s}`).join("\n")}`,
        gate: "事实锚点已校验"
      };
    }
    if (modeId === "natural") {
      return { text: sentences.join(""), gate: "保真校验通过" };
    }
    if (modeId === "formal") {
      return {
        text: sentences.map(s => s.replace(/别/g, "请勿").replace(/先/g, "建议优先")).join(""),
        gate: "保真校验通过"
      };
    }
    if (modeId === "todo") {
      if (cleaned.includes("小王") && !cleaned.includes("AuthInterceptor")) return { text: SCENARIOS.todo.doc, gate: "事实锚点已校验" };
      return {
        text: sentences.map(s => `- [ ] ${s.replace(/[。！？.!?]+$/, "")}`).join("\n"),
        gate: "事实锚点已校验"
      };
    }
    if (modeId === "report") {
      return {
        text: `### 核心结论\n${sentences[0]}\n${sentences.length > 1 ? `\n### 关键要点与后续\n${sentences.slice(1).map(s => `- ${s}`).join("\n")}` : ""}`,
        gate: "事实锚点已校验"
      };
    }
    const preset = api.presetById(modeId);
    return {
      text: `### ${preset.name}\n${sentences.map(s => `- ${s}`).join("\n")}`,
      gate: preset.gate === "FactAnchor" ? "事实锚点已校验" : "保真校验通过"
    };
  }

  function recomputeDoc(api) {
    const out = synthesizeOutput(composerState.raw, composerState.output, composerState.mode, api);
    composerState.doc = out.text;
    composerState.gate = out.gate;
  }

  function saveCurrentToRecent(api) {
    const raw = composerState.raw.trim();
    if (!raw) return;
    if (composerState.recentDrafts[0]?.raw === raw) return;
    composerState.recentDrafts.unshift({
      id: `d-${Date.now()}`,
      title: raw.slice(0, 26) + (raw.length > 26 ? "…" : ""),
      raw,
      mode: composerState.mode,
      output: composerState.output,
      time: "刚刚"
    });
    if (composerState.recentDrafts.length > 5) composerState.recentDrafts.length = 5;
    api.state.general.recentCount = composerState.recentDrafts.length;
    api.renderAll();
  }

  function rememberAppChoice(api) {
    if (!api.state.general.rememberChoices || composerState.output === "raw" || composerState.target === "none") return;
    const mapId = { agent: "cursor", linear: "linear", "report-doc": "feishu", notes: "slack" }[composerState.target];
    if (!mapId) return;
    const app = api.state.general.apps.find(a => a.id === mapId);
    const val = composerState.output === "translation"
      ? `translate:${api.state.voice.direction}`
      : `polish:${composerState.mode}`;
    if (app) {
      app.choice = val;
      app.used = "刚刚";
      api.renderAll();
    }
  }

  function formatMd(text) {
    return String(text ?? "").split("\n").map(line => {
      if (/^\s*#{1,6}\s/.test(line)) return `<span class="md-h">${esc(line)}</span>`;
      const m = line.match(/^(\s*-\s*\[[ xX]\])(.*)$/);
      const inline = s => esc(s).replace(/`([^`\n]+)`/g, '<span class="md-code">`$1`</span>');
      if (m) return `<span class="md-box">${esc(m[1])}</span>${inline(m[2])}`;
      return inline(line);
    }).join("\n");
  }

  function showPage(name) {
    document.querySelectorAll(".page").forEach(p => { p.hidden = p.id !== `page-${name}`; });
    document.querySelectorAll(".top .tab").forEach(t => {
      if (t.dataset.page === name) t.setAttribute("aria-current", "page");
      else t.removeAttribute("aria-current");
    });
  }

  function renderComposerSync() {
    const api = window.LiveLingoSettings;
    if (!api) return;
    const S = api.state;
    const isCopyOnly = composerState.target === "none";
    const tMeta = isCopyOnly ? { name: "仅复制", menubar: "Desktop", title: "无活动输入框", chip: "没有输入框 · 仅复制", placeholder: "当前为仅复制模式…" } : (TARGETS[composerState.target] ?? TARGETS.agent);
    $("menubar-title").textContent = tMeta.menubar;
    $("target-title").textContent = tMeta.title;
    if (!isCopyOnly) $("app-context").innerHTML = tMeta.context;
    $("target-text").placeholder = tMeta.placeholder;
    const destBtn = $("dest");
    destBtn.className = `chip dest ${isCopyOnly ? "warn" : ""}`;
    destBtn.innerHTML = isCopyOnly ? `<span>${esc(tMeta.chip)}</span>` : `${ic("target")}<span>${esc(tMeta.chip)}</span>`;
    document.querySelectorAll("#menu-target [data-target]").forEach(btn => {
      btn.setAttribute("aria-checked", String(btn.dataset.target === composerState.target));
    });

    // Always recompute preview from current raw + output + mode + glossary
    recomputeDoc(api);

    // Privacy chip & model card synced from Settings
    const clouds = api.computeCloudStages();
    const speech = api.activeModelMeta("speech");
    const cleaner = api.activeModelMeta("cleaner");
    const translator = api.activeModelMeta("translator");
    const priv = $("privacy");
    if (clouds.length > 0) {
      priv.className = "chip warn";
      priv.innerHTML = `<span class="dot"></span><span>云端 · ${esc(clouds.join("、"))}</span>`;
    } else {
      priv.className = "chip ok";
      priv.innerHTML = `<span class="dot"></span><span>本地</span>`;
    }
    $("models-list").innerHTML = `
      <div><dt>语音识别</dt><dd>${esc(speech.fullName)}（${esc(speech.sourceLabel)}）</dd></div>
      <div><dt>整理与润色</dt><dd>${esc(cleaner.fullName)}（${esc(cleaner.sourceLabel)}）</dd></div>
      <div><dt>翻译</dt><dd>${esc(translator.fullName)}（${esc(translator.sourceLabel)}）</dd></div>
    `;

    // Output pill & popover menu synced from enabled presets
    const enabledPresets = S.polish.presets.filter(p => p.enabled);
    const activePreset = enabledPresets.find(p => p.id === composerState.mode) ?? enabledPresets[0];
    const dirLabel = S.voice.direction === "English" ? "English" : "中文";
    const swapTarget = S.voice.direction === "English" ? "中文" : "English";
    const outPill = $("out-pill");
    if (composerState.output === "translation") {
      outPill.innerHTML = `${ic("globe")}<span>→ ${dirLabel}</span>${ic("chevron")}`;
    } else if (composerState.output === "raw" || !S.voice.cleanup) {
      outPill.innerHTML = `<span>原话</span>${ic("chevron")}`;
    } else {
      outPill.innerHTML = `${ic("sparkle")}<span>${esc(activePreset.pill)}</span>${ic("chevron")}`;
    }

    $("output-items").innerHTML = `
      ${S.voice.cleanup ? `
        <div class="menu-group-label">整理与场景润色（保持原语言）</div>
        ${enabledPresets.map(p => {
          const cap = capOf("polish:" + p.id);
          return `
          <button type="button" role="menuitemradio" data-pick-mode="${esc(p.id)}" aria-checked="${composerState.output === "clean" && activePreset.id === p.id}">
            <span class="menu-item-main">
              <span>${esc(p.name)}</span>
              <span class="menu-item-desc">${esc(p.desc)}</span>
            </span>
            ${cap ? `<kbd aria-hidden="true">${cap}</kbd>` : ""}
          </button>`;
        }).join("")}
        <hr>
      ` : ""}
      <button type="button" role="menuitemradio" data-pick-output="translation" aria-checked="${composerState.output === "translation"}">
        <span>翻译成 ${dirLabel}</span>
        <kbd aria-hidden="true">${capOf("translate")}</kbd>
      </button>
      <button type="button" role="menuitemradio" data-pick-output="raw" aria-checked="${composerState.output === "raw"}">
        <span>原话（不整理）</span>
        <kbd aria-hidden="true">${capOf("original")}</kbd>
      </button>
      <hr>
      <button type="button" data-pick-act="swap">
        <span>翻译目标改为 ${swapTarget}</span>
        <kbd aria-hidden="true">${capOf("swap")}</kbd>
      </button>
      ${S.voice.cleanup ? `<button type="button" data-pick-act="managePolish">
        <span>管理润色方式…</span>
      </button>` : ""}
      ${S.general.rememberChoices ? `<p class="menu-note">选择会为「${esc(tMeta.name)}」记住</p>` : ""}
    `;

    // Source & result + wide split-pane & zoom state (matches desktop default wide layout)
    const srcEl = $("src");
    if (document.activeElement !== srcEl) srcEl.value = composerState.raw;
    const hasText = composerState.raw.trim().length > 0;
    const showRes = hasText && composerState.output !== "raw" && (composerState.output === "translation" || S.voice.cleanup);
    const comp = $("composer");
    comp.hidden = composerState.hidden;
    comp.classList.toggle("wide", composerState.wide);
    comp.classList.toggle("zoomed", composerState.zoomed);
    comp.classList.toggle("has-res", showRes);
    $("route-chip").hidden = !composerState.whole;
    document.querySelector('#menu-more [data-act="wide"]')?.setAttribute("aria-checked", String(composerState.wide));
    document.querySelector('#menu-more [data-act="whole"]')?.setAttribute("aria-checked", String(composerState.whole));
    const zoomBtn = $("zoom-btn");
    if (zoomBtn) {
      const lbl = composerState.zoomed ? "还原窗口" : "放大窗口";
      zoomBtn.setAttribute("aria-label", lbl);
      zoomBtn.title = `${lbl} (${capOf("zoom")})`;
      zoomBtn.innerHTML = ic(composerState.zoomed ? "shrink" : "expand");
    }
    $("mic")?.classList.toggle("recording", composerState.recording);
    $("mic").title = `${composerState.recording ? "结束听写" : "开始听写"}（${capOf("dictate")}）`;
    $("copy").disabled = !hasText;
    $("copy").title = `复制将写入的内容（${capOf("copy")}）`;
    document.querySelectorAll("[data-cap]").forEach(el => { el.textContent = capOf(el.dataset.cap); });
    $("res").hidden = !showRes;
    if (showRes) {
      $("res-label").textContent = composerState.output === "translation" ? `将写入 · 翻译成 ${dirLabel}` : `将写入 · ${activePreset.name}`;
      $("gate-chip").textContent = composerState.gate;
      $("gate-chip").hidden = !composerState.gate;
      $("doc-view").hidden = false;
      $("doc-view").innerHTML = `<div class="doc-text">${formatMd(composerState.doc)}</div>`;
    }

    // Status & model line
    const stageMeta = composerState.recording ? speech : (composerState.output === "translation" ? translator : cleaner);
    const stageName = composerState.recording ? "识别" : (composerState.output === "translation" ? "翻译" : "润色");
    $("status").textContent = composerState.recording
      ? `正在听写 · 0:${String(composerState.recordSecs).padStart(2, "0")} · 再次点击或按 ${capOf("dictate")} 结束`
      : !hasText
        ? "等待输入 · 点麦克风或直接键入文本"
        : `${composerState.output === "raw" ? "原话已就绪" : composerState.output === "translation" ? "译文已就绪" : activePreset.name + "已就绪"} · ${clouds.length > 0 ? "含云端处理" : "全部本机"}`;
    const ml = $("model-line");
    ml.hidden = !composerState.recording && (composerState.output === "raw" || !hasText);
    ml.className = `model-line ${stageMeta.source === "cloud" ? "warn" : stageMeta.source === "host" ? "host" : ""}`;
    ml.innerHTML = `<span class="dot"></span><span>${stageName} · ${esc(stageMeta.name)} · ${esc(stageMeta.sourceLabel)}</span>`;

    // Hotkey labels & commit button
    const hkText = api.formatHotkeyDisplay(S.general.hotkey);
    document.querySelectorAll(".k-summon").forEach(el => { el.textContent = hkText; });
    const commitKey = S.general.commitKey === "Enter" ? "↵" : (S.platform === "mac" ? "⌃↵" : "Ctrl+↵");
    document.querySelectorAll(".k-commit").forEach(el => { el.textContent = commitKey; });
    $("commit").disabled = !hasText || composerState.recording;
    $("commit").innerHTML = isCopyOnly
      ? `<span>复制</span><kbd>${commitKey}</kbd>`
      : `<span>写入 ${esc(tMeta.chip)}</span><kbd>${commitKey}</kbd>`;
  }

  document.addEventListener("DOMContentLoaded", () => {
    const api = window.LiveLingoSettings;
    const urlPage = new URLSearchParams(location.search).get("page");
    if (urlPage === "play" || urlPage === "notes" || urlPage === "settings") {
      showPage(urlPage);
    }

    // Top bar page tabs
    document.querySelectorAll(".top .tab").forEach(btn => {
      btn.addEventListener("click", () => {
        showPage(btn.dataset.page);
        if (btn.dataset.page === "play") renderComposerSync();
      });
    });

    // Platform toggle
    document.querySelectorAll("[data-platform]").forEach(btn => {
      btn.addEventListener("click", () => {
        const p = btn.dataset.platform;
        document.documentElement.dataset.platform = p;
        document.querySelectorAll("[data-platform]").forEach(b => b.setAttribute("aria-checked", String(b === btn)));
        api.state.platform = p;
        api.renderAll();
      });
    });

    // Quick theme toggle button in top bar
    $("theme-btn").addEventListener("click", () => {
      const cur = document.documentElement.dataset.theme === "dark" ? "Light" : "Dark";
      api.state.general.theme = cur;
      api.renderAll();
    });

    // Composer scenarios
    document.querySelectorAll(".scenario").forEach(btn => {
      btn.addEventListener("click", () => {
        const sc = SCENARIOS[btn.dataset.scenario];
        if (!sc) return;
        Object.assign(composerState, sc);
        renderComposerSync();
      });
    });

    // Composer menu actions (matches 20-composer-parts.js pickOutput & pickMore)
    $("output-items").addEventListener("click", e => {
      const mBtn = e.target.closest("[data-pick-mode]");
      if (mBtn) {
        composerState.output = "clean";
        composerState.mode = mBtn.dataset.pickMode;
        $("menu-output").hidePopover?.();
        rememberAppChoice(api);
        renderComposerSync();
        return;
      }
      const oBtn = e.target.closest("[data-pick-output]");
      if (oBtn) {
        composerState.output = oBtn.dataset.pickOutput;
        $("menu-output").hidePopover?.();
        rememberAppChoice(api);
        renderComposerSync();
        return;
      }
      const aBtn = e.target.closest("[data-pick-act]");
      if (aBtn) {
        const act = aBtn.dataset.pickAct;
        $("menu-output").hidePopover?.();
        if (act === "swap") {
          api.state.voice.direction = api.state.voice.direction === "English" ? "Chinese" : "English";
          api.renderAll();
          api.showToast(`翻译目标已切换为 ${api.state.voice.direction === "English" ? "English" : "中文"}`);
        } else if (act === "managePolish") {
          showPage("settings");
          api.showSection("polish");
        }
      }
    });

    const DICTATION_CHUNKS = [
      "呃我想改一下登录和缓存这块，",
      "先把客户端重试设成 3 次吧……哦不对，网关层已经有重试了，客户端干脆把自动重试关掉，只保留 401 刷新 token 的逻辑。",
      "另外 Redis 超时设成 30 秒不是 30 分钟。",
      "保持 `CacheService` 公共 API 不变，你先帮我查一下 `AuthInterceptor` 还有没有隐式重试，列出来给我看，先千万别直接改代码。"
    ];

    function stopDictation() {
      if (!composerState.recording) return;
      clearInterval(composerState.recordTimer);
      composerState.recordTimer = null;
      composerState.recording = false;
      renderComposerSync();
      api.showToast("听写已结束，已生成最新预览");
    }

    function toggleDictation() {
      if (composerState.recording) {
        stopDictation();
        return;
      }
      composerState.recording = true;
      composerState.recordSecs = 0;
      composerState.recordStep = 0;
      composerState.raw = "";
      renderComposerSync();
      composerState.recordTimer = setInterval(() => {
        composerState.recordSecs += 1;
        if (composerState.recordStep < DICTATION_CHUNKS.length) {
          composerState.raw += DICTATION_CHUNKS[composerState.recordStep++];
        }
        renderComposerSync();
        if (composerState.recordStep >= DICTATION_CHUNKS.length && composerState.recordSecs >= 5) {
          stopDictation();
        }
      }, 900);
    }

    function openRecentDialog() {
      const list = composerState.recentDrafts;
      const body = list.length === 0
        ? `<p>还没有最近草稿。关闭或写入浮窗后会出现在这里；退出应用后自动清除。</p>`
        : `<div>${list.map((d, i) => `
            <div style="display:flex;align-items:center;justify-content:space-between;gap:12px;padding:10px 0;border-top:${i ? "1px solid var(--line)" : "0"}">
              <div style="min-inline-size:0;flex:1">
                <b style="display:block;font-size:13.5px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap">${esc(d.title)}</b>
                <span style="font-size:12px;color:var(--muted)">${esc(d.time)} · ${d.output === "translation" ? "翻译" : esc(api.presetById(d.mode).name)}</span>
              </div>
              <button type="button" class="btn compact" data-restore-draft="${i}">恢复</button>
            </div>
          `).join("")}</div>
          <div class="dlg-actions">
            <button type="button" class="btn" id="dlg-clear-recent">清除最近草稿</button>
            <button type="button" class="btn primary" id="dlg-recent-close" autofocus>关闭</button>
          </div>`;
      api.openDialog("最近草稿", body, {
        onOpen(el) {
          el.querySelectorAll("[data-restore-draft]").forEach(btn => {
            btn.addEventListener("click", () => {
              const d = composerState.recentDrafts[Number(btn.dataset.restoreDraft)];
              if (!d) return;
              composerState.raw = d.raw;
              composerState.mode = d.mode;
              composerState.output = d.output;
              composerState.hidden = false;
              api.closeDialog();
              renderComposerSync();
              api.showToast("已恢复选定草稿");
            });
          });
          el.querySelector("#dlg-clear-recent")?.addEventListener("click", () => {
            composerState.recentDrafts = [];
            api.state.general.recentCount = 0;
            api.closeDialog();
            api.renderAll();
            api.showToast("已清除最近草稿");
          });
          el.querySelector("#dlg-recent-close")?.addEventListener("click", api.closeDialog);
        }
      });
    }

    function openExportDialog() {
      if (!composerState.raw.trim()) {
        api.showToast("草稿为空，无法导出");
        return;
      }
      const formats = [["txt", "TXT 纯文本"], ["markdown", "Markdown (.md)"], ["srt", "SRT 字幕"]];
      api.openDialog("导出内容", `
        <p>导出当前选定的输出内容（${composerState.output === "raw" ? "原话" : esc($("res-label").textContent)}）：</p>
        <pre style="margin-top:10px;padding:10px 12px;border-radius:10px;background:var(--surface-subtle);border:1px solid var(--line);font:12.5px/1.5 var(--mono);max-height:180px;overflow:auto;white-space:pre-wrap">${esc(composerState.output === "raw" ? composerState.raw : composerState.doc)}</pre>
        <div class="dlg-actions">
          ${formats.map(([id, label]) => `<button type="button" class="btn ${composerState.lastExportFormat === id ? "primary" : ""}" data-export-fmt="${id}" ${composerState.lastExportFormat === id ? "autofocus" : ""}>导出 ${label}</button>`).join("")}
        </div>
      `, {
        onOpen(el) {
          el.querySelectorAll("[data-export-fmt]").forEach(btn => {
            btn.addEventListener("click", () => {
              const fmt = btn.dataset.exportFmt;
              composerState.lastExportFormat = fmt;
              const content = composerState.output === "raw" ? composerState.raw : composerState.doc;
              const body = fmt === "srt" ? `1\n00:00:00,000 --> 00:00:06,000\n${content}\n` : content;
              const ext = fmt === "markdown" ? "md" : fmt;
              const blob = new Blob([body], { type: "text/plain;charset=utf-8" });
              const a = document.createElement("a");
              a.href = URL.createObjectURL(blob);
              a.download = `livelingo-draft.${ext}`;
              a.click();
              setTimeout(() => URL.revokeObjectURL(a.href), 1000);
              api.closeDialog();
              api.showToast(`已导出 livelingo-draft.${ext}`);
            });
          });
        }
      });
    }

    function openImportDialog() {
      api.openDialog("导入录音文件", `
        <p>选择一个本地 WAV / 音频文件（演示环境将即时模拟离线转录并填充到浮窗）：</p>
        <div class="dlg-actions">
          <button type="button" class="btn" id="imp-cancel">取消</button>
          <button type="button" class="btn primary" id="imp-sample" autofocus>导入示例会议录音（meeting-01.wav）</button>
        </div>
      `, {
        onOpen(el) {
          el.querySelector("#imp-cancel").addEventListener("click", api.closeDialog);
          el.querySelector("#imp-sample").addEventListener("click", () => {
            composerState.raw = "今天架构评审定了三件事：第一，客户端关闭自动重试只留 401 刷新 Token；第二，Redis 超时明确设为 30 秒；第三，会后由小王整理会议纪要同步全员。";
            composerState.output = "clean";
            composerState.mode = "report";
            composerState.hidden = false;
            api.closeDialog();
            renderComposerSync();
            api.showToast("已导入并转录 meeting-01.wav");
          });
        }
      });
    }

    function commitDraft() {
      const text = composerState.output === "raw" ? composerState.raw : composerState.doc;
      if (!text.trim() || composerState.recording) return;
      saveCurrentToRecent(api);
      if (composerState.target === "none") {
        navigator.clipboard?.writeText(text).catch(() => {});
        api.showToast("已复制到剪贴板");
      } else {
        $("target-text").value = text;
        $("target-text").focus();
        api.showToast(`已写入 ${TARGETS[composerState.target].chip}`);
      }
    }

    function runComposerShortcut(id) {
      if (id.startsWith("polish:")) {
        const modeId = id.slice(7);
        if (api.state.voice.cleanup && api.state.polish.presets.some(p => p.id === modeId && p.enabled)) {
          composerState.output = "clean";
          composerState.mode = modeId;
          rememberAppChoice(api);
          renderComposerSync();
          api.showToast(`已切换润色方式：${api.presetById(modeId).name}`);
        }
        return;
      }
      switch (id) {
        case "dictate":
          toggleDictation();
          break;
        case "translate":
          composerState.output = "translation";
          rememberAppChoice(api);
          renderComposerSync();
          break;
        case "original":
          composerState.output = "raw";
          renderComposerSync();
          break;
        case "swap":
          api.state.voice.direction = api.state.voice.direction === "English" ? "Chinese" : "English";
          api.renderAll();
          api.showToast(`翻译目标已切换为 ${api.state.voice.direction === "English" ? "English" : "中文"}`);
          break;
        case "copy": {
          const text = composerState.output === "raw" ? composerState.raw : composerState.doc;
          if (!text.trim()) return;
          navigator.clipboard?.writeText(text).catch(() => {});
          api.showToast("已复制到剪贴板");
          break;
        }
        case "new":
          saveCurrentToRecent(api);
          stopDictation();
          composerState.raw = "";
          composerState.doc = "";
          renderComposerSync();
          $("src").focus();
          api.showToast("已新建空白草稿");
          break;
        case "recent":
          openRecentDialog();
          break;
        case "import":
          openImportDialog();
          break;
        case "export":
          openExportDialog();
          break;
        case "zoom":
          composerState.zoomed = !composerState.zoomed;
          renderComposerSync();
          break;
        case "settings":
          showPage("settings");
          api.showSection("general");
          break;
      }
    }

    $("menu-more").addEventListener("click", e => {
      const act = e.target.closest("[data-act]")?.dataset.act;
      if (!act) return;
      $("menu-more").hidePopover?.();
      if (act === "wide") {
        composerState.wide = !composerState.wide;
        renderComposerSync();
        api.showToast(composerState.wide ? "已切换为宽屏左右对照" : "已切换为紧凑上下布局");
        return;
      }
      if (act === "whole") {
        composerState.whole = !composerState.whole;
        renderComposerSync();
        api.showToast(composerState.whole ? "已开启整段处理（保留格式）" : "已恢复自动分段 / 整段策略");
        return;
      }
      if (act === "history") { runComposerShortcut("recent"); return; }
      runComposerShortcut(act);
    });

    $("tray-menu").addEventListener("click", e => {
      const act = e.target.closest("[data-act]")?.dataset.act;
      if (!act) return;
      $("tray-menu").hidePopover?.();
      if (act === "summon") { composerState.hidden = false; renderComposerSync(); $("src").focus(); }
      else if (act === "hide") { saveCurrentToRecent(api); composerState.hidden = true; renderComposerSync(); api.showToast("已隐藏浮窗，草稿已保留"); }
      else if (act === "history") { openRecentDialog(); }
      else if (act === "settings") { showPage("settings"); api.showSection("general"); }
    });

    $("summon").addEventListener("click", () => {
      composerState.hidden = false;
      renderComposerSync();
      if (api.state.voice.autoDictate && !composerState.raw.trim()) {
        toggleDictation();
      } else {
        $("src").focus();
      }
      api.showToast("已唤起浮窗");
    });

    $("close").addEventListener("click", () => {
      stopDictation();
      saveCurrentToRecent(api);
      composerState.hidden = true;
      renderComposerSync();
      api.showToast("已隐藏浮窗并保留草稿，点右侧「模拟全局呼出」可重新打开");
    });

    $("zoom-btn").addEventListener("click", () => runComposerShortcut("zoom"));
    $("mic").addEventListener("click", () => runComposerShortcut("dictate"));

    // Keyboard shortcuts matching ComposerShortcuts.cs 1:1 + Enter/Ctrl+Enter commit & Esc hide
    document.addEventListener("keydown", e => {
      if ($("page-play").hidden || e.isComposing || e.keyCode === 229 || $("dlg").open) return;

      if (e.key === "Escape") {
        const openPop = document.querySelector(".menu:popover-open");
        if (openPop) { openPop.hidePopover?.(); return; }
        if (!composerState.hidden) {
          e.preventDefault();
          stopDictation();
          saveCurrentToRecent(api);
          composerState.hidden = true;
          renderComposerSync();
          api.showToast("已隐藏浮窗（保留草稿）");
        }
        return;
      }

      if (e.key === "Enter" && !e.shiftKey && !composerState.hidden && (e.target.id === "src" || e.target === document.body || e.target.id === "mic")) {
        const ctrl = e.ctrlKey || e.metaKey;
        const needCtrl = api.state.general.commitKey === "CtrlEnter";
        const matchCommit = needCtrl ? ctrl : (!ctrl && !e.altKey);
        if (matchCommit) {
          e.preventDefault();
          commitDraft();
          return;
        }
      }

      const mac = api.state.platform === "mac";
      const mod = mac ? (e.metaKey && !e.ctrlKey) : (e.ctrlKey && !e.metaKey);
      if (!mod || e.altKey) return;
      const sc = SHORTCUTS.find(s => s.code === e.code && s.shift === e.shiftKey);
      if (sc) {
        e.preventDefault();
        runComposerShortcut(sc.id);
      }
    });

    $("src").addEventListener("input", e => {
      composerState.raw = e.target.value;
      renderComposerSync();
    });

    $("menu-target").addEventListener("click", e => {
      const btn = e.target.closest("[data-target]");
      if (!btn) return;
      const t = btn.dataset.target;
      composerState.target = t;
      // Apply app preference or smart host match when switching target app
      if (t !== "none") {
        const mapId = { agent: "cursor", linear: "linear", "report-doc": "feishu", notes: "slack" }[t];
        const remembered = api.state.general.rememberChoices ? api.state.general.apps.find(a => a.id === mapId) : null;
        if (remembered && remembered.choice !== "default") {
          if (remembered.choice.startsWith("polish:")) {
            composerState.output = "clean";
            composerState.mode = remembered.choice.slice(7);
          } else if (remembered.choice.startsWith("translate:")) {
            composerState.output = "translation";
            api.state.voice.direction = remembered.choice.slice(10);
          }
        } else if (api.state.polish.smartHostPolish && TARGETS[t]?.smartPreset) {
          composerState.output = "clean";
          composerState.mode = TARGETS[t].smartPreset;
        }
      }
      $("menu-target").hidePopover?.();
      renderComposerSync();
    });

    $("copy").addEventListener("click", () => runComposerShortcut("copy"));

    $("commit").addEventListener("click", commitDraft);

    // Sync recent draft clear from Settings page
    window.addEventListener("ll:clear-recent", () => {
      composerState.recentDrafts = [];
    });

    api.subscribe(renderComposerSync);
    api.state.general.recentCount = composerState.recentDrafts.length;
    api.renderAll();
    renderComposerSync();
  });
})();
