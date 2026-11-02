using System.Text.Json.Nodes;
using LiveLingo.Desktop.Composer;
using LiveLingo.Desktop.Web;
using Microsoft.Playwright;

namespace LiveLingo.Web.Tests;

/// <summary>What the page sends when the user acts, parsed by the same parser the host uses.</summary>
public sealed class ComposerInteractionTests(BrowserFixture browsers)
{
    [Theory]
    [InlineData("webkit")]
    [InlineData("chromium")]
    public async Task VisibleClearButton_ResetsTheWholeDraft(string engine)
    {
        await using var page = await browsers.OpenAsync(engine, "translate-ready");
        Assert.Equal(1, await page.Page.Locator("#clear").CountAsync());
        await page.ClearIntentsAsync();
        await page.Page.Locator("#clear").ClickAsync();
        Assert.Equal(1, await page.CountAsync<NewDraftIntent>());
    }

    [Theory]
    [InlineData("webkit")]
    [InlineData("chromium")]
    public async Task ModelDetails_AreAvailableFromTheExistingPrivacyChip(string engine)
    {
        await using var page = await browsers.OpenAsync(engine, "translate-ready");
        await page.ClearIntentsAsync();
        await page.Page.Locator("#privacy").ClickAsync();
        Assert.Equal(1, await page.CountAsync<ModelInfoIntent>());
    }

    [Theory]
    [InlineData("webkit")]
    [InlineData("chromium")]
    public async Task ClearFocus_WaitsUntilTheSourceIsVisibleAgain(string engine)
    {
        await using var page = await browsers.OpenAsync(engine, "listening");
        await page.ReceiveAsync("focus", new System.Text.Json.Nodes.JsonObject { ["target"] = "source" });
        await page.PushAsync("empty");
        Assert.Equal("src", await page.Page.EvaluateAsync<string>("document.activeElement.id"));
    }

