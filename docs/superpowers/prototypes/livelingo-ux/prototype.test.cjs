const { test } = require("node:test");
const assert = require("node:assert/strict");
const { existsSync } = require("node:fs");
const path = require("node:path");

const available = existsSync(path.join(__dirname, "app.js"));
test("the interrupted prototype has a persistent behavior implementation", () => {
  assert.ok(available, "The prototype behavior file has not been rebuilt.");
});
const Draft = available ? require("./app.js").Draft : null;
const check = (name, fn) => test(name, { skip: !available }, fn);

function ready() {
  const draft = new Draft("agent");
  const ticket = draft.append("这个 PR 先不要合并。");
  draft.complete(ticket, "clean", "这个 PR 先不要合并。");
  draft.complete(ticket, "translation", "Do not merge this PR yet.");
  return { draft, ticket };
}

check("translation is independent of cleanup and selects the actual output language", () => {
  const draft = new Draft("agent");
  const ticket = draft.append("先不要合并。");
  draft.output = "translation";
  draft.complete(ticket, "translation", "Do not merge yet.");
  assert.equal(draft.requestCommit().text, "Do not merge yet.");
});

check("manual cleanup edits are not overwritten by late model results", () => {
  const { draft, ticket } = ready();
  draft.edit(0, "clean", "请先不要合并这个 PR。");
  assert.equal(draft.complete(ticket, "clean", "错误的迟到结果"), false);
  assert.equal(draft.units[0].clean, "请先不要合并这个 PR。");
  assert.equal(draft.units[0].raw, "这个 PR 先不要合并。");
});

check("a sentence can use raw text without reverting other sentences", () => {
  const { draft, ticket } = ready();
  const second = draft.append("嗯，等 CI 跑完。");
  draft.complete(second, "clean", "等 CI 跑完。");
  draft.useRaw(0);
  assert.equal(draft.complete(ticket, "clean", "不要保留原文"), false);
  assert.equal(draft.text(), "这个 PR 先不要合并。\n等 CI 跑完。");
});

check("a rejected cleanup exposes its reason and safely retains the original", () => {
  const draft = new Draft("agent");
  const ticket = draft.append("超时为 30 秒。");
  draft.complete(ticket, "clean", "超时为 30 分钟。", "候选改动了单位");
  assert.equal(draft.units[0].clean, "超时为 30 秒。");
  assert.equal(draft.units[0].warning, "候选改动了单位");
  assert.equal(draft.requestCommit().text, "超时为 30 秒。");
});

check("replacing the source invalidates old jobs and preserves exact layout", () => {
  const { draft, ticket } = ready();
  const raw = '  {\n    "message": "你好",\n    "timeout": 30\n  }\n';
  draft.replaceInput(raw, "whole");
  assert.equal(draft.complete(ticket, "translation", "stale"), false);
  draft.output = "raw";
  assert.equal(draft.route, "whole");
  assert.equal(draft.text(), raw);
});

check("appending more dictation retains existing manual edits and completed translations", () => {
  const { draft } = ready();
  draft.edit(0, "clean", "我手动修改的句子。");
  const second = draft.append("再检查缓存。");
  draft.complete(second, "clean", "再检查缓存。");
  assert.equal(draft.units[0].translation, "Do not merge this PR yet.");
  assert.equal(draft.text(), "我手动修改的句子。\n再检查缓存。");
});

check("switching the target language invalidates only translations", () => {
  const { draft, ticket } = ready();
  draft.invalidateTranslations();
  assert.equal(draft.units[0].clean, "这个 PR 先不要合并。");
  assert.equal(draft.complete(ticket, "translation", "wrong language"), false);
  draft.output = "translation";
  assert.equal(draft.requestCommit().reason, "processing");
});

check("submission is blocked during recording and while selected output is pending", () => {
  const draft = new Draft("agent");
  draft.append("尚未完成的句子。");
  draft.recording = true;
  assert.equal(draft.requestCommit().reason, "recording");
  draft.recording = false;
  assert.equal(draft.requestCommit().reason, "processing");
});

check("a failed translation is not presented as a successful translation", () => {
  const { draft, ticket } = ready();
  draft.invalidateTranslations();
  draft.fail(draft.ticket(0), "translation", "连接超时");
  draft.output = "translation";
  assert.equal(draft.requestCommit().reason, "unavailable");
  assert.equal(draft.units[0].translationError, "连接超时");
  assert.equal(draft.complete(ticket, "translation", "late"), false);
});

check("explicit raw fallback cancels late cleanup and permits submission", () => {
  const draft = new Draft("agent");
  const ticket = draft.append("不要改动接口。");
  draft.useRaw(0);
  assert.equal(draft.complete(ticket, "clean", "改动接口。"), false);
  assert.equal(draft.requestCommit().text, "不要改动接口。");
});

check("target loss blocks insertion; retargeting requires an explicit new destination", () => {
  const { draft } = ready();
  draft.targetLost = true;
  assert.equal(draft.requestCommit().reason, "target-lost");
  draft.retarget("notes");
  assert.equal(draft.requestCommit().target, "notes");
});

check("editing source text does not silently revalidate a lost input target", () => {
  const { draft } = ready();
  draft.targetLost = true;
  draft.replaceInput("新的输入文本。", "units");
  draft.output = "raw";
  assert.equal(draft.targetLost, true);
  assert.equal(draft.requestCommit().reason, "target-lost");
});

check("no input target yields a copy action rather than injecting into another app", () => {
  const { draft } = ready();
  draft.retarget(null);
  assert.equal(draft.requestCommit().action, "copy");
});

check("an immutable submitted snapshot is returned once and late tasks cannot rewrite it", () => {
  const { draft, ticket } = ready();
  const first = draft.requestCommit();
  assert.equal(first.ok, true);
  assert.equal(draft.requestCommit(), first);
  assert.equal(draft.complete(ticket, "clean", "晚到改写"), false);
  assert.equal(first.text, "这个 PR 先不要合并。");
});

check("source-language cleanup edits do not silently rewrite raw-based translations", () => {
  const { draft } = ready();
  draft.edit(0, "clean", "不同的手动文稿。");
  draft.output = "translation";
  assert.equal(draft.text(), "Do not merge this PR yet.");
});

check("empty manual translations are explicitly unavailable for submission", () => {
  const { draft } = ready();
  draft.edit(0, "translation", "");
  draft.output = "translation";
  assert.equal(draft.requestCommit().reason, "unavailable");
});

check("reset invalidates every job from the previous session", () => {
  const { draft, ticket } = ready();
  draft.reset("agent");
  assert.equal(draft.units.length, 0);
  assert.equal(draft.complete(ticket, "clean", "previous session"), false);
  assert.equal(draft.requestCommit().reason, "empty");
});
