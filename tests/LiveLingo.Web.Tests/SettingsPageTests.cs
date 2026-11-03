using LiveLingo.Desktop.Settings;
using LiveLingo.Desktop.Web;
using Microsoft.Playwright;

namespace LiveLingo.Web.Tests;

/// <summary>What the settings page draws for each golden snapshot, and what it sends when the user acts, in both engines.</summary>
public sealed class SettingsPageTests(BrowserFixture browsers)
{
    public static TheoryData<string> Engines => new() { "webkit", "chromium" };

    private static string[] AllFixtures() =>
        [.. Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Fixtures"), "settings-*.json")
            .Select(Path.GetFileNameWithoutExtension).Where(n => n != "settings-strings").Order(StringComparer.Ordinal)!];

    private static string S(string key) => ComposerPage.SettingsStr(key);

    // --- drawing ----------------------------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task Handshake_SendsReadyOnceAndDrawsTheFirstGroup(string engine)
    {
        await using var page = await browsers.OpenSettingsAsync(engine);

        Assert.Single(await page.IntentsAsync(), i => i is ReadyIntent);
        Assert.Equal("zh-CN", await page.Page.EvaluateAsync<string>("document.documentElement.lang"));
        Assert.Equal("page", await page.Page.Locator("#nav-general").GetAttributeAsync("aria-current"));
        Assert.Equal(6, await page.Page.Locator("#panel .srow").CountAsync());
        Assert.True(page.ConsoleErrors.Count == 0, string.Join("\n", page.ConsoleErrors));
        Assert.Empty(page.Requests);
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task NothingIsDrawnBeforeTheFirstSnapshot(string engine)
    {
        await using var page = await browsers.OpenBareSettingsAsync(engine);

        Assert.Equal(0, await page.Page.Locator(".settings").CountAsync());
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task EverySnapshot_DrawsWithoutErrorsAndIsAccessibleAndReadable(string engine)
    {
        await using var page = await browsers.OpenSettingsAsync(engine);
        var problems = new List<string>();

        foreach (var fixture in AllFixtures())
        {
            await page.PushAsync(fixture);
            var unnamed = await page.Page.EvaluateAsync<string[]>(
                "() => [...document.querySelectorAll('button, input, select')].filter(e => !(e.getAttribute('aria-label') || e.textContent.trim() || (e.labels && e.labels.length))).map(e => e.outerHTML.slice(0, 60))");
            problems.AddRange(unnamed.Select(u => $"{fixture}: no accessible name: {u}"));
            var small = await page.Page.EvaluateAsync<string[]>(
                @"() => [...document.querySelectorAll('.settings *')].filter(e => [...e.childNodes].some(n => n.nodeType === 3 && n.textContent.trim()) && parseFloat(getComputedStyle(e).fontSize) < 12)
                    .map(e => e.tagName + '.' + e.className + ' ' + getComputedStyle(e).fontSize)");
            problems.AddRange(small.Select(s => $"{fixture}: text under 12px: {s}"));
        }

        Assert.Empty(problems);
        Assert.Empty(page.ConsoleErrors);
        Assert.Empty(page.Requests);
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task OnlyTheCurrentGroupIsShown_AndTheNavigationMarksIt(string engine)
    {
        await using var page = await browsers.OpenSettingsAsync(engine, "settings-voice");

        Assert.Equal("page", await page.Page.Locator("#nav-voice").GetAttributeAsync("aria-current"));
        Assert.Null(await page.Page.Locator("#nav-general").GetAttributeAsync("aria-current"));
        Assert.Equal(1, await page.Page.Locator("#mic").CountAsync());
        Assert.Equal(0, await page.Page.Locator("#hotkey").CountAsync());
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task TheGeneralGroup_ShowsTheCurrentValues(string engine)
    {
        await using var page = await browsers.OpenSettingsAsync(engine);

        Assert.Equal("Enter", await page.Page.Locator("#commit").InputValueAsync());
        Assert.Equal("en-US", await page.Page.Locator("#language").InputValueAsync());
        Assert.Equal("Auto", await page.Page.Locator("#theme").InputValueAsync());
        Assert.False(await page.Page.Locator("#motion").IsCheckedAsync());
        Assert.Equal(S("theme.auto"), (await page.Page.Locator("#theme option:checked").InnerTextAsync()).Trim());
        Assert.NotEmpty(await page.Page.Locator("#hotkey").InnerTextAsync());
        Assert.Equal(0, await page.Page.Locator(".err").CountAsync());
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task AnExplicitTheme_BeatsTheSystem_AndReducedMotionTurnsAnimationOff(string engine)
    {
        await using var page = await browsers.OpenSettingsAsync(engine, "settings-dark", ColorScheme.Light);

        Assert.Equal("dark", await page.Page.EvaluateAsync<string>("document.documentElement.dataset.theme"));
        Assert.Equal("off", await page.Page.EvaluateAsync<string>("document.documentElement.dataset.motion"));
        Assert.True(await page.Page.Locator("#motion").IsCheckedAsync());
        Assert.Equal("CtrlEnter", await page.Page.Locator("#commit").InputValueAsync());
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task TheAutoTheme_FollowsTheSystem(string engine)
    {
        await using var dark = await browsers.OpenSettingsAsync(engine, "settings-general", ColorScheme.Dark);
        await using var light = await browsers.OpenSettingsAsync(engine, "settings-general", ColorScheme.Light);

        Assert.Equal("dark", await dark.Page.EvaluateAsync<string>("document.documentElement.dataset.theme"));
        Assert.Equal("light", await light.Page.EvaluateAsync<string>("document.documentElement.dataset.theme"));
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task AnInvalidHotkey_ShowsItsMessage(string engine)
    {
        await using var page = await browsers.OpenSettingsAsync(engine, "settings-hotkey-invalid");

        Assert.Equal(S("hotkey.invalid"), (await page.Page.Locator(".err[role=alert]").InnerTextAsync()).Trim());
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task TheVoiceGroup_ListsTheDevicesAndTheTerms(string engine)
    {
        await using var page = await browsers.OpenSettingsAsync(engine, "settings-voice");

        Assert.Equal([S("mic.default"), "USB Mic", "Built-in"], (await page.Page.Locator("#mic option").AllInnerTextsAsync()).Select(t => t.Trim()));
        Assert.Equal("USB Mic", await page.Page.Locator("#mic").InputValueAsync());
        Assert.False(await page.Page.Locator("#cleanup").IsCheckedAsync());
        Assert.Equal(2, await page.Page.Locator(".term").CountAsync());
        Assert.Contains("cache penetration", await page.Page.Locator(".term").Nth(1).InnerTextAsync());
        Assert.True(await page.Page.Locator("#term-add").IsDisabledAsync());
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task PolishRequirements_SaveOnlyWhenLeavingTheBox(string engine)
    {
        await using var page = await browsers.OpenSettingsAsync(engine, "settings-voice");
        Assert.NotEmpty(await page.Page.Locator("#polish").InputValueAsync());
        await page.ClearIntentsAsync();
        await page.Page.Locator("#polish").FillAsync("Keep my voice. Fix grammar without adding facts.");
        Assert.DoesNotContain(await page.IntentsAsync(), i => i is SettingIntent);
        await page.Page.Locator("#nav-general").ClickAsync();
        var saved = await page.LastIntentAsync<SettingIntent>();
        Assert.Equal("polishPrompt", saved.Key);
        Assert.Equal("Keep my voice. Fix grammar without adding facts.", saved.Value);
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task PolishRequirements_HaveResetAndValidation(string engine)
    {
        await using var page = await browsers.OpenSettingsAsync(engine, "settings-polish-custom");
        await page.Page.Locator("#polish-reset").ClickAsync();
        Assert.Equal(new SettingIntent("polishPrompt", "") { V = 1 }, await page.LastIntentAsync<SettingIntent>());
        await page.PushAsync("settings-polish-invalid");
        Assert.Equal("true", await page.Page.Locator("#polish").GetAttributeAsync("aria-invalid"));
        Assert.Contains(S("polish.invalid"), await page.Page.Locator(".polish .err").InnerTextAsync());
        Assert.Equal("bad", await page.Page.Locator("#polish").InputValueAsync());
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task AStoredKey_IsReportedButNeverShown(string engine)
    {
        await using var page = await browsers.OpenSettingsAsync(engine, "settings-cloud");

        Assert.True(await page.Page.Locator("#cloud-translate").IsCheckedAsync());
        Assert.False(await page.Page.Locator("#cloud-clean").IsCheckedAsync());
        Assert.Equal("", await page.Page.Locator("#cloud-key").InputValueAsync());
        Assert.Equal("password", await page.Page.Locator("#cloud-key").GetAttributeAsync("type"));
        Assert.Equal(1, await page.Page.Locator("#forget-key").CountAsync());
        Assert.Equal("https://api.example.com/v1", await page.Page.Locator("#cloud-url").InputValueAsync());
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task WithoutAStoredKey_ThereIsNothingToForget(string engine)
    {
        await using var page = await browsers.OpenSettingsAsync(engine, "settings-privacy");

        Assert.Equal(0, await page.Page.Locator("#forget-key").CountAsync());
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task ACloudStepThatCannotRun_ShowsWhy(string engine)
    {
        await using var page = await browsers.OpenSettingsAsync(engine, "settings-cloud-invalid");

        Assert.Equal(S("cloud.invalid"), (await page.Page.Locator(".err[role=alert]").InnerTextAsync()).Trim());
        Assert.False(await page.Page.Locator("#cloud-translate").IsCheckedAsync());
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task TheModelsGroup_ShowsTheChipsTheLegacyModelsAndTheVersion(string engine)
    {
        await using var page = await browsers.OpenSettingsAsync(engine, "settings-models");

        Assert.Equal(3, await page.Page.Locator(".chips .chip").CountAsync());
        Assert.Equal("2.0.0", (await page.Page.Locator("#version").InnerTextAsync()).Trim());
        Assert.Contains("8.5", await page.Page.Locator("#legacy-remove").Locator("xpath=ancestor::div[contains(@class,'srow')]").InnerTextAsync());
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task Diagnostics_OpenInAModalDialog_ListTheStatus_AndEscapeGivesTheFocusBack(string engine)
    {
        await using var page = await browsers.OpenSettingsAsync(engine, "settings-diagnostics");

        await page.Page.Locator("#diag-open").ClickAsync();

        Assert.Equal("diag-close", await page.Page.EvaluateAsync<string>("document.activeElement.id"));
        var text = await page.Page.Locator("#diag-text").InnerTextAsync();
        Assert.Contains("2.0.0", text);
        Assert.Contains("macOS 15.0 Arm64", text);
        Assert.DoesNotContain("sk-stored", text);

        await page.Page.Keyboard.PressAsync("Escape");

        Assert.Equal(0, await page.Page.Locator("#diag-text").CountAsync());
        Assert.Equal("diag-open", await page.Page.EvaluateAsync<string>("document.activeElement.id"));
        Assert.All(await page.IntentsAsync(), intent => Assert.IsType<ReadyIntent>(intent));
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task NoLegacyModels_NoRemoveRow(string engine)
    {
        await using var page = await browsers.OpenSettingsAsync(engine, "settings-models");

        await page.PushAsync("settings-voice", s => s["section"] = "models");

        Assert.Equal(0, await page.Page.Locator("#legacy-remove").CountAsync());
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task HostText_IsNeverInterpretedAsMarkup(string engine)
    {
        await using var page = await browsers.OpenSettingsAsync(engine, "settings-voice");

        await page.PushAsync("settings-voice", s => s["voice"]!["terms"]![0]!["source"] = "<img src=x onerror=\"window.__pwned=1\">");

        Assert.Equal(0, await page.Page.Locator(".term img").CountAsync());
        Assert.Contains("<img", await page.Page.Locator(".term").First.InnerTextAsync());
        Assert.Empty(page.ConsoleErrors);
    }

    // --- consent ----------------------------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task TheConsentDialog_NamesTheStep_AndStartsOnTheSafeAnswer(string engine)
    {
        await using var page = await browsers.OpenSettingsAsync(engine, "settings-consent");

        var dialog = page.Page.GetByRole(AriaRole.Dialog);
        Assert.Equal(S("consent.clean"), (await dialog.Locator("h2").InnerTextAsync()).Trim());
        Assert.Equal("consent-keep", await page.Page.EvaluateAsync<string>("document.activeElement.id"));
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task Allowing_SendsTheAnswerAndNothingElse(string engine)
    {
        await using var page = await browsers.OpenSettingsAsync(engine, "settings-consent");
        await page.ClearIntentsAsync();

        await page.Page.Locator("#consent-allow").ClickAsync();

        var intents = await page.IntentsAsync();
        Assert.True(Assert.IsType<ConsentIntent>(Assert.Single(intents)).Allow);
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task EscapeAndTheCloseButton_AreARefusal(string engine)
    {
        await using var page = await browsers.OpenSettingsAsync(engine, "settings-consent");

        await page.Page.Keyboard.PressAsync("Escape");
        await page.Page.GetByRole(AriaRole.Button, new() { Name = S("closeAria") }).ClickAsync();

        var answers = (await page.IntentsAsync()).OfType<ConsentIntent>().ToList();
        Assert.Equal(2, answers.Count);
        Assert.All(answers, a => Assert.False(a.Allow));
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task TabNeverLeavesTheDialog(string engine)
    {
        await using var page = await browsers.OpenSettingsAsync(engine, "settings-consent");

        for (var i = 0; i < 5; i++)
        {
            await page.Page.Keyboard.PressAsync("Tab");
            Assert.True(await page.Page.EvaluateAsync<bool>("!!document.activeElement.closest('[role=dialog]')"));
        }
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task ClosingTheDialog_ReturnsTheFocusToTheSwitch(string engine)
    {
        await using var page = await browsers.OpenSettingsAsync(engine, "settings-consent");

        await page.PushAsync("settings-cloud");

        Assert.Equal("cloud-clean", await page.Page.EvaluateAsync<string>("document.activeElement.id"));
    }

    // --- acting -----------------------------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task Choosing_SendsTheSettingWithItsValue(string engine)
    {
        await using var page = await browsers.OpenSettingsAsync(engine);

        await page.Page.Locator("#commit").SelectOptionAsync("CtrlEnter");
        await page.Page.Locator("#language").SelectOptionAsync("en-US");
        await page.Page.Locator("#theme").SelectOptionAsync("Dark");

        Assert.Equal(
            [("commitKey", "CtrlEnter"), ("language", "en-US"), ("theme", "Dark")],
            (await page.IntentsAsync()).OfType<SettingIntent>().Select(s => (s.Key, s.Value)));
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task TheComposerPreferences_AreShownAndSentLikeAnyOtherSetting(string engine)
    {
        await using var general = await browsers.OpenSettingsAsync(engine);
        Assert.True(await general.Page.Locator("#remember").IsCheckedAsync());
        await general.Page.Locator("#remember").ClickAsync();
        var remember = await general.LastIntentAsync<SettingIntent>();
        Assert.Equal(("rememberChoices", "false"), (remember.Key, remember.Value));

        await using var voice = await browsers.OpenSettingsAsync(engine, "settings-voice");
        Assert.Equal("Clean", await voice.Page.Locator("#output").InputValueAsync());
        Assert.False(await voice.Page.Locator("#auto-dictate").IsCheckedAsync());
        Assert.True(await voice.Page.Locator("#preload").IsCheckedAsync());
        await voice.Page.Locator("#output").SelectOptionAsync("Translate");
        await voice.Page.Locator("#auto-dictate").ClickAsync();
        await voice.Page.Locator("#preload").ClickAsync();

        Assert.Equal(
            [("defaultOutput", "Translate"), ("autoDictate", "true"), ("preloadSpeech", "false")],
            (await voice.IntentsAsync()).OfType<SettingIntent>().Select(s => (s.Key, s.Value)));
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task TheRememberedApps_AreListedAndForgottenOneByOneOrAllAtOnce(string engine)
    {
        await using var none = await browsers.OpenSettingsAsync(engine, "settings-privacy");
        Assert.Equal(0, await none.Page.Locator(".app").CountAsync());
        Assert.Equal(S("apps.empty"), (await none.Page.Locator("#apps-empty").InnerTextAsync()).Trim());
        Assert.True(await none.Page.Locator("#apps-clear").IsDisabledAsync());

        await using var page = await browsers.OpenSettingsAsync(engine, "settings-apps");

        var rows = page.Page.Locator(".app");
        Assert.Equal(3, await rows.CountAsync());
        Assert.Contains("Slack", await rows.Nth(0).InnerTextAsync());
        Assert.Contains("2026-11-0", await rows.Nth(0).InnerTextAsync());
        Assert.Contains("English", await rows.Nth(0).InnerTextAsync());
        Assert.Contains("仅复制", await rows.Nth(2).InnerTextAsync());

        await rows.Nth(1).GetByRole(AriaRole.Button).ClickAsync();
        Assert.Equal("com.microsoft.VSCode", (await page.LastIntentAsync<ForgetAppIntent>()).AppId);

        await page.Page.Locator("#apps-clear").ClickAsync();
        Assert.IsType<ClearAppsIntent>((await page.IntentsAsync()).Last());
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task VoiceChoices_SendTheDeviceNameAndTheDirection(string engine)
    {
        await using var page = await browsers.OpenSettingsAsync(engine, "settings-voice");

        await page.Page.Locator("#mic").SelectOptionAsync("Built-in");
        await page.Page.Locator("#direction").SelectOptionAsync("Chinese");
        await page.Page.Locator("#cleanup").ClickAsync();

        Assert.Equal(
            [("microphone", "Built-in"), ("direction", "Chinese"), ("cleanup", "true")],
            (await page.IntentsAsync()).OfType<SettingIntent>().Select(s => (s.Key, s.Value)));
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task ModelChoice_SendsTheTier(string engine)
    {
        await using var page = await browsers.OpenSettingsAsync(engine, "settings-models");

        await page.Page.Locator("#tier").SelectOptionAsync("Fast");

        var tier = await page.LastIntentAsync<SettingIntent>();
        Assert.Equal(("tier", "Fast"), (tier.Key, tier.Value));
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task ASwitch_SendsItsNewValue_AndKeepsShowingTheSnapshotUntilTheHostAnswers(string engine)
    {
        await using var page = await browsers.OpenSettingsAsync(engine);

        await page.Page.Locator("#motion").ClickAsync();

        var setting = await page.LastIntentAsync<SettingIntent>();
        Assert.Equal(("reduceMotion", "true"), (setting.Key, setting.Value));
        Assert.False(await page.Page.Locator("#motion").IsCheckedAsync());

        await page.PushAsync("settings-dark");
        Assert.True(await page.Page.Locator("#motion").IsCheckedAsync());
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task ACloudSwitch_AsksTheHost_NeverDecidesItself(string engine)
    {
        await using var page = await browsers.OpenSettingsAsync(engine, "settings-privacy");

        await page.Page.Locator("#cloud-translate").ClickAsync();
        await page.Page.Locator("#cloud-clean").ClickAsync();

        Assert.Equal(
            [(CloudStep.Translate, true), (CloudStep.Clean, true)],
            (await page.IntentsAsync()).OfType<CloudToggleIntent>().Select(t => (t.Step, t.On)));
        Assert.False(await page.Page.Locator("#cloud-translate").IsCheckedAsync());
        Assert.Equal(0, await page.Page.GetByRole(AriaRole.Dialog).CountAsync());
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task TurningACloudStepOff_IsSentLikeAnyOtherToggle(string engine)
    {
        await using var page = await browsers.OpenSettingsAsync(engine, "settings-cloud");

        await page.Page.Locator("#cloud-translate").ClickAsync();

        var toggle = await page.LastIntentAsync<CloudToggleIntent>();
        Assert.Equal((CloudStep.Translate, false), (toggle.Step, toggle.On));
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task Typing_SendsEveryChange_AndAStaleSnapshotNeverOverwritesIt(string engine)
    {
        await using var page = await browsers.OpenSettingsAsync(engine, "settings-privacy");
        var url = page.Page.Locator("#cloud-url");

        await url.FocusAsync();
        await page.Page.Keyboard.TypeAsync("https://a");
        await page.PushAsync("settings-privacy"); // the host answers a frame late, with the text from before

        Assert.Equal("https://a", await url.InputValueAsync());
        Assert.Equal("https://a", (await page.LastIntentAsync<SettingIntent>()).Value);
        Assert.Equal("cloudBaseUrl", (await page.LastIntentAsync<SettingIntent>()).Key);

        await page.Page.Locator("#cloud-model").FocusAsync();
        Assert.Equal("", await url.InputValueAsync()); // once it lost the focus, the box shows the snapshot again
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task TheModelNameIsSentAsTyped(string engine)
    {
        await using var page = await browsers.OpenSettingsAsync(engine, "settings-privacy");

        await page.Page.Locator("#cloud-model").FillAsync("gpt-deploy");

        var setting = await page.LastIntentAsync<SettingIntent>();
        Assert.Equal(("cloudModel", "gpt-deploy"), (setting.Key, setting.Value));
    }

    // --- the hotkey -------------------------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task TheHotkeyBox_RecordsTheChordTheUserPresses(string engine)
    {
        await using var page = await browsers.OpenSettingsAsync(engine);

        await page.Page.Locator("#hotkey").ClickAsync();
        Assert.Equal(S("hotkey.prompt"), (await page.Page.Locator("#hotkey").InnerTextAsync()).Trim());
        await page.Page.Keyboard.PressAsync("Control+Shift+K");

        var hotkey = await page.LastIntentAsync<SettingIntent>();
        Assert.Equal(("hotkey", "Ctrl+Shift+K"), (hotkey.Key, hotkey.Value));
        Assert.NotEqual(S("hotkey.prompt"), (await page.Page.Locator("#hotkey").InnerTextAsync()).Trim());
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task TheHotkeyBox_UsesThePhysicalKey_NotTheCharacter(string engine)
    {
        await using var page = await browsers.OpenSettingsAsync(engine);

        await page.Page.Locator("#hotkey").ClickAsync();
        // Shift turns the 1 into "!" and the digit row differs per layout; the physical key is what is registered.
        await page.Page.Keyboard.PressAsync("Control+Shift+Digit1");

        Assert.Equal("Ctrl+Shift+1", (await page.LastIntentAsync<SettingIntent>()).Value);
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task TheHotkeyBox_WaitsForAChord_AndEscapeCancels(string engine)
    {
        await using var page = await browsers.OpenSettingsAsync(engine);

        await page.Page.Locator("#hotkey").ClickAsync();
        await page.Page.Keyboard.PressAsync("Control"); // a modifier alone
        await page.Page.Keyboard.PressAsync("k"); // a key alone
        await page.Page.Keyboard.PressAsync("Control+F5"); // a key neither platform can register
        Assert.Equal(0, await page.CountAsync<SettingIntent>());
        Assert.Equal(S("hotkey.prompt"), (await page.Page.Locator("#hotkey").InnerTextAsync()).Trim());

        await page.Page.Keyboard.PressAsync("Escape");

        Assert.Equal(0, await page.CountAsync<SettingIntent>());
        Assert.NotEqual(S("hotkey.prompt"), (await page.Page.Locator("#hotkey").InnerTextAsync()).Trim());
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task LeavingTheHotkeyBox_StopsRecording(string engine)
    {
        await using var page = await browsers.OpenSettingsAsync(engine);

        await page.Page.Locator("#hotkey").ClickAsync();
        await page.Page.Locator("#commit").FocusAsync();

        Assert.NotEqual(S("hotkey.prompt"), (await page.Page.Locator("#hotkey").InnerTextAsync()).Trim());
    }

    // --- the glossary -----------------------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task AddingATerm_SendsItAndEmptiesTheForm(string engine)
    {
        await using var page = await browsers.OpenSettingsAsync(engine, "settings-voice");

        await page.Page.Locator("#term-source").FillAsync("幂等键");
        await page.Page.Locator("#term-target").FillAsync("idempotency key");
        await page.Page.Locator("#term-add").ClickAsync();

        var add = await page.LastIntentAsync<AddTermIntent>();
        Assert.Equal(("幂等键", "idempotency key"), (add.Source, add.Target));
        Assert.Equal("", await page.Page.Locator("#term-source").InputValueAsync());
        Assert.Equal("", await page.Page.Locator("#term-target").InputValueAsync());
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task EnterAddsTheTerm_ButNotWhileAnInputMethodConfirms(string engine)
    {
        await using var page = await browsers.OpenSettingsAsync(engine, "settings-voice");
        await page.Page.Locator("#term-source").FillAsync("Redis");

        await page.Page.EvaluateAsync(@"() => {
            const box = document.getElementById('term-source');
            box.focus();
            box.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', keyCode: 229, bubbles: true, cancelable: true }));
            box.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', isComposing: true, bubbles: true, cancelable: true }));
        }");
        Assert.Equal(0, await page.CountAsync<AddTermIntent>());

        await page.Page.Locator("#term-source").PressAsync("Enter");

        Assert.Equal(1, await page.CountAsync<AddTermIntent>());
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task RemovingATerm_SendsItsPosition(string engine)
    {
        await using var page = await browsers.OpenSettingsAsync(engine, "settings-voice");

        await page.Page.Locator(".term").Nth(1).GetByRole(AriaRole.Button).ClickAsync();

        Assert.Equal(1, (await page.LastIntentAsync<RemoveTermIntent>()).Index);
    }

    // --- the key ----------------------------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task TheKey_IsSentOnEnter_AndTheBoxIsEmptiedAtOnce(string engine)
    {
        await using var page = await browsers.OpenSettingsAsync(engine, "settings-privacy");

        await page.Page.Locator("#cloud-key").FillAsync("sk-typed");
        await page.Page.Locator("#cloud-key").PressAsync("Enter");

        Assert.Equal("sk-typed", (await page.LastIntentAsync<SaveKeyIntent>()).Value);
        Assert.Equal("", await page.Page.Locator("#cloud-key").InputValueAsync());
        Assert.Equal(1, await page.CountAsync<SaveKeyIntent>());
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task TheKey_IsAlsoSentWhenTheBoxLosesTheFocus(string engine)
    {
        await using var page = await browsers.OpenSettingsAsync(engine, "settings-privacy");

        await page.Page.Locator("#cloud-key").FillAsync("sk-typed");
        await page.Page.Locator("#cloud-model").FocusAsync();

        Assert.Equal("sk-typed", (await page.LastIntentAsync<SaveKeyIntent>()).Value);
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task AnEmptyKey_SendsNothing(string engine)
    {
        await using var page = await browsers.OpenSettingsAsync(engine, "settings-privacy");

        await page.Page.Locator("#cloud-key").FocusAsync();
        await page.Page.Keyboard.PressAsync("Enter");
        await page.Page.Locator("#cloud-model").FocusAsync();

        Assert.Equal(0, await page.CountAsync<SaveKeyIntent>());
    }

    // --- the rest ---------------------------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task ForgettingTheKey_RecentDraftsAndLegacyModels_AreOneClickIntents(string engine)
    {
        await using var cloud = await browsers.OpenSettingsAsync(engine, "settings-cloud");
        await cloud.Page.Locator("#forget-key").ClickAsync();
        await cloud.Page.Locator("#recent-clear").ClickAsync();
        await using var models = await browsers.OpenSettingsAsync(engine, "settings-models");
        await models.Page.Locator("#legacy-remove").ClickAsync();

        Assert.Equal(1, await cloud.CountAsync<ForgetKeyIntent>());
        Assert.Equal(1, await cloud.CountAsync<ClearRecentIntent>());
        Assert.Equal(1, await models.CountAsync<RemoveLegacyModelsIntent>());
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task TheUpdateRow_ChecksOnRequest_AndShowsTheDownloadsProgress(string engine)
    {
        await using var idle = await browsers.OpenSettingsAsync(engine, "settings-models");
        await idle.Page.Locator("#update-action").ClickAsync();
        Assert.Equal(1, await idle.CountAsync<CheckUpdatesIntent>());

        await using var downloading = await browsers.OpenSettingsAsync(engine, "settings-update-downloading");
        Assert.Contains("42%", await downloading.Page.Locator("#update-status").InnerTextAsync());
        Assert.Equal("42", await downloading.Page.Locator("#update-progress").GetAttributeAsync("aria-valuenow"));
        Assert.True(await downloading.Page.Locator("#update-action").IsDisabledAsync());

        await using var checking = await browsers.OpenSettingsAsync(engine, "settings-update-checking");
        Assert.Contains("indeterminate", await checking.Page.Locator("#update-progress").GetAttributeAsync("class"));
        Assert.True(await checking.Page.Locator("#update-action").IsDisabledAsync());
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task TheUpdateRow_RestartsIntoADownloadedVersion_OrOpensTheDownloadPage(string engine)
    {
        await using var ready = await browsers.OpenSettingsAsync(engine, "settings-update-ready");
        await ready.Page.Locator("#update-action").ClickAsync();
        Assert.Equal(1, await ready.CountAsync<RestartToUpdateIntent>());

        await using var unsupported = await browsers.OpenSettingsAsync(engine, "settings-update-unsupported");
        Assert.Contains("无法自动更新", await unsupported.Page.Locator("#update-status").InnerTextAsync());
        await unsupported.Page.Locator("#update-action").ClickAsync();
        Assert.Equal(1, await unsupported.CountAsync<OpenDownloadsIntent>());
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task TheUpdateRow_SaysWhenAnUpdateCheckFailedOrUpdatesAreOff(string engine)
    {
        await using var failed = await browsers.OpenSettingsAsync(engine, "settings-update-failed");
        Assert.Contains("Unable to connect", await failed.Page.Locator("#update-status").InnerTextAsync());
        Assert.False(await failed.Page.Locator("#update-action").IsDisabledAsync());

        await using var off = await browsers.OpenSettingsAsync(engine, "settings-update-off");
        Assert.Contains("已关闭自动更新", await off.Page.Locator("#update-status").InnerTextAsync());
        Assert.Equal(0, await off.Page.Locator("#update-action").CountAsync());
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task TheNavigation_AsksTheHost_AndTheGroupChangesWithTheNextSnapshot(string engine)
    {
        await using var page = await browsers.OpenSettingsAsync(engine);

        await page.Page.Locator("#nav-privacy").ClickAsync();

        Assert.Equal("privacy", (await page.LastIntentAsync<OpenSettingsIntent>()).Section);
        Assert.Equal("page", await page.Page.Locator("#nav-general").GetAttributeAsync("aria-current"));

        await page.PushAsync("settings-privacy");
        Assert.Equal("page", await page.Page.Locator("#nav-privacy").GetAttributeAsync("aria-current"));
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task TheSettingsWindowNeverReportsASize(string engine)
    {
        await using var page = await browsers.OpenSettingsAsync(engine, "settings-voice");
        await page.PushAsync("settings-privacy");

        Assert.Equal(0, await page.CountAsync<SizeIntent>());
    }
}
