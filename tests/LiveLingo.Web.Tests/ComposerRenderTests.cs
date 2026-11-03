using System.Text.Json.Nodes;
using Microsoft.Playwright;

namespace LiveLingo.Web.Tests;

/// <summary>What the page draws for each golden snapshot, in both engines.</summary>
public sealed class ComposerRenderTests(BrowserFixture browsers)
{
    [Theory]
    [InlineData("webkit")]
    [InlineData("chromium")]
    public async Task FinalPolish_IsClearlyMarkedAndCannotBeCopiedEarly(string engine)
    {
        await using var page = await browsers.OpenAsync(engine, "polishing");
        Assert.Contains("正在润色全文", await page.Page.Locator("#status").InnerTextAsync());
        Assert.True(await page.Page.Locator("#copy").IsDisabledAsync());
        Assert.True(await page.Page.Locator("#commit").IsEnabledAsync());
    }

    public static TheoryData<string> Engines => new() { "webkit", "chromium" };

    public static TheoryData<string, string> BannerFixtures => new()
    {
        { "webkit", "model-missing" }, { "webkit", "downloading" }, { "webkit", "download-failed" }, { "webkit", "mic-denied" },
        { "webkit", "no-microphone" }, { "webkit", "write-failed" }, { "webkit", "target-lost" }, { "webkit", "no-speech" },
        { "chromium", "model-missing" }, { "chromium", "downloading" }, { "chromium", "download-failed" }, { "chromium", "mic-denied" },
        { "chromium", "no-microphone" }, { "chromium", "write-failed" }, { "chromium", "target-lost" }, { "chromium", "no-speech" },
    };

