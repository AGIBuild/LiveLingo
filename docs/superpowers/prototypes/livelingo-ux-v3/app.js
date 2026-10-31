(function () {
  "use strict";
  const { Draft, splitSentences, routeFor } = window.LiveLingoDraft;
  const $ = id => document.getElementById(id);
  const esc = text => String(text ?? "").replace(/[&<>"']/g, ch => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[ch]);
  const icon = name => `<svg class="ic" aria-hidden="true"><use href="#i-${name}"/></svg>`;

  /* ---------- preset corpus: the prototype runs no model ---------- */
  const ZH_EN = [
    { raw: "嗯，这个 PR 先不要合并，等 CI 跑完再说。", clean: "这个 PR 先不要合并，等 CI 跑完再说。", translation: "Do not merge this PR yet. Wait for CI to finish." },
    { raw: "Redis 的超时设置为 30 秒，不是 30 分钟。", clean: "Redis 的超时设置为 30 秒，不是 30 分钟。", translation: "Set the Redis timeout to 30 seconds, not 30 minutes.", gate: { candidate: "Redis 的超时设置为 30 分钟。", reason: "整理稿把「30 秒」改成了「30 分钟」，已保留原话" } },
    { raw: "就是，保持 `CacheService` 的 API 不变，只修改错误处理。", clean: "保持 `CacheService` 的 API 不变，只修改错误处理。", translation: "Keep the API of `CacheService` unchanged. Only modify error handling." },
    { raw: "今天主要讨论缓存穿透的处理方案。", clean: "今天主要讨论缓存穿透的处理方案。", translation: "Today we mainly discuss how to handle cache penetration." },
    { raw: "会后请小王整理纪要，并同步给所有人。", clean: "会后请小王整理纪要，并同步给所有人。", translation: "After the meeting, please ask Xiao Wang to write the minutes and share them with everyone." }
  ].map(item => ({ ...item, dir: "zh-en" }));
  const EN_ZH = [
    { raw: "Please do not merge this PR until CI finishes.", clean: "Please do not merge this PR until CI finishes.", translation: "在 CI 跑完前，请不要合并这个 PR。" },
    { raw: "Set the Redis timeout to 30 seconds, not 30 minutes.", clean: "Set the Redis timeout to 30 seconds, not 30 minutes.", translation: "将 Redis 的超时设为 30 秒，不是 30 分钟。" },
    { raw: "Keep the API of `CacheService` unchanged.", clean: "Keep the API of `CacheService` unchanged.", translation: "保持 `CacheService` 的 API 不变。" }
  ].map(item => ({ ...item, dir: "en-zh" }));
  const DOC_RAW = '# 部署约束\n\n- 不要更改 `CacheService` 的 API。\n- 超时为 30 秒，日志不包含凭据。\n\n```json\n{\n  "timeout": 30,\n  "api_key": "${API_KEY}"\n}\n```\n';
  const DOC = { raw: DOC_RAW, clean: DOC_RAW, dir: "zh-en", translation: '# Deployment constraints\n\n- Do not change the API of `CacheService`.\n- The timeout is 30 seconds. Logs must not contain credentials.\n\n```json\n{\n  "timeout": 30,\n  "api_key": "${API_KEY}"\n}\n```\n' };
  const FIXTURES = [...ZH_EN, ...EN_ZH, DOC];
  const SAMPLES = { "zh-en": ZH_EN, "en-zh": EN_ZH };
  const LANG = { "zh-en": { src: "中文", dst: "English" }, "en-zh": { src: "English", dst: "中文" } };
  const NO_MODEL = "演示语料之外的文本没有译文（原型未接入模型）";
  const REASONS = { recording: "请先结束录音。", empty: "草稿是空的。", processing: "选定的内容还在处理中。", unavailable: "有句子没有可用的结果：重试、手动输入，或把输出改为「原话」。", "target-lost": "原输入框已失效，请重新选择目标或复制。" };

  /* ---------- state ---------- */
  const draft = new Draft("agent");
  const ui = {
    direction: "zh-en", editing: null, editValue: "", wide: false, forceWhole: false, armed: false, timeline: false, fileName: "",
    slow: false, gate: false, handsFree: false, composing: false, cloud: { translation: false, cleanup: false }, cleanup: "on",
    notice: null, micBlock: null, history: [], secs: 0, tail: "", theme: "auto", binding: "Ctrl+Alt+T", commitKey: "enter", platform: "mac"
  };
  const rec = { id: 0, timer: null, index: 0 };
  const inflight = new Set();
  const terms = [{ source: "缓存穿透", target: "cache penetration" }, { source: "CacheService", target: "CacheService" }];
  let syncTimer = null, toastTimer = null, lastStruct = "", lastData = "", lastPointerUp = 0;

  /* ---------- helpers ---------- */
  const fixtureFor = raw => FIXTURES.find(item => item.raw === raw);
  const mmss = total => `${Math.floor(total / 60)}:${String(total % 60).padStart(2, "0")}`;
  const stamp = total => `${String(Math.floor(total / 60)).padStart(2, "0")}:${String(total % 60).padStart(2, "0")}`;
  const SYMBOLS = { ctrl: "⌃", alt: "⌥", shift: "⇧", meta: "⌘", cmd: "⌘" };
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
  const isWide = () => innerWidth >= 900 && (ui.wide || draft.route === "whole" || ui.timeline);
  const popoverOpen = () => document.querySelector(".menu:popover-open");
  const dialogOpen = () => $("dlg").open;

  function toast(message) {
    $("toast").textContent = message;
    $("toast").hidden = false;
    clearTimeout(toastTimer);
    toastTimer = setTimeout(() => { $("toast").hidden = true; }, 3600);
  }
  function dialog(title, body) {
    $("dlg-title").textContent = title;
    $("dlg-body").innerHTML = body;
    if (!$("dlg").open) $("dlg").showModal();
  }
  function showPage(name) {
    document.querySelectorAll(".page").forEach(page => { page.hidden = page.id !== `page-${name}`; });
    document.querySelectorAll(".tab").forEach(tab => { if (tab.dataset.page === name) tab.setAttribute("aria-current", "page"); else tab.removeAttribute("aria-current"); });
  }
  function showSettings(section = "general") {
    showPage("settings");
    document.querySelectorAll("[data-panel]").forEach(panel => { panel.hidden = panel.dataset.panel !== section; });
    document.querySelectorAll("[data-sec]").forEach(button => button.classList.toggle("active", button.dataset.sec === section));
  }
  function applyTheme() {
    const theme = ui.theme === "auto" ? (matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light") : ui.theme;
    document.documentElement.dataset.theme = theme;
  }

  /* ---------- processing simulation ---------- */
  function run(unit, kind) {
    const key = `${unit.id}:${kind}:${draft.translationRevision}`;
    if (ui.slow || inflight.has(key)) return;
    inflight.add(key);
    const ticket = draft.ticketFor(unit);
    const fixture = fixtureFor(unit.raw);
    let text, warning = "";
    if (kind === "clean") {
      if (ui.cleanup === "off") text = unit.raw;
      else if (ui.gate && fixture?.gate) { text = fixture.gate.candidate; warning = fixture.gate.reason; }
      else text = fixture ? fixture.clean : unit.raw;
    } else text = fixture && fixture.dir === ui.direction ? fixture.translation : null;
    setTimeout(() => {
      inflight.delete(key);
      if (text == null) draft.fail(ticket, kind, NO_MODEL); else draft.complete(ticket, kind, text, warning);
      render();
    }, kind === "clean" ? 380 : 650);
  }
  function ensure() {
    const kind = draft.output;
    if (kind === "raw") return;
    draft.units.forEach(unit => { if (unit[kind] === null && !unit[kind + "Error"]) run(unit, kind); });
  }

  /* ---------- source <-> draft ---------- */
  function autosize() {
    const field = $("src");
    if (field.hidden) return;
    field.style.height = "auto";
    field.style.height = `${Math.min(field.scrollHeight, 180)}px`;
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
    if (ui.micBlock) { toast(ui.micBlock === "permission" ? "麦克风权限未开启 · 仍可直接打字" : "语音模型还在下载 · 现在可以直接打字"); $("src").focus(); return; }
    if (draft.snapshot) fresh();
    if (ui.editing !== null) saveEdit();
    flushSource();
    ui.notice = null;
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
        rec.index++; position = 0; ui.tail = ""; pause = 10;
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
    ui.history = [{ time: new Date().toLocaleTimeString(), units: structuredClone(draft.units), output: draft.output, route: draft.route, direction: ui.direction, timeline: ui.timeline, fileName: ui.fileName }, ...ui.history].slice(0, 5);
  }
  function fresh() {
    clearInterval(rec.timer); rec.id++; rec.index = 0;
    clearTimeout(syncTimer); syncTimer = null;
    saveHistory();
    inflight.clear();
    draft.reset(draft.target);
    Object.assign(ui, { editing: null, armed: false, timeline: false, fileName: "", slow: false, gate: false, handsFree: false, tail: "", notice: null });
    draft.output = "clean";
    $("src").value = "";
    autosize();
    $("composer").hidden = false;
    lastStruct = "";
    render();
  }
  function restore(index) {
    const entry = ui.history[index];
    if (!entry) return;
    clearInterval(rec.timer); rec.id++;
    inflight.clear();
    draft.reset(draft.target);
    draft.restore(structuredClone(entry.units), entry.output, entry.route);
    Object.assign(ui, { direction: entry.direction, timeline: entry.timeline, fileName: entry.fileName, slow: false, editing: null, armed: false, notice: null });
    if (!ui.timeline) setSource();
    $("composer").hidden = false;
    lastStruct = "";
    ensure();
    render();
  }
  function setOutput(kind) {
    if (draft.snapshot) { toast("已写入的内容已冻结 · 新草稿后可继续"); return; }
    draft.output = kind;
    ui.editing = null;
    ui.armed = false;
    ensure();
    render();
  }
  function swapDirection() {
    if (draft.snapshot) return;
    ui.direction = ui.direction === "zh-en" ? "en-zh" : "zh-en";
    draft.invalidateTranslations();
    ensure();
    render();
  }
  function loadTranscript(name, fromPicker) {
    fresh();
    ui.timeline = true;
    ui.fileName = name;
    ZH_EN.forEach(item => draft.append(item.raw));
    ensure();
    render();
    if (fromPicker) toast("已载入示例转录（不会读取所选文件的音频）");
  }
  function showComposer() {
    showPage("play");
    if (draft.snapshot) fresh();
    $("composer").hidden = false;
    render();
    if (draft.units.length === 0 || !(draft.recording || ui.timeline)) $("src").focus(); else $("mic").focus();
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
    const unit = draft.units.find(item => item.id === id);
    if (!unit || draft.snapshot) return;
    ui.editing = id;
    ui.editValue = unit[draft.output] ?? "";
    ui.armed = false;
    lastStruct = "";
    render();
    $(`edit-${id}`)?.focus();
  }
  function saveEdit() {
    if (ui.editing === null) return;
    draft.edit(draft.indexOf(ui.editing), draft.output, $(`edit-${ui.editing}`).value);
    ui.editing = null;
    lastStruct = "";
    render();
    toast("已保存 · 之后的模型结果不会覆盖这一版");
  }
  function cancelEdit() { ui.editing = null; lastStruct = ""; render(); }
  function rowAction(action, id) {
    const index = draft.indexOf(id);
    if (index < 0 || draft.snapshot) return;
    if (action === "edit") startEdit(id);
    if (action === "raw") { draft.useRaw(index); ui.armed = false; lastStruct = ""; render(); }
    if (action === "retry") {
      const unit = draft.units[index];
      unit[draft.output + "Error"] = ""; unit[draft.output] = null;
      ensure(); render();
    }
    if (action === "save") saveEdit();
    if (action === "cancel") cancelEdit();
  }
  function commit(fromKey) {
    if (draft.snapshot) return;
    if (ui.armed) { if (!fromKey) { ui.armed = false; render(); toast("已取消自动写入"); } return; }
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
      toast(`已写入${result.target === "notes" ? "笔记" : " Agent"} · 未发送`);
      field.focus();
    } else copyText(result.text);
    render();
  }
  async function copyText(text) {
    try { await navigator.clipboard.writeText(text); toast("已复制"); }
    catch { dialog("请手动复制", `<p>浏览器未允许写入剪贴板，内容没有丢失。</p><pre tabindex="0">${esc(text)}</pre>`); }
  }
  function copyCurrent() {
    const ready = draft.recording ? { ok: false, reason: "recording" } : draft.contentReadiness();
    if (!ready.ok) { toast(REASONS[ready.reason]); return; }
    copyText(draft.text());
  }
  function download(format) {
    const ready = draft.recording ? { ok: false } : draft.contentReadiness();
    if (!ready.ok) { toast("选定的内容未完成，不能导出半截结果"); return; }
    const srt = draft.units.map((unit, i) => `${i + 1}\n00:${stamp(i * 3)},000 --> 00:${stamp(i * 3 + 3)},000\n${unit[draft.output] ?? ""}\n`).join("\n");
    const body = format === "srt" ? srt : format === "md" ? `# LiveLingo 草稿（原型示例）\n\n${draft.text()}` : draft.text();
    const url = URL.createObjectURL(new Blob([body], { type: "text/plain;charset=utf-8" }));
    const link = document.createElement("a");
    link.href = url; link.download = `livelingo-prototype.${format}`; link.click();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
    toast(format === "srt" ? "已导出 SRT · 时间为示例，非真实对齐" : "已导出");
  }

  /* ---------- rendering ---------- */
  function unitHTML(unit, index) {
    const kind = draft.output, state = draft.state(unit, kind), owned = unit[kind + "Owned"], editing = ui.editing === unit.id;
    const warn = kind === "clean" && unit.warning;
    let body;
    if (editing) body = `<label class="sr-only" for="edit-${unit.id}">编辑第 ${index + 1} 句</label><textarea class="edit" id="edit-${unit.id}" rows="3">${esc(ui.editValue)}</textarea><div class="edit-actions"><button type="button" data-row="save" data-id="${unit.id}">保存 <kbd>${commitKeyLabel()}</kbd></button><button type="button" data-row="cancel" data-id="${unit.id}">取消 <kbd>Esc</kbd></button></div>`;
    else if (state === "pending") body = `<div class="skel" aria-hidden="true"></div><div class="skel short" aria-hidden="true"></div><span class="sr-only">处理中</span>`;
    else if (state === "error") body = `<p class="row-error">${esc(unit[kind + "Error"])}</p><div class="inline-actions"><button type="button" data-row="retry" data-id="${unit.id}">重试</button><button type="button" data-row="edit" data-id="${unit.id}">手动输入</button></div>`;
    else if (state === "empty") body = `<p class="row-empty">（空）</p>`;
    else body = `<p class="row-text">${esc(unit[kind])}</p>${warn ? `<p class="row-note">${esc(unit.warning)}</p>` : owned ? `<p class="row-note owned">已手动修改</p>` : ""}`;
    const tools = draft.snapshot || draft.recording || editing ? "" : `<div class="row-tools">${kind === "clean" ? `<button type="button" class="icon-btn" data-row="raw" data-id="${unit.id}" aria-label="第 ${index + 1} 句改用原话" title="改用原话">${icon("undo")}</button>` : ""}<button type="button" class="icon-btn" data-row="edit" data-id="${unit.id}" aria-label="编辑第 ${index + 1} 句" title="编辑">${icon("edit")}</button></div>`;
    return `<article class="unit ${warn ? "warn" : ""} ${owned ? "owned" : ""}" data-id="${unit.id}">${body}${tools}</article>`;
  }
  function bannerState() {
    if (draft.targetLost) return { tone: "bad", text: "原输入框已失效，草稿已保留。为避免写进错误的窗口，不会自动写入。", actions: [["重新选择", "retarget"], ["复制", "copy"]] };
    return ui.notice;
  }
  function statusText() {
    if (draft.snapshot) return "已写入";
    if (draft.recording) return ui.handsFree ? `正在听 · ${mmss(ui.secs)} · 点击结束` : `按住说话 · ${mmss(ui.secs)} · 松开结束`;
    if (ui.armed) return "处理完成后自动写入 · Esc 取消";
    if (!draft.units.length) return "";
    const ready = draft.contentReadiness();
    if (ready.reason === "processing") return draft.output === "translation" ? "正在翻译…" : "正在整理…";
    if (ready.reason === "unavailable") return "有句子未完成 · 可重试或改用原话";
    const where = ui.cloud.translation || ui.cloud.cleanup ? "含云端处理" : "本地处理";
    return `${draft.units.length} 句 · ${where}`;
  }
  function render() {
    const lang = LANG[ui.direction], output = draft.output;
    const composer = $("composer");
    composer.classList.toggle("wide", isWide());
    composer.classList.toggle("has-res", draft.units.length > 0 && output !== "raw");

    // header
    const dest = $("dest");
    dest.className = `chip dest${draft.targetLost ? " bad" : draft.target === null ? " warn" : ""}`;
    dest.innerHTML = draft.targetLost ? `${icon("alert")}原输入框已失效` : draft.target === null ? "没有输入框 · 仅复制" : `${icon("target")}${draft.target === "notes" ? "笔记" : "Agent"}`;
    const cloud = [ui.cloud.translation && "翻译", ui.cloud.cleanup && "整理"].filter(Boolean);
    $("privacy").className = `chip privacy ${cloud.length ? "warn" : "ok"}`;
    $("privacy").innerHTML = `<i class="dot"></i><span class="lbl">${cloud.length ? `云端 · ${cloud.join("、")}` : "本地"}</span>`;
    $("privacy").setAttribute("aria-label", cloud.length ? `云端处理：${cloud.join("、")}` : "全部本地处理");
    const pillLabel = { clean: `${lang.src} · 整理`, translation: `→ ${lang.dst}`, raw: "原话" }[output];
    $("out-pill").innerHTML = `${output === "translation" ? icon("globe") : ""}${pillLabel}${icon("chevron")}`;
    const menuLabels = { clean: `保持${lang.src}（整理）`, translation: `翻译成 ${lang.dst}`, raw: "原话（不整理）" };
    document.querySelectorAll("[data-out]").forEach(item => {
      item.setAttribute("aria-checked", String(item.dataset.out === output));
      if (item.dataset.out !== "raw") item.textContent = menuLabels[item.dataset.out];
    });
    document.querySelector('[data-act="swap"]').textContent = `语言：${lang.src} → ${lang.dst}　⇄ 互换`;
    document.querySelectorAll("[data-target]").forEach(item => item.setAttribute("aria-checked", String((item.dataset.target === "none" ? null : item.dataset.target) === draft.target)));
    document.querySelector('[data-act="wide"]').setAttribute("aria-checked", String(ui.wide));
    document.querySelector('[data-act="whole"]').setAttribute("aria-checked", String(ui.forceWhole));
    document.querySelectorAll(".k-summon").forEach(el => { el.textContent = keyLabel(ui.binding); });
    document.querySelectorAll(".k-commit").forEach(el => { el.textContent = commitKeyLabel(); });
    $("menubar-title").textContent = draft.target === "notes" ? "Notes" : "Cursor";
    $("target-title").textContent = draft.target === "notes" ? "Notes — 新笔记" : "Cursor — Agent";

    // banner
    const banner = bannerState();
    $("banner").hidden = !banner;
    if (banner) {
      $("banner").className = `banner ${banner.tone}`;
      $("banner").innerHTML = `${icon(banner.tone === "info" ? "info" : "alert")}<p>${esc(banner.text)}</p>${banner.progress ? `<progress max="100" value="${banner.progress}" aria-label="下载进度"></progress>` : ""}<span class="actions">${(banner.actions || []).map(([label, act]) => `<button type="button" data-banner="${act}">${esc(label)}</button>`).join("")}</span>`;
    }

    // source
    const live = draft.recording || ui.timeline;
    $("src").hidden = live;
    $("src-live").hidden = !live;
    if (!live) autosize();
    $("src").readOnly = !!draft.snapshot;
    if (live) {
      const lines = draft.units.map((unit, i) => `<div class="line">${ui.timeline ? `<time>${stamp(i * 3)}</time>` : ""}<span>${esc(unit.raw)}</span></div>`);
      if (ui.tail) lines.push(`<div class="line tail"><span>${esc(ui.tail)}</span><i class="caret"></i></div>`);
      $("src-live").innerHTML = lines.join("");
      $("src-live").scrollTop = $("src-live").scrollHeight;
    }
    $("src-label").textContent = ui.timeline ? `转录 · ${ui.fileName}` : draft.recording ? "正在听…" : "原话";
    $("route-chip").hidden = draft.route !== "whole";
    $("hint").hidden = draft.units.length > 0 || draft.recording || $("src").value.trim() !== "";

    // result rows (skipped while nothing changed, so hover and focus are never disturbed)
    const showRes = draft.units.length > 0 && output !== "raw";
    $("res").hidden = !showRes;
    $("res-label").textContent = output === "translation" ? `将写入 · ${lang.dst}` : "将写入";
    const struct = JSON.stringify([output, ui.editing, draft.snapshot !== null, draft.recording]);
    const data = JSON.stringify(draft.units.map(unit => [unit.id, unit.clean, unit.translation, unit.cleanError, unit.translationError, unit.warning, unit.cleanOwned, unit.translationOwned]));
    // While a sentence is being edited, late results must not rebuild the DOM and steal the caret.
    if (showRes && (struct !== lastStruct || (data !== lastData && ui.editing === null))) {
      const body = $("rows");
      const wasBottom = $("composer").querySelector(".c-body").scrollTop;
      body.innerHTML = draft.units.map(unitHTML).join("");
      lastStruct = struct; lastData = data;
      $("composer").querySelector(".c-body").scrollTop = draft.recording ? 1e6 : wasBottom;
    }

    // footer
    const mic = $("mic");
    mic.classList.toggle("recording", draft.recording);
    mic.setAttribute("aria-label", draft.recording ? "结束听写" : "开始听写，长按为按住说话");
    const status = $("status");
    status.textContent = statusText();
    const ready = draft.contentReadiness();
    status.className = `status${!draft.recording && draft.units.length && ready.reason === "unavailable" ? " warn" : ""}`;
    $("copy").disabled = draft.recording || !ready.ok;
    const commitButton = $("commit");
    const live2 = draft.readiness();
    commitButton.disabled = !!draft.snapshot || (!ui.armed && !live2.ok && live2.reason !== "processing");
    commitButton.innerHTML = ui.armed ? `<span class="spinner" aria-hidden="true"></span>完成后写入` : draft.snapshot ? "已写入" : `${draft.target === null ? "复制" : `写入 ${draft.target === "notes" ? "笔记" : "Agent"}`} <kbd>${commitKeyLabel()}</kbd>`;
    commitButton.setAttribute("aria-label", ui.armed ? "取消完成后自动写入" : `${draft.target === null ? "复制" : "写入"}`);

    if (ui.armed) {
      const state = draft.readiness();
      if (state.ok) { ui.armed = false; submit(); }
      else if (state.reason !== "processing") { ui.armed = false; toast(REASONS[state.reason]); render(); }
    }
  }

  /* ---------- scenarios ---------- */
  function scenario(name) {
    showPage("play");
    ui.micBlock = null;
    fresh();
    draft.retarget(name === "no-target" ? null : "agent");
    ui.direction = "zh-en";
    if (name === "pair") startRecording(true);
    else if (name === "translate") { draft.output = "translation"; startRecording(true); }
    else if (name === "document") { draft.output = "translation"; $("src").value = DOC_RAW; autosize(); syncFromSource(); }
    else if (name === "meeting") loadTranscript("会议录音（示例）.m4a");
    else {
      ZH_EN.slice(0, 3).forEach(item => draft.append(item.raw));
      setSource();
      if (name === "gate") ui.gate = true;
      if (name === "slow") { ui.slow = true; ui.notice = { tone: "info", text: "处理比平时慢。你可以继续等待，或改用原话。", actions: [["改用原话", "use-raw"], ["继续等待", "keep-waiting"]] }; }
      if (name === "failure") draft.output = "translation";
      if (name === "lost") draft.targetLost = true;
      if (name === "no-audio") ui.notice = { tone: "warn", text: "没有检测到声音，已停止录音，已有内容保留。", actions: [["检查输入设备", "mic-settings"], ["知道了", "dismiss"]] };
      if (name === "permission") { ui.micBlock = "permission"; ui.notice = { tone: "warn", text: "麦克风权限未开启 · 仍可直接打字。授权后无需重启（示意）。", actions: [["打开系统设置", "open-os"], ["知道了", "dismiss"]] }; }
      if (name === "download") { ui.micBlock = "download"; ui.notice = { tone: "info", text: "语音模型下载中 · 现在可以直接打字。", progress: 46, actions: [["后台下载", "dismiss"]] }; }
      ensure();
      if (name === "failure") {
        draft.invalidateTranslations();
        draft.units.forEach((unit, i) => draft.fail(draft.ticket(i), "translation", "示例连接超时 · 原话和整理稿未丢失"));
      }
      render();
    }
  }

  /* ---------- events ---------- */
  document.addEventListener("click", event => {
    const el = event.target.closest("button, [data-sec]");
    if (!el) return;
    if (el.classList.contains("tab")) showPage(el.dataset.page);
    if (el.dataset.scenario) scenario(el.dataset.scenario);
    if (el.dataset.sec) showSettings(el.dataset.sec);
    if (el.dataset.row) rowAction(el.dataset.row, Number(el.dataset.id));
    if (el.dataset.banner) bannerAction(el.dataset.banner);
    if (el.dataset.out) { setOutput(el.dataset.out); el.closest("[popover]")?.hidePopover(); }
    if (el.dataset.target) { draft.retarget(el.dataset.target === "none" ? null : el.dataset.target); render(); el.closest("[popover]")?.hidePopover(); }
    if (el.dataset.platform) {
      ui.platform = el.dataset.platform;
      document.documentElement.dataset.platform = ui.platform;
      document.querySelectorAll("[data-platform]").forEach(b => { if (b.getAttribute("role") === "radio") b.setAttribute("aria-checked", String(b.dataset.platform === ui.platform)); });
      render();
    }
    if (el.dataset.act) { menuAction(el.dataset.act); const pop = el.closest("[popover]"); if (pop && !["wide", "whole"].includes(el.dataset.act)) pop.hidePopover(); }
    if (el.dataset.restore !== undefined) { $("dlg").close(); restore(Number(el.dataset.restore)); }
    if (el.dataset.export) { $("dlg").close(); download(el.dataset.export); }
    if (el.dataset.dlg === "close") $("dlg").close();
    if (el.dataset.dlg === "clipboard") { $("dlg").close(); copyCurrent(); }
  });
  function bannerAction(action) {
    if (action === "retarget") $("dest").click();
    if (action === "copy") copyCurrent();
    if (action === "dismiss") { ui.notice = null; render(); }
    if (action === "use-raw") { ui.notice = null; ui.slow = false; setOutput("raw"); }
    if (action === "keep-waiting") { ui.notice = null; ui.slow = false; inflight.clear(); ensure(); render(); }
    if (action === "mic-settings") { ui.notice = null; showSettings("voice"); }
    if (action === "open-os") toast("（演示）将打开系统隐私设置");
  }
  function menuAction(act) {
    if (act === "summon") showComposer();
    if (act === "hide") hideComposer();
    if (act === "settings") showSettings("general");
    if (act === "new") { fresh(); $("src").focus(); }
    if (act === "swap") swapDirection();
    if (act === "import") $("file").click();
    if (act === "wide") { ui.wide = !ui.wide; render(); }
    if (act === "whole") { ui.forceWhole = !ui.forceWhole; if (!draft.recording && !ui.timeline) { syncFromSource(); } render(); }
    if (act === "history") dialog("最近草稿", ui.history.length ? ui.history.map((entry, i) => `<div class="hist"><span>${esc(entry.time)} · ${esc(entry.units[0]?.raw.slice(0, 36))}</span><button type="button" class="btn" data-restore="${i}">恢复</button></div>`).join("") : "<p>还没有草稿。关闭或写入后会出现在这里；刷新页面会清除。</p>");
    if (act === "export") dialog("导出当前内容", `<p>导出当前选定的内容。SRT 使用示例时间，不代表真实音频对齐。</p><div class="dlg-actions"><button type="button" class="btn" data-export="txt">TXT</button><button type="button" class="btn" data-export="md">Markdown</button><button type="button" class="btn" data-export="srt">SRT</button></div>`);
  }
  $("summon").addEventListener("click", showComposer);
  $("close").addEventListener("click", hideComposer);
  $("commit").addEventListener("click", () => commit(false));
  $("copy").addEventListener("click", copyCurrent);
  $("dlg-close").addEventListener("click", () => $("dlg").close());
  $("file").addEventListener("change", () => { const file = $("file").files[0]; if (file) loadTranscript(file.name, true); $("file").value = ""; });
  $("edge").addEventListener("change", () => { if ($("edge").value) scenario($("edge").value); $("edge").selectedIndex = 0; });
  $("theme-btn").addEventListener("click", () => { ui.theme = document.documentElement.dataset.theme === "dark" ? "light" : "dark"; $("theme").value = ui.theme; applyTheme(); });

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
    if (draft.recording) stopRecording(); else startRecording(true);
  });

  // source input
  const src = $("src");
  src.addEventListener("input", () => {
    if (ui.editing !== null) saveEdit();
    autosize();
    ui.armed = false;
    clearTimeout(syncTimer);
    syncTimer = null;
    if (!ui.composing) syncTimer = setTimeout(syncFromSource, 400);
    render();
  });
  src.addEventListener("compositionstart", () => { ui.composing = true; });
  src.addEventListener("compositionend", () => { ui.composing = false; clearTimeout(syncTimer); syncTimer = setTimeout(syncFromSource, 400); });

  // menus
  document.querySelectorAll(".menu").forEach(menu => menu.addEventListener("toggle", event => {
    if (event.newState !== "open") return;
    const anchor = document.querySelector(`[popovertarget="${menu.id}"]`);
    const rect = anchor.getBoundingClientRect(), width = menu.offsetWidth, height = menu.offsetHeight;
    let top = rect.bottom + 6;
    if (top + height > innerHeight - 8) top = Math.max(8, rect.top - height - 6);
    menu.style.left = `${Math.min(Math.max(8, rect.right - width), innerWidth - width - 8)}px`;
    menu.style.top = `${top}px`;
    (menu.querySelector('[aria-checked="true"]') || menu.querySelector("button"))?.focus();
  }));
  document.addEventListener("keydown", event => {
    const menu = popoverOpen();
    if (!menu || !["ArrowDown", "ArrowUp"].includes(event.key)) return;
    event.preventDefault();
    const items = [...menu.querySelectorAll("button")];
    const next = items.indexOf(document.activeElement) + (event.key === "ArrowDown" ? 1 : -1);
    items[(next + items.length) % items.length].focus();
  }, true);

  // keyboard
  document.addEventListener("keydown", event => {
    if (event.isComposing || ui.composing) return;
    if (matches(event, ui.binding)) { event.preventDefault(); showComposer(); return; }
    if (dialogOpen() || popoverOpen() || $("page-play").hidden || $("composer").hidden) return;
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
    if (target.classList?.contains("edit")) { event.preventDefault(); saveEdit(); return; }
    if (!(target === src || target === document.body || target === mic)) return;
    const chord = ui.commitKey === "ctrl-enter" ? event.ctrlKey || event.metaKey : !event.ctrlKey && !event.metaKey && !event.altKey;
    if (chord) { event.preventDefault(); commit(true); }
  });
  $("rows").addEventListener("input", event => { if (event.target.classList.contains("edit")) ui.editValue = event.target.value; });
  window.addEventListener("blur", () => { if (draft.recording && !ui.handsFree) stopRecording(); });
  window.addEventListener("resize", render);

  // settings
  $("summon-binding").addEventListener("input", () => { ui.binding = $("summon-binding").value; render(); });
  $("commit-binding").addEventListener("change", () => { ui.commitKey = $("commit-binding").value; render(); });
  $("theme").addEventListener("change", () => { ui.theme = $("theme").value; applyTheme(); });
  $("reduce-motion").addEventListener("change", () => { document.documentElement.dataset.motion = $("reduce-motion").checked ? "off" : ""; });
  $("cleanup").addEventListener("change", () => { ui.cleanup = $("cleanup").value; });
  $("direction-setting").addEventListener("change", () => { if ($("direction-setting").value !== ui.direction) swapDirection(); });
  $("clear-history").addEventListener("click", () => { ui.history = []; toast("最近草稿已清除"); });
  $("show-diagnostics").addEventListener("click", () => dialog("本次状态（不含正文）", `<pre>${esc(JSON.stringify({ output: draft.output, route: draft.route, recording: draft.recording, target: draft.target, targetLost: draft.targetLost, units: draft.units.length, submitted: !!draft.snapshot, cloud: ui.cloud }, null, 2))}</pre>`));
  ["cloud-translation", "cloud-cleanup"].forEach(id => $(id).addEventListener("change", () => {
    const kind = id === "cloud-translation" ? "translation" : "cleanup";
    if (!$(id).checked) { ui.cloud[kind] = false; render(); return; }
    $(id).checked = false;
    dialog(`同意云端${kind === "translation" ? "翻译" : "整理"}？`, `<p>开启后，相应文本会发送到你配置的服务商；语音不会上传。本演示不发送任何请求。</p><div class="dlg-actions"><button type="button" class="btn" data-dlg="close">保持本地</button><button type="button" class="btn primary" id="allow-cloud">同意</button></div>`);
    $("allow-cloud").addEventListener("click", () => { ui.cloud[kind] = true; $(id).checked = true; $("dlg").close(); render(); });
  }));
  function renderTerms() {
    $("terms-list").innerHTML = terms.map((term, i) => `<div class="term-row"><span>${esc(term.source)}</span><span aria-hidden="true">→</span><span>${esc(term.target)}</span><button type="button" data-term="${i}" aria-label="删除术语 ${esc(term.source)}">删除</button></div>`).join("");
  }
  $("terms-list").addEventListener("click", event => { const del = event.target.closest("[data-term]"); if (del) { terms.splice(Number(del.dataset.term), 1); renderTerms(); } });
  $("term-form").addEventListener("submit", event => {
    event.preventDefault();
    const source = $("term-source").value.trim(), target = $("term-target").value.trim();
    if (!source || !target || terms.some(term => term.source.toLowerCase() === source.toLowerCase())) { toast("原词不能为空，也不能重复"); return; }
    terms.push({ source, target }); event.target.reset(); renderTerms();
    toast("已添加 · 原型的预设译文不会应用术语表");
  });

  matchMedia("(prefers-color-scheme: dark)").addEventListener("change", () => { if (ui.theme === "auto") applyTheme(); });
  applyTheme(); renderTerms(); autosize(); render();
})();
