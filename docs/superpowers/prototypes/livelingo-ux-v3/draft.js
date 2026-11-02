// Prototype state model. It only simulates the draft rules of the product design; it runs no speech or translation.
(function (root) {
  "use strict";

  // ponytail: naive splitter (abbreviations like "e.g. Redis" split early); the product uses its own sentence segmenter.
  const SENTENCE_BREAK = /\n+|(?<=[。！？!?][”"」』）)]*)(?![”"」』）)])\s*|(?<=[a-z0-9)\]”"']\.)\s+(?=[A-Z"“'(\[])/;
  function splitSentences(text) {
    return text.split(SENTENCE_BREAK).map(part => part?.trim()).filter(Boolean);
  }

  function routeFor(text) {
    const structured = text.split("\n").filter(line => /^\s*(#{1,6}\s|[-*]\s|\d+\.\s|[{}\[\]<]|```)/.test(line)).length >= 3;
    return text.includes("```") || structured || text.length > 400 ? "whole" : "units";
  }

  class Draft {
    constructor(target) { this.seq = 0; this.epoch = 0; this.reset(target); }

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

    newUnit(raw) {
      return { id: ++this.seq, raw, clean: null, translation: null, cleanOwned: false, translationOwned: false, cleanError: "", translationError: "", warning: "" };
    }
    ticketFor(unit) { return { epoch: this.epoch, id: unit.id, translationRevision: this.translationRevision }; }
    ticket(index) { return this.ticketFor(this.units[index]); }
    find(ticket) { return ticket.epoch === this.epoch ? this.units.find(unit => unit.id === ticket.id) : undefined; }
    indexOf(id) { return this.units.findIndex(unit => unit.id === id); }

    append(raw) {
      if (this.snapshot) throw new Error("Start a new draft before adding text.");
      const unit = this.newUnit(raw);
      this.units.push(unit);
      return this.ticketFor(unit);
    }

    // Reconciles the units with the sentences of the source text. Units whose raw text is unchanged keep their results and
    // manual edits; only new sentences return tickets for processing.
    syncSource(sentences, route) {
      if (this.snapshot) throw new Error("Submitted text is frozen.");
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
      return created.map(unit => this.ticketFor(unit));
    }

    restore(units, output, route) {
      this.units = units;
      this.output = output;
      this.route = route;
      this.seq = Math.max(this.seq, ...units.map(unit => unit.id));
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

    text() {
      return this.units.map(unit => unit[this.output] ?? "").join(this.route === "whole" ? "" : "\n");
    }

    // Content readiness is shared by copy, export and insertion; insertion additionally needs a live target and no recording.
    contentReadiness() {
      if (!this.units.length) return { ok: false, reason: "empty" };
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
      this.snapshot = Object.freeze({ ok: true, text: this.text(), action: this.target === null ? "copy" : "insert", target: this.target });
      return this.snapshot;
    }
  }

  const api = { Draft, splitSentences, routeFor };
  if (typeof module !== "undefined") module.exports = api;
  else root.LiveLingoDraft = api;
})(typeof window !== "undefined" ? window : globalThis);
