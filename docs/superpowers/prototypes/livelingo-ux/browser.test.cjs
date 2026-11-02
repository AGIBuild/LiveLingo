const { chromium } = require("playwright");
const assert = require("node:assert/strict");
const { pathToFileURL } = require("node:url");
const path = require("node:path");

const root = __dirname;
const results = [];
async function check(name, fn) {
  await fn();
  results.push(name);
  console.log("PASS", name);
}

(async () => {
  const browser = await chromium.launch({ headless: true });
  try {
    const page = await browser.newPage({ viewport: { width: 1480, height: 1100 } });
    const errors = [];
    const network = [];
    page.on("pageerror", error => errors.push(error.message));
    page.on("request", request => { if (/^https?:/.test(request.url())) network.push(request.url()); });
    await page.goto(pathToFileURL(path.join(root, "index.html")).href);
    await check("offline assets load without script errors", async () => {
      assert.equal(await page.locator("#session-status").innerText(), "0 个单元 · 可继续追加口述");
      assert.deepEqual(errors, []);
      assert.deepEqual(network, []);
    });
    await page.screenshot({ path: path.join(root, "overview.png"), fullPage: true });
    await check("translations appear during recording before stop, with submission blocked", async () => {
      await page.locator('.scenario[data-scenario="translate"]').click();
      await page.waitForFunction(() => document.querySelector(".sentence-text")?.textContent.includes("Do not merge this PR"));
      assert.match(await page.locator("#session-status").innerText(), /正在录音/);
      assert.equal(await page.locator("#commit").isEnabled(), false);
      await page.locator("#copy").click();
      assert.match(await page.locator("#announcement").innerText(), /请先结束录音/);
      await page.screenshot({ path: path.join(root, "translation-live.png"), fullPage: true });
      await page.locator("#record").click();
    });
    await check("manual sentence edit survives subsequent dictation", async () => {
      await page.locator('.scenario[data-scenario="pair"]').click();
      await page.waitForFunction(() => document.querySelector('[data-unit="0"] .sentence-meta')?.textContent.includes("已完成"));
      await page.locator("#record").click();
      await page.locator('[data-unit-action="edit"]').first().click();
      await page.locator(".sentence-editor").fill("这是我人工修改的句子。");
      await page.locator('[data-unit-action="save"]').click();
      await page.locator("#record").click();
      await page.waitForFunction(() => document.querySelectorAll(".sentence").length >= 2);
      await page.locator("#record").click();
      assert.equal(await page.locator('[data-unit="0"] .sentence-text').innerText(), "这是我人工修改的句子。");
      assert.match(await page.locator('[data-unit="0"] .sentence-meta').innerText(), /已手动修改/);
    });
    await check("gate rejection retains raw text and offers sentence-level editing", async () => {
      await page.locator(".edge-cases summary").click();
      await page.locator('.edge-cases [data-scenario="gate"]').click();
      await page.waitForFunction(() => document.querySelector(".warning-inline")?.textContent.includes("已拒绝"));
      assert.match(await page.locator(".warning-inline").innerText(), /30 秒.*30 分钟/);
      assert.equal(await page.locator('[data-unit="1"] .sentence-text').innerText(), "Redis 的超时设置为 30 秒，不是 30 分钟。");
      await page.locator('[data-unit="1"] [data-unit-action="raw"]').click();
    });
    await check("paste chooses whole-text route and preserves literal code/indentation in the fixture", async () => {
      await page.locator('.scenario[data-scenario="document"]').click();
      await page.waitForFunction(() => document.querySelector(".sentence-text")?.textContent.startsWith("# Deployment constraints"));
      assert.equal(await page.locator("#route").inputValue(), "whole");
      const translated = await page.locator(".sentence-text").innerText();
      assert.match(translated, /"api_key": "\$\{API_KEY\}"/);
      assert.match(translated, /\n  "timeout": 30,/);
      await page.locator("#announcement").evaluate(el => { el.hidden = true; });
      await page.screenshot({ path: path.join(root, "long-document.png"), fullPage: true });
      await page.locator("#source-input").evaluate(el => {
        el.value = "# 自定义完成文本\n\n`keep_me`";
        el.dispatchEvent(new Event("input", { bubbles: true }));
        el.dispatchEvent(new Event("paste", { bubbles: true }));
      });
      await page.waitForFunction(() => document.querySelector("#route").value === "whole");
      await page.waitForFunction(() => document.querySelector(".sentence-text")?.textContent.includes("原型没有运行模型"));
      assert.equal(await page.locator("#commit").isEnabled(), false);
      await page.locator('[data-output="raw"]').click();
      assert.equal(await page.locator(".sentence-text").innerText(), "# 自定义完成文本\n\n`keep_me`");
    });
    await check("IME confirmation cannot submit; Shift+Enter remains a newline", async () => {
      await page.locator("#source-input").focus();
      const before = await page.locator("#target-text").inputValue();
      await page.locator("#source-input").evaluate(el => {
        el.dispatchEvent(new KeyboardEvent("keydown", { key: "Enter", isComposing: true, bubbles: true }));
      });
      assert.equal(await page.locator("#target-text").inputValue(), before);
      await page.locator("#source-input").press("End");
      await page.locator("#source-input").press("Shift+Enter");
      assert.match(await page.locator("#source-input").inputValue(), /\n$/);
    });
    await check("Tab moves keyboard focus rather than changing output version", async () => {
      const output = await page.locator('[data-output].active').getAttribute("data-output");
      await page.locator("#translate-on").focus();
      await page.keyboard.press("Tab");
      assert.equal(await page.locator("#direction").evaluate(el => el === document.activeElement), true);
      assert.equal(await page.locator('[data-output].active').getAttribute("data-output"), output);
    });
    await check("target loss prevents submission until explicitly rebound", async () => {
      await page.locator('.edge-cases [data-scenario="lost"]').click();
      await page.waitForFunction(() => document.querySelectorAll(".sentence-meta").length === 3 && [...document.querySelectorAll(".sentence-meta")].every(el => !el.textContent.includes("处理中")));
      assert.equal(await page.locator("#commit").isEnabled(), false);
      await page.locator('[data-action="retarget"]').click();
      await page.locator('[data-dialog-action="bind-notes"]').click();
      assert.match(await page.locator("#destination").innerText(), /笔记/);
      assert.equal(await page.locator("#commit").isEnabled(), true);
      await page.locator("#commit").click();
      assert.match(await page.locator("#target-text").inputValue(), /Redis/);
      assert.match(await page.locator("#toast").innerText(), /未发送/);
    });
    await check("editing raw input cannot bypass lost-target confirmation", async () => {
      await page.locator('.edge-cases [data-scenario="lost"]').click();
      await page.locator('[data-mode="text"]').click();
      await page.locator("#source-input").fill("重新输入的文字。");
      await page.locator('[data-output="raw"]').click();
      assert.match(await page.locator("#destination").innerText(), /失效/);
      assert.equal(await page.locator("#commit").isEnabled(), false);
    });
    await check("no target exposes copy instead of OS injection", async () => {
      await page.locator('.edge-cases [data-scenario="no-target"]').click();
      await page.waitForFunction(() => !document.querySelector("#commit").disabled);
      assert.match(await page.locator("#commit").innerText(), /复制/);
      await page.locator("#commit").click();
      assert.equal(await page.locator("#modal-title").innerText(), "当前版本 · 可复制");
      await page.locator("#modal-close").click();
    });
    await check("unknown translation never silently falls back to source", async () => {
      await page.locator('.edge-cases [data-scenario="failure"]').click();
      assert.equal(await page.locator("#commit").isEnabled(), false);
      assert.match(await page.locator("#warning").innerText(), /不会自动改成原文输出/);
      await page.locator("#use-raw-all").click();
      assert.equal(await page.locator('[data-output="raw"]').getAttribute("aria-pressed"), "true");
      assert.equal(await page.locator("#commit").isEnabled(), true);
    });
    await check("history restores its original language direction together with the translated text", async () => {
      await page.locator("#direction").selectOption("zh-en");
      await page.locator('.scenario[data-scenario="meeting"]').click();
      await page.locator("#translate-on").check();
      await page.waitForFunction(() => !document.querySelector("#commit").disabled);
      await page.locator("#close-composer").click();
      await page.locator("#summon").click();
      await page.locator("#direction").selectOption("en-zh");
      await page.locator("#history").click();
      await page.locator('[data-restore="0"]').click();
      assert.equal(await page.locator("#direction").inputValue(), "zh-en");
      assert.match(await page.locator("#output-hint").innerText(), /英文译文/);
      assert.match(await page.locator(".sentence-text").first().innerText(), /Do not merge/);
    });
    await check("dictation flushes pending typed-text processing instead of stranding it", async () => {
      await page.locator('.scenario[data-scenario="document"]').click();
      await page.locator("#text-sample").click();
      await page.locator("#record").click();
      await page.waitForFunction(() => document.querySelectorAll(".sentence").length >= 2);
      await page.locator("#record").click();
      await page.waitForFunction(() => document.querySelector('[data-unit="0"] .sentence-meta')?.textContent.includes("已完成"), null, { timeout: 2500 });
      assert.match(await page.locator('[data-unit="0"] .sentence-text').innerText(), /Do not merge/);
    });
    await check("restored pending drafts restart valid jobs rather than waiting forever", async () => {
      await page.locator('.edge-cases [data-scenario="slow"]').click();
      await page.locator("#close-composer").click();
      await page.locator("#summon").click();
      await page.locator("#reset").click();
      await page.locator("#history").click();
      await page.locator('[data-restore="0"]').click();
      await page.waitForFunction(() => !document.querySelector("#commit").disabled, null, { timeout: 2500 });
      assert.match(await page.locator(".sentence-text").first().innerText(), /Do not merge/);
    });
    await check("Enter after microphone-stop submits the reviewed version rather than restarting recording", async () => {
      await page.locator("#record").focus();
      await page.keyboard.press("Enter");
      assert.match(await page.locator("#target-text").inputValue(), /Do not merge/);
      assert.equal(await page.locator("#composer").isHidden(), true);
    });
    await check("transcription permits pause/continuation and shows read-only source evidence", async () => {
      await page.locator('.scenario[data-scenario="meeting"]').click();
      await page.waitForFunction(() => !document.querySelector("#commit").disabled);
      assert.equal(await page.locator("#source-input").getAttribute("readonly"), "");
      await page.locator("#audio-file").setInputFiles({ name: "review-audio.wav", mimeType: "audio/wav", buffer: Buffer.alloc(44) });
      assert.match(await page.locator("#file-note").innerText(), /review-audio\.wav.*不会读取音频/);
      await page.locator("#record").click();
      await page.locator("#pause").click();
      assert.match(await page.locator("#session-status").innerText(), /已暂停/);
      await page.locator("#pause").click();
      await page.waitForFunction(() => document.querySelectorAll(".sentence").length >= 4);
      await page.locator("#record").click();
      assert.match(await page.locator(".sentence-meta").first().innerText(), /示例时间/);
    });
    await check("tray exposes summon, draft recovery, and the settings center", async () => {
      await page.locator('[popovertarget="tray-menu"]').click();
      await page.locator('[data-tray-action="settings"]').click();
      assert.equal(await page.locator('[data-settings-panel="general"]').isVisible(), true);
      await page.locator('[data-page="experience"]').click();
      await page.locator('[popovertarget="tray-menu"]').click();
      await page.locator('[data-tray-action="summon"]').click();
      assert.equal(await page.locator("#composer").isVisible(), true);
    });
    await check("all six settings pages are reachable and cloud authorizations are separate", async () => {
      await page.locator('[data-page="settings"]').click();
      for (const name of ["general", "speech", "language", "privacy", "models", "about"]) {
        await page.locator(`[data-settings="${name}"]`).click();
        assert.equal(await page.locator(`[data-settings-panel="${name}"]`).isVisible(), true);
      }
      await page.locator('[data-settings="privacy"]').click();
      await page.locator("#cloud-translation").click();
      assert.equal(await page.locator("#cloud-translation").isChecked(), false);
      await page.locator("#allow-cloud").click();
      assert.equal(await page.locator("#cloud-translation").isChecked(), true);
      assert.equal(await page.locator("#cloud-cleanup").isChecked(), false);
      await page.locator('[data-settings="language"]').click();
      await page.locator("#term-source").fill("原型测试词");
      await page.locator("#term-target").fill("prototype test term");
      await page.locator('#term-form button[type="submit"]').click();
      assert.match(await page.locator("#terms-list").innerText(), /prototype test term/);
      await page.locator("#announcement").evaluate(el => { el.hidden = true; });
      await page.screenshot({ path: path.join(root, "settings.png"), fullPage: true });
    });
    await check("Windows labels, dark theme, and 390px viewport do not horizontally overflow", async () => {
      await page.locator("#platform").selectOption("win");
      assert.match(await page.locator(".summon-key").innerText(), /Ctrl/);
      await page.locator('[data-settings="general"]').click();
      await page.locator("#theme").selectOption("dark");
      await page.locator("#reduce-motion").check();
      assert.match(await page.locator("body").getAttribute("class"), /dark.*reduce-motion/);
      await page.setViewportSize({ width: 390, height: 844 });
      for (const name of ["experience", "states", "settings", "design"]) {
        await page.locator(`[data-page="${name}"]`).click();
        const dimensions = await page.evaluate(() => [document.documentElement.scrollWidth, innerWidth]);
        assert.ok(dimensions[0] <= dimensions[1] + 1, `${name} overflow: ${dimensions}`);
      }
      await page.locator('[data-page="experience"]').click();
      for (const name of ["pair", "document", "meeting"]) {
        await page.locator(`.scenario[data-scenario="${name}"]`).click();
        const composer = await page.locator("#composer").boundingBox();
        const target = await page.locator(".target-input").boundingBox();
        const desktop = await page.locator("#desktop").boundingBox();
        assert.ok(composer.y + composer.height <= target.y, `${name}: composer covers the original input`);
        assert.ok(composer.y >= desktop.y + 29, `${name}: composer clipped above desktop`);
        if (name === "pair") await page.locator("#record").click();
      }
    });
    await check("runtime remains error-free and completely offline after all scenarios", async () => {
      assert.deepEqual(errors, []);
      assert.deepEqual(network, []);
    });
    console.log(`VERIFIED ${results.length} browser scenarios`);
  } finally {
    await browser.close();
  }
})().catch(error => { console.error(error); process.exitCode = 1; });
