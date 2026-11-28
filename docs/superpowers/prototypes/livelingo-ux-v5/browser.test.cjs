const assert = require("node:assert/strict");
const { pathToFileURL } = require("node:url");
const path = require("node:path");
const { chromium } = require("playwright");

const root = __dirname;
const results = [];
async function check(name, fn) {
  await fn();
  results.push(name);
  console.log("PASS", name);
}

// Visible, at-rest interactive controls and sub-12px text inside a root element.
const measure = (page, selector) => page.evaluate(sel => {
  const scope = document.querySelector(sel);
  const visible = el => {
    const box = el.getBoundingClientRect();
    if (!box.width || !box.height) return false;
    for (let node = el; node && node !== document.body; node = node.parentElement) {
      const style = getComputedStyle(node);
      if (style.display === "none" || style.visibility === "hidden" || style.opacity === "0") return false;
    }
    return true;
  };
  const controls = [...scope.querySelectorAll("button, input, select, textarea, summary, a[href]")].filter(visible);
  const small = [];
  const walker = document.createTreeWalker(scope, NodeFilter.SHOW_TEXT);
  for (let node; (node = walker.nextNode());) {
    if (!node.textContent.trim() || !visible(node.parentElement)) continue;
    const size = parseFloat(getComputedStyle(node.parentElement).fontSize);
    if (size < 12) small.push(`${size}px "${node.textContent.trim().slice(0, 20)}"`);
  }
  const sizes = new Set();
  scope.querySelectorAll("*").forEach(el => {
    if (visible(el) && [...el.childNodes].some(n => n.nodeType === 3 && n.textContent.trim())) {
      sizes.add(getComputedStyle(el).fontSize);
    }
  });
  return { controls: controls.length, small, fontSizes: sizes.size, height: Math.round(scope.getBoundingClientRect().height) };
}, selector);

async function shot(page, file, selector) {
  await page.mouse.move(4, 4);
  await page.waitForFunction(() => !document.getAnimations().some(a => a.effect?.target?.id === "composer"));
  await page.evaluate(() => {
    document.querySelector("#toast").hidden = true;
    document.querySelector("#target-text").value = "";
  });
  const target = selector ? page.locator(selector) : page;
  if (selector) await page.evaluate(() => { document.querySelector(".top").style.position = "static"; });
  await target.screenshot({ path: path.join(root, file) });
  if (selector) await page.evaluate(() => { document.querySelector(".top").style.position = ""; });
}

const luminance = ([r, g, b]) => {
  const f = c => { c /= 255; return c <= .03928 ? c / 12.92 : ((c + .055) / 1.055) ** 2.4; };
  return .2126 * f(r) + .7152 * f(g) + .0722 * f(b);
};
const ratio = (a, b) => {
  const [hi, lo] = [luminance(a), luminance(b)].sort((x, y) => y - x);
  return (hi + .05) / (lo + .05);
};