    private static string[] AllFixtures() =>
        [.. Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Fixtures"), "*.json")
            .Select(Path.GetFileNameWithoutExtension).Where(n => n != "strings" && !n!.StartsWith("settings-", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)!];

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task AnEmptyComposer_OffersTheUnsentDraftAsOneClick_AndShowsNothingOtherwise(string engine)
    {
        await using var none = await browsers.OpenAsync(engine, "empty");
        Assert.Equal(0, await none.Page.Locator("#resume").CountAsync());

        await using var page = await browsers.OpenAsync(engine, "resume-draft");

        Assert.Equal("", await page.Page.Locator("#src").InputValueAsync());
        var resume = page.Page.Locator("#resume");
        Assert.Contains(ComposerPage.Str("resume"), await resume.InnerTextAsync());
        Assert.Contains("这个 PR 先不要合并", await resume.InnerTextAsync());

        await resume.ClickAsync();

        Assert.Equal(0, (await page.LastIntentAsync<LiveLingo.Desktop.Web.RestoreRecentIntent>()).Index);
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task WhileTheSpeechModelsAreGettingReady_TheEmptyComposerSaysSo(string engine)
    {
        await using var ready = await browsers.OpenAsync(engine, "empty");
        Assert.Equal("", (await ready.Page.Locator("#status").InnerTextAsync()).Trim());

        await using var preparing = await browsers.OpenAsync(engine, "speech-preparing");

        Assert.Contains("语音模型准备中", await preparing.Page.Locator("#status").InnerTextAsync());
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task ARememberedChoice_IsSaidUnderTheHintWithOneClickBack_AndTheOutputMenuSaysWhoseChoicesAreKept(string engine)
    {
        await using var none = await browsers.OpenAsync(engine, "empty");
        Assert.Equal(0, await none.Page.Locator("#remembered").CountAsync());

        await using var page = await browsers.OpenAsync(engine, "remembered");

        var note = page.Page.Locator("#remembered");
        Assert.Contains("Slack", await note.InnerTextAsync());
        await page.Page.Locator("#restore-defaults").ClickAsync();
        Assert.IsType<LiveLingo.Desktop.Web.RestoreDefaultsIntent>((await page.IntentsAsync()).Last());

        await page.Page.Locator("#out-pill").ClickAsync();
        Assert.Contains("Slack", await page.Page.Locator(".menu-note").InnerTextAsync());
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task TheExportDialog_MarksAndFocusesTheFormatExportedLast(string engine)
    {
        await using var page = await browsers.OpenAsync(engine, "export-last");
        await page.Page.Locator("#more").ClickAsync();
        await page.Page.Locator(".menu button", new() { HasText = "导出" }).ClickAsync();

        var markdown = page.Page.GetByRole(AriaRole.Button, new() { Name = "Markdown" });
        Assert.Equal("true", await markdown.GetAttributeAsync("aria-current"));
        Assert.Equal(1, await page.Page.Locator(".dlg-actions .last").CountAsync());
        Assert.True(await markdown.EvaluateAsync<bool>("el => el === document.activeElement"));
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task Handshake_SendsReadyOnceAndTheFirstSnapshotDrawsTheEmptyComposer(string engine)
    {
        await using var page = await browsers.OpenAsync(engine);

        var intents = await page.IntentsAsync();
        Assert.Single(intents, i => i is LiveLingo.Desktop.Web.ReadyIntent);
        Assert.Equal(JsonNode.Parse(ComposerPage.FixtureText("strings"))!["strings"]!["placeholder"]!.GetValue<string>(),
            await page.Page.Locator("#src").GetAttributeAsync("placeholder"));
        Assert.Equal("zh-CN", await page.Page.EvaluateAsync<string>("document.documentElement.lang"));
        Assert.Equal(0, await page.Page.Locator("#res").CountAsync());
        Assert.True(await page.Page.Locator("#commit").IsDisabledAsync());
        Assert.Empty(page.ConsoleErrors);
        Assert.Empty(page.Requests);
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task NothingIsDrawnBeforeTheFirstSnapshot(string engine)
    {
        await using var page = await browsers.OpenBareAsync(engine);

        Assert.Equal(0, await page.Page.Locator("#composer").CountAsync());
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task EverySnapshot_DrawsWithoutErrorsAndIsAccessibleAndReadable(string engine)
    {
        await using var page = await browsers.OpenAsync(engine);
        var problems = new List<string>();

        foreach (var fixture in AllFixtures())
        {
            await page.PushAsync(fixture);
            var unnamed = await page.Page.EvaluateAsync<string[]>(
                "() => [...document.querySelectorAll('button, [role=img], textarea, progress')].filter(e => !(e.getAttribute('aria-label') || e.textContent.trim() || (e.labels && e.labels.length))).map(e => e.outerHTML.slice(0, 60))");
            problems.AddRange(unnamed.Select(u => $"{fixture}: no accessible name: {u}"));
            var small = await page.Page.EvaluateAsync<string[]>(
                @"() => [...document.querySelectorAll('#app *')].filter(e => [...e.childNodes].some(n => n.nodeType === 3 && n.textContent.trim()) && parseFloat(getComputedStyle(e).fontSize) < 12)
                    .map(e => e.tagName + '.' + e.className + ' ' + getComputedStyle(e).fontSize)");
            problems.AddRange(small.Select(s => $"{fixture}: text under 12px: {s}"));
        }

        Assert.Empty(problems);
        Assert.Empty(page.ConsoleErrors);
        Assert.Empty(page.Requests);
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task ReadySnapshot_ShowsOneArticlePerSentenceAndTheCommitLabel(string engine)
    {
        await using var page = await browsers.OpenAsync(engine, "translate-ready");

        Assert.Equal(["T:先检查缓存。", "T:然后更新索引。"], await page.Page.Locator("#rows .row-text").AllInnerTextsAsync());
        Assert.Equal("写入 Slack ↵", (await page.Page.Locator("#commit").InnerTextAsync()).Replace("\n", " ").Trim());
        Assert.Equal("先检查缓存。然后更新索引。", await page.Page.Locator("#src").InputValueAsync());
        Assert.Equal("Slack", (await page.Page.Locator("#dest").InnerTextAsync()).Trim());
        Assert.Equal("→ English", (await page.Page.Locator("#out-pill").InnerTextAsync()).Trim());
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task PendingSnapshot_ShowsSkeletonsWithAnAnnouncedLabel(string engine)
    {
        await using var page = await browsers.OpenAsync(engine, "translate-pending");

        Assert.True(await page.Page.Locator(".skel").CountAsync() >= 2);
        Assert.Equal(ComposerPage.Str("rowProcessing"), (await page.Page.Locator(".unit .sr-only").First.InnerTextAsync()).Trim());
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task ArmedCommit_ShowsASpinnerAndNoKeyCap(string engine)
    {
        await using var page = await browsers.OpenAsync(engine, "translate-armed");

        Assert.Equal(1, await page.Page.Locator("#commit .spinner").CountAsync());
        Assert.Equal(0, await page.Page.Locator("#commit kbd").CountAsync());
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task WrittenSnapshot_ShowsTheWrittenLabelWithoutAKeyCap(string engine)
    {
        await using var page = await browsers.OpenAsync(engine, "written");

        Assert.Equal(0, await page.Page.Locator("#commit kbd").CountAsync());
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task FailedRow_ShowsItsErrorWithRetryAndManualEdit(string engine)
    {
        await using var page = await browsers.OpenAsync(engine, "row-failed");

        Assert.Equal(1, await page.Page.Locator(".row-error").CountAsync());
        Assert.Equal(1, await page.Page.GetByRole(AriaRole.Button, new() { Name = ComposerPage.Str("rowRetry") }).CountAsync());
        Assert.Equal(1, await page.Page.GetByRole(AriaRole.Button, new() { Name = ComposerPage.Str("rowManual") }).CountAsync());
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task GateWarning_MarksTheRowAndShowsItsNote(string engine)
    {
        await using var page = await browsers.OpenAsync(engine, "gate-warning");

        Assert.True(await page.Page.Locator(".unit.warn").CountAsync() >= 1);
        Assert.True(await page.Page.Locator(".row-note").CountAsync() >= 1);
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task ListeningSnapshot_ShowsLiveLinesWithACaretAndARecordingMic(string engine)
    {
        await using var page = await browsers.OpenAsync(engine, "listening");

        Assert.True(await page.Page.Locator("#src").IsHiddenAsync());
        Assert.True(await page.Page.Locator("#src-live .line").CountAsync() >= 1);
        Assert.Equal(1, await page.Page.Locator("#src-live .caret").CountAsync());
        Assert.Contains("recording", await page.Page.Locator("#mic").GetAttributeAsync("class"));
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task Timeline_ShowsTimestampsOnItsLines(string engine)
    {
        await using var page = await browsers.OpenAsync(engine, "timeline");

        Assert.True(await page.Page.Locator("#src-live time").CountAsync() >= 1);
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task WholeWide_ShowsTheRouteChip(string engine)
    {
        await using var page = await browsers.OpenAsync(engine, "whole-wide", BrowserFixture.WideWidth);

        Assert.Equal(1, await page.Page.Locator("#route-chip").CountAsync());
        Assert.Contains("wide", await page.Page.Locator("#composer").GetAttributeAsync("class"));
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task CopyOnly_ShowsTheWarningDestination(string engine)
    {
        await using var page = await browsers.OpenAsync(engine, "copy-only");

        Assert.Contains("warn", await page.Page.Locator("#dest").GetAttributeAsync("class"));
        Assert.Equal(0, await page.Page.Locator("#dest svg").CountAsync());
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task TargetLost_ShowsTheDestinationAsBad(string engine)
    {
        await using var page = await browsers.OpenAsync(engine, "target-lost");

        Assert.Contains("bad", await page.Page.Locator("#dest").GetAttributeAsync("class"));
    }

    [Theory]
    [MemberData(nameof(BannerFixtures))]
    public async Task Banner_ShowsItsMessageAndEachActionSendsItsIntent(string engine, string fixture)
    {
        await using var page = await browsers.OpenAsync(engine, fixture);
        var banner = JsonNode.Parse(ComposerPage.FixtureText(fixture))!["banner"]!;

        Assert.Equal(banner["message"]!.GetValue<string>(), (await page.Page.Locator("#banner p").InnerTextAsync()).Trim());
        foreach (var action in banner["actions"]!.AsArray())
        {
            await page.ClearIntentsAsync();
            await page.Page.Locator("#banner button", new() { HasText = action!["label"]!.GetValue<string>() }).ClickAsync();

            var sent = await page.LastIntentAsync<LiveLingo.Desktop.Web.BannerIntent>();
            Assert.Equal(Enum.Parse<LiveLingo.Desktop.Composer.BannerAction>(action["id"]!.GetValue<string>(), true), sent.Action);
        }
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task DownloadingBanner_ShowsAProgressBar(string engine)
    {
        await using var page = await browsers.OpenAsync(engine, "downloading");

        Assert.Equal(1, await page.Page.Locator("#banner progress").CountAsync());
        Assert.Equal(0, await page.Page.Locator("#download").CountAsync());
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task ADownloadInTheBackground_ShowsItsProgressInTheFooter_AndNoBanner(string engine)
    {
        await using var page = await browsers.OpenAsync(engine, "download-background");

        Assert.Equal(0, await page.Page.Locator("#banner").CountAsync());
        Assert.Equal("46", await page.Page.Locator("#download progress").GetAttributeAsync("value"));
        Assert.Contains("46%", await page.Page.Locator("#download").InnerTextAsync());
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task ReducedMotionSnapshot_TurnsAnimationsOff(string engine)
    {
        await using var page = await browsers.OpenAsync(engine, "reduced-motion");

        Assert.Equal("off", await page.Page.EvaluateAsync<string>("document.documentElement.dataset.motion"));
        await page.PushAsync("translate-ready");
        Assert.Equal("", await page.Page.EvaluateAsync<string>("document.documentElement.dataset.motion"));
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task Theme_FollowsTheSystemColorScheme(string engine)
    {
        await using var light = await browsers.OpenAsync(engine, "translate-ready");
        await using var dark = await browsers.OpenAsync(engine, "translate-ready", scheme: ColorScheme.Dark);

        Assert.Equal("light", await light.Page.EvaluateAsync<string>("document.documentElement.dataset.theme"));
        Assert.Equal("dark", await dark.Page.EvaluateAsync<string>("document.documentElement.dataset.theme"));
        const string panel = "getComputedStyle(document.getElementById('composer')).backgroundColor";
        Assert.NotEqual(await light.Page.EvaluateAsync<string>(panel), await dark.Page.EvaluateAsync<string>(panel));
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task Toast_ShowsTheHostText(string engine)
    {
        await using var page = await browsers.OpenAsync(engine, "translate-ready");

        await page.ReceiveAsync("toast", new JsonObject { ["text"] = "已复制" });

        Assert.Equal("已复制", await page.Page.Locator("#toast").InnerTextAsync());
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task StaleSnapshot_IsIgnored(string engine)
    {
        await using var page = await browsers.OpenAsync(engine, "translate-ready");

        await page.ReceiveAsync("composer", JsonNode.Parse(ComposerPage.FixtureText("empty")));

        Assert.Equal(2, await page.Page.Locator("#rows .unit").CountAsync());
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task FocusMessage_MovesFocusToTheRequestedControl(string engine)
    {
        await using var page = await browsers.OpenAsync(engine, "translate-ready");

        await page.ReceiveAsync("focus", new JsonObject { ["target"] = "mic" });
        Assert.Equal("mic", await page.Page.EvaluateAsync<string>("document.activeElement.id"));

        await page.ReceiveAsync("focus", new JsonObject { ["target"] = "source" });
        Assert.Equal("src", await page.Page.EvaluateAsync<string>("document.activeElement.id"));
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task UnknownMessageType_ThrowsInTheHostCall(string engine)
    {
        await using var page = await browsers.OpenAsync(engine);

        await Assert.ThrowsAnyAsync<PlaywrightException>(() => page.ReceiveAsync("surprise", null));
    }
}
