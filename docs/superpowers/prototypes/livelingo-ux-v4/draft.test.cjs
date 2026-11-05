const { test } = require("node:test");
const assert = require("node:assert/strict");
const { Draft, splitSentences, routeFor } = require("./draft.js");

function ready() {
  const draft = new Draft("agent");
  const ticket = draft.append("这个 PR 先不要合并。");
  draft.complete(ticket, "clean", "这个 PR 先不要合并。");
  draft.complete(ticket, "translation", "Do not merge this PR yet.");
  return { draft, ticket };
}

test("translation is independent of cleanup and selects the actual output language", () => {
  const draft = new Draft("agent");
  const ticket = draft.append("先不要合并。");
  draft.output = "translation";
  draft.complete(ticket, "translation", "Do not merge yet.");
  assert.equal(draft.requestCommit().text, "Do not merge yet.");
});

test("manual cleanup edits are not overwritten by late model results", () => {
  const { draft, ticket } = ready();
  draft.edit(0, "clean", "请先不要合并这个 PR。");
  assert.equal(draft.complete(ticket, "clean", "错误的迟到结果"), false);
  assert.equal(draft.units[0].clean, "请先不要合并这个 PR。");
  assert.equal(draft.units[0].raw, "这个 PR 先不要合并。");
});

test("a sentence can use raw text without reverting other sentences", () => {
  const { draft, ticket } = ready();
  const second = draft.append("嗯，等 CI 跑完。");
  draft.complete(second, "clean", "等 CI 跑完。");
  draft.useRaw(0);
  assert.equal(draft.complete(ticket, "clean", "不要保留原文"), false);
  assert.equal(draft.text(), "这个 PR 先不要合并。\n等 CI 跑完。");
});

test("a rejected cleanup exposes its reason and safely retains the original", () => {
  const draft = new Draft("agent");
  const ticket = draft.append("超时为 30 秒。");
  draft.complete(ticket, "clean", "超时为 30 分钟。", "候选改动了单位");
  assert.equal(draft.units[0].clean, "超时为 30 秒。");
  assert.equal(draft.units[0].warning, "候选改动了单位");
  assert.equal(draft.requestCommit().text, "超时为 30 秒。");
});

test("switching the target language invalidates only translations", () => {
  const { draft, ticket } = ready();
  draft.invalidateTranslations();
  assert.equal(draft.units[0].clean, "这个 PR 先不要合并。");
  assert.equal(draft.complete(ticket, "translation", "wrong language"), false);
  draft.output = "translation";
  assert.equal(draft.requestCommit().reason, "processing");
});

test("submission is blocked during recording and while selected output is pending", () => {
  const draft = new Draft("agent");
  draft.append("尚未完成的句子。");
  draft.recording = true;
  assert.equal(draft.requestCommit().reason, "recording");
  draft.recording = false;
  assert.equal(draft.requestCommit().reason, "processing");
});

test("a failed translation is not presented as a successful translation", () => {
  const { draft, ticket } = ready();
  draft.invalidateTranslations();
  draft.fail(draft.ticket(0), "translation", "连接超时");
  draft.output = "translation";
  assert.equal(draft.requestCommit().reason, "unavailable");
  assert.equal(draft.units[0].translationError, "连接超时");
  assert.equal(draft.complete(ticket, "translation", "late"), false);
});

test("explicit raw fallback cancels late cleanup and permits submission", () => {
  const draft = new Draft("agent");
  const ticket = draft.append("不要改动接口。");
  draft.useRaw(0);
  assert.equal(draft.complete(ticket, "clean", "改动接口。"), false);
  assert.equal(draft.requestCommit().text, "不要改动接口。");
});

test("target loss blocks insertion but still allows copying; rebinding is explicit", () => {
  const { draft } = ready();
  draft.targetLost = true;
  assert.equal(draft.requestCommit().reason, "target-lost");
  assert.equal(draft.contentReadiness().ok, true);
  draft.retarget("notes");
  assert.equal(draft.requestCommit().target, "notes");
});

test("no input target yields a copy action rather than injecting into another app", () => {
  const { draft } = ready();
  draft.retarget(null);
  assert.equal(draft.requestCommit().action, "copy");
});

test("an immutable submitted snapshot is returned once and late tasks cannot rewrite it", () => {
  const { draft, ticket } = ready();
  const first = draft.requestCommit();
  assert.equal(first.ok, true);
  assert.equal(draft.requestCommit(), first);
  assert.equal(draft.complete(ticket, "clean", "晚到改写"), false);
  assert.equal(first.text, "这个 PR 先不要合并。");
});

