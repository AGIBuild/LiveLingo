using System.Text.Json.Nodes;
using LiveLingo.Desktop.Composer;
using LiveLingo.Desktop.Web;
using Microsoft.Playwright;

namespace LiveLingo.Web.Tests;

/// <summary>What the page sends when the user acts, parsed by the same parser the host uses.</summary>
public sealed class ComposerInteractionTests(BrowserFixture browsers)
{
    public static TheoryData<string> Engines => new() { "webkit", "chromium" };

    // --- keyboard -------------------------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task Enter_InTheSourceCommitsFromTheKey(string engine)
    {
        await using var page = await browsers.OpenAsync(engine, "translate-ready");
        await page.Page.Locator("#src").FocusAsync();

        await page.Page.Keyboard.PressAsync("Enter");

        Assert.True((await page.LastIntentAsync<CommitIntent>()).FromKey);
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task ShiftEnter_IsANewlineAndNeverCommits(string engine)
    {
        await using var page = await browsers.OpenAsync(engine, "translate-ready");
        await page.Page.Locator("#src").FocusAsync();

        await page.Page.Keyboard.PressAsync("Shift+Enter");

        Assert.Equal(0, await page.CountAsync<CommitIntent>());
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task EnterThatConfirmsAnImeComposition_NeverCommits(string engine)
    {
        await using var page = await browsers.OpenAsync(engine, "translate-ready");

        // WKWebView reports the confirming Enter with isComposing=false and keyCode 229 (measured on the Apple M2).
        await page.Page.EvaluateAsync(@"() => {
            const src = document.getElementById('src');
            src.focus();
            src.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', keyCode: 229, bubbles: true, cancelable: true }));
            src.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', isComposing: true, bubbles: true, cancelable: true }));
        }");

        Assert.Equal(0, await page.CountAsync<CommitIntent>());
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task CtrlEnterMode_CommitsOnlyWithTheChord(string engine)
    {
        await using var page = await browsers.OpenAsync(engine, "cloud-ctrl-enter");
        await page.Page.Locator("#src").FocusAsync();

        await page.Page.Keyboard.PressAsync("Enter");
        Assert.Equal(0, await page.CountAsync<CommitIntent>());

        await page.Page.Keyboard.PressAsync("Control+Enter");
        Assert.True((await page.LastIntentAsync<CommitIntent>()).FromKey);
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task Escape_HidesTheComposer(string engine)
    {
        await using var page = await browsers.OpenAsync(engine, "translate-ready");
        await page.Page.Locator("#src").FocusAsync();

        await page.Page.Keyboard.PressAsync("Escape");

        Assert.Equal(1, await page.CountAsync<CancelIntent>());
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task Escape_ClosesAnOpenMenuFirstAndGivesFocusBack(string engine)
    {
        await using var page = await browsers.OpenAsync(engine, "translate-ready");
        await page.Page.Locator("#out-pill").ClickAsync();

        await page.Page.Keyboard.PressAsync("Escape");

        Assert.Equal(0, await page.Page.Locator(".menu").CountAsync());
        Assert.Equal("out-pill", await page.Page.EvaluateAsync<string>("document.activeElement.id"));
        Assert.Equal(0, await page.CountAsync<CancelIntent>());
    }

    // --- source text ----------------------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task Typing_SendsTheTextWithGrowingRevisions(string engine)
    {
        await using var page = await browsers.OpenAsync(engine, "translate-ready");
        var src = page.Page.Locator("#src");

        await src.PressSequentiallyAsync("ab");

        var sent = (await page.IntentsAsync()).OfType<SourceIntent>().ToList();
        Assert.Equal(["先检查缓存。然后更新索引。a", "先检查缓存。然后更新索引。ab"], sent.Select(s => s.Text));
        Assert.True(sent[0].Rev > 1 && sent[1].Rev == sent[0].Rev + 1);
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task Snapshot_NeverOverwritesTypingUnlessTheHostChangedTheText(string engine)
    {
        await using var page = await browsers.OpenAsync(engine, "translate-ready");
        var src = page.Page.Locator("#src");
        await src.FillAsync("typed");
        var rev = (await page.LastIntentAsync<SourceIntent>()).Rev;

        await page.PushAsync("translate-ready", s => { s["source"]!["text"] = "echo"; s["source"]!["sourceRev"] = rev; });
        Assert.Equal("typed", await src.InputValueAsync());

        await page.PushAsync("translate-ready", s => { s["source"]!["text"] = "from speech"; s["source"]!["sourceRev"] = rev + 1; });
        Assert.Equal("from speech", await src.InputValueAsync());
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task Composition_SendsOnlyTheConfirmedText(string engine)
    {
        await using var page = await browsers.OpenAsync(engine, "empty");
        var src = page.Page.Locator("#src");
        await src.EvaluateAsync("e => e.dispatchEvent(new CompositionEvent('compositionstart'))");

        await src.FillAsync("你好");
        await page.PushAsync("empty", s => { s["source"]!["text"] = "host"; s["source"]!["sourceRev"] = 9; });

        Assert.Equal(0, await page.CountAsync<SourceIntent>());
        Assert.Equal("你好", await src.InputValueAsync());

        await src.EvaluateAsync("e => e.dispatchEvent(new CompositionEvent('compositionend'))");
        Assert.Equal("你好", (await page.LastIntentAsync<SourceIntent>()).Text);
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task ReadOnlySource_RejectsTyping(string engine)
    {
        await using var page = await browsers.OpenAsync(engine, "translate-ready");
        await page.PushAsync("translate-ready", s => s["source"]!["editable"] = false);

        Assert.True(await page.Page.Locator("#src").GetAttributeAsync("readonly") is not null);
    }

    // --- buttons --------------------------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task Buttons_SendTheirIntents(string engine)
    {
        await using var page = await browsers.OpenAsync(engine, "translate-ready");

        await page.Page.Locator("#commit").ClickAsync();
        Assert.False((await page.LastIntentAsync<CommitIntent>()).FromKey);
        await page.Page.Locator("#copy").ClickAsync();
        Assert.Equal(1, await page.CountAsync<CopyIntent>());
        await page.Page.Locator("#close").ClickAsync();
        Assert.Equal(1, await page.CountAsync<HideIntent>());
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task Mic_PointerSendsDownThenUpAndNoToggle(string engine)
    {
        await using var page = await browsers.OpenAsync(engine, "translate-ready");
        var box = (await page.Page.Locator("#mic").BoundingBoxAsync())!;

        await page.Page.Mouse.MoveAsync(box.X + box.Width / 2, box.Y + box.Height / 2);
        await page.Page.Mouse.DownAsync();
        await page.Page.Mouse.UpAsync();

        Assert.Equal(["MicDownIntent", "MicUpIntent"],
            (await page.IntentsAsync()).Where(i => i is MicDownIntent or MicUpIntent or MicToggleIntent).Select(i => i.GetType().Name));
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task Mic_SpaceToggles(string engine)
    {
        await using var page = await browsers.OpenAsync(engine, "translate-ready");
        await page.Page.Locator("#mic").FocusAsync();

        await page.Page.Keyboard.PressAsync("Space");

        Assert.Equal(1, await page.CountAsync<MicToggleIntent>());
        Assert.Equal(0, await page.CountAsync<MicDownIntent>());
    }

    // --- sentences ------------------------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task RowTools_EditAndUseOriginalSendTheSentenceIndex(string engine)
    {
        await using var page = await browsers.OpenAsync(engine, "gate-warning");
        var original = page.Page.Locator("[aria-label*='改用原话']").First;
        var index = int.Parse(System.Text.RegularExpressions.Regex.Match(await original.GetAttributeAsync("aria-label") ?? "", @"\d+").Value) - 1;

        await original.ClickAsync();
        Assert.Equal(index, (await page.LastIntentAsync<UseOriginalIntent>()).I);

        await page.Page.Locator($"[aria-label='编辑第 {index + 1} 句']").ClickAsync();
        Assert.Equal(index, (await page.LastIntentAsync<EditIntent>()).I);
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task FailedRow_RetryAndManualSendTheSentenceIndex(string engine)
    {
        await using var page = await browsers.OpenAsync(engine, "row-failed");

        await page.Page.GetByRole(AriaRole.Button, new() { Name = ComposerPage.Str("rowRetry") }).ClickAsync();
        await page.Page.GetByRole(AriaRole.Button, new() { Name = ComposerPage.Str("rowManual") }).ClickAsync();

        Assert.IsType<RetryIntent>((await page.IntentsAsync()).Single(i => i is RetryIntent));
        Assert.IsType<EditIntent>((await page.IntentsAsync()).Single(i => i is EditIntent));
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task Editing_StartsWithTheHostTextFocusedAndReportsEveryChange(string engine)
    {
        await using var page = await browsers.OpenAsync(engine, "editing");
        var edit = page.Page.Locator(".edit");

        Assert.Equal("C:先检查缓存。", await edit.InputValueAsync());
        await page.ReceiveAsync("focus", new JsonObject { ["target"] = "edit" });
        Assert.Equal("edit-0", await page.Page.EvaluateAsync<string>("document.activeElement.id"));

        await edit.PressSequentiallyAsync("!");
        Assert.Equal("C:先检查缓存。!", (await page.LastIntentAsync<EditTextIntent>()).Text);

        await page.Page.Keyboard.PressAsync("Enter");
        Assert.Equal(1, await page.CountAsync<SaveEditIntent>());
        Assert.Equal(0, await page.CountAsync<CommitIntent>());
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task Editing_ButtonsSaveAndCancel(string engine)
    {
        await using var page = await browsers.OpenAsync(engine, "editing");

        await page.Page.GetByRole(AriaRole.Button, new() { Name = ComposerPage.Str("rowSave") }).ClickAsync();
        await page.Page.GetByRole(AriaRole.Button, new() { Name = ComposerPage.Str("rowCancel") }).ClickAsync();

        Assert.Equal(1, await page.CountAsync<SaveEditIntent>());
        Assert.Equal(1, await page.CountAsync<CancelEditIntent>());
    }

    // --- menus ----------------------------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task OutputMenu_FocusesTheCheckedItemAndPicksWithTheKeyboard(string engine)
    {
        await using var page = await browsers.OpenAsync(engine, "translate-ready");

        await page.Page.Locator("#out-pill").ClickAsync();
        Assert.Equal("true", await page.Page.EvaluateAsync<string>("document.activeElement.getAttribute('aria-checked')"));
        Assert.Equal(4, await page.Page.Locator(".menu button").CountAsync());

        await page.Page.Keyboard.PressAsync("ArrowDown");
        await page.Page.Keyboard.PressAsync("Enter");

        Assert.Equal(ComposerOutput.Original, (await page.LastIntentAsync<OutputIntent>()).Value);
        Assert.Equal(0, await page.Page.Locator(".menu").CountAsync());
        Assert.Equal(0, await page.CountAsync<CommitIntent>());
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task OutputMenu_ArrowKeysWrapAndHomeEndJump(string engine)
    {
        await using var page = await browsers.OpenAsync(engine, "translate-ready");
        await page.Page.Locator("#out-pill").ClickAsync();
        const string active = "document.activeElement.textContent";

        await page.Page.Keyboard.PressAsync("End");
        Assert.Contains("互换", await page.Page.EvaluateAsync<string>(active));
        await page.Page.Keyboard.PressAsync("ArrowDown");
        Assert.Contains("整理", await page.Page.EvaluateAsync<string>(active));
        await page.Page.Keyboard.PressAsync("ArrowUp");
        Assert.Contains("互换", await page.Page.EvaluateAsync<string>(active));
        await page.Page.Keyboard.PressAsync("Home");
        Assert.Contains("整理", await page.Page.EvaluateAsync<string>(active));
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task OutputMenu_SwapSendsSwapLanguage(string engine)
    {
        await using var page = await browsers.OpenAsync(engine, "translate-ready");
        await page.Page.Locator("#out-pill").ClickAsync();

        await page.Page.Locator(".menu button", new() { HasText = "互换" }).ClickAsync();

        Assert.Equal(1, await page.CountAsync<SwapLanguageIntent>());
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task Menu_ClosesOnAPointerPressElsewhere(string engine)
    {
        await using var page = await browsers.OpenAsync(engine, "translate-ready");
        await page.Page.Locator("#out-pill").ClickAsync();

        await page.Page.Locator("#status").ClickAsync();

        Assert.Equal(0, await page.Page.Locator(".menu").CountAsync());
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task OpeningAnotherMenu_ReplacesTheOpenOne(string engine)
    {
        await using var page = await browsers.OpenAsync(engine, "translate-ready");
        await page.Page.Locator("#out-pill").ClickAsync();

        await page.Page.Locator("#dest").ClickAsync();

        Assert.Equal(1, await page.Page.Locator(".menu").CountAsync());
        Assert.Equal("true", await page.Page.Locator("#dest").GetAttributeAsync("aria-expanded"));
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task TargetMenu_PicksCopyOnly(string engine)
    {
        await using var page = await browsers.OpenAsync(engine, "translate-ready");
        await page.Page.Locator("#dest").ClickAsync();

        await page.Page.Locator(".menu button", new() { HasText = "仅复制" }).ClickAsync();

        Assert.True((await page.LastIntentAsync<CopyOnlyIntent>()).On);
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task MoreMenu_SwitchesStayOpenAndSendTheirNewValue(string engine)
    {
        await using var page = await browsers.OpenAsync(engine, "translate-ready");
        await page.Page.Locator("#more").ClickAsync();

        await page.Page.Locator(".menu button", new() { HasText = "宽屏" }).ClickAsync();
        await page.Page.Locator(".menu button", new() { HasText = "整段" }).ClickAsync();

        Assert.True((await page.LastIntentAsync<WideIntent>()).On);
        Assert.True((await page.LastIntentAsync<WholeIntent>()).On);
        Assert.Equal(1, await page.Page.Locator(".menu").CountAsync());
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task MoreMenu_ACheckedSwitchSendsOff(string engine)
    {
        await using var page = await browsers.OpenAsync(engine, "whole-wide", BrowserFixture.WideWidth);
        await page.PushAsync("whole-wide", s => s["menus"]!["more"]![4]!["checked"] = true);
        await page.Page.Locator("#more").ClickAsync();

        await page.Page.Locator(".menu button", new() { HasText = "宽屏" }).ClickAsync();

        Assert.False((await page.LastIntentAsync<WideIntent>()).On);
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task MoreMenu_CommandsSendTheirIntentAndClose(string engine)
    {
        await using var page = await browsers.OpenAsync(engine, "translate-ready");

        await page.Page.Locator("#more").ClickAsync();
        await page.Page.Locator(".menu button", new() { HasText = "新草稿" }).ClickAsync();
        await page.Page.Locator("#more").ClickAsync();
        await page.Page.Locator(".menu button", new() { HasText = "设置" }).ClickAsync();

        Assert.Equal(1, await page.CountAsync<NewDraftIntent>());
        Assert.Equal("general", (await page.LastIntentAsync<OpenSettingsIntent>()).Section);
        Assert.Equal(0, await page.Page.Locator(".menu").CountAsync());
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task MoreMenu_DisabledItemsCannotBePicked(string engine)
    {
        await using var page = await browsers.OpenAsync(engine, "translate-ready");
        await page.Page.Locator("#more").ClickAsync();

        Assert.True(await page.Page.Locator(".menu button", new() { HasText = "导入" }).IsDisabledAsync());
    }

    // --- dialogs --------------------------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task ExportDialog_OffersSrtOnlyWithATimelineAndSendsTheFormat(string engine)
    {
        await using var plain = await browsers.OpenAsync(engine, "translate-ready");
        await OpenDialogAsync(plain, "导出");
        Assert.True(await plain.Page.GetByRole(AriaRole.Button, new() { Name = "SRT" }).IsDisabledAsync());

        await using var timeline = await browsers.OpenAsync(engine, "timeline");
        await OpenDialogAsync(timeline, "导出");
        await timeline.Page.GetByRole(AriaRole.Button, new() { Name = "SRT" }).ClickAsync();

        Assert.Equal(ExportFormat.Srt, (await timeline.LastIntentAsync<ExportIntent>()).Format);
        Assert.Equal(0, await timeline.Page.Locator(".dialog").CountAsync());
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task Dialog_ReplacesTheBodyAndEscapeClosesItBeforeAnythingElse(string engine)
    {
        await using var page = await browsers.OpenAsync(engine, "translate-ready");
        await OpenDialogAsync(page, "导出");

        Assert.Contains("dialog-open", await page.Page.Locator("#composer").GetAttributeAsync("class"));
        Assert.True(await page.Page.Locator(".c-body").IsHiddenAsync());

        await page.Page.Keyboard.PressAsync("Escape");

        Assert.Equal(0, await page.Page.Locator(".dialog").CountAsync());
        Assert.Equal(0, await page.CountAsync<CancelIntent>());
        Assert.Equal("more", await page.Page.EvaluateAsync<string>("document.activeElement.id"));
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task RecentDialog_ListsDraftsRestoresOneAndClears(string engine)
    {
        await using var page = await browsers.OpenAsync(engine, "translate-ready");
        await page.PushAsync("translate-ready", s =>
        {
            s["dialogs"]!["recent"] = new JsonArray(new JsonObject { ["index"] = 3, ["label"] = "昨天的草稿" });
            s["dialogs"]!["canRestore"] = true;
        });
        await OpenDialogAsync(page, "最近");

        await page.Page.GetByRole(AriaRole.Button, new() { Name = ComposerPage.Str("recentClear") }).ClickAsync();
        Assert.Equal(1, await page.CountAsync<ClearRecentIntent>());

        await page.Page.GetByRole(AriaRole.Button, new() { Name = ComposerPage.Str("recentRestore") }).ClickAsync();
        Assert.Equal(3, (await page.LastIntentAsync<RestoreRecentIntent>()).Index);
        Assert.Equal(0, await page.Page.Locator(".dialog").CountAsync());
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task RecentDialog_WithoutDraftsSaysSo(string engine)
    {
        await using var page = await browsers.OpenAsync(engine, "translate-ready");

        await OpenDialogAsync(page, "最近");

        Assert.Contains(ComposerPage.Str("recentEmpty"), await page.Page.Locator(".dialog").InnerTextAsync());
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task HostOpenMenu_OpensTheTargetMenu(string engine)
    {
        await using var page = await browsers.OpenAsync(engine, "target-lost");

        await page.ReceiveAsync("openMenu", new JsonObject { ["id"] = "target" });

        Assert.Equal(1, await page.Page.Locator(".menu").CountAsync());
    }

    // --- window size ----------------------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task Size_ReportsTheContentAndGrowsWithTheResult(string engine)
    {
        await using var page = await browsers.OpenAsync(engine, "empty");
        await page.Page.WaitForFunctionAsync("() => window.__raw.some(s => JSON.parse(s).name === 'size')");
        var empty = await page.LastIntentAsync<SizeIntent>();

        await page.ClearIntentsAsync();
        await page.PushAsync("translate-ready");
        await page.WaitForIntentAsync("size");
        var ready = await page.LastIntentAsync<SizeIntent>();

        Assert.Equal(BrowserFixture.NarrowWidth, empty.Width);
        Assert.Equal(BrowserFixture.NarrowWidth, ready.Width);
        Assert.True(ready.Height > empty.Height, $"{ready.Height} should exceed {empty.Height}");
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task Size_ReportsTheWideWidthAndNeverTheSameSizeTwice(string engine)
    {
        await using var page = await browsers.OpenAsync(engine, "whole-wide", BrowserFixture.WideWidth);
        await page.Page.WaitForFunctionAsync("() => window.__raw.some(s => JSON.parse(s).name === 'size')");
        await page.PushAsync("whole-wide");
        await page.PushAsync("whole-wide");
        await page.Page.WaitForTimeoutAsync(100);

        var sizes = (await page.IntentsAsync()).OfType<SizeIntent>().ToList();

        Assert.Equal(BrowserFixture.WideWidth, sizes[^1].Width);
        Assert.Equal(sizes.Count, sizes.Distinct().Count());
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task Size_IncludesAnOpenMenuThatExtendsBelowThePanel(string engine)
    {
        await using var page = await browsers.OpenAsync(engine, "empty");
        await page.Page.WaitForFunctionAsync("() => window.__raw.some(s => JSON.parse(s).name === 'size')");
        await page.ClearIntentsAsync();

        await page.Page.Locator("#more").ClickAsync();
        await page.WaitForIntentAsync("size");

        var menuBottom = await page.Page.EvaluateAsync<double>("document.querySelector('.menu').getBoundingClientRect().bottom");
        Assert.True((await page.LastIntentAsync<SizeIntent>()).Height >= menuBottom + 18 - 1);
    }

    private static async Task OpenDialogAsync(ComposerPage page, string item)
    {
        await page.Page.Locator("#more").ClickAsync();
        await page.Page.Locator(".menu button", new() { HasText = item }).ClickAsync();
    }
}