(async () => {
  const browser = await chromium.launch({ headless: true });
  try {
    const context = await browser.newContext({ viewport: { width: 1480, height: 1000 }, permissions: ["clipboard-read", "clipboard-write"] });
    const page = await context.newPage();
    const errors = [], network = [];
    page.on("pageerror", error => errors.push(error.message));
    page.on("request", request => { if (/^https?:/.test(request.url())) network.push(request.url()); });
    await page.goto(pathToFileURL(path.join(root, "index.html")).href);
    const composer = page.locator("#composer");
    const metrics = {};

    await check("loads offline with the composer ready, Cursor smart-defaulting to AI Task Spec, and no script errors", async () => {
      assert.equal(await composer.isVisible(), true);
      assert.deepEqual(errors, []);
      assert.deepEqual(network, []);
      assert.match(await page.locator("#out-pill").innerText(), /润色 · AI 任务指令/);
      await page.waitForFunction(() => getComputedStyle(document.querySelector("#composer")).opacity === "1" && !document.getAnimations().some(a => a.effect?.target?.id === "composer"));
      metrics.idle = await measure(page, "#composer");
      await shot(page, "overview.png");
    });

    await check("v5: AI Task Spec hoists inverted goal, prunes retracted spoken branch, and adapts density on short steering", async () => {
      await page.locator('.scenario[data-scenario="compare"]').click();
      await page.waitForFunction(() => document.querySelector("#doc-view .doc-text")?.textContent.includes("### 任务目标"));
      const delegatedText = await page.locator("#doc-view .doc-text").innerText();
      assert.match(delegatedText, /AuthInterceptor/);
      assert.match(delegatedText, /401/);
      assert.match(delegatedText, /30 秒/);
      assert.doesNotMatch(delegatedText, /3 次/, "retracted spoken retry count must be pruned");
      assert.match(await page.locator("#out-pill").innerText(), /润色 · AI 任务指令/);
      assert.equal(await page.locator("#gate-chip").innerText(), "事实锚点已校验");
      await shot(page, "polish-prompt.png", "#composer");

      // Short 2-sentence mid-turn steering uses adaptive compact prose without bloated ### headings
      await page.locator('.scenario[data-scenario="ai-steer"]').click();
      await page.waitForFunction(() => document.querySelector("#doc-view .doc-text")?.textContent.includes("公共 API 签名"));
      const steerText = await page.locator("#doc-view .doc-text").innerText();
      assert.doesNotMatch(steerText, /###/, "short steering must not force empty Markdown headings");
      await shot(page, "polish-steer.png", "#composer");

      // Restore complex delegation and capture unified polish menu screenshot
      await page.locator('.scenario[data-scenario="compare"]').click();
      await page.waitForFunction(() => document.querySelector("#doc-view .doc-text")?.textContent.includes("### 任务目标"));
      await page.locator("#out-pill").click();
      await page.waitForFunction(() => {
        const m = document.querySelector("#menu-output");
        return m.matches(":popover-open") && getComputedStyle(m).opacity === "1" && !document.getAnimations().some(a => a.effect?.target === m);
      });
      const presetTitles = await page.locator("#menu-output [data-polish] .menu-item-title").allInnerTexts();
      assert.equal(presetTitles.length, 5);
      assert.match(presetTitles[0], /^AI 任务指令/);
      assert.match(presetTitles[1], /^口语顺句/);
      assert.match(presetTitles[2], /^得体书面/);
      assert.match(presetTitles[3], /^待办与派单/);
      assert.match(presetTitles[4], /^汇报与纪要/);
      const frame = await composer.boundingBox();
      const menuBox = await page.locator("#menu-output").boundingBox();
      const topY = Math.max(0, Math.min(frame.y, menuBox.y) - 16);
      const botY = Math.max(frame.y + frame.height, menuBox.y + menuBox.height) + 16;
      await page.screenshot({
        path: path.join(root, "polish-menu.png"),
        clip: { x: frame.x - 12, y: topY, width: frame.width + 24, height: botY - topY }
      });
      await page.keyboard.press("Escape");
      await page.waitForFunction(() => !document.querySelector("#menu-output").matches(":popover-open"));
    });

    await check("v5: same spoken draft transforms across AI Task, Todo, Report, Formal, and Natural without losing manual edits", async () => {
      // Switch to Todo via menu
      await page.locator("#out-pill").click();
      await page.locator('#menu-output [data-polish="todo"]').click();
      await page.waitForFunction(() => document.querySelector("#doc-view .doc-text")?.textContent.includes("- [ ]"));
      assert.match(await page.locator("#doc-view .doc-text").innerText(), /30 秒/);
      assert.match(await page.locator("#doc-view .doc-text").innerText(), /负责人：小王/);
      await shot(page, "polish-todo.png", "#composer");

      // Switch to Report
      await page.locator("#out-pill").click();
      await page.locator('#menu-output [data-polish="report"]').click();
      await page.waitForFunction(() => document.querySelector("#doc-view .doc-text")?.textContent.includes("### 核心结论"));
      assert.match(await page.locator("#doc-view .doc-text").innerText(), /### 下一步行动/);
      await shot(page, "polish-report.png", "#composer");

      // Manually edit the Report document, switch to Formal and back, and verify manual edit is preserved
      await page.locator(".doc-card").hover();
      await page.locator('.doc-tools [data-row="edit"]').click();
      await page.locator("#edit-doc").fill("### 核心结论\n人工确认过的架构周报版本（保留 401 与超时 30 秒）。");
      await page.keyboard.press("Meta+Enter");
      assert.match(await page.locator("#doc-view").innerText(), /人工确认过的架构周报版本/);

      // Switch to Formal
      await page.locator("#out-pill").click();
      await page.locator('#menu-output [data-polish="formal"]').click();
      await page.waitForFunction(() => document.querySelector("#doc-view .doc-text")?.textContent.includes("401 状态码刷新 Token"));

      // Switch back to Report -> manual edit still intact!
      await page.locator("#out-pill").click();
      await page.locator('#menu-output [data-polish="report"]').click();
      assert.match(await page.locator("#doc-view .doc-text").innerText(), /人工确认过的架构周报版本/);

      // Long 5-sentence structured output auto-collapses source to a 1-line summary; expanding reveals editable #src
      assert.equal(await page.locator("#src-collapsed").isVisible(), true);
      assert.equal(await page.locator("#src-toggle").getAttribute("aria-expanded"), "false");
      await page.locator("#src-toggle").click();
      assert.equal(await page.locator("#src-toggle").getAttribute("aria-expanded"), "true");
      assert.equal(await page.locator("#src").isVisible(), true);

      // Copy button gives direct check-icon confirmation without disabling repeat copies
      await page.locator("#copy").click();
      await page.waitForFunction(() => document.querySelector("#copy").hasAttribute("data-copied"));
      assert.equal(await page.locator("#copy-ic").getAttribute("href"), "#i-check");
      assert.equal(await page.locator("#copy").isDisabled(), false);

      // Appending new source text marks manual Report stale and blocks silent write until regenerated
      const oldSrc = await page.locator("#src").inputValue();
      await page.locator("#src").fill(oldSrc + "\n再补一句监控告警。");
      await page.waitForFunction(() => document.querySelector("#gate-chip")?.textContent === "待重新确认");
      assert.equal(await page.locator("#commit").isDisabled(), true);
      await page.locator('.doc-card [data-row="retry"]').click();
      await page.waitForFunction(() => document.querySelector("#gate-chip")?.textContent === "事实锚点已校验");
      assert.equal(await page.locator("#commit").isEnabled(), true);
      await page.locator("#src-toggle").click();
      assert.equal(await page.locator("#src-collapsed").isVisible(), true);
      await page.mouse.move(4, 4);
      await page.waitForFunction(() => getComputedStyle(document.querySelector(".doc-tools")).opacity === "0");
      metrics.reviewDoc = await measure(page, "#composer");
    });

    await check("v5: FactAnchorGate intercepts execution hijack, roleplay slop, and tampered numbers with explicit recovery", async () => {
      await page.locator("#edge").selectOption("prompt-hijack");
      await page.waitForFunction(() => document.querySelector(".doc-card.bad"));
      assert.match(await page.locator(".doc-card.bad").innerText(), /直接回答并执行|AI 任务指令/);
      assert.equal(await page.locator("#gate-chip").innerText(), "事实锚点已拦截");
      assert.equal(await page.locator("#commit").isDisabled(), true);
      await shot(page, "polish-gate-hijack.png", "#composer");

      // Retry recovers cleanly
      await page.locator('.doc-card [data-row="retry"]').click();
      await page.waitForFunction(() => document.querySelector("#doc-view .doc-text")?.textContent.includes("### 任务目标"));
      assert.equal(await page.locator("#commit").isEnabled(), true);

      // Roleplay slop ("你是一位资深架构师…") is also caught
      await page.locator("#edge").selectOption("prompt-slop");
      await page.waitForFunction(() => document.querySelector(".doc-card.bad"));
      assert.match(await page.locator(".doc-card.bad").innerText(), /角色扮演套话/);
      assert.equal(await page.locator("#commit").isDisabled(), true);

      // Tampered number in Todo mode is also caught
      await page.locator("#edge").selectOption("fact-anchor");
      await page.waitForFunction(() => document.querySelector(".doc-card.bad"));
      assert.match(await page.locator(".doc-card.bad").innerText(), /数字「30」/);
      assert.equal(await page.locator("#commit").isDisabled(), true);
    });

    await check("v5: per-app memory remembers Cursor -> AI Task and Linear -> Todo, and allows one-click restore", async () => {
      await page.locator('.scenario[data-scenario="compare"]').click();
      await page.waitForFunction(() => document.querySelector("#doc-view .doc-text"));
      await page.locator('.scenario[data-scenario="todo"]').click();
      await page.waitForFunction(() => document.querySelector("#doc-view .doc-text")?.textContent.includes("- [ ]"));

      // Clear draft in Linear -> empty composer remembers Todo for Linear
      await page.locator("#more").click();
      await page.locator('#menu-more [data-act="new"]').click();
      assert.match(await page.locator("#out-pill").innerText(), /润色 · 待办与派单/);
      assert.match(await page.locator("#remembered").innerText(), /已沿用「Linear」上次的选择：润色 · 待办与派单/);

      // Switch target to Cursor -> automatically switches to Cursor's remembered AI Task Spec
      await page.locator("#dest").click();
      await page.locator('#menu-target [data-target="agent"]').click();
      assert.match(await page.locator("#out-pill").innerText(), /润色 · AI 任务指令/);
      assert.match(await page.locator("#remembered").innerText(), /已沿用「Cursor」上次的选择：润色 · AI 任务指令/);

      // Restore defaults clears explicit memory back to global default 口语顺句
      await page.locator("#restore-defaults").click();
      assert.match(await page.locator("#out-pill").innerText(), /口语顺句/);
      assert.equal(await page.locator("#remembered").isHidden(), true);
    });

    await check("v5: settings supports presets, starter chips, model quality hints, and direct per-app mode assignment", async () => {
      await page.locator('[data-page="settings"]').click();
      await page.locator('[data-sec="voice"]').click();
      assert.equal(await page.locator("[data-preset-card]").count(), 5);
      await shot(page, "polish-presets.png", "#page-settings");

      // Add a custom preset: test validation + one-click starter template chip
      await page.locator("#add-preset").click();
      await page.locator("#pr-name").fill("AI 任务指令");
      await page.locator("#pr-prompt").fill("短");
      await page.locator('#preset-form button[type="submit"]').click();
      assert.match(await page.locator("#pr-error").innerText(), /同名/);
      await page.locator('[data-starter="0"]').click();
      assert.equal(await page.locator("#pr-name").inputValue(), "Bug 缺陷与复现");
      assert.equal(await page.locator("#pr-error").isHidden(), true);
      await shot(page, "polish-preset-form.png", "#dlg");
      await page.locator('#preset-form button[type="submit"]').click();
      assert.equal(await page.locator("[data-preset-card]").count(), 6);

      // Model quality tier hint appears honestly when 2B is picked, and clears on 9B/4B
      await page.locator('[data-sec="models"]').click();
      await page.locator("#cleaner-model").selectOption("Qwen3.5 2B");
      assert.equal(await page.locator("#cleaner-quality-hint").isVisible(), true);
      await page.locator("#cleaner-model").selectOption("Qwen3.5 4B");
      assert.equal(await page.locator("#cleaner-quality-hint").isHidden(), true);

      // Pre-assign an app's polish mode directly in Settings -> General -> Apps table, and test arrow-key tablist nav
      await page.locator("#tab-voice").focus();
      await page.keyboard.press("ArrowUp");
      assert.equal(await page.locator("#tab-general").getAttribute("aria-selected"), "true");
      await page.locator('[data-app-mode="report-doc"]').selectOption("clean:report");
      await shot(page, "settings-apps.png", "#page-settings");

      // Use the new custom preset in Composer
      await page.locator('[data-page="play"]').click();
      await page.locator("#src").fill("Redis 的超时设置为 30 秒，不是 30 分钟。\n保持 `CacheService` 的 API 不变，只修改错误处理。");
      await page.locator("#out-pill").click();
      await page.locator('#menu-output [data-polish="custom1"]').click();
      await page.waitForFunction(() => document.querySelector("#doc-view .doc-text")?.textContent.includes("### Bug 缺陷与复现"));
      assert.match(await page.locator("#out-pill").innerText(), /润色 · Bug 缺陷与复现/);

      // Delete custom preset: destructive dialog must autofocus the safe '保留' action first
      await page.locator('[data-page="settings"]').click();
      await page.locator('[data-sec="voice"]').click();
      await page.locator('[data-preset-del="custom1"]').click();
      const focusedSafe = await page.evaluate(() => document.activeElement?.textContent?.trim());
      assert.equal(focusedSafe, "保留", "destructive confirmation must autofocus the safe action");
      await page.locator("#delete-preset").click();
      assert.equal(await page.locator("[data-preset-card]").count(), 5);
      await page.locator('[data-page="play"]').click();
      assert.match(await page.locator("#out-pill").innerText(), /口语顺句/);
    });

    await check("tap on the microphone is hands-free; translation is visible before recording stops; commit is blocked", async () => {
      await page.locator('.scenario[data-scenario="translate"]').click();
      await page.waitForFunction(() => document.querySelector(".row-text")?.textContent.includes("Do not merge this PR"));
      assert.match(await page.locator("#status").innerText(), /正在听/);
      assert.equal(await page.locator("#commit").isDisabled(), true);
      assert.equal(await page.locator("#copy").isDisabled(), true);
      metrics.live = await measure(page, "#composer");
      await shot(page, "translation-live.png");
      await page.locator("#mic").click();
      assert.equal(await page.locator("#mic").getAttribute("class").then(c => c.includes("recording")), false);
    });

    await check("Enter writes the selected English output into the original input without sending", async () => {
      await page.locator("#src").focus();
      await page.keyboard.press("Enter");
      await page.waitForFunction(() => document.querySelector("#target-text").value.includes("Do not merge this PR"));
      assert.equal(await composer.isHidden(), true);
      assert.match(await page.locator("#toast").innerText(), /未发送/);
    });

    await check("press-and-hold records only while held; a quick tap keeps recording hands-free", async () => {
      await page.locator("#summon").click();
      const box = await page.locator("#mic").boundingBox();
      await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2);
      await page.mouse.down();
      await page.waitForTimeout(600);
      assert.match(await page.locator("#status").innerText(), /按住说话/);
      await page.mouse.up();
      assert.doesNotMatch(await page.locator("#status").innerText(), /正在听|按住说话/);
      await page.locator("#mic").click();
      await page.waitForTimeout(500);
      assert.match(await page.locator("#status").innerText(), /正在听/);
      await page.locator("#mic").click();
    });

    await check("typing flows into reviewed output; Enter during processing queues and writes automatically", async () => {
      await page.locator("#more").click();
      await page.locator(`#menu-more [data-act="new"]`).click();
      await page.locator("#target-text").fill("");
      await page.locator("#src").focus();
      await page.keyboard.type("这个 PR 先不要合并，等 CI 跑完再说。");
      await page.keyboard.press("Enter");
      assert.match(await page.locator("#commit").innerText(), /完成后写入/);
      await page.keyboard.press("Enter");
      assert.match(await page.locator("#commit").innerText(), /完成后写入/, "a second Enter must not cancel the queue");
      await page.waitForFunction(() => document.querySelector("#target-text").value.includes("这个 PR 先不要合并"));
      assert.equal(await composer.isHidden(), true);
    });

    await check("text outside the demo corpus has no fake translation and offers a way out", async () => {
      await page.locator("#summon").click();
      await page.locator("#more").click();
      await page.locator(`#menu-more [data-act="new"]`).click();
      await page.locator("#src").fill("随便写点别的内容。");
      await page.locator("#out-pill").click();
      await page.locator('[data-out="translation"]').click();
      await page.waitForFunction(() => document.querySelector(".row-error"));
      assert.match(await page.locator(".row-error").innerText(), /未接入模型/);
      assert.equal(await page.locator("#commit").isDisabled(), true);
      await page.locator("#out-pill").click();
      await page.locator('[data-out="raw"]').click();
      assert.equal(await page.locator("#commit").isEnabled(), true);
    });

    await check("sentence tools appear on hover; edits and 'use original' are kept as the draft grows", async () => {
      await page.locator("#more").click();
      await page.locator(`#menu-more [data-act="new"]`).click();
      await page.locator("#out-pill").click();
      await page.locator('#menu-output [data-polish="natural"]').click();
      await page.locator("#src").fill("嗯，这个 PR 先不要合并，等 CI 跑完再说。");
      await page.waitForFunction(() => document.querySelector(".row-text"));
      const unit = page.locator(".unit").first();
      assert.equal(await unit.locator(".row-tools").evaluate(el => getComputedStyle(el).opacity), "0");
      await unit.hover();
      await page.waitForFunction(() => getComputedStyle(document.querySelector(".row-tools")).opacity === "1");
      await unit.locator('[data-row="raw"]').click();
      assert.match(await page.locator(".row-text").first().innerText(), /^嗯，/);
      await page.locator(".unit").first().hover();
      await page.locator(".unit").first().locator('[data-row="edit"]').click();
      await page.locator(".edit").fill("请先不要合并这个 PR。");
      await page.keyboard.press("Enter");
      assert.match(await page.locator(".unit").first().innerText(), /已手动修改/);
      await page.locator("#src").fill("嗯，这个 PR 先不要合并，等 CI 跑完再说。\nRedis 的超时设置为 30 秒，不是 30 分钟。");
      await page.waitForFunction(() => document.querySelectorAll(".unit .row-text").length === 2);
      assert.equal(await page.locator(".unit .row-text").first().innerText(), "请先不要合并这个 PR。");
    });

    await check("a rejected cleanup shows its reason and keeps the original wording", async () => {
      await page.locator("#edge").selectOption("gate");
      await page.waitForFunction(() => document.querySelector(".row-note")?.textContent.includes("30 秒"));
      assert.equal(await page.locator(".unit").nth(1).locator(".row-text").innerText(), "Redis 的超时设置为 30 秒，不是 30 分钟。");
      metrics.review = await measure(page, "#composer");
    });

    await check("pasted technical text uses the whole-text path, keeps code/layout and widens automatically", async () => {
      await page.locator('.scenario[data-scenario="document"]').click();
      await page.waitForFunction(() => document.querySelector(".row-text")?.textContent.startsWith("# Deployment constraints"));
      assert.equal(await page.locator("#route-chip").isVisible(), true);
      assert.match(await composer.getAttribute("class"), /wide/);
      const text = await page.locator(".row-text").innerText();
      assert.match(text, /"api_key": "\$\{API_KEY\}"/);
      assert.match(text, /\n  "timeout": 30,/);
      assert.ok((await composer.boundingBox()).width > 900);
      await shot(page, "long-document.png");
    });

    await check("target loss blocks writing, still allows copy, and requires an explicit new target", async () => {
      await page.locator("#edge").selectOption("lost");
      await page.waitForFunction(() => document.querySelectorAll(".unit .row-text").length === 3);
      assert.match(await page.locator("#banner").innerText(), /不会自动写入/);
      assert.equal(await page.locator("#commit").isDisabled(), true);
      assert.equal(await page.locator("#copy").isEnabled(), true);
      await page.locator('[data-banner="retarget"]').click();
      await page.locator('[data-target="notes"]').click();
      assert.equal(await page.locator("#commit").isEnabled(), true);
      await page.locator("#commit").click();
      assert.match(await page.locator("#toast").innerText(), /Slack/);
    });

    await check("no input target turns the action into copy", async () => {
      await page.locator("#edge").selectOption("no-target");
      await page.waitForFunction(() => document.querySelectorAll(".unit .row-text").length === 3);
      assert.match(await page.locator("#commit").innerText(), /复制/);
      await page.locator("#commit").click();
      await page.waitForFunction(() => /已复制/.test(document.querySelector("#toast").textContent) || document.querySelector("#dlg").open);
    });

    await check("meeting transcript shows a timeline and an imported file does not read audio", async () => {
      await page.locator('.scenario[data-scenario="meeting"]').click();
      assert.match(await page.locator(".src-live time").nth(1).innerText(), /00:03/);
      assert.match(await composer.getAttribute("class"), /wide/);
      await page.locator("#more").click();
      const chooser = page.waitForEvent("filechooser");
      await page.locator(`#menu-more [data-act="import"]`).click();
      await (await chooser).setFiles({ name: "weekly.wav", mimeType: "audio/wav", buffer: Buffer.alloc(44) });
      assert.match(await page.locator("#src-label").innerText(), /weekly\.wav/);
      assert.match(await page.locator("#toast").innerText(), /不会读取/);
    });

    await check("banners replace modals: slow, no audio, permission and download keep typing available", async () => {
      await page.locator("#edge").selectOption("slow");
      assert.match(await page.locator("#banner").innerText(), /处理比平时慢/);
      await page.locator('[data-banner="use-raw"]').click();
      assert.equal(await page.locator("#commit").isEnabled(), true);
      for (const name of ["no-audio", "permission", "download"]) {
        await page.locator("#edge").selectOption(name);
        assert.equal(await page.locator("#banner").isVisible(), true, name);
        assert.equal(await page.locator("#dlg").evaluate(el => el.open), false, `${name} must not open a modal`);
      }
      await page.locator("#mic").click();
      assert.match(await page.locator("#toast").innerText(), /下载|直接打字/);
    });

    await check("output menu is keyboard operable and language direction can be swapped", async () => {
      await page.locator('.scenario[data-scenario="pair"]').click();
      await page.locator("#mic").click();
      await page.locator("#out-pill").focus();
      await page.keyboard.press("Enter");
      await page.waitForFunction(() => document.querySelector("#menu-output").contains(document.activeElement));
      const focusOutline = await page.evaluate(() => getComputedStyle(document.activeElement).outlineStyle);
      assert.equal(focusOutline, "solid", "keyboard-focused menu item must keep a visible outline");
      await page.keyboard.press("ArrowDown");
      await page.keyboard.press("Enter");
      assert.match(await page.locator("#out-pill").innerText(), /得体书面/);
      await page.locator("#out-pill").click();
      await page.locator('[data-out="translation"]').click();
      assert.match(await page.locator("#out-pill").innerText(), /English/);
      await page.locator("#out-pill").click();
      await page.locator('[data-act="swap"]').click();
      assert.match(await page.locator("#out-pill").innerText(), /中文/);
    });

    await check("keys pressed on another tab never act on the hidden composer", async () => {
      await page.locator('.scenario[data-scenario="pair"]').click();
      await page.locator("#mic").click();
      await page.locator("#target-text").fill("");
      await page.locator('[data-page="settings"]').click();
      await page.locator("#page-settings h1").click();
      await page.keyboard.press("Enter");
      await page.keyboard.press("Escape");
      await page.locator('[data-page="play"]').click();
      assert.equal(await composer.isVisible(), true);
      assert.equal(await page.locator("#target-text").inputValue(), "");
      assert.equal(await page.locator("#commit").innerText().then(t => /已写入/.test(t)), false);
    });

    await check("IME confirmation and Shift+Enter never write; Ctrl+Enter mode requires the chord", async () => {
      await page.locator("#more").click();
      await page.locator(`#menu-more [data-act="new"]`).click();
      await page.locator("#out-pill").click();
      await page.locator('#menu-output [data-polish="natural"]').click();
      await page.locator("#target-text").fill("");
      await page.locator("#src").fill("这个 PR 先不要合并，等 CI 跑完再说。");
      await page.waitForFunction(() => document.querySelector(".row-text"));
      await page.locator("#src").evaluate(el => el.dispatchEvent(new KeyboardEvent("keydown", { key: "Enter", isComposing: true, bubbles: true })));
      // Safari / WKWebView out-of-order confirming Enter (isComposing: false, keyCode: 229)
      await page.locator("#src").evaluate(el => el.dispatchEvent(new KeyboardEvent("keydown", { key: "Enter", isComposing: false, keyCode: 229, bubbles: true })));
      await page.locator("#src").press("Shift+Enter");
      assert.equal(await page.locator("#target-text").inputValue(), "");
      await page.locator('[data-page="settings"]').click();
      await page.locator('[data-sec="general"]').click();
      await page.locator("#commit-binding").selectOption("ctrl-enter");
      await page.locator('[data-page="play"]').click();
      await page.locator("#src").focus();
      await page.keyboard.press("Enter");
      assert.equal(await page.locator("#target-text").inputValue(), "");
      await page.keyboard.press("Control+Enter");
      await page.waitForFunction(() => document.querySelector("#target-text").value.includes("这个 PR"));
      await page.locator('[data-page="settings"]').click();
      await page.locator("#commit-binding").selectOption("enter");
      await page.locator('[data-page="play"]').click();
    });

    await check("cloud endpoints and model line stay truthful", async () => {
      await page.locator('[data-page="settings"]').click();
      await page.locator('[data-sec="privacy"]').click();
      await shot(page, "endpoints.png", "#page-settings");
      await page.locator("#cloud-cleanup").click();
      await page.locator("#allow-cloud").click();
      await page.locator('[data-page="play"]').click();
      await page.locator('.scenario[data-scenario="compare"]').click();
      await page.waitForFunction(() => document.querySelector("#doc-view .doc-text"));
      assert.equal(await page.locator("#model-line").innerText(), "润色 · gpt-4o-mini · 云端（Foundry · 东亚）");
      assert.match(await page.locator("#model-line").getAttribute("class"), /cloud/);
      await shot(page, "model-line-cloud.png", "#composer");

      await page.locator('[data-page="settings"]').click();
      await page.locator('[data-sec="privacy"]').click();
      await page.locator("#cloud-cleanup").click();
      await page.locator('[data-page="play"]').click();
      assert.equal(await page.locator("#model-line").innerText(), "润色 · Qwen3.5 4B · 本地");
    });

    await check("light and dark themes meet WCAG AA contrast for the design tokens", async () => {
      for (const theme of ["light", "dark"]) {
        await page.evaluate(value => { document.documentElement.dataset.theme = value; }, theme);
        const pairs = await page.evaluate(() => {
          const probe = document.createElement("span");
          document.body.append(probe);
          const read = name => {
            probe.style.color = `var(${name})`;
            const c = getComputedStyle(probe).color.match(/[\d.]+/g).map(Number);
            return { rgb: c.slice(0, 3), alpha: c[3] ?? 1 };
          };
          const blend = (top, base) => top.rgb.map((v, i) => Math.round(v * top.alpha + base.rgb[i] * (1 - top.alpha)));
          const panel = read("--panel"), result = {};
          const on = (fg, bg) => ({ fg: read(fg).rgb, bg: blend(read(bg), panel) });
          result["text/panel"] = on("--text", "--panel");
          result["muted/panel"] = on("--muted", "--panel");
          result["faint/panel"] = on("--faint", "--panel");
          result["accent-ink/accent-soft"] = on("--accent-ink", "--accent-soft");
          result["ok/ok-bg"] = on("--ok", "--ok-bg");
          result["warn/warn-bg"] = on("--warn", "--warn-bg");
          result["danger/danger-bg"] = on("--danger", "--danger-bg");
          result["info/info-bg"] = on("--info", "--info-bg");
          result["ok/panel"] = on("--ok", "--panel");
          result["white/accent"] = { fg: [255, 255, 255], bg: read("--accent").rgb };
          probe.remove();
          return result;
        });
        for (const [name, { fg, bg }] of Object.entries(pairs)) {
          assert.ok(ratio(fg, bg) >= 4.5, `${theme} ${name} contrast ${ratio(fg, bg).toFixed(2)}`);
        }
      }
      await page.evaluate(() => { document.documentElement.dataset.theme = "light"; });
    });

    await check("no text below 12px anywhere on the three pages", async () => {
      for (const name of ["play", "settings", "notes"]) {
        await page.locator(`[data-page="${name}"]`).click();
        const result = await measure(page, `#page-${name}`);
        assert.deepEqual(result.small, [], name);
      }
    });

    await check("Windows variant moves the tray to the taskbar and uses Ctrl labels", async () => {
      await page.locator('[data-page="play"]').click();
      await page.locator('[data-platform="win"]').click();
      assert.match(await page.locator(".rail .k-summon").innerText(), /Ctrl\+Alt\+T/);
      const bar = await page.locator(".menubar").boundingBox(), win = await page.locator(".app-window").boundingBox();
      assert.ok(bar.y > win.y, "taskbar below the app window");
      await shot(page, "windows.png");
      await page.locator('[data-platform="mac"]').click();
    });

    await check("dark theme composer stays legible", async () => {
      await page.locator("#theme-btn").click();
      await page.locator('.scenario[data-scenario="compare"]').click();
      await page.waitForFunction(() => document.querySelector("#doc-view .doc-text"));
      await shot(page, "dark.png");
      await page.locator("#theme-btn").click();
    });

    await check("390px viewport: no horizontal overflow and the composer never covers the target input", async () => {
      await page.setViewportSize({ width: 390, height: 900 });
      for (const name of ["settings", "notes", "play"]) {
        await page.locator(`[data-page="${name}"]`).click();
        const [scrollWidth, innerWidth] = await page.evaluate(() => [document.documentElement.scrollWidth, innerWidth]);
        assert.ok(scrollWidth <= innerWidth + 1, `${name} overflows`);
      }
      for (const name of ["compare", "todo", "document"]) {
        await page.locator(`.scenario[data-scenario="${name}"]`).click();
        const box = await composer.boundingBox(), field = await page.locator(".field").boundingBox(), desk = await page.locator("#desktop").boundingBox();
        assert.ok(box.y + box.height <= field.y, `${name}: composer covers the input`);
        assert.ok(box.y >= desk.y + 30, `${name}: composer is clipped at the top`);
        assert.ok(box.x >= 0 && box.x + box.width <= 390, `${name}: composer outside viewport`);
      }
      await shot(page, "mobile.png", "#desktop");
      await page.setViewportSize({ width: 1480, height: 1000 });
    });

    await check("design budget: few controls at rest and no text below 12px", async () => {
      assert.ok(metrics.idle.controls >= 6 && metrics.idle.controls <= 9, `idle controls ${metrics.idle.controls}`);
      assert.ok(metrics.live.controls >= 6 && metrics.live.controls <= 9, `live controls ${metrics.live.controls}`);
      assert.ok(metrics.review.controls >= 6 && metrics.review.controls <= 10, `review controls ${metrics.review.controls}`);
      assert.ok(metrics.reviewDoc.controls >= 6 && metrics.reviewDoc.controls <= 10, `reviewDoc controls ${metrics.reviewDoc.controls}`);
      for (const [state, value] of Object.entries(metrics)) {
        assert.deepEqual(value.small, [], state);
        assert.ok(value.fontSizes <= 6, `${state} font sizes ${value.fontSizes}`);
      }
    });

    await check("runtime stays error-free and fully offline", async () => {
      assert.deepEqual(errors, []);
      assert.deepEqual(network, []);
    });

    console.log("METRICS", JSON.stringify(metrics));
    console.log(`VERIFIED ${results.length} browser scenarios`);
  } finally {
    await browser.close();
  }
})().catch(error => { console.error(error); process.exitCode = 1; });
