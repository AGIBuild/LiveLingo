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
  scope.querySelectorAll("*").forEach(el => { if (visible(el) && [...el.childNodes].some(n => n.nodeType === 3 && n.textContent.trim())) sizes.add(getComputedStyle(el).fontSize); });
  return { controls: controls.length, small, fontSizes: sizes.size, height: Math.round(scope.getBoundingClientRect().height) };
}, selector);

// Screenshots are design deliverables: wait for the entrance animation and remove leftovers of earlier steps.
async function shot(page, file, selector) {
  await page.waitForFunction(() => !document.getAnimations().some(a => a.effect?.target?.id === "composer"));
  await page.evaluate(() => { document.querySelector("#toast").hidden = true; document.querySelector("#target-text").value = ""; });
  const target = selector ? page.locator(selector) : page;
  if (selector) await page.evaluate(() => { document.querySelector(".top").style.position = "static"; });
  await target.screenshot({ path: path.join(root, file) });
  if (selector) await page.evaluate(() => { document.querySelector(".top").style.position = ""; });
}

const luminance = ([r, g, b]) => { const f = c => { c /= 255; return c <= .03928 ? c / 12.92 : ((c + .055) / 1.055) ** 2.4; }; return .2126 * f(r) + .7152 * f(g) + .0722 * f(b); };
const ratio = (a, b) => { const [hi, lo] = [luminance(a), luminance(b)].sort((x, y) => y - x); return (hi + .05) / (lo + .05); };

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

    await check("loads offline with the composer ready and no script errors", async () => {
      assert.equal(await composer.isVisible(), true);
      assert.deepEqual(errors, []);
      assert.deepEqual(network, []);
      await page.waitForFunction(() => getComputedStyle(document.querySelector("#composer")).opacity === "1" && !document.getAnimations().some(a => a.effect?.target?.id === "composer"));
      metrics.idle = await measure(page, "#composer");
      await shot(page, "overview.png");
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
      await page.locator("#more").click(); await page.locator(`#menu-more [data-act="new"]`).click();
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
      await page.locator("#more").click(); await page.locator(`#menu-more [data-act="new"]`).click();
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
      assert.match(await page.locator("#toast").innerText(), /笔记/);
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
      await page.keyboard.press("ArrowDown");
      await page.keyboard.press("Enter");
      assert.match(await page.locator("#out-pill").innerText(), /English/);
      await page.locator("#out-pill").click();
      await page.locator('[data-act="swap"]').click();
      assert.match(await page.locator("#out-pill").innerText(), /中文/);
    });

    await check("leaving slow mode via 'use original' does not strand later cleanup jobs", async () => {
      await page.locator("#edge").selectOption("slow");
      await page.locator('[data-banner="use-raw"]').click();
      await page.locator("#out-pill").click();
      await page.locator('[data-out="clean"]').click();
      await page.waitForFunction(() => !document.querySelector(".skel") && document.querySelectorAll(".unit .row-text").length === 3, null, { timeout: 3000 });
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

    await check("editing the source while a sentence editor is open keeps the list truthful and never loses the edit silently", async () => {
      await page.locator("#edge").selectOption("gate");
      await page.waitForFunction(() => document.querySelectorAll(".unit .row-text").length === 3);
      await page.locator(".unit").first().hover();
      await page.locator(".unit").first().locator('[data-row="edit"]').click();
      await page.locator(".edit").fill("请先不要合并这个 PR。");
      const original = await page.locator("#src").inputValue();
      await page.locator("#src").fill(original + "\n今天主要讨论缓存穿透的处理方案。");
      await page.waitForFunction(() => document.querySelectorAll(".unit .row-text").length === 4, null, { timeout: 3000 });
      assert.equal(await page.locator(".edit").count(), 0);
      assert.equal(await page.locator(".unit .row-text").first().innerText(), "请先不要合并这个 PR。");
      assert.match(await page.locator(".unit").first().innerText(), /已手动修改/);
      assert.ok(original.length > 0);
    });

    await check("sentence editing is unavailable while dictating, so rows never freeze mid-recording", async () => {
      await page.locator("#mic").click();
      await page.waitForFunction(() => document.querySelectorAll(".unit .row-text").length >= 1);
      assert.equal(await page.locator(".row-tools").count(), 0);
      await page.locator("#mic").click();
      await page.waitForFunction(() => document.querySelectorAll(".row-tools").length >= 1);
    });

    await check("Esc hides and keeps the draft; the global shortcut and history bring it back", async () => {
      await page.locator("#src").focus();
      await page.keyboard.press("Escape");
      assert.equal(await composer.isHidden(), true);
      await page.keyboard.press("Control+Alt+KeyT");
      assert.equal(await composer.isVisible(), true);
      await page.locator("#more").click(); await page.locator(`#menu-more [data-act="history"]`).click();
      assert.ok(await page.locator("[data-restore]").count() >= 1);
      await page.locator("[data-restore]").first().click();
      assert.equal(await composer.isVisible(), true);
    });

    await check("IME confirmation and Shift+Enter never write; Ctrl+Enter mode requires the chord", async () => {
      await page.locator("#more").click(); await page.locator(`#menu-more [data-act="new"]`).click();
      await page.locator("#target-text").fill("");
      await page.locator("#src").fill("这个 PR 先不要合并，等 CI 跑完再说。");
      await page.waitForFunction(() => document.querySelector(".row-text"));
      await page.locator("#src").evaluate(el => el.dispatchEvent(new KeyboardEvent("keydown", { key: "Enter", isComposing: true, bubbles: true })));
      await page.locator("#src").press("Shift+Enter");
      assert.equal(await page.locator("#target-text").inputValue(), "");
      await page.locator('[data-page="settings"]').click();
      await page.locator("#commit-binding").selectOption("ctrl-enter");
      await page.locator('[data-page="play"]').click();
      await page.locator("#src").focus();
      await page.keyboard.press("Enter");
      assert.equal(await page.locator("#target-text").inputValue(), "");
      await page.keyboard.press("Control+Enter");
      await page.waitForFunction(() => document.querySelector("#target-text").value.includes("这个 PR"));
    });

    await check("settings: four sections, separate cloud consent, glossary rules", async () => {
      await page.locator('[data-page="settings"]').click();
      for (const section of ["general", "voice", "privacy", "models"]) {
        await page.locator(`[data-sec="${section}"]`).click();
        assert.equal(await page.locator(`[data-panel="${section}"]`).isVisible(), true);
      }
      await page.locator('[data-sec="privacy"]').click();
      await page.locator("#cloud-translation").click();
      assert.equal(await page.locator("#cloud-translation").isChecked(), false);
      await page.locator("#allow-cloud").click();
      assert.equal(await page.locator("#cloud-cleanup").isChecked(), false);
      await page.locator('[data-sec="voice"]').click();
      await page.locator("#term-source").fill("缓存穿透");
      await page.locator("#term-target").fill("duplicate");
      await page.locator('#term-form button[type="submit"]').click();
      assert.match(await page.locator("#toast").innerText(), /不能为空，也不能重复/);
      await page.locator("#term-source").fill("幂等键");
      await page.locator("#term-target").fill("idempotency key");
      await page.locator('#term-form button[type="submit"]').click();
      assert.match(await page.locator("#terms-list").innerText(), /idempotency key/);
      await shot(page, "settings.png", "#page-settings");
      await page.locator('[data-page="play"]').click();
      assert.match(await page.locator("#privacy").innerText(), /云端 · 翻译/);
      await page.locator('[data-page="settings"]').click();
      await page.locator('[data-sec="privacy"]').click();
      await page.locator("#cloud-translation").click();
      assert.equal(await page.locator("#cloud-translation").isChecked(), false);
      await page.locator('[data-page="play"]').click();
      assert.match(await page.locator("#privacy").innerText(), /本地/);
    });

    await check("cloud endpoints: add many, one enabled at a time, switching never reroutes silently", async () => {
      const enabled = () => page.locator("[data-ep-on]:checked").count();
      const names = () => page.locator(".endpoint .ep-title b").allInnerTexts();
      await page.locator('[data-page="settings"]').click();
      await page.locator('[data-sec="privacy"]').click();
      assert.deepEqual(await names(), ["Foundry · 东亚", "OpenAI", "本机 llama.cpp"]);
      assert.equal(await enabled(), 1);
      await shot(page, "endpoints.png", "#page-settings");

      // The form rejects unusable endpoints and keeps the dialog open.
      await page.locator("#add-endpoint").click();
      await page.locator("#ep-name").fill("OpenAI");
      await page.locator('#ep-form button[type="submit"]').click();
      assert.match(await page.locator("#ep-error").innerText(), /同名/);
      await page.locator("#ep-name").fill("Contoso");
      await page.locator("#ep-url").fill("http://contoso.example/v1");
      await page.locator('#ep-form button[type="submit"]').click();
      assert.match(await page.locator("#ep-error").innerText(), /https/);
      await page.locator("#ep-url").fill("https://contoso.example/v1/");
      await page.locator("#ep-model").fill("contoso-chat");
      await page.locator('#ep-form button[type="submit"]').click();
      assert.match(await page.locator("#ep-error").innerText(), /密钥/);
      await shot(page, "endpoint-form.png", "#dlg");
      await page.locator("#ep-key").fill("not-a-real-key");
      await page.locator('#ep-form button[type="submit"]').click();
      assert.equal((await names()).at(-1), "Contoso");
      assert.equal(await enabled(), 1, "a new endpoint is added disabled");
      assert.equal(await page.locator('[data-ep-on="ep4"]').isChecked(), false);

      // With cloud translation on, switching asks first and names the new destination.
      await page.locator("#cloud-translation").click();
      assert.match(await page.locator("#dlg-body").innerText(), /Foundry · 东亚/);
      await page.locator("#allow-cloud").click();
      await page.locator('[data-ep-on="ep2"]').click();
      assert.match(await page.locator("#dlg-body").innerText(), /OpenAI.*api\.openai\.com/);
      await page.locator('[data-dlg="close"]').click();
      assert.equal(await page.locator('[data-ep-on="ep1"]').isChecked(), true);
      assert.equal(await page.locator('[data-ep-on="ep2"]').isChecked(), false);
      await page.locator('[data-ep-on="ep2"]').click();
      await page.locator("#switch-ep").click();
      assert.equal(await enabled(), 1);
      assert.equal(await page.locator('[data-ep-on="ep2"]').isChecked(), true);
      assert.equal(await page.locator("#cloud-translation").isChecked(), true);
      await page.locator('[data-page="play"]').click();
      assert.match(await page.locator("#privacy").getAttribute("aria-label"), /OpenAI/);
      await page.locator('[data-page="settings"]').click();
      await page.locator('[data-sec="privacy"]').click();

      // Disabling the active endpoint, or editing its host, turns the cloud switches off.
      await page.locator('[data-ep-on="ep2"]').click();
      assert.equal(await enabled(), 0);
      assert.equal(await page.locator("#cloud-translation").isChecked(), false);
      assert.match(await page.locator("#toast").innerText(), /已关闭/);
      await page.locator("#cloud-translation").click();
      assert.equal(await page.locator("#cloud-translation").isChecked(), false);
      assert.equal(await page.locator("#dlg").evaluate(dlg => dlg.open), false, "no consent dialog without an endpoint");
      await page.locator('[data-ep-on="ep1"]').click();
      await page.locator("#cloud-cleanup").click();
      await page.locator("#allow-cloud").click();
      await page.locator('[data-ep-edit="ep1"]').click();
      await page.locator("#ep-url").fill("https://other.example/v1");
      await page.locator('#ep-form button[type="submit"]').click();
      assert.equal(await page.locator("#cloud-cleanup").isChecked(), false);

      // Deleting works for the active and for inactive endpoints; the list may become empty.
      await page.locator('[data-ep-del="ep4"]').click();
      await page.locator("#delete-ep").click();
      assert.equal((await names()).includes("Contoso"), false);
      for (const id of ["ep1", "ep2", "ep3"]) { await page.locator(`[data-ep-del="${id}"]`).click(); await page.locator("#delete-ep").click(); }
      assert.match(await page.locator("#endpoints-list").innerText(), /还没有端点/);
      assert.match(await page.locator("#endpoints-sub").innerText(), /本机/);
      await page.locator("#add-endpoint").click();
      await page.locator("#ep-name").fill("Foundry · 东亚");
      await page.locator("#ep-url").fill("https://livelingo-eastasia.openai.azure.com/openai/v1");
      await page.locator("#ep-model").fill("gpt-4o-mini");
      await page.locator("#ep-key").fill("not-a-real-key");
      await page.locator('#ep-form button[type="submit"]').click();
      assert.equal(await enabled(), 0);
    });

    await check("model line: a quiet line right of the microphone, below the status, never a floating box", async () => {
      const line = page.locator("#model-line");
      const geometry = () => page.evaluate(() => {
        const box = selector => document.querySelector(selector).getBoundingClientRect();
        return { mic: box("#mic"), status: box("#status"), line: box("#model-line"), copy: box("#copy"), foot: box(".c-foot") };
      });
      await page.locator('[data-page="play"]').click();
      await page.waitForFunction(() => document.querySelector("#toast").hidden);

      // Dictating: the speech model, local, shown at once and without any popup.
      await page.locator('.scenario[data-scenario="pair"]').click();
      await page.waitForFunction(() => !document.querySelector("#model-line").hidden && document.querySelectorAll(".unit .row-text").length > 0);
      assert.equal(await line.innerText(), "识别 · X-ASR + Qwen3-ASR · 本地");
      assert.equal(await page.locator("#toast").isHidden(), true);
      const g = await geometry();
      assert.ok(g.line.left >= g.mic.right, "right of the microphone");
      assert.ok(g.line.top >= g.status.bottom - 1, "below the status text");
      assert.ok(g.line.bottom <= g.foot.bottom, "inside the footer");
      assert.ok(g.line.right <= g.copy.left, "never under the copy button");
      assert.equal(Math.round(g.foot.height), Math.round(g.mic.height) + 20, "the footer does not grow");
      await shot(page, "model-line-local.png", "#composer");

      // Not dictating: the model of the chosen output, and none for the original wording.
      // Always on while there is something to show: after a result, the model of the chosen output stays visible.
      await page.locator("#mic").click();
      assert.equal(await line.innerText(), "整理 · Qwen3.5 4B · 本地");
      await page.locator("#out-pill").click();
      await page.locator('[data-out="raw"]').click();
      assert.equal(await line.isHidden(), true);

      // Translation in the cloud names the enabled endpoint and keeps the cloud colour of the privacy chip.
      await page.locator('.scenario[data-scenario="translate"]').click();
      await page.waitForFunction(() => document.querySelector(".row-text")?.textContent.includes("Do not merge this PR"));
      await page.locator("#mic").click();
      assert.equal(await line.innerText(), "翻译 · Hy-MT2-1.8B · 本地");
      await page.locator('[data-page="settings"]').click();
      await page.locator('[data-sec="privacy"]').click();
      await page.locator("[data-ep-on]").first().click();
      await page.locator("#cloud-translation").click();
      await page.locator("#allow-cloud").click();
      await page.locator('[data-page="play"]').click();
      assert.equal(await line.innerText(), "翻译 · gpt-4o-mini · 云端（Foundry · 东亚）");
      assert.match(await line.getAttribute("class"), /cloud/);
      await shot(page, "model-line-cloud.png", "#composer");
      await page.locator('[data-page="settings"]').click();
      await page.locator('[data-sec="privacy"]').click();
      await page.locator("#cloud-translation").click();
      await page.locator('[data-page="play"]').click();
      assert.equal(await line.innerText(), "翻译 · Hy-MT2-1.8B · 本地");

      // The privacy chip opens a card next to it with every stage in full; it needs no timer and closes on Escape.
      await page.locator("#privacy").click();
      await page.waitForFunction(() => document.querySelector("#menu-models").matches(":popover-open"));
      const card = await page.evaluate(() => {
        const box = selector => document.querySelector(selector).getBoundingClientRect();
        return { chip: box("#privacy"), card: box("#menu-models"), rows: [...document.querySelectorAll("#models-list dd")].map(dd => dd.textContent) };
      });
      assert.ok(card.card.top >= card.chip.bottom || card.card.bottom <= card.chip.top, "next to the chip, never over it");
      assert.equal(card.rows.length, 3);
      assert.match(card.rows[0], /X-ASR 480 ms streaming \+ Qwen3-ASR 0\.6B（本地，已实际加载）/);
      assert.match(card.rows[1], /Qwen3\.5 4B（本地，已实际加载）/);
      assert.match(card.rows[2], /Hy-MT2 1\.8B（本地，已实际加载）/);
      const frame = await composer.boundingBox();
      await page.screenshot({ path: path.join(root, "model-card.png"), clip: { x: frame.x - 12, y: Math.max(0, frame.y - 240), width: frame.width + 24, height: frame.height + 252 } });
      await page.keyboard.press("Escape");
      assert.equal(await page.locator("#menu-models").evaluate(el => el.matches(":popover-open")), false);
      assert.equal(await page.locator("#toast").isHidden(), true);

      // Models that are loading or being replaced say so in the same line.
      await page.locator("#edge").selectOption("model-switch");
      assert.equal(await line.innerText(), "翻译 · 正在切换到 Hy-MT2-1.8B（上次 Hy-MT2-7B）");
      assert.match(await line.getAttribute("class"), /info/);
      await shot(page, "model-line-switch.png", "#composer");
      await page.locator("#edge").selectOption("model-preparing");
      assert.equal(await line.innerText(), "识别 · 正在准备 X-ASR + Qwen3-ASR");
      await page.locator("#mic").click();

      // Narrow windows keep the footer to the essentials, like the status text.
      await page.setViewportSize({ width: 390, height: 900 });
      assert.equal(await line.isHidden(), true);
      await page.setViewportSize({ width: 1480, height: 1000 });
    });

    await check("light and dark themes meet WCAG AA contrast for the design tokens", async () => {
      for (const theme of ["light", "dark"]) {
        await page.evaluate(value => { document.documentElement.dataset.theme = value; }, theme);
        const pairs = await page.evaluate(() => {
          const probe = document.createElement("span"); document.body.append(probe);
          const read = name => { probe.style.color = `var(${name})`; const c = getComputedStyle(probe).color.match(/[\d.]+/g).map(Number); return { rgb: c.slice(0, 3), alpha: c[3] ?? 1 }; };
          const blend = (top, base) => top.rgb.map((v, i) => Math.round(v * top.alpha + base.rgb[i] * (1 - top.alpha)));
          const panel = read("--panel"), result = {};
          const on = (fg, bg) => ({ fg: read(fg).rgb, bg: blend(read(bg), panel) });
          result["text/panel"] = on("--text", "--panel"); result["muted/panel"] = on("--muted", "--panel"); result["faint/panel"] = on("--faint", "--panel");
          result["accent-ink/accent-soft"] = on("--accent-ink", "--accent-soft"); result["warn/warn-bg"] = on("--warn", "--warn-bg");
          result["danger/danger-bg"] = on("--danger", "--danger-bg"); result["info/info-bg"] = on("--info", "--info-bg"); result["ok/panel"] = on("--ok", "--panel");
          result["white/accent"] = { fg: [255, 255, 255], bg: read("--accent").rgb };
          probe.remove();
          return result;
        });
        for (const [name, { fg, bg }] of Object.entries(pairs)) assert.ok(ratio(fg, bg) >= 4.5, `${theme} ${name} contrast ${ratio(fg, bg).toFixed(2)}`);
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
      await page.locator("#tray-btn").click();
      assert.equal(await page.locator("#tray-menu").isVisible(), true);
      await page.keyboard.press("Escape");
      await shot(page, "windows.png");
      await page.locator('[data-platform="mac"]').click();
    });

    await check("dark theme composer stays legible", async () => {
      await page.locator("#theme-btn").click();
      await page.locator('.scenario[data-scenario="translate"]').click();
      await page.waitForFunction(() => document.querySelector(".row-text"));
      await shot(page, "dark.png");
      await page.locator("#mic").click();
      await page.locator("#theme-btn").click();
    });

    await check("390px viewport: no horizontal overflow and the composer never covers the target input", async () => {
      await page.setViewportSize({ width: 390, height: 900 });
      for (const name of ["settings", "notes", "play"]) {
        await page.locator(`[data-page="${name}"]`).click();
        const [scrollWidth, innerWidth] = await page.evaluate(() => [document.documentElement.scrollWidth, innerWidth]);
        assert.ok(scrollWidth <= innerWidth + 1, `${name} overflows`);
      }
      for (const name of ["pair", "document", "meeting"]) {
        await page.locator(`.scenario[data-scenario="${name}"]`).click();
        if (name === "pair") await page.locator("#mic").click();
        const box = await composer.boundingBox(), field = await page.locator(".field").boundingBox(), desk = await page.locator("#desktop").boundingBox();
        assert.ok(box.y + box.height <= field.y, `${name}: composer covers the input`);
        assert.ok(box.y >= desk.y + 30, `${name}: composer is clipped at the top`);
        assert.ok(box.x >= 0 && box.x + box.width <= 390, `${name}: composer outside viewport`);
      }
      await page.waitForFunction(() => document.querySelectorAll(".unit .row-text").length === 5);
      await shot(page, "mobile.png", "#desktop");
      await page.setViewportSize({ width: 1480, height: 1000 });
    });

    await check("design budget: few controls at rest and no text below 12px", async () => {
      // A lower bound guards against measuring a half-rendered (animating) composer.
      assert.ok(metrics.idle.controls >= 6 && metrics.idle.controls <= 9, `idle controls ${metrics.idle.controls}`);
      assert.ok(metrics.live.controls >= 6 && metrics.live.controls <= 9, `live controls ${metrics.live.controls}`);
      assert.ok(metrics.review.controls >= 6 && metrics.review.controls <= 10, `review controls ${metrics.review.controls}`);
      for (const [state, value] of Object.entries(metrics)) { assert.deepEqual(value.small, [], state); assert.ok(value.fontSizes <= 6, `${state} font sizes ${value.fontSizes}`); }
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
