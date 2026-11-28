const { test } = require("node:test");
const assert = require("node:assert/strict");
const { Draft, splitSentences, routeFor, checkFactAnchors } = require("./draft.js");

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

// --- v5 multi-scenario polish & FactAnchorGate tests ---

test("switching polish modes preserves raw text, natural cleanup, and per-mode cached outputs", () => {
  const { draft } = ready();
  draft.setPolishMode("prompt");
  assert.equal(draft.usesDocumentPolish(), true);
  assert.equal(draft.requestCommit().reason, "processing");

  const promptTicket = draft.docTicket("prompt");
  draft.completeDoc(promptTicket, "### 目标\n暂缓合并当前 PR。");
  assert.equal(draft.text(), "### 目标\n暂缓合并当前 PR。");

  draft.setPolishMode("todo");
  assert.equal(draft.docState("todo"), "pending");
  const todoTicket = draft.docTicket("todo");
  draft.completeDoc(todoTicket, "- [ ] 等待 CI 完成后再合并 PR");
  assert.equal(draft.text(), "- [ ] 等待 CI 完成后再合并 PR");

  // Switching back to prompt or natural uses cached results immediately without losing either.
  draft.setPolishMode("prompt");
  assert.equal(draft.contentReadiness().ok, true);
  assert.equal(draft.text(), "### 目标\n暂缓合并当前 PR。");

  draft.setPolishMode("natural");
  assert.equal(draft.usesDocumentPolish(), false);
  assert.equal(draft.text(), "这个 PR 先不要合并。");
});

test("manual edit of a structured polish document is protected, marked stale on new speech, and recoverable via retry", () => {
  const { draft } = ready();
  draft.setPolishMode("report");
  const oldTicket = draft.docTicket("report");
  draft.editDoc("report", "### 进展\n人工确认过的汇报稿。");
  assert.equal(draft.completeDoc(oldTicket, "迟到的模型结果"), false);
  assert.equal(draft.text(), "### 进展\n人工确认过的汇报稿。");

  // Appending new speech marks the owned document stale instead of silently overwriting or writing outdated text.
  draft.append("再补一句超时改成 30 秒。");
  assert.equal(draft.docState("report"), "stale");
  assert.equal(draft.requestCommit().reason, "stale");
  assert.equal(draft.docEntry("report").text, "### 进展\n人工确认过的汇报稿。");

  // Explicit retry clears stale state and permits fresh generation.
  draft.retryDoc("report");
  assert.equal(draft.docState("report"), "pending");
  const freshTicket = draft.docTicket("report");
  draft.completeDoc(freshTicket, "### 进展\n包含 30 秒超时的最新汇报稿。");
  assert.equal(draft.requestCommit().text, "### 进展\n包含 30 秒超时的最新汇报稿。");
});

test("FactAnchorGate allows structural headings/lists and ordinals, but blocks hijack, invented/lost numbers, and lost literals", () => {
  const raw = "嗯，帮我看下 `CacheService` 的连接池，超时改成 30 秒，不要动公共 API。";

  // Valid AI prompt rewrite with numbered list 1. 2. (ordinals must not count as invented numbers)
  const validPrompt = "### 目标\n排查并调整 `CacheService` 连接池超时配置。\n\n### 约束与验收\n1. 超时设为 30 秒\n2. 保持公共 API 不变";
  assert.equal(checkFactAnchors(raw, validPrompt, { forbidExecution: true }).ok, true);

  // Hijack: model answers the prompt or writes implementation code instead of polishing the prompt
  const hijacked = "好的，这是为你修改后的 `CacheService` 代码：\n```csharp\npublic int Timeout = 30;\n```";
  assert.equal(checkFactAnchors(raw, hijacked, { forbidExecution: true }).reason, "hijack");

  // Number changed from 30 to 60
  const wrongNum = "- [ ] 将 `CacheService` 超时调整为 60 秒";
  assert.equal(checkFactAnchors(raw, wrongNum).reason, "number-lost");

  // Invented deadline number ("周五前" ok if words, "18 点前" has invented number 18)
  const inventedNum = "- [ ] 18 点前将 `CacheService` 超时改为 30 秒";
  assert.equal(checkFactAnchors(raw, inventedNum).reason, "number-invented");

  // Lost backtick code identifier
  const lostLiteral = "- [ ] 将缓存服务连接池超时改为 30 秒";
  assert.equal(checkFactAnchors(raw, lostLiteral).reason, "literal-lost");

  // Self-retracted spoken numbers ("设成 3 次，哦不对") and negated contrast ("不是 15 分钟") can be pruned cleanly
  const spokenRetraction = "先把重试设成 3 次，哦不对，网关层有重试了，客户端关掉重试，只留 401 刷新，超时设为 30 秒，不是 15 分钟。";
  const prunedPrompt = "### 任务目标\n关闭客户端自动重试，仅保留 `401` 刷新逻辑，并将超时设为 30 秒。";
  assert.equal(checkFactAnchors(spokenRetraction, prunedPrompt, { forbidExecution: true }).ok, true);
  // Losing the active number 401 still fails
  const lost401 = "### 任务目标\n关闭客户端自动重试，并将超时设为 30 秒。";
  assert.equal(checkFactAnchors(spokenRetraction, lost401, { forbidExecution: true }).reason, "number-lost");

  // Unrequested roleplay filler slop ("你是一位资深架构师…") is blocked
  const roleplaySlop = "你是一位资深系统架构师，请排查 `CacheService` 超时 30 秒配置。";
  assert.equal(checkFactAnchors(raw, roleplaySlop, { forbidExecution: true }).reason, "slop");
});
