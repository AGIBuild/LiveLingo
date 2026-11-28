// Prototype v5 state model: extends unit-level draft invariants with scenario polish modes,
// whole-document structured drafts, per-mode manual ownership/staleness, and FactAnchorGate.
(function (root) {
  "use strict";

  const SENTENCE_BREAK = /\n+|(?<=[。！？!?][”"」』）)]*)(?![”"」』）)])\s*|(?<=[a-z0-9)\]”"']\.)\s+(?=[A-Z"“'(\[])/;
  function splitSentences(text) {
    return text.split(SENTENCE_BREAK).map(part => part?.trim()).filter(Boolean);
  }

  function routeFor(text) {
    const structured = text.split("\n").filter(line => /^\s*(#{1,6}\s|[-*]\s|\d+\.\s|[{}\[\]<]|```)/.test(line)).length >= 3;
    return text.includes("```") || structured || text.length > 400 ? "whole" : "units";
  }

  // Deterministic FactAnchorGate for structured polish modes (AI Prompt, Todo, Report, Custom):
  // allows structural reorganization, headings, bullet compression, while blocking invented/lost numbers,
  // broken inline literals, and prompt-execution hijack (answering instead of polishing into a prompt).
  function stripListOrdinals(text) {
    return String(text ?? "").replace(/^\s*\d+[.)、]\s+/gm, "");
  }

  function extractNumbersWithOffsets(text, ignoreOrdinals) {
    const cleaned = ignoreOrdinals ? stripListOrdinals(text) : String(text ?? "");
    return [...cleaned.matchAll(/\d+(?:\.\d+)?/g)].map(m => ({ value: m[0], index: m.index ?? 0, end: (m.index ?? 0) + m[0].length, source: cleaned }));
  }

  // A spoken number followed shortly by an explicit self-retraction ("设成 3 次……哦不对，关掉自动重试")
  // or preceded immediately by a contrast negation ("不是 30 分钟") may be pruned by a high-quality polisher.
  const RETRACTION_AFTER = /^[^。！？\n]{0,20}(不对|说错了|口误|算了|改成|改为|更正|干脆关掉|取消)/;
  const NEGATED_BEFORE = /(不是|而非|不要设成|别设成|切勿设为)\s*$/;

  function isPrunableSpokenNumber(entry) {
    const after = entry.source.slice(entry.end, entry.end + 28);
    const before = entry.source.slice(Math.max(0, entry.index - 10), entry.index);
    return RETRACTION_AFTER.test(after) || NEGATED_BEFORE.test(before);
  }

  function extractBacktickLiterals(text) {
    return [...String(text ?? "").matchAll(/`([^`\n]+)`/g)].map(m => m[1]);
  }

  const HIJACK_PREAMBLES = [
    /^\s*(好的|当然可以|没问题|明白了)[，,！!：:\s]*(下面|以下|这是|我来|为您|为你)/,
    /^\s*(Sure|Certainly|Here is|Below is)\b[^.\n]*:/i
  ];
  const ROLEPLAY_SLOP = /(你是一位|你是一个|作为一名)[^，。\n]{0,18}(资深|专家|大师|架构师)/;

  function checkFactAnchors(raw, candidate, options = {}) {
    const trimmed = String(candidate ?? "").trim();
    if (!trimmed) return { ok: false, reason: "empty", message: "生成结果为空" };

    if (options.forbidExecution) {
      if (HIJACK_PREAMBLES.some(re => re.test(trimmed))) {
        return { ok: false, reason: "hijack", message: "模型直接回答并执行了请求，而非提炼为 AI 任务指令" };
      }
      if (!String(raw ?? "").includes("```") && /```\w+\s*\n/.test(trimmed)) {
        return { ok: false, reason: "hijack", message: "模型生成了实现代码块，而非提炼指令约束" };
      }
      if (!ROLEPLAY_SLOP.test(String(raw ?? "")) && ROLEPLAY_SLOP.test(trimmed)) {
        return { ok: false, reason: "slop", message: "模型凭空注入了低信噪比角色扮演套话" };
      }
    }

    const rawEntries = extractNumbersWithOffsets(raw, false);
    const rawNums = rawEntries.map(e => e.value);
    const requiredNums = rawEntries.filter(e => !isPrunableSpokenNumber(e)).map(e => e.value);
    const candNums = extractNumbersWithOffsets(trimmed, true).map(e => e.value);
    for (const num of requiredNums) {
      if (!candNums.includes(num)) {
        return { ok: false, reason: "number-lost", detail: num, message: `结构化润色丢失或改动了关键数字「${num}」` };
      }
    }
    for (const num of candNums) {
      if (!rawNums.includes(num)) {
        return { ok: false, reason: "number-invented", detail: num, message: `结构化润色凭空编造了原话没有的数字「${num}」` };
      }
    }

    const rawLiterals = extractBacktickLiterals(raw);
    for (const lit of rawLiterals) {
      if (!trimmed.includes(lit)) {
        return { ok: false, reason: "literal-lost", detail: lit, message: `结构化润色丢失了代码标识符「${lit}」` };
      }
    }

    return { ok: true };
  }

  class Draft {
    constructor(target) {
      this.seq = 0;
      this.epoch = 0;
      this.reset(target);
    }

    reset(target) {
      this.epoch++;
      this.sourceRevision = 0;
      this.translationRevision = 0;
      this.units = [];
      this.output = "clean";
      this.polishMode = "natural";
      this.route = "units";
      this.timeline = false;
      this.target = target;
      this.targetLost = false;
      this.recording = false;
      this.snapshot = null;
      this.docs = new Map();
      this.modeRevisions = new Map();
    }

    newUnit(raw) {
      return {
        id: ++this.seq,
        raw,
        clean: null,
        translation: null,
        cleanOwned: false,
        translationOwned: false,
        cleanError: "",
        translationError: "",
        warning: ""
      };
    }

    usesDocumentPolish() {
      return this.output === "clean" && this.polishMode !== "natural" && !this.recording && !this.timeline;
    }

    setPolishMode(modeId) {
      if (this.snapshot) return false;
      this.output = "clean";
      this.polishMode = modeId || "natural";
      return true;
    }

    bumpSourceRevision() {
      this.sourceRevision++;
      for (const [modeId, doc] of this.docs.entries()) {
        if (doc.owned) {
          doc.stale = true;
        } else {
          this.docs.delete(modeId);
        }
      }
    }

    docEntry(modeId = this.polishMode) {
      return this.docs.get(modeId) ?? null;
    }

    docTicket(modeId = this.polishMode) {
      return {
        epoch: this.epoch,
        modeId,
        sourceRevision: this.sourceRevision,
        modeRevision: this.modeRevisions.get(modeId) ?? 0
      };
    }

    validDocTicket(ticket) {
      return !this.snapshot
        && ticket.epoch === this.epoch
        && ticket.sourceRevision === this.sourceRevision
        && ticket.modeRevision === (this.modeRevisions.get(ticket.modeId) ?? 0);
    }

    completeDoc(ticket, text, warning = "") {
      if (!this.validDocTicket(ticket)) return false;
      const existing = this.docs.get(ticket.modeId);
      if (existing?.owned) return false;
      this.docs.set(ticket.modeId, {
        text,
        owned: false,
        stale: false,
        error: "",
        warning
      });
      return true;
    }

    failDoc(ticket, reason) {
      if (!this.validDocTicket(ticket)) return false;
      const existing = this.docs.get(ticket.modeId);
      if (existing?.owned) return false;
      this.docs.set(ticket.modeId, {
        text: null,
        owned: false,
        stale: false,
        error: reason,
        warning: ""
      });
      return true;
    }

    editDoc(modeId, text) {
      if (this.snapshot) throw new Error("Submitted text is frozen.");
      this.docs.set(modeId, {
        text,
        owned: true,
        stale: false,
        error: "",
        warning: ""
      });
    }

    retryDoc(modeId = this.polishMode) {
      if (this.snapshot) return false;
      this.modeRevisions.set(modeId, (this.modeRevisions.get(modeId) ?? 0) + 1);
      this.docs.delete(modeId);
      return true;
    }

    invalidatePolishMode(modeId) {
      if (this.snapshot) return;
      this.modeRevisions.set(modeId, (this.modeRevisions.get(modeId) ?? 0) + 1);
      const existing = this.docs.get(modeId);
      if (!existing) return;
      if (existing.owned) existing.stale = true;
      else this.docs.delete(modeId);
    }

    ticketFor(unit) { return { epoch: this.epoch, id: unit.id, translationRevision: this.translationRevision }; }
    ticket(index) { return this.ticketFor(this.units[index]); }
    find(ticket) { return ticket.epoch === this.epoch ? this.units.find(unit => unit.id === ticket.id) : undefined; }
    indexOf(id) { return this.units.findIndex(unit => unit.id === id); }

    append(raw) {
      if (this.snapshot) throw new Error("Start a new draft before adding text.");
      const unit = this.newUnit(raw);
      this.units.push(unit);
      this.bumpSourceRevision();
      return this.ticketFor(unit);
    }

    syncSource(sentences, route) {
      if (this.snapshot) throw new Error("Submitted text is frozen.");
      const prevRaw = this.units.map(u => u.raw);
      const unchanged = prevRaw.length === sentences.length && prevRaw.every((r, i) => r === sentences[i]) && this.route === route;
      const pool = new Map();
      for (const unit of this.units) {
        if (!pool.has(unit.raw)) pool.set(unit.raw, []);
        pool.get(unit.raw).push(unit);
      }
      const created = [];
      this.units = sentences.map(raw => {
        const reused = pool.get(raw)?.shift();
        if (reused) return reused;
        const unit = this.newUnit(raw);
        created.push(unit);
        return unit;
      });
      this.route = route;
      if (!unchanged) this.bumpSourceRevision();
      return created.map(unit => this.ticketFor(unit));
    }

    restore(units, output, route, polishMode = "natural", docs = null) {
      this.units = units;
      this.output = output;
      this.polishMode = polishMode || "natural";
      this.route = route;
      this.docs = new Map(docs ? Object.entries(docs) : []);
      this.seq = Math.max(this.seq, 0, ...units.map(unit => unit.id));
    }

    exportDocs() {
      return Object.fromEntries(this.docs.entries());
    }

    complete(ticket, kind, text, warning = "") {
      const unit = this.snapshot ? undefined : this.find(ticket);
      if (!unit || unit[kind + "Owned"]) return false;
      if (kind === "translation" && ticket.translationRevision !== this.translationRevision) return false;
      unit[kind] = warning ? unit.raw : text;
      unit[kind + "Error"] = "";
      if (warning) unit.warning = warning;
      return true;
    }

    fail(ticket, kind, reason) {
      if (!this.complete(ticket, kind, null)) return false;
      this.find(ticket)[kind + "Error"] = reason;
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

    state(unit, kind) {
      if (kind === "raw") return "ready";
      if (unit[kind + "Error"]) return "error";
      if (unit[kind] === null) return "pending";
      return unit[kind].trim() ? "ready" : "empty";
    }

    docState(modeId = this.polishMode) {
      if (!this.units.length) return "empty";
      const doc = this.docs.get(modeId);
      if (!doc) return "pending";
      if (doc.error) return "error";
      if (doc.stale) return "stale";
      if (doc.text === null) return "pending";
      return doc.text.trim() ? "ready" : "unavailable";
    }

    rawText() {
      return this.units.map(unit => unit.raw).join(this.route === "whole" ? "" : "\n");
    }

    text() {
      if (this.usesDocumentPolish()) {
        return this.docs.get(this.polishMode)?.text ?? "";
      }
      return this.units.map(unit => unit[this.output] ?? "").join(this.route === "whole" ? "" : "\n");
    }

    contentReadiness() {
      if (!this.units.length) return { ok: false, reason: "empty" };
      if (this.usesDocumentPolish()) {
        const st = this.docState(this.polishMode);
        if (st === "pending") return { ok: false, reason: "processing" };
        if (st === "stale") return { ok: false, reason: "stale" };
        if (st === "error" || st === "unavailable") return { ok: false, reason: "unavailable" };
        return this.text().trim() ? { ok: true } : { ok: false, reason: "empty" };
      }
      if (this.output !== "raw") {
        const states = this.units.map(unit => this.state(unit, this.output));
        if (states.some(state => state === "error" || state === "empty")) return { ok: false, reason: "unavailable" };
        if (states.includes("pending")) return { ok: false, reason: "processing" };
      }
      return this.text().trim() ? { ok: true } : { ok: false, reason: "empty" };
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
      this.snapshot = Object.freeze({
        ok: true,
        text: this.text(),
        action: this.target === null ? "copy" : "insert",
        target: this.target,
        output: this.output,
        polishMode: this.polishMode
      });
      return this.snapshot;
    }
  }

  const api = { Draft, splitSentences, routeFor, checkFactAnchors };
  if (typeof module !== "undefined") module.exports = api;
  else root.LiveLingoDraft = api;
})(typeof window !== "undefined" ? window : globalThis);