test("empty manual translations are explicitly unavailable for submission", () => {
  const { draft } = ready();
  draft.edit(0, "translation", "");
  draft.output = "translation";
  assert.equal(draft.requestCommit().reason, "unavailable");
});

test("reset invalidates every job from the previous session", () => {
  const { draft, ticket } = ready();
  draft.reset("agent");
  assert.equal(draft.units.length, 0);
  assert.equal(draft.complete(ticket, "clean", "previous session"), false);
  assert.equal(draft.requestCommit().reason, "empty");
});

test("source reconcile keeps manual edits and finished translations of unchanged sentences", () => {
  const { draft } = ready();
  draft.edit(0, "clean", "我手动修改的句子。");
  const created = draft.syncSource(["这个 PR 先不要合并。", "再检查缓存。"], "units");
  assert.equal(created.length, 1);
  assert.equal(draft.units[0].clean, "我手动修改的句子。");
  assert.equal(draft.units[0].translation, "Do not merge this PR yet.");
  assert.equal(draft.units[1].raw, "再检查缓存。");
});

test("a late result for a removed sentence cannot land on another sentence that reuses its index", () => {
  const draft = new Draft("agent");
  const [first] = draft.syncSource(["旧句子。"], "units");
  draft.syncSource(["新句子。"], "units");
  assert.equal(draft.complete(first, "clean", "旧结果"), false);
  assert.equal(draft.units[0].clean, null);
});

test("duplicate sentences each keep their own unit", () => {
  const draft = new Draft("agent");
  draft.syncSource(["好的。", "好的。"], "units");
  draft.edit(1, "clean", "好。");
  draft.syncSource(["好的。", "好的。", "继续。"], "units");
  assert.equal(draft.units.length, 3);
  assert.equal(draft.units[0].clean, null);
  assert.equal(draft.units[1].raw, "好的。");
});

test("replacing the whole source with a different document invalidates old jobs and keeps exact layout", () => {
  const { draft, ticket } = ready();
  const raw = '  {\n    "message": "你好",\n    "timeout": 30\n  }\n';
  draft.syncSource([raw], "whole");
  assert.equal(draft.complete(ticket, "translation", "stale"), false);
  draft.output = "raw";
  assert.equal(draft.route, "whole");
  assert.equal(draft.text(), raw);
});

test("editing source text never revalidates a lost input target", () => {
  const { draft } = ready();
  draft.targetLost = true;
  draft.syncSource(["新的输入文本。"], "units");
  draft.output = "raw";
  assert.equal(draft.requestCommit().reason, "target-lost");
});

test("restoring a draft keeps unit ids unique for later jobs", () => {
  const { draft } = ready();
  const saved = structuredClone(draft.units);
  draft.reset("agent");
  draft.restore(saved, "translation", "units");
  const next = draft.append("新追加。");
  assert.equal(new Set(draft.units.map(unit => unit.id)).size, 2);
  assert.equal(draft.complete(next, "clean", "新追加。"), true);
  assert.equal(draft.output, "translation");
});

test("splitSentences handles Chinese, English, quotes, decimals and newlines", () => {
  assert.deepEqual(splitSentences("先不要合并。等 CI 跑完！好吗？"), ["先不要合并。", "等 CI 跑完！", "好吗？"]);
  assert.deepEqual(splitSentences("他说：“好。”然后走了。"), ["他说：“好。”", "然后走了。"]);
  assert.deepEqual(splitSentences("Set the timeout to 3.5 seconds. Then restart."), ["Set the timeout to 3.5 seconds.", "Then restart."]);
  assert.deepEqual(splitSentences("第一行\n\n第二行\n"), ["第一行", "第二行"]);
  assert.deepEqual(splitSentences("   "), []);
});

test("routeFor chooses whole-text for code, structured or long text, and units for short speech", () => {
  assert.equal(routeFor("这个 PR 先不要合并。"), "units");
  assert.equal(routeFor("看这个：\n```js\nconst a = 1;\n```"), "whole");
  assert.equal(routeFor("# 标题\n- 一\n- 二\n- 三"), "whole");
  assert.equal(routeFor("字".repeat(401)), "whole");
  assert.equal(routeFor("第一句。\n第二句。\n第三句。"), "units");
});