    [Theory]
    [InlineData("webkit")]
    [InlineData("chromium")]
    public async Task Clearing_DisablesRestartAndInsertionUntilResetCompletes(string engine)
    {
        await using var page = await browsers.OpenAsync(engine, "clearing");
        Assert.True(await page.Page.Locator("#clear").IsDisabledAsync());
        Assert.True(await page.Page.Locator("#mic").IsDisabledAsync());
        Assert.True(await page.Page.Locator("#commit").IsDisabledAsync());
        Assert.Contains("清空", await page.Page.Locator("#status").InnerTextAsync());
    }

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
    public async Task Mic_ClickToggles_AndPressingAloneDoesNot(string engine)
    {
        await using var page = await browsers.OpenAsync(engine, "translate-ready");
        var box = (await page.Page.Locator("#mic").BoundingBoxAsync())!;
        await page.Page.Mouse.MoveAsync(box.X + box.Width / 2, box.Y + box.Height / 2);

        await page.Page.Mouse.DownAsync();
        Assert.Equal(0, await page.CountAsync<MicToggleIntent>());
        await page.Page.Mouse.UpAsync();

        Assert.Equal(1, await page.CountAsync<MicToggleIntent>());
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task Mic_SpaceToggles(string engine)
    {
        await using var page = await browsers.OpenAsync(engine, "translate-ready");
        await page.Page.Locator("#mic").FocusAsync();

        await page.Page.Keyboard.PressAsync("Space");

        Assert.Equal(1, await page.CountAsync<MicToggleIntent>());
    }

    // --- shortcuts ------------------------------------------------------------------------------------------------

    public static TheoryData<string, string, string> Shortcuts()
    {
        var data = new TheoryData<string, string, string>();
        foreach (var engine in new[] { "webkit", "chromium" })
        {
            data.Add(engine, "Meta+D", nameof(MicToggleIntent));
            data.Add(engine, "Meta+1", nameof(OutputIntent));
            data.Add(engine, "Meta+2", nameof(OutputIntent));
            data.Add(engine, "Meta+3", nameof(OutputIntent));
            data.Add(engine, "Meta+L", nameof(SwapLanguageIntent));
            data.Add(engine, "Meta+Shift+C", nameof(CopyIntent));
            data.Add(engine, "Meta+N", nameof(NewDraftIntent));
            data.Add(engine, "Meta+E", nameof(ZoomIntent));
            data.Add(engine, "Meta+,", nameof(OpenSettingsIntent));
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Shortcuts))]
    public async Task Shortcut_SendsTheIntentOfItsButton_FromTheTextBox(string engine, string keys, string intent)
    {
        await using var page = await browsers.OpenAsync(engine, "translate-ready");
        await page.Page.Locator("#src").FocusAsync();
        var text = await page.Page.Locator("#src").InputValueAsync();
        await page.ClearIntentsAsync();

        await page.Page.Keyboard.PressAsync(keys);

        Assert.Equal([intent], (await page.IntentsAsync()).Select(i => i.GetType().Name));
        Assert.Equal(text, await page.Page.Locator("#src").InputValueAsync());
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task OutputShortcuts_SelectTheOutputOfTheirNumber(string engine)
    {
        await using var page = await browsers.OpenAsync(engine, "translate-ready");
        await page.Page.Locator("#src").FocusAsync();
        await page.ClearIntentsAsync();

        await page.Page.Keyboard.PressAsync("Meta+1");
        await page.Page.Keyboard.PressAsync("Meta+2");
        await page.Page.Keyboard.PressAsync("Meta+3");

        Assert.Equal(
            [LiveLingo.Desktop.Composer.ComposerOutput.Clean, LiveLingo.Desktop.Composer.ComposerOutput.Translate, LiveLingo.Desktop.Composer.ComposerOutput.Original],
            (await page.IntentsAsync()).OfType<OutputIntent>().Select(i => i.Value));
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task OutputShortcut_OfAnOutputTheSettingsDoNotOffer_DoesNothing(string engine)
    {
        await using var page = await browsers.OpenAsync(engine, "no-cleanup");
        await page.ClearIntentsAsync();

        await page.Page.Keyboard.PressAsync("Meta+1");

        Assert.Equal(0, await page.CountAsync<OutputIntent>());
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task DialogShortcuts_OpenTheRecentAndTheExportDialog(string engine)
    {
        await using var page = await browsers.OpenAsync(engine, "translate-ready");

        await page.Page.Keyboard.PressAsync("Meta+R");
        Assert.Equal(ComposerPage.Str("recentTitle"), await page.Page.Locator("#dlg-title").InnerTextAsync());
        await page.Page.Keyboard.PressAsync("Escape");
        Assert.Equal(0, await page.Page.Locator(".dialog").CountAsync());

        await page.Page.Keyboard.PressAsync("Meta+S");
        Assert.Equal(ComposerPage.Str("exportTitle"), await page.Page.Locator("#dlg-title").InnerTextAsync());
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task ActionsThatAreNotAvailable_AreNotRunByTheirShortcut(string engine)
    {
        // Nothing to copy in an empty composer; importing is only for an empty one, so it is available here.
        await using var empty = await browsers.OpenAsync(engine, "empty");
        await empty.ClearIntentsAsync();
        await empty.Page.Keyboard.PressAsync("Meta+Shift+C");
        Assert.Equal(0, await empty.CountAsync<CopyIntent>());
        await empty.Page.Keyboard.PressAsync("Meta+O");
        Assert.Equal(1, await empty.CountAsync<ImportIntent>());

        await using var typed = await browsers.OpenAsync(engine, "translate-ready");
        await typed.ClearIntentsAsync();
        await typed.Page.Keyboard.PressAsync("Meta+O");
        Assert.Equal(0, await typed.CountAsync<ImportIntent>());
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task Shortcuts_FollowTheModifierOfTheSnapshot(string engine)
    {
        await using var page = await browsers.OpenAsync(engine, "translate-ready");
        await page.PushAsync("translate-ready", s => s["options"]!["modifier"] = "ctrl");
        await page.ClearIntentsAsync();

        await page.Page.Keyboard.PressAsync("Meta+D");
        Assert.Equal(0, await page.CountAsync<MicToggleIntent>());
        await page.Page.Keyboard.PressAsync("Control+D");
        Assert.Equal(1, await page.CountAsync<MicToggleIntent>());
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task PlainKeysAndOtherChords_DoNotTriggerAShortcut(string engine)
    {
        await using var page = await browsers.OpenAsync(engine, "empty");
        await page.Page.Locator("#src").FocusAsync();
        await page.ClearIntentsAsync();

        await page.Page.Keyboard.PressAsync("D");
        await page.Page.Keyboard.PressAsync("Meta+Alt+D");
        await page.Page.Keyboard.PressAsync("Meta+Shift+D");
        await page.Page.Keyboard.PressAsync("Control+D");

        Assert.DoesNotContain((await page.IntentsAsync()), i => i is not SourceIntent);
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task AShortcutInTheMiddleOfAnImeComposition_IsNotRun(string engine)
    {
        await using var page = await browsers.OpenAsync(engine, "empty");
        await page.ClearIntentsAsync();

        await page.Page.EvaluateAsync(@"() => document.getElementById('src').dispatchEvent(
            new KeyboardEvent('keydown', { code: 'KeyD', key: 'd', metaKey: true, isComposing: true, bubbles: true, cancelable: true }))");

        Assert.Equal(0, await page.CountAsync<MicToggleIntent>());
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task ShortcutsAreSuspendedWhileADialogIsOpen(string engine)
    {
        await using var page = await browsers.OpenAsync(engine, "translate-ready");
        await page.Page.Keyboard.PressAsync("Meta+R");
        await page.ClearIntentsAsync();

        await page.Page.Keyboard.PressAsync("Meta+D");

        Assert.Equal(0, await page.CountAsync<MicToggleIntent>());
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task AShortcutWhileAMenuIsOpen_ClosesTheMenuAndRuns(string engine)
    {
        await using var page = await browsers.OpenAsync(engine, "translate-ready");
        await page.Page.Locator("#more").ClickAsync();
        await page.ClearIntentsAsync();

        await page.Page.Keyboard.PressAsync("Meta+N");

        Assert.Equal(1, await page.CountAsync<NewDraftIntent>());
        Assert.Equal(0, await page.Page.Locator(".menu").CountAsync());
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task TheKeyCapsOfTheShortcuts_AreShownInTheMenusAndOnTheMicrophone(string engine)
    {
        await using var page = await browsers.OpenAsync(engine, "translate-ready");

        Assert.Contains("⌘D", await page.Page.Locator("#mic").GetAttributeAsync("aria-label"));
        await page.Page.Locator("#more").ClickAsync();
        var menu = await page.Page.Locator(".menu").InnerTextAsync();
        Assert.Contains("⌘N", menu);
        Assert.Contains("⌘,", menu);
        await page.Page.Keyboard.PressAsync("Escape");
        await page.Page.Locator("#out-pill").ClickAsync();
        var output = await page.Page.Locator(".menu").InnerTextAsync();
        Assert.Contains("⌘1", output);
        Assert.Contains("⌘2", output);
        Assert.Contains("⌘L", output);
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

    // --- moving and fitting the window ------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task DraggingTheHeader_ReportsTheDragOnce(string engine)
    {
        await using var page = await browsers.OpenAsync(engine, "translate-ready");
        var spacer = (await page.Page.Locator(".c-head .spacer").BoundingBoxAsync())!;
        var (x, y) = (spacer.X + spacer.Width / 2, spacer.Y + spacer.Height / 2);
        await page.ClearIntentsAsync();

        await page.Page.Mouse.MoveAsync(x, y);
        await page.Page.Mouse.DownAsync();
        await page.Page.Mouse.MoveAsync(x + 20, y + 10, new() { Steps = 5 });
        await page.Page.Mouse.MoveAsync(x + 60, y + 30, new() { Steps = 5 });
        await page.Page.Mouse.UpAsync();

        Assert.Equal(1, await page.CountAsync<DragIntent>());
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task AClickOnTheHeaderThatBarelyMoves_IsNotADrag(string engine)
    {
        await using var page = await browsers.OpenAsync(engine, "translate-ready");
        var spacer = (await page.Page.Locator(".c-head .spacer").BoundingBoxAsync())!;
        var (x, y) = (spacer.X + spacer.Width / 2, spacer.Y + spacer.Height / 2);
        await page.ClearIntentsAsync();

        await page.Page.Mouse.MoveAsync(x, y);
        await page.Page.Mouse.DownAsync();
        await page.Page.Mouse.MoveAsync(x + 2, y + 1);
        await page.Page.Mouse.UpAsync();
        await page.Page.Mouse.MoveAsync(x + 80, y);

        Assert.Equal(0, await page.CountAsync<DragIntent>());
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task PressingAControlInTheHeader_IsNotADrag(string engine)
    {
        await using var page = await browsers.OpenAsync(engine, "translate-ready");
        var pill = (await page.Page.Locator("#out-pill").BoundingBoxAsync())!;
        await page.ClearIntentsAsync();

        await page.Page.Mouse.MoveAsync(pill.X + 5, pill.Y + 5);
        await page.Page.Mouse.DownAsync();
        await page.Page.Mouse.MoveAsync(pill.X + 40, pill.Y + 30, new() { Steps = 5 });
        await page.Page.Mouse.UpAsync();

        Assert.Equal(0, await page.CountAsync<DragIntent>());
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task PressingTheBody_IsNotADrag(string engine)
    {
        await using var page = await browsers.OpenAsync(engine, "translate-ready");
        var footer = (await page.Page.Locator(".c-foot").BoundingBoxAsync())!;
        await page.ClearIntentsAsync();

        await page.Page.Mouse.MoveAsync(footer.X + footer.Width / 2, footer.Y + 2);
        await page.Page.Mouse.DownAsync();
        await page.Page.Mouse.MoveAsync(footer.X + footer.Width / 2 + 60, footer.Y + 2, new() { Steps = 5 });
        await page.Page.Mouse.UpAsync();

        Assert.Equal(0, await page.CountAsync<DragIntent>());
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task DraggingTheRightEdge_AsksForTheWindowWidthOnly(string engine)
    {
        await using var page = await browsers.OpenAsync(engine, "translate-ready");
        var width = await page.Page.EvaluateAsync<int>("window.innerWidth");
        var edge = (await page.Page.Locator(".rz-r").BoundingBoxAsync())!;
        var (x, y) = (edge.X + edge.Width / 2, edge.Y + edge.Height / 2);
        await page.ClearIntentsAsync();

        await page.Page.Mouse.MoveAsync(x, y);
        await page.Page.Mouse.DownAsync();
        await page.Page.Mouse.MoveAsync(x + 40, y, new() { Steps = 4 });
        await page.Page.Mouse.MoveAsync(x + 90, y + 30, new() { Steps = 4 });
        await page.Page.Mouse.UpAsync();

        var asked = await page.LastIntentAsync<ResizeIntent>();
        Assert.Equal(width + 90, asked.Width);
        Assert.Null(asked.Height);
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task DraggingTheCorner_AsksForTheWidthAndTheHeight(string engine)
    {
        await using var page = await browsers.OpenAsync(engine, "translate-ready");
        var size = await page.Page.EvaluateAsync<int[]>("[window.innerWidth, window.innerHeight]");
        var corner = (await page.Page.Locator(".rz-br").BoundingBoxAsync())!;
        var (x, y) = (corner.X + corner.Width / 2, corner.Y + corner.Height / 2);
        await page.ClearIntentsAsync();

        await page.Page.Mouse.MoveAsync(x, y);
        await page.Page.Mouse.DownAsync();
        await page.Page.Mouse.MoveAsync(x + 50, y + 70, new() { Steps = 5 });
        await page.Page.Mouse.UpAsync();

        var asked = await page.LastIntentAsync<ResizeIntent>();
        Assert.Equal(size[0] + 50, asked.Width);
        Assert.Equal(size[1] + 70, asked.Height);
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task AfterTheDragEnds_MovingThePointerResizesNothing(string engine)
    {
        await using var page = await browsers.OpenAsync(engine, "translate-ready");
        var edge = (await page.Page.Locator(".rz-b").BoundingBoxAsync())!;
        var (x, y) = (edge.X + edge.Width / 2, edge.Y + edge.Height / 2);

        await page.Page.Mouse.MoveAsync(x, y);
        await page.Page.Mouse.DownAsync();
        await page.Page.Mouse.MoveAsync(x, y + 30, new() { Steps = 3 });
        await page.Page.Mouse.UpAsync();
        await page.ClearIntentsAsync();
        await page.Page.Mouse.MoveAsync(x + 100, y + 100, new() { Steps = 3 });

        Assert.Equal(0, await page.CountAsync<ResizeIntent>());
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task TheZoomButton_IsInTheHeader_NextToClose_AndNotInTheMenu(string engine)
    {
        await using var page = await browsers.OpenAsync(engine, "translate-ready");
        var zoom = (await page.Page.Locator("#zoom").BoundingBoxAsync())!;
        var close = (await page.Page.Locator("#close").BoundingBoxAsync())!;
        Assert.True(zoom.X + zoom.Width <= close.X + 1, "The zoom button sits left of the close button.");
        Assert.InRange(zoom.Y + zoom.Height / 2, close.Y + close.Height / 2 - 1, close.Y + close.Height / 2 + 1);

        await page.Page.Locator("#more").ClickAsync();
        Assert.Equal(0, await page.Page.Locator(".menu button", new() { HasText = "放大" }).CountAsync());
        await page.Page.Keyboard.PressAsync("Escape");
        await page.ClearIntentsAsync();

        await page.Page.Locator("#zoom").ClickAsync();

        Assert.Equal(1, await page.CountAsync<ZoomIntent>());
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task TheZoomButton_ShowsRestore_WhileTheHeightIsFixed(string engine)
    {
        await using var page = await browsers.OpenAsync(engine, "translate-ready");
        var before = await page.Page.Locator("#zoom").GetAttributeAsync("aria-label");

        await page.ReceiveAsync("frame", new JsonObject { ["fixedHeight"] = true });
        var during = await page.Page.Locator("#zoom").GetAttributeAsync("aria-label");
        await page.ReceiveAsync("frame", new JsonObject { ["fixedHeight"] = false });

        Assert.NotEqual(before, during);
        Assert.Equal(before, await page.Page.Locator("#zoom").GetAttributeAsync("aria-label"));
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task InTheSideBySideLayout_AFixedHeightLetsTheColumnsFillTheWindow_WithoutAScrollbarWhileThereIsRoom(string engine)
    {
        await using var page = await browsers.OpenAsync(engine, "whole-wide");

        await page.ReceiveAsync("frame", new JsonObject { ["fixedHeight"] = true });

        var layout = await page.Page.EvaluateAsync<double[]>(
            "(() => { const b = document.querySelector('.c-body'), s = document.querySelector('.src'); return [b.scrollHeight - b.clientHeight, s.clientHeight - (b.clientHeight - parseFloat(getComputedStyle(b).paddingBottom))]; })()");
        Assert.True(layout[0] <= 1, $"The body scrolls by {layout[0]} px.");
        Assert.True(Math.Abs(layout[1]) <= 1, $"The source column is {layout[1]} px off the body height.");
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task DoubleClickingAnEdge_AsksToEnlargeOrRestoreTheWindow(string engine)
    {
        await using var page = await browsers.OpenAsync(engine, "translate-ready");
        var edge = (await page.Page.Locator(".rz-br").BoundingBoxAsync())!;
        await page.ClearIntentsAsync();

        await page.Page.Mouse.DblClickAsync(edge.X + edge.Width / 2, edge.Y + edge.Height / 2);

        Assert.Equal(1, await page.CountAsync<ZoomIntent>());
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task ThePanelFillsTheWindow_WhenTheHostFixedTheHeight(string engine)
    {
        await using var page = await browsers.OpenAsync(engine, "translate-ready");
        const string height = "document.getElementById('composer').offsetHeight";
        var natural = await page.Page.EvaluateAsync<int>(height);

        await page.ReceiveAsync("frame", new JsonObject { ["fixedHeight"] = true });

        var viewport = await page.Page.EvaluateAsync<int>("window.innerHeight");
        var panel = await page.Page.EvaluateAsync<int>(height);
        Assert.True(Math.Abs(panel - (viewport - 36)) <= 1, $"The panel is {panel} px tall in a {viewport} px window (it was {natural} px).");

        await page.ReceiveAsync("frame", new JsonObject { ["fixedHeight"] = false });
        Assert.Equal(natural, await page.Page.EvaluateAsync<int>(height));
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task ThePanelLimitOfTheHost_ShortensThePanelAndTheBodyScrolls(string engine)
    {
        await using var page = await browsers.OpenAsync(engine, "whole-wide");
        var before = (await page.Page.Locator("#composer").BoundingBoxAsync())!.Height;

        await page.ReceiveAsync("limit", new JsonObject { ["maxHeight"] = 200 });

        var after = (await page.Page.Locator("#composer").BoundingBoxAsync())!.Height;
        Assert.True(before > 200, $"The fixture must be taller than the limit to prove anything (was {before}).");
        Assert.True(after <= 200, $"The panel is {after} px tall with a limit of 200.");
        Assert.True(await page.Page.EvaluateAsync<bool>("(() => { const b = document.querySelector('.c-body'); return b.scrollHeight > b.clientHeight; })()"));
        Assert.True(await page.Page.Locator("#commit").IsVisibleAsync());
    }

    private static async Task OpenDialogAsync(ComposerPage page, string item)
    {
        await page.Page.Locator("#more").ClickAsync();
        await page.Page.Locator(".menu button", new() { HasText = item }).ClickAsync();
    }
}
