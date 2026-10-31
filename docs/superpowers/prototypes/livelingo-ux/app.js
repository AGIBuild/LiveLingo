(function () {
  "use strict";

  // This state model belongs only to the interactive prototype; it does not run the product pipeline.
  class Draft {
    constructor(target) { this.epoch = 0; this.reset(target); }
    reset(target) {
      this.epoch++;
      this.translationRevision = 0;
      this.units = [];
      this.output = "clean";
      this.route = "units";
      this.target = target;
      this.targetLost = false;
      this.recording = false;
      this.snapshot = null;
    }
    ticket(index) { return { epoch: this.epoch, index, translationRevision: this.translationRevision }; }
    append(raw) {
      if (this.snapshot) throw new Error("Start a new draft before adding text.");
      this.units.push({ raw, clean: null, translation: null, cleanOwned: false, translationOwned: false, cleanError: "", translationError: "", warning: "" });
      return this.ticket(this.units.length - 1);
    }
    replaceInput(raw, route) {
      const output = this.output;
      const targetLost = this.targetLost;
      this.reset(this.target);
      this.output = output;
      this.targetLost = targetLost;
      this.route = route;
      return raw.trim() ? this.append(raw) : null;
    }
    complete(ticket, kind, text, warning = "") {
      const unit = this.units[ticket.index];
      if (this.snapshot || ticket.epoch !== this.epoch || !unit || unit[kind + "Owned"]) return false;
      if (kind === "translation" && ticket.translationRevision !== this.translationRevision) return false;
      unit[kind] = warning ? unit.raw : text;
      unit[kind + "Error"] = "";
      if (warning) unit.warning = warning;
      return true;
    }
    fail(ticket, kind, reason) {
      if (!this.complete(ticket, kind, null)) return false;
      this.units[ticket.index][kind + "Error"] = reason;
      return true;
    }
    edit(index, kind, text) {
      if (this.snapshot) throw new Error("Submitted text is frozen.");
      const unit = this.units[index];
      unit[kind] = text;
      unit[kind + "Owned"] = true;
      unit[kind + "Error"] = "";
    }
    useRaw(index) { this.edit(index, "clean", this.units[index].raw); }
    invalidateTranslations() {
      if (this.snapshot) return;
      this.translationRevision++;
      this.units.forEach(unit => { unit.translation = null; unit.translationOwned = false; unit.translationError = ""; });
    }
    retarget(target) { this.target = target; this.targetLost = false; }
    text() {
      return this.units.map(unit => unit[this.output] ?? "").join(this.route === "whole" ? "" : "\n");
    }
    contentReadiness() {
      if (!this.units.length) return { ok: false, reason: "empty" };
      if (this.output !== "raw") {
        if (this.units.some(unit => unit[this.output + "Error"] || unit[this.output + "Owned"] && !unit[this.output]?.trim())) return { ok: false, reason: "unavailable" };
        if (this.units.some(unit => unit[this.output] === null)) return { ok: false, reason: "processing" };
      }
      if (!this.text().trim()) return { ok: false, reason: "empty" };
      return { ok: true };
    }
    readiness() {
      if (this.targetLost) return { ok: false, reason: "target-lost" };
      if (this.recording) return { ok: false, reason: "recording" };
      return this.contentReadiness();
    }
    requestCommit() {
      if (this.snapshot) return this.snapshot;
      const ready = this.readiness();
      if (!ready.ok) return ready;
      this.snapshot = Object.freeze({ ok: true, text: this.text(), action: this.target === null ? "copy" : "insert", target: this.target });
      return this.snapshot;
    }
  }
  if (typeof module !== "undefined") module.exports = { Draft };
  if (typeof document === "undefined") return;

  const $ = id => document.getElementById(id);
  const escape = text => String(text ?? "").replace(/[&<>"']/g, char => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[char]);
  const SAMPLES = {
    "zh-en": [
      { raw: "嗯，这个 PR 先不要合并，等 CI 跑完再说。", clean: "这个 PR 先不要合并，等 CI 跑完再说。", translation: "Do not merge this PR yet. Wait for CI to finish." },
      { raw: "Redis 的超时设置为 30 秒，不是 30 分钟。", clean: "Redis 的超时设置为 30 秒，不是 30 分钟。", translation: "Set the Redis timeout to 30 seconds, not 30 minutes." },
      { raw: "就是，保持 `CacheService` 的 API 不变，只修改错误处理。", clean: "保持 `CacheService` 的 API 不变，只修改错误处理。", translation: "Keep the API of `CacheService` unchanged. Only modify error handling." }
    ],
    "en-zh": [
      { raw: "Please do not merge this PR until CI finishes.", clean: "Please do not merge this PR until CI finishes.", translation: "在 CI 跑完前，请不要合并这个 PR。" },
      { raw: "Set the Redis timeout to 30 seconds, not 30 minutes.", clean: "Set the Redis timeout to 30 seconds, not 30 minutes.", translation: "将 Redis 的超时设为 30 秒，不是 30 分钟。" },
      { raw: "Keep the API of `CacheService` unchanged.", clean: "Keep the API of `CacheService` unchanged.", translation: "保持 `CacheService` 的 API 不变。" }
    ]
  };
  const DOCUMENT = {
    raw: '# 部署约束\n\n- 不要更改 `CacheService` 的 API。\n- 超时为 30 秒，日志不包含凭据。\n\n```json\n{\n  "timeout": 30,\n  "api_key": "${API_KEY}"\n}\n```\n',
    clean: '# 部署约束\n\n- 不要更改 `CacheService` 的 API。\n- 超时为 30 秒，日志不包含凭据。\n\n```json\n{\n  "timeout": 30,\n  "api_key": "${API_KEY}"\n}\n```\n',
    translation: '# Deployment constraints\n\n- Do not change the API of `CacheService`.\n- The timeout is 30 seconds. Logs must not contain credentials.\n\n```json\n{\n  "timeout": 30,\n  "api_key": "${API_KEY}"\n}\n```\n'
  };
  const STATES = [
    ["01", "轻量待命", "捕获原输入目标，打字或点麦克风开始。", "pair", "回填目标始终可见"],
    ["02", "连续听写", "灰色实时尾部与已稳定原文分区。", "pair", "按住 / 免按住都有入口"],
    ["03", "真正的边说边译", "录音尚未结束，已完成句的译文已出现。", "translate", "开启翻译即选择译文输出"],
    ["04", "整段技术文本", "完成文本按整段处理，不拆坏代码和版式。", "document", "格式检查不是正确性保证"],
    ["05", "长录音转录", "展开时间轴，暂停、续录、选择文件与导出。", "meeting", "文件流程仅示例演示"],
    ["06", "整理被拒绝", "候选改动单位，工作稿保留原文，可自行编辑。", "gate", "不把危险候选当默认答案"],
    ["07", "正在处理最后一句", "未完成的选定版本不可回填；可明确改用原文。", "slow", "不提交半截译文"],
    ["08", "翻译失败", "原文和整理稿仍在，失败原因及出口可见。", "failure", "不静默替换成成功形状的原文"],
    ["09", "原目标失效", "草稿保留，明确选择新目标后才能回填。", "lost", "不写进新的前台窗口"],
    ["10", "没有输入目标", "独立草稿 / 翻译阅读场景，主动作变为复制。", "no-target", "不伪装成可注入输入框"],
    ["11", "无声音 / 设备断开", "停下采集并保留已有内容，给出检查入口。", "no-audio", "不无声地结束会话"],
    ["12", "缺权限 / 模型未就绪", "能力分别提示，纯文本编辑仍然可用。", "permission", "不笼统承诺无需重启"]
  ];
  const draft = new Draft("agent");
  let mode = "voice", expanded = false, composing = false, sourceTimer = null;
  let recordTimer = null, recordSession = 0, recordIndex = 0, liveText = "", paused = false;
  let editor = null, history = [], announcementTimer = null, slow = false, fixtureWarning = false;
  const terms = [{ source: "缓存穿透", target: "cache penetration" }, { source: "CacheService", target: "CacheService" }];
  const reasons = { "recording": "请先结束录音，再确认回填。", "empty": "草稿为空。", "processing": "选定版本仍在处理，不会回填半截结果。", "unavailable": "选定版本不可用。请编辑、重新请求，或明确选择原文。", "target-lost": "原输入目标已失效。请重新绑定，或复制草稿。" };

  function announce(text) {
    $("announcement").textContent = text;
    $("announcement").hidden = false;
    clearTimeout(announcementTimer);
    announcementTimer = setTimeout(() => { $("announcement").hidden = true; }, 5000);
  }
  function showPage(page) {
    document.querySelectorAll(".page").forEach(panel => { panel.hidden = panel.id !== page; });
    document.querySelectorAll("[data-page]").forEach(button => {
      button.classList.toggle("active", button.dataset.page === page);
      if (button.dataset.page === page) button.setAttribute("aria-current", "page"); else button.removeAttribute("aria-current");
    });
  }
  function modal(title, content) {
    $("modal-title").textContent = title;
    $("modal-body").innerHTML = content;
    if (!$("modal").open) $("modal").showModal();
  }
  function saveHistory() {
    if (!draft.units.length) return;
    const entry = { time: new Date().toLocaleTimeString(), units: structuredClone(draft.units), output: draft.output, route: draft.route, direction: $("direction").value };
    history = [entry, ...history].slice(0, 5);
  }
  function stopRecording() {
    clearInterval(recordTimer);
    recordSession++;
    const pendingTail = liveText;
    draft.recording = false;
    paused = false;
    liveText = "";
    if (pendingTail.trim()) {
      const ticket = draft.append(pendingTail);
      draft.complete(ticket, "clean", pendingTail);
      draft.units[ticket.index].warning = "仅有实时尾部，示例未提供二遍结果；已显式保留原文。";
      draft.fail(ticket, "translation", "尾部不在预设翻译语料中。请补充完整句，或编辑译文。");
    }
    render();
  }
  function fresh() {
    clearInterval(recordTimer);
    clearTimeout(sourceTimer);
    sourceTimer = null;
    recordSession++;
    saveHistory();
    draft.reset(draft.target);
    draft.output = $("translate-on").checked ? "translation" : $("cleanup").value === "off" ? "raw" : "clean";
    liveText = ""; paused = false; slow = false; editor = null; fixtureWarning = false;
    $("source-input").value = "";
    $("route").value = "units";
    $("composer").hidden = false;
    $("toast").hidden = true;
    render();
  }
  function fixtureFor(raw) {
    return [...SAMPLES["zh-en"], ...SAMPLES["en-zh"], DOCUMENT].find(item => item.raw === raw);
  }
  function schedule(ticket, kind) {
    const raw = draft.units[ticket.index]?.raw;
    const fixture = fixtureFor(raw);
    const targetEnglish = $("direction").value === "zh-en";
    let text = kind === "clean" ? fixture?.clean : fixture?.translation;
    if (kind === "translation" && fixture === DOCUMENT && !targetEnglish) text = null;
    const fixtureEnglish = fixture && SAMPLES["en-zh"].includes(fixture);
    if (kind === "translation" && fixture && fixture !== DOCUMENT && fixtureEnglish === targetEnglish) {
      text = null;
    }
    if (slow) return;
    setTimeout(() => {
      if (text == null) {
        draft.fail(ticket, kind, "此文本不在预设语料中；原型没有运行模型。可手动编辑或改用原文。");
      } else {
        const warning = kind === "clean" && fixtureWarning && raw.includes("30 秒") ? "候选把 30 秒改成 30 分钟，已拒绝并保留原文。" : "";
        draft.complete(ticket, kind, text, warning);
      }
      render();
    }, kind === "clean" ? 380 : 650);
  }
  function process(ticket) {
    if (!ticket) return;
    if ($("cleanup").value === "off") draft.useRaw(ticket.index); else schedule(ticket, "clean");
    if ($("translate-on").checked) schedule(ticket, "translation");
  }
  function flushSource() {
    if (sourceTimer === null) return;
    clearTimeout(sourceTimer);
    sourceTimer = null;
    process(draft.units.length ? draft.ticket(draft.units.length - 1) : null);
  }
  function loadSamples() {
    SAMPLES[$("direction").value].forEach(fixture => process(draft.append(fixture.raw)));
    render();
  }
  function startRecording(hold = false) {
    if (draft.snapshot) fresh();
    if (draft.recording) return;
    flushSource();
    draft.recording = true;
    paused = false;
    $("composer").hidden = false;
    const run = ++recordSession;
    const samples = SAMPLES[$("direction").value];
    let position = 0;
    let delayTicks = 0;
    $("session-status").dataset.gesture = hold ? "按住" : "免按住";
    recordTimer = setInterval(() => {
      if (run !== recordSession || paused) return;
      if (delayTicks) { delayTicks--; return; }
      const sample = samples[recordIndex % samples.length];
      position = Math.min(position + 3, sample.raw.length);
      liveText = sample.raw.slice(0, position);
      if (position === sample.raw.length) {
        process(draft.append(sample.raw));
        recordIndex++;
        position = 0;
        liveText = "";
        delayTicks = 10;
      }
      render();
    }, 100);
    render();
  }
  function updateText(route = draft.route) {
    if (composing) return;
    clearTimeout(sourceTimer);
    clearInterval(recordTimer);
    recordSession++;
    const source = $("source-input").value;
    const ticket = draft.replaceInput(source, route);
    editor = null;
    $("route").value = route;
    render();
    sourceTimer = setTimeout(() => { sourceTimer = null; process(ticket); render(); }, 450);
  }
  function setOutput(output) {
    if (draft.snapshot) { announce("已回填的版本已冻结；开始新草稿后可继续修改。"); return; }
    draft.output = output;
    if (output === "translation") {
      $("translate-on").checked = true;
      draft.units.forEach((unit, index) => { if (unit.translation === null && !unit.translationError) schedule(draft.ticket(index), "translation"); });
    }
    editor = null;
    render();
  }
  function currentValue(unit) { return unit[draft.output]; }
  function sentenceMarkup(unit, index) {
    const kind = draft.output;
    const text = currentValue(unit);
    const pending = kind !== "raw" && text === null && !unit[kind + "Error"];
    const error = unit[kind + "Error"];
    const label = unit[kind + "Owned"] ? "已手动修改" : error ? "结果不可用" : pending ? "处理中" : unit.warning && kind === "clean" ? "已用原文" : kind === "raw" ? "识别原文" : "已完成";
    const time = mode === "transcript" ? `${String(Math.floor(index * 3 / 60)).padStart(2, "0")}:${String(index * 3 % 60).padStart(2, "0")} · 示例时间` : `#${index + 1}`;
    const editing = editor === index;
    return `<article class="sentence" data-unit="${index}">
      <div class="sentence-meta"><span>${time}</span><span class="${unit[kind + "Owned"] ? "owner" : ""}">${label}</span></div>
      ${editing ? `<label class="sr-only" for="sentence-edit-${index}">编辑第 ${index + 1} 句</label><textarea class="sentence-editor" id="sentence-edit-${index}" rows="3">${escape(text ?? "")}</textarea><div class="sentence-actions"><button type="button" data-unit-action="save" data-index="${index}">保存修改</button><button type="button" data-unit-action="cancel" data-index="${index}">取消编辑</button></div>` : `<p class="sentence-text ${pending ? "pending" : ""}">${escape(error || (pending ? "正在完成当前句…" : text))}</p>`}
      ${unit.warning && kind === "clean" ? `<p class="warning-inline">${escape(unit.warning)}</p>` : ""}
      ${kind === "translation" ? `<p class="source-reference">翻译来源 · 识别原文：${escape(unit.raw)}</p>` : ""}
      ${kind !== "raw" ? `<details><summary>查看识别原文</summary><p>${escape(unit.raw)}</p></details>` : ""}
      ${!draft.snapshot && !editing ? `<div class="sentence-actions">${kind !== "raw" ? `<button type="button" data-unit-action="edit" data-index="${index}">编辑${kind === "translation" ? "译文" : "这一句"}</button>` : ""}${kind === "clean" ? `<button type="button" data-unit-action="raw" data-index="${index}">这一句用原文</button>` : ""}${error ? `<button type="button" data-unit-action="retry" data-index="${index}">重新请求（演示）</button>` : ""}</div>` : ""}
    </article>`;
  }
  function peekCommit() {
    // Rendering must not freeze a draft; only an explicit submission may create its snapshot.
    if (draft.snapshot) return { ok: false, reason: "submitted" };
    return draft.readiness();
  }
  function render() {
    $("empty-draft").hidden = draft.units.length > 0 || draft.recording;
    if (!document.activeElement?.classList.contains("sentence-editor")) {
      $("sentences").innerHTML = draft.units.map(sentenceMarkup).join("");
    }
    $("live-tail").hidden = !liveText;
    $("live-tail").textContent = "实时识别 · " + liveText;
    $("source-panel").hidden = !(mode === "text" || expanded);
    $("transcript-tools").hidden = mode !== "transcript";
    $("composer").classList.toggle("expanded", expanded);
    $("record").classList.toggle("recording", draft.recording);
    $("record").setAttribute("aria-label", draft.recording ? "结束录音" : "开始免按住听写");
    $("pause").hidden = !draft.recording;
    $("pause").textContent = paused ? "继续" : "暂停";
    $("hold-record").disabled = draft.recording && $("session-status").dataset.gesture !== "按住";
    const dest = draft.target === null ? "无输入目标 · 复制" : draft.target === "notes" ? "回填 · 笔记" : "回填 · Agent";
    $("destination").textContent = draft.targetLost ? "原目标已失效" : dest;
    $("target-title").textContent = draft.target === "notes" ? "Notes / 新目标（明确选择）" : "VS Code / Agent";
    $("route-label").textContent = draft.route === "whole" ? "完成文本 · 整段路径" : "输入中 · 完整句路径";
    $("source-input").readOnly = mode !== "text" || draft.snapshot !== null || draft.recording;
    document.querySelector('label[for="source-input"]').textContent = mode === "text" ? "原文 · 可编辑" : "识别原文 · 只读记录";
    if (mode !== "text") $("source-input").value = draft.units.map(unit => unit.raw).join("\n");
    const cloud = [];
    if ($("cloud-translation").checked) cloud.push("翻译");
    if ($("cloud-cleanup").checked) cloud.push("整理");
    $("privacy-state").className = "privacy " + (cloud.length ? "cloud" : "local");
    $("privacy-state").textContent = cloud.length ? `${cloud.join(" / ")}云端 · 演示` : "全部本地 · 演示";
    document.querySelectorAll("[data-output]").forEach(button => {
      button.classList.toggle("active", button.dataset.output === draft.output);
      button.setAttribute("aria-pressed", String(button.dataset.output === draft.output));
    });
    const ready = peekCommit();
    $("commit").disabled = !ready.ok;
    $("commit").textContent = draft.snapshot ? "已完成回填" : draft.target === null ? "复制当前版本" : `写入${draft.target === "notes" ? "笔记" : " Agent"}`;
    const key = document.createElement("kbd");
    key.className = "commit-key";
    key.textContent = $("commit-binding").value === "ctrl-enter" ? ($("platform").value === "mac" ? "⌃↵" : "Ctrl↵") : "↵";
    $("commit").append(key);
    const kindLabel = { clean: "整理稿", raw: "识别原文", translation: $("direction").value === "zh-en" ? "英文译文" : "中文译文" }[draft.output];
    $("output-hint").textContent = `回填版本：${kindLabel}。原文对照不改变输出目标。`;
    $("session-status").textContent = draft.recording ? `${paused ? "已暂停" : "正在录音"} · ${$("session-status").dataset.gesture} · 演示` : draft.snapshot ? "已冻结提交快照" : ready.reason === "processing" && !ready.ok ? "最后一段处理中 · 不会回填半截结果" : ready.reason === "unavailable" && !ready.ok ? "结果不可用 · 原文仍在" : `${draft.units.length} 个单元 · 可继续追加口述`;
    const notices = [];
    if (draft.targetLost) notices.push('原输入框已失效，草稿未丢失。<button type="button" data-action="retarget">重新选择目标</button><button type="button" data-action="copy-draft">复制草稿</button>');
    if (draft.output === "translation" && draft.units.some(unit => unit.translationError)) notices.push("译文不可用，不会自动改成原文输出。你可以编辑译文，或明确选择识别原文。");
    if (cloud.length) notices.push(`${escape(cloud.join(" / "))}云端已单独授权。此原型没有网络调用；正式产品须展示真实服务商与发送范围。`);
    $("warning").hidden = notices.length === 0;
    $("warning").innerHTML = notices.join("<br>");
  }
  function changeMode(next) {
    mode = next;
    document.querySelectorAll("[data-mode]").forEach(button => { button.classList.toggle("active", button.dataset.mode === mode); button.setAttribute("aria-pressed", String(button.dataset.mode === mode)); });
    if (mode === "text") $("source-input").value = draft.units.map(unit => unit.raw).join("\n");
    render();
  }
  function scenario(name) {
    showPage("experience");
    fresh();
    draft.retarget(name === "no-target" ? null : "agent");
    changeMode(["document"].includes(name) ? "text" : name === "meeting" ? "transcript" : "voice");
    expanded = name === "meeting" || name === "document";
    if (name === "translate" || name === "document" || name === "failure" || name === "slow") {
      $("translate-on").checked = true;
      draft.output = "translation";
    } else {
      $("translate-on").checked = false;
      draft.output = $("cleanup").value === "off" ? "raw" : "clean";
    }
    if (name === "pair" || name === "translate") startRecording();
    else if (name === "document") {
      $("direction").value = "zh-en";
      $("source-input").value = DOCUMENT.raw;
      updateText("whole");
    } else if (name === "meeting" || name === "lost" || name === "no-target" || name === "gate") {
      fixtureWarning = name === "gate";
      loadSamples();
      if (name === "lost") draft.targetLost = true;
    } else if (name === "slow") { slow = true; loadSamples(); }
    else if (name === "failure") {
      loadSamples();
      draft.invalidateTranslations();
      draft.units.forEach((_, index) => draft.fail(draft.ticket(index), "translation", "示例连接超时。原文和整理稿未丢失。"));
    } else if (name === "no-audio") {
      loadSamples();
      modal("没有检测到声音", '<p>已停止录音，已有草稿保留。请检查输入设备、系统音量及权限，再继续录音。</p><div class="dialog-actions"><button type="button" class="primary" data-dialog-action="speech-settings">检查输入设备</button><button type="button" class="secondary" data-dialog-action="close">保留草稿</button></div>');
    } else if (name === "permission") {
      modal("权限分开申请，不阻塞打字", '<p>麦克风用于听写；辅助功能用于回填；macOS 输入监控权限视热键实现决定。拒绝麦克风仍可打字，拒绝回填权限仍可复制。</p><p>授权后重新检测状态；不能承诺所有运行环境都无需重启。</p><div class="dialog-actions"><button type="button" class="primary" data-dialog-action="text-mode">先用文字输入</button><button type="button" class="secondary" data-dialog-action="close">知道了</button></div>');
    } else if (name === "download") {
      modal("模型尚未就绪", '<p>识别 / 整理 / 翻译按能力显示准备状态。纯文本编辑不被阻塞，模型下载必须另行授权。</p><p>下载失败、校验失败、磁盘不足都应明确显示；这里不执行真实下载。</p><div class="dialog-actions"><button type="button" class="primary" data-dialog-action="models-settings">查看模型状态</button><button type="button" class="secondary" data-dialog-action="text-mode">先编辑文本</button></div>');
    }
    render();
  }
  function submit() {
    if (draft.snapshot) return;
    if (editor !== null) { announce("请先保存或取消当前句的编辑。"); return; }
    const result = draft.requestCommit();
    if (!result.ok) { announce(reasons[result.reason]); return; }
    saveHistory();
    if (result.action === "insert") {
      const field = $("target-text");
      field.value += (field.value ? "\n" : "") + result.text;
      $("composer").hidden = true;
      $("toast").textContent = "已模拟回填到捕获的输入框 · 未发送。真实跨应用回填与撤销仍需验证。";
      $("toast").hidden = false;
      field.focus();
    } else {
      modal("当前版本 · 可复制", `<p>没有输入框，不尝试回填到其他窗口。以下为模拟复制结果；点击下方按钮可使用浏览器剪贴板。</p><pre>${escape(result.text)}</pre><div class="dialog-actions"><button type="button" class="primary" data-dialog-action="clipboard">复制到剪贴板</button></div>`);
    }
    render();
  }
  async function copy() {
    if (draft.recording) { announce(reasons.recording); return; }
    if (editor !== null) { announce("请先保存或取消当前句的编辑。"); return; }
    const ready = draft.contentReadiness();
    if (!ready.ok) { announce(reasons[ready.reason]); return; }
    const text = draft.text();
    try {
      await navigator.clipboard.writeText(text);
      announce("当前选定版本已复制。");
    } catch (error) {
      modal("浏览器未允许剪贴板写入", `<p>${escape(error.message)}。可以选择下面的文字手动复制；草稿没有丢失。</p><pre tabindex="0">${escape(text)}</pre>`);
    }
  }
  function download(format) {
    const ready = draft.recording ? { ok: false } : draft.contentReadiness();
    if (!ready.ok && !draft.snapshot) { announce("选定版本未完成，不能导出半截结果。请改用原文或完成编辑。"); return; }
    const text = draft.snapshot?.text ?? draft.text();
    const stamp = ms => `00:${String(Math.floor(ms / 60000)).padStart(2, "0")}:${String(Math.floor(ms / 1000) % 60).padStart(2, "0")},${String(ms % 1000).padStart(3, "0")}`;
    const contents = format === "srt" ? draft.units.map((unit, index) => `${index + 1}\n${stamp(index * 3000)} --> ${stamp((index + 1) * 3000)}\n${unit[draft.output] ?? ""}\n`).join("\n") : format === "md" ? "# LiveLingo 草稿（原型示例）\n\n" + text : text;
    const url = URL.createObjectURL(new Blob([contents], { type: "text/plain;charset=utf-8" }));
    const link = document.createElement("a");
    link.href = url; link.download = `livelingo-prototype.${format}`; link.click();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
    announce(format === "srt" ? "已导出 SRT；时间来自演示单元，不是真实音频对齐。" : "已导出当前版本。");
  }
  function settingsPage(name) {
    showPage("settings");
    document.querySelectorAll("[data-settings-panel]").forEach(panel => { panel.hidden = panel.dataset.settingsPanel !== name; });
    document.querySelectorAll("[data-settings]").forEach(button => button.classList.toggle("active", button.dataset.settings === name));
  }
  function renderTerms() {
    $("terms-list").innerHTML = terms.map((term, index) => `<div class="term-row"><span>${escape(term.source)}</span><span>→</span><span>${escape(term.target)}</span><button type="button" data-delete-term="${index}" class="text-button" aria-label="删除术语 ${escape(term.source)}">删除</button></div>`).join("");
  }
  function unitAction(action, index) {
    const unit = draft.units[index];
    if (!unit || draft.snapshot) return;
    if (action === "raw") { draft.useRaw(index); render(); }
    if (action === "edit") {
      editor = index; render();
      $(`sentence-edit-${index}`).focus();
    }
    if (action === "save") {
      const input = $(`sentence-edit-${index}`);
      draft.edit(index, draft.output, input.value);
      input.blur(); editor = null; render();
      announce("已保存人工修改，后续模型结果不会覆盖这一版。");
    }
    if (action === "cancel") { $(`sentence-edit-${index}`).blur(); editor = null; render(); }
    if (action === "retry") {
      slow = false; unit[draft.output + "Error"] = ""; unit[draft.output] = null;
      schedule(draft.ticket(index), draft.output); render();
    }
  }
  document.addEventListener("click", event => {
    const button = event.target.closest("button");
    if (!button) return;
    if (button.dataset.page) showPage(button.dataset.page);
    if (button.dataset.scenario) scenario(button.dataset.scenario);
    if (button.dataset.mode) changeMode(button.dataset.mode);
    if (button.dataset.output) setOutput(button.dataset.output);
    if (button.dataset.settings) settingsPage(button.dataset.settings);
    if (button.dataset.trayAction) {
      $("tray-menu").hidePopover();
      const action = button.dataset.trayAction;
      if (action === "summon") $("summon").click();
      if (action === "history") $("history").click();
      if (action === "translation") setOutput(draft.output === "translation" ? "clean" : "translation");
      if (action === "settings") settingsPage("general");
      if (action === "diagnostics") $("show-diagnostics").click();
      if (action === "hide") $("close-composer").click();
    }
    if (button.dataset.unitAction) unitAction(button.dataset.unitAction, Number(button.dataset.index));
    if (button.dataset.action === "retarget") modal("明确重新绑定目标", '<p>选择新目标才允许写入；不会自动采用新的前台窗口。</p><div class="dialog-actions"><button type="button" class="primary" data-dialog-action="bind-notes">绑定示例笔记输入框</button><button type="button" class="secondary" data-dialog-action="close">保留原草稿</button></div>');
    if (button.dataset.action === "copy-draft") copy();
    if (button.dataset.deleteTerm !== undefined) { terms.splice(Number(button.dataset.deleteTerm), 1); renderTerms(); }
    const dialogAction = button.dataset.dialogAction;
    if (dialogAction) {
      $("modal").close();
      if (dialogAction === "speech-settings") settingsPage("speech");
      if (dialogAction === "models-settings") settingsPage("models");
      if (dialogAction === "text-mode") changeMode("text");
      if (dialogAction === "bind-notes") { draft.retarget("notes"); render(); }
      if (dialogAction === "clipboard") copy();
    }
    if (button.dataset.export) { $("modal").close(); download(button.dataset.export); }
    if (button.dataset.restore !== undefined) {
      const entry = history[Number(button.dataset.restore)];
      clearInterval(recordTimer); recordSession++;
      draft.reset(draft.target);
      draft.units = structuredClone(entry.units); draft.output = entry.output; draft.route = entry.route;
      $("direction").value = entry.direction;
      $("translate-on").checked = draft.output === "translation"; $("source-input").value = draft.units.map(unit => unit.raw).join("\n"); $("route").value = draft.route;
      slow = false;
      draft.units.forEach((unit, index) => {
        const ticket = draft.ticket(index);
        if (unit.clean === null && !unit.cleanError && !unit.cleanOwned) schedule(ticket, "clean");
        if ($("translate-on").checked && unit.translation === null && !unit.translationError && !unit.translationOwned) schedule(ticket, "translation");
      });
      $("modal").close(); $("composer").hidden = false; liveText = ""; render();
    }
  });
  $("summon").addEventListener("click", () => { showPage("experience"); $("composer").hidden = false; if (draft.snapshot) fresh(); render(); if (mode === "text") $("source-input").focus(); else $("record").focus(); });
  $("close-composer").addEventListener("click", () => { if (draft.recording) stopRecording(); saveHistory(); $("composer").hidden = true; announce("已隐藏。草稿保留在本次原型内存中，重新呼出即可继续。"); });
  $("record").addEventListener("click", () => { if (draft.recording) stopRecording(); else startRecording(); });
  $("hold-record").addEventListener("pointerdown", event => { event.preventDefault(); event.currentTarget.setPointerCapture(event.pointerId); startRecording(true); });
  $("hold-record").addEventListener("pointerup", () => { if (draft.recording) stopRecording(); });
  $("hold-record").addEventListener("pointercancel", () => { if (draft.recording) stopRecording(); });
  $("hold-record").addEventListener("keydown", event => { if ((event.key === " " || event.key === "Enter") && !event.repeat) { event.preventDefault(); startRecording(true); } });
  $("hold-record").addEventListener("keyup", event => { if (event.key === " " || event.key === "Enter") { event.preventDefault(); stopRecording(); } });
  $("pause").addEventListener("click", () => { paused = !paused; render(); });
  $("reset").addEventListener("click", fresh);
  $("expand").addEventListener("click", () => { expanded = !expanded; if (expanded) $("source-input").value = draft.units.map(unit => unit.raw).join("\n"); $("expand").setAttribute("aria-label", expanded ? "收起长文工作台" : "展开长文工作台"); render(); });
  $("source-input").addEventListener("input", () => updateText("units"));
  $("source-input").addEventListener("compositionstart", () => { composing = true; });
  $("source-input").addEventListener("compositionend", () => { composing = false; updateText("units"); });
  $("source-input").addEventListener("paste", () => { setTimeout(() => updateText("whole"), 0); });
  $("route").addEventListener("change", () => updateText($("route").value));
  $("text-sample").addEventListener("click", () => { fresh(); changeMode("text"); $("source-input").value = SAMPLES[$("direction").value][0].raw; updateText("units"); });
  $("paste-sample").addEventListener("click", () => { $("direction").value = "zh-en"; $("source-input").value = DOCUMENT.raw; updateText("whole"); });
  $("transcript-demo").addEventListener("click", () => { fresh(); changeMode("transcript"); expanded = true; loadSamples(); });
  $("audio-file").addEventListener("change", () => { const file = $("audio-file").files[0]; $("file-note").textContent = file ? `已选择 ${file.name}；不会读取音频内容。请载入会议示例体验流程。` : "未选择文件。"; });
  $("translate-on").addEventListener("change", () => setOutput($("translate-on").checked ? "translation" : $("cleanup").value === "off" ? "raw" : "clean"));
  $("direction").addEventListener("change", () => { draft.invalidateTranslations(); if ($("translate-on").checked) draft.units.forEach((_, index) => schedule(draft.ticket(index), "translation")); render(); });
  $("use-raw-all").addEventListener("click", () => { if (draft.snapshot) return; slow = false; draft.units.forEach((_, index) => draft.useRaw(index)); setOutput("raw"); announce("已明确改用原文；晚到的整理结果不会覆盖原文工作稿。"); });
  $("copy").addEventListener("click", copy);
  $("commit").addEventListener("click", submit);
  $("history").addEventListener("click", () => {
    modal("最近草稿 · 仅本次原型内存", history.length ? history.map((entry, index) => `<div class="history-row"><span>${escape(entry.time)} · ${escape(entry.units[0]?.raw.slice(0, 45))}</span><button type="button" class="secondary" data-restore="${index}">恢复</button></div>`).join("") : "<p>暂无最近草稿。关闭或回填后可以在这里找回；刷新页面会清除。</p>");
  });
  $("export").addEventListener("click", () => modal("导出当前选定版本", '<p>TXT 与 Markdown 保留当前文本。SRT 只使用演示单元的示例时间，不代表真实对齐；结构化整段不适合字幕格式。</p><div class="dialog-actions"><button type="button" class="secondary" data-export="txt">TXT</button><button type="button" class="secondary" data-export="md">Markdown</button><button type="button" class="secondary" data-export="srt">SRT（示例时间）</button></div>'));
  $("modal-close").addEventListener("click", () => $("modal").close());
  $("clear-history").addEventListener("click", () => { history = []; announce("本次原型的最近草稿已清除。"); });
  $("theme").addEventListener("change", () => document.body.classList.toggle("dark", $("theme").value === "dark"));
  $("reduce-motion").addEventListener("change", () => document.body.classList.toggle("reduce-motion", $("reduce-motion").checked));
  $("commit-binding").addEventListener("change", render);
  $("summon-binding").addEventListener("input", () => { document.querySelectorAll(".summon-key").forEach(key => { key.textContent = $("summon-binding").value || "未设置"; }); });
  $("platform").addEventListener("change", () => { $("desktop-platform").textContent = $("platform").value === "mac" ? "macOS" : "Windows"; document.querySelectorAll(".summon-key").forEach(key => { key.textContent = $("platform").value === "mac" && $("summon-binding").value === "Ctrl+Alt+T" ? "⌃⌥T" : $("summon-binding").value; }); render(); });
  $("cleanup").addEventListener("change", () => { if (!$("translate-on").checked) setOutput($("cleanup").value === "off" ? "raw" : "clean"); });
  ["cloud-translation", "cloud-cleanup"].forEach(id => $(id).addEventListener("change", () => {
    if ($(id).checked) {
      $(id).checked = false;
      const label = id === "cloud-translation" ? "翻译" : "整理";
      modal(`单独授权云端${label}`, `<p>正式产品会把${label}所需的原文发送至指定服务商，语音上传须另行授权。本次仅演示授权界面，不发送任何请求。</p><div class="dialog-actions"><button type="button" class="primary" id="allow-cloud">同意本次界面演示</button><button type="button" class="secondary" data-dialog-action="close">保持本地</button></div>`);
      $("allow-cloud").addEventListener("click", () => { $(id).checked = true; $("modal").close(); render(); });
    } else render();
  }));
  $("show-diagnostics").addEventListener("click", () => modal("本次原型状态 · 不包含正文", `<pre>${escape(JSON.stringify({ mode, route: draft.route, output: draft.output, recording: draft.recording, target: draft.target, targetLost: draft.targetLost, units: draft.units.length, submitted: !!draft.snapshot }, null, 2))}</pre>`));
  $("term-form").addEventListener("submit", event => {
    event.preventDefault();
    const source = $("term-source").value.trim(), target = $("term-target").value.trim();
    if (!source || !target) { announce("原词和目标表达都不能为空。"); return; }
    if (terms.some(term => term.source.toLowerCase() === source.toLowerCase())) { announce("此原词已有词条，请先删除旧词条再添加。"); return; }
    terms.push({ source, target }); event.target.reset(); renderTerms();
    announce("词条已加入本次原型。示例译文是预设的，不会伪装成模型已应用这个词条。");
  });
  document.addEventListener("keydown", event => {
    if (event.isComposing || composing || $("modal").open) return;
    const editing = event.target instanceof HTMLTextAreaElement || event.target instanceof HTMLInputElement || event.target instanceof HTMLSelectElement;
    if (event.key === "Escape" && !draft.snapshot) {
      if (editor !== null) { document.activeElement.blur(); editor = null; render(); return; }
      if (draft.recording) stopRecording();
      saveHistory(); $("composer").hidden = true; return;
    }
    if (event.key === "Enter" && !event.shiftKey && !$("composer").hidden && !draft.recording) {
      if (event.target.id === "source-input" || event.target.id === "record" || event.target === document.body) {
        const shortcutMatches = $("commit-binding").value === "ctrl-enter" ? event.ctrlKey : !event.ctrlKey && !event.metaKey && !event.altKey;
        if (shortcutMatches) {
          event.preventDefault();
          flushSource();
          submit();
        }
      }
    }
    if (!editing && event.ctrlKey && event.altKey && event.key.toLowerCase() === "t") { event.preventDefault(); $("summon").click(); }
  });
  window.addEventListener("blur", () => { if (draft.recording && $("session-status").dataset.gesture === "按住") stopRecording(); });
  $("state-grid").innerHTML = STATES.map(([number, title, description, name, hint]) => `<button type="button" class="state-card" data-scenario="${name}"><span>STATE / ${number}</span><h2>${escape(title)}</h2><p>${escape(description)}</p><small>${escape(hint)} ↗</small></button>`).join("");
  renderTerms(); render();
})();
