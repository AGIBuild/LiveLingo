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
        Assert.Equal(4, await page.Page.Locator("nav button").CountAsync());
        Assert.Equal(0, await page.Page.Locator("#nav-privacy").CountAsync());
        Assert.Equal("page", await page.Page.Locator("#nav-general").GetAttributeAsync("aria-current"));
        Assert.Equal(7, await page.Page.Locator("#panel .srow").CountAsync());
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
    public async Task ModelsPrivacyStripAndInlineEndpoints_ShowCrossStageUsageSwitchAllToLocal_AndNeverExposeKeys(string engine)
    {
        await using var page = await browsers.OpenSettingsAsync(engine, "settings-cloud");

        Assert.Equal(1, await page.Page.Locator("#cloud-disable-all").CountAsync());
        Assert.Contains(S("privacy.badge.cloud"), await page.Page.Locator("#models-privacy-strip").InnerTextAsync());
        Assert.Contains("Main", await page.Page.Locator("#model-cloud-card-translate-main").InnerTextAsync());
        Assert.Contains("Other", await page.Page.Locator("#model-cloud-card-translate-other").InnerTextAsync());
        Assert.Contains(S("output.clean"), await page.Page.Locator("#model-cloud-card-translate-main").InnerTextAsync());
        Assert.Contains("other.example.com", await page.Page.Locator("#model-cloud-card-translate-other").InnerTextAsync());
        Assert.Contains(S("cloud.keySaved"), await page.Page.Locator("#model-cloud-card-translate-other").InnerTextAsync());
        Assert.Equal(0, await page.Page.Locator("input[type=password]").CountAsync());

        await page.ClearIntentsAsync();
        await page.Page.Locator("#cloud-disable-all").ClickAsync();
        Assert.IsType<DisableAllCloudIntent>((await page.IntentsAsync()).Last());
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task WithoutCloudActive_ModelsPrivacyStripSaysEverythingStaysLocal(string engine)
    {
        await using var page = await browsers.OpenSettingsAsync(engine, "settings-models");

        Assert.Equal(0, await page.Page.Locator("#cloud-disable-all").CountAsync());
        Assert.Contains(S("privacy.badge.local"), await page.Page.Locator("#models-privacy-strip").InnerTextAsync());
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task ACloudStepThatCannotRun_ShowsWhy(string engine)
    {
        await using var page = await browsers.OpenSettingsAsync(engine, "settings-cloud-invalid");

        Assert.Equal(S("cloud.invalid"), (await page.Page.Locator(".err[role=alert]").InnerTextAsync()).Trim());
        Assert.False(await page.Page.Locator("#model-cloud-translate-main").IsCheckedAsync());
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task TheModelsAndAboutGroups_AreSeparate_CategorizedByPurpose_AndShowLocalStorageInventory(string engine)
    {
        await using var page = await browsers.OpenSettingsAsync(engine, "settings-models");

        // About items live on their own section, never cluttering the Models page.
        Assert.Equal(1, await page.Page.Locator("#nav-about").CountAsync());
        Assert.Equal(0, await page.Page.Locator("#version").CountAsync());
        Assert.Equal(0, await page.Page.Locator("#update-action").CountAsync());

        // Hardware banner + 4 focused category/storage tabs; only one panel renders at a time.
        Assert.Equal(1, await page.Page.Locator("#models-hw").CountAsync());
        Assert.Equal(1, await page.Page.Locator("#models-best-badge").CountAsync());
        Assert.Equal(4, await page.Page.Locator("#models-tabs [role=tab]").CountAsync());
        Assert.Equal(1, await page.Page.Locator("#stage-cloud-empty-speech").CountAsync());
        Assert.Contains("Qwen-Audio-3.1-ASR", await page.Page.Locator("#stage-cloud-empty-speech").InnerTextAsync());
        Assert.Equal(["#1", "#2", "#3"], await page.Page.Locator("#stage-speech .mcard:not(.cloud) .mrank").AllInnerTextsAsync());
        Assert.Contains("Qwen3-ASR 1.7B", await page.Page.Locator("#model-card-qwen3-asr-1\\.7b-int8").InnerTextAsync());
        Assert.Equal(1, await page.Page.Locator("#model-download-qwen3-asr-1\\.7b-int8").CountAsync());
        Assert.Equal(0, await page.Page.Locator("#stage-cleaner").CountAsync());
        Assert.Equal(0, await page.Page.Locator("#stage-translator").CountAsync());

        // Switch across purpose categories and verify ranked options, inline cloud onboarding & hardware gating.
        await page.Page.Locator("#model-tab-cleaner").ClickAsync();
        Assert.Equal(1, await page.Page.Locator("#stage-cloud-empty-cleaner").CountAsync());
        Assert.Equal(["#1", "#2", "#3"], await page.Page.Locator("#stage-cleaner .mcard:not(.cloud) .mrank").AllInnerTextsAsync());
        Assert.True(await page.Page.Locator("#model-radio-qwen3\\.5-9b-q4km").IsDisabledAsync());
        Assert.True(await page.Page.Locator("#model-radio-qwen3\\.5-4b-q4km").IsCheckedAsync());

        await page.Page.Locator("#model-tab-cleaner").PressAsync("ArrowRight");
        Assert.Equal(["#1", "#2"], await page.Page.Locator("#stage-translator .mcard:not(.cloud) .mrank").AllInnerTextsAsync());
        Assert.True(await page.Page.Locator("#model-radio-hy-mt2-1\\.8b-q4km").IsCheckedAsync());

        // Clicking the disk usage summary jumps directly to Local Storage with full inventory and sizes.
        await page.Page.Locator("#models-disk-link").ClickAsync();
        Assert.Equal("true", await page.Page.Locator("#model-tab-storage").GetAttributeAsync("aria-selected"));
        Assert.Equal("/models", (await page.Page.Locator("#storage-path").InnerTextAsync()).Trim());
        Assert.Equal(7, await page.Page.Locator(".storage-item").CountAsync());
        Assert.Contains("8.5", await page.Page.Locator("#legacy-remove").Locator("xpath=ancestor::div[contains(@class,'srow')]").InnerTextAsync());

        // About section displays Version, Updates and Diagnostics independently.
        await page.PushAsync("settings-diagnostics");
        Assert.Equal("page", await page.Page.Locator("#nav-about").GetAttributeAsync("aria-current"));
        Assert.Equal("2.0.0", (await page.Page.Locator("#version").InnerTextAsync()).Trim());
        Assert.Equal(1, await page.Page.Locator("#diag-open").CountAsync());
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
        await page.Page.Locator("#model-tab-storage").ClickAsync();
        Assert.Equal(1, await page.Page.Locator("#legacy-remove").CountAsync());

        await page.PushAsync("settings-voice", s => s["section"] = "models");

        Assert.Equal(0, await page.Page.Locator("#legacy-remove").CountAsync());
        Assert.Equal(1, await page.Page.Locator("#storage-empty").CountAsync());
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
        Assert.Contains("Main", await dialog.InnerTextAsync());
        Assert.Contains("api.example.com", await dialog.InnerTextAsync());
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
    public async Task ClosingTheDialog_ReturnsTheFocusToTheCloudOptionCard(string engine)
    {
        await using var page = await browsers.OpenSettingsAsync(engine, "settings-consent");

        await page.PushAsync("settings-consent", s => s["models"]!["consent"] = null);

        Assert.Equal("model-cloud-clean-main", await page.Page.EvaluateAsync<string>("document.activeElement.id"));
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
        await using var none = await browsers.OpenSettingsAsync(engine, "settings-general");
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
        await page.Page.Locator("#builtin-terms").ClickAsync();

        Assert.Equal(
            [("microphone", "Built-in"), ("direction", "Chinese"), ("cleanup", "true"), ("builtInGlossary", "false")],
            (await page.IntentsAsync()).OfType<SettingIntent>().Select(s => (s.Key, s.Value)));

        await page.ClearIntentsAsync();
        Assert.True(await page.Page.Locator("#corr-add").IsDisabledAsync());
        await page.Page.Locator("#corr-spoken").FillAsync("Scale");
        await page.Page.Locator("#corr-canonical").FillAsync("Skill");
        await page.Page.Locator("#corr-add").ClickAsync();
        Assert.Equal(new AddCorrectionIntent("Scale", "Skill") { V = 1 }, await page.LastIntentAsync<AddCorrectionIntent>());
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task ModelCards_DownloadCenter_AndStorage_SendSelectDownloadCancelDeleteAndApplyBestMatchIntents(string engine)
    {
        await using var page = await browsers.OpenSettingsAsync(engine, "settings-models");
        await page.ClearIntentsAsync();

        await page.Page.Locator("#model-tab-cleaner").ClickAsync();
        await page.Page.Locator("#model-use-qwen3\\.5-2b-q4km").ClickAsync();
        Assert.Equal(new SelectModelIntent("qwen3.5-2b-q4km") { V = 1 }, await page.LastIntentAsync<SelectModelIntent>());

        await page.Page.Locator("#model-tab-storage").ClickAsync();
        await page.Page.Locator("#storage-delete-qwen3\\.5-2b-q4km").ClickAsync();
        Assert.Equal(new DeleteModelIntent("qwen3.5-2b-q4km") { V = 1 }, await page.LastIntentAsync<DeleteModelIntent>());

        await page.Page.Locator("#model-tab-translator").ClickAsync();
        await page.Page.Locator("#model-download-hy-mt2-7b-q4km").ClickAsync();
        Assert.Equal(new DownloadModelIntent("hy-mt2-7b-q4km") { V = 1 }, await page.LastIntentAsync<DownloadModelIntent>());

        // Even while staying on Speech tab, the persistent top Download Center and sidebar badge show exact model & byte progress.
        await page.Page.Locator("#model-tab-speech").ClickAsync();
        await page.PushAsync("settings-models-downloading");
        await page.ClearIntentsAsync();
        Assert.Equal("42%", (await page.Page.Locator("#nav-models-badge").InnerTextAsync()).Trim());
        Assert.Contains("Hy-MT2 7B", await page.Page.Locator("#dl-item-hy-mt2-7b-q4km").InnerTextAsync());
        Assert.Contains("1.94 GB / 4.62 GB · 42%", await page.Page.Locator("#dl-item-hy-mt2-7b-q4km").InnerTextAsync());
        await page.Page.Locator("#dl-cancel-hy-mt2-7b-q4km").ClickAsync();
        Assert.Equal(new CancelModelDownloadIntent("hy-mt2-7b-q4km") { V = 1 }, await page.LastIntentAsync<CancelModelDownloadIntent>());

        await page.Page.Locator("#models-apply-best").ClickAsync();
        Assert.IsType<ApplyRecommendedModelsIntent>((await page.IntentsAsync()).Last());
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
    public async Task UnifiedStageRadioGroup_MarksCloudOrLocalExclusively_AndSendsCloudAndLocalSwitchIntents(string engine)
    {
        await using var page = await browsers.OpenSettingsAsync(engine, "settings-endpoint-switch");

        // First examine and dismiss the endpoint-switch consent dialog: names both Main and Other and starts on Keep Current.
        var dialog = page.Page.GetByRole(AriaRole.Dialog);
        var dialogText = await dialog.InnerTextAsync();
        Assert.Equal(S("consent.translate"), (await dialog.Locator("h2").InnerTextAsync()).Trim());
        Assert.Contains("Main", dialogText);
        Assert.Contains("Other", dialogText);
        Assert.Contains("other.example.com", dialogText);
        Assert.Equal("consent-keep", await page.Page.EvaluateAsync<string>("document.activeElement.id"));
        await page.Page.Locator("#consent-allow").ClickAsync();
        Assert.True((await page.LastIntentAsync<ConsentIntent>()).Allow);

        // Close modal in snapshot and verify single-radio exclusivity on the Models -> Translation tab:
        // Cloud Main is checked (●), while local Hy-MT2 1.8B is unchecked ( ) and offers immediate local switch or disk deletion.
        await page.PushAsync("settings-endpoint-switch", s => s["models"]!["consent"] = null);
        await page.ClearIntentsAsync();

        Assert.True(await page.Page.Locator("#model-cloud-translate-main").IsCheckedAsync());
        Assert.False(await page.Page.Locator("#model-cloud-translate-other").IsCheckedAsync());
        Assert.False(await page.Page.Locator("#model-radio-hy-mt2-1\\.8b-q4km").IsCheckedAsync());
        Assert.Equal(1, await page.Page.Locator("#stage-translator input[type=radio]:checked").CountAsync());
        Assert.Equal(1, await page.Page.Locator("#model-delete-hy-mt2-1\\.8b-q4km").CountAsync());

        // Selecting another cloud endpoint asks the host (never checks the radio on its own).
        await page.Page.Locator("#model-cloud-use-translate-other").ClickAsync();
        Assert.Equal(new SelectCloudModelIntent(CloudStep.Translate, "other") { V = 1 }, await page.LastIntentAsync<SelectCloudModelIntent>());
        Assert.False(await page.Page.Locator("#model-cloud-translate-other").IsCheckedAsync());

        // Switching back to local can be done either from the active cloud card ("Switch to local") or by clicking an installed local model.
        await page.Page.Locator("#model-cloud-local-translate-main").ClickAsync();
        Assert.Equal(new UseLocalStageIntent(CloudStep.Translate) { V = 1 }, await page.LastIntentAsync<UseLocalStageIntent>());

        await page.Page.Locator("#model-use-hy-mt2-1\\.8b-q4km").ClickAsync();
        Assert.Equal(new SelectModelIntent("hy-mt2-1.8b-q4km") { V = 1 }, await page.LastIntentAsync<SelectModelIntent>());

        // Inline "+ Add cloud service" and "Edit" open the endpoint modal right from the Models page without navigating away.
        await page.Page.Locator("#stage-cloud-add-translator").ClickAsync();
        await page.Page.Locator("#stage-cloud-edit-translator-main").ClickAsync();
        Assert.Equal(
            [new OpenEndpointFormIntent(null) { V = 1 }, new OpenEndpointFormIntent("main") { V = 1 }],
            (await page.IntentsAsync()).OfType<OpenEndpointFormIntent>());
    }

    // --- the endpoints ----------------------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task AddingEditingAndDeleting_AreHandledInlineInModelsWithoutLeavingThePage(string engine)
    {
        await using var page = await browsers.OpenSettingsAsync(engine, "settings-cloud");

        await page.Page.Locator("#stage-cloud-add-translator").ClickAsync();
        await page.Page.Locator("#stage-cloud-edit-translator-other").ClickAsync();
        await page.Page.Locator("#stage-cloud-delete-translator-other").ClickAsync();

        Assert.Equal(
            [new OpenEndpointFormIntent(null) { V = 1 }, new OpenEndpointFormIntent("other") { V = 1 }],
            (await page.IntentsAsync()).OfType<OpenEndpointFormIntent>());
        Assert.Equal(new DeleteEndpointIntent("other") { V = 1 }, await page.LastIntentAsync<DeleteEndpointIntent>());
        Assert.Equal(0, await page.Page.GetByRole(AriaRole.Dialog).CountAsync());
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task TheAddForm_StartsOnTheName_HidesTheKey_AndEscapeClosesIt(string engine)
    {
        await using var page = await browsers.OpenSettingsAsync(engine, "settings-endpoint-add");

        var dialog = page.Page.GetByRole(AriaRole.Dialog);
        Assert.Equal(S("cloud.form.add"), (await dialog.Locator("h2").InnerTextAsync()).Trim());
        Assert.Equal("ep-name", await page.Page.EvaluateAsync<string>("document.activeElement.id"));
        Assert.Equal("password", await page.Page.Locator("#ep-key").GetAttributeAsync("type"));
        Assert.Equal(S("cloud.form.create"), (await page.Page.Locator("#ep-save").InnerTextAsync()).Trim());

        await page.Page.Keyboard.PressAsync("Escape");

        Assert.Equal(1, await page.CountAsync<CloseEndpointFormIntent>());
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task SubmittingTheForm_SendsWhatWasTyped_OnceWithTheKey(string engine)
    {
        await using var page = await browsers.OpenSettingsAsync(engine, "settings-endpoint-add");

        await page.Page.Locator("#ep-name").FillAsync("Contoso");
        await page.Page.Locator("#ep-url").FillAsync("https://contoso.example/v1");
        await page.Page.Locator("#ep-model").FillAsync("contoso-chat");
        await page.Page.Locator("#ep-key").FillAsync("sk-typed");
        await page.Page.Locator("#ep-key").PressAsync("Enter");

        Assert.Equal(
            new SaveEndpointIntent("Contoso", "https://contoso.example/v1", "contoso-chat", "sk-typed") { V = 1 },
            await page.LastIntentAsync<SaveEndpointIntent>());
        Assert.Equal(1, await page.CountAsync<SaveEndpointIntent>());
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task TheEditForm_ShowsTheSavedValues_AndNeverTheKey(string engine)
    {
        await using var page = await browsers.OpenSettingsAsync(engine, "settings-endpoint-edit");

        Assert.Equal(S("cloud.form.edit"), (await page.Page.GetByRole(AriaRole.Dialog).Locator("h2").InnerTextAsync()).Trim());
        Assert.Equal("Main", await page.Page.Locator("#ep-name").InputValueAsync());
        Assert.Equal("https://api.example.com/v1", await page.Page.Locator("#ep-url").InputValueAsync());
        Assert.Equal("m", await page.Page.Locator("#ep-model").InputValueAsync());
        Assert.Equal("", await page.Page.Locator("#ep-key").InputValueAsync());
        Assert.Equal(S("cloud.keyStored"), await page.Page.Locator("#ep-key").GetAttributeAsync("placeholder"));
        Assert.Equal(S("cloud.form.save"), (await page.Page.Locator("#ep-save").InnerTextAsync()).Trim());
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task ARefusedForm_ShowsWhy_KeepsWhatWasTyped_AndFocusesTheField(string engine)
    {
        await using var page = await browsers.OpenSettingsAsync(engine, "settings-endpoint-add");
        await page.Page.Locator("#ep-name").FillAsync("Contoso");
        await page.Page.Locator("#ep-url").FillAsync("http://contoso.example/v1");
        await page.Page.Locator("#ep-key").FillAsync("sk-typed");
        await page.Page.Locator("#ep-save").ClickAsync();

        await page.PushAsync("settings-endpoint-error");

        Assert.Equal(S("cloud.error.urlInvalid"), (await page.Page.Locator(".endpoint-form .err[role=alert]").InnerTextAsync()).Trim());
        Assert.Equal("true", await page.Page.Locator("#ep-url").GetAttributeAsync("aria-invalid"));
        Assert.Equal("ep-url", await page.Page.EvaluateAsync<string>("document.activeElement.id"));
        Assert.Equal("Contoso", await page.Page.Locator("#ep-name").InputValueAsync());
        Assert.Equal("sk-typed", await page.Page.Locator("#ep-key").InputValueAsync());
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task TabNeverLeavesTheForm_AndVisitsEveryField(string engine)
    {
        await using var page = await browsers.OpenSettingsAsync(engine, "settings-endpoint-add");

        var visited = new List<string>();
        for (var i = 0; i < 7; i++)
        {
            await page.Page.Keyboard.PressAsync("Tab");
            Assert.True(await page.Page.EvaluateAsync<bool>("!!document.activeElement.closest('[role=dialog]')"));
            visited.Add(await page.Page.EvaluateAsync<string>("document.activeElement.id"));
        }

        // The close button has no id; after the last control the focus wraps to the first field.
        Assert.Equal(["ep-cancel", "ep-key", "ep-model", "ep-name", "ep-save", "ep-url"], visited.Where(id => id != "").Distinct().Order());
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task Deleting_NamesTheEndpointAndAffectedCloudSteps_AndEscapeKeepsIt(string engine)
    {
        await using var page = await browsers.OpenSettingsAsync(engine, "settings-endpoint-delete");

        var dialog = page.Page.GetByRole(AriaRole.Dialog);
        var text = await dialog.InnerTextAsync();
        Assert.Equal(S("cloud.delete.title").Replace("{0}", "Main"), (await dialog.Locator("h2").InnerTextAsync()).Trim());
        Assert.Contains(S("cloud.delete.body"), text);
        Assert.Contains(S("cloud.translate"), text);
        Assert.Contains("destructive", await page.Page.Locator("#endpoint-confirm").GetAttributeAsync("class"));

        await page.Page.Keyboard.PressAsync("Escape");

        Assert.False((await page.LastIntentAsync<EndpointAnswerIntent>()).Allow);
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task SpeechStage_OffersQwenAudioPreset_AndAsksExplicitAudioUploadConsent(string engine)
    {
        await using var page = await browsers.OpenSettingsAsync(engine, "settings-speech-endpoint-add");

        Assert.Equal(1, await page.Page.Locator("#ep-preset-qwen-asr").CountAsync());
        await page.Page.Locator("#ep-preset-qwen-asr").ClickAsync();
        Assert.Equal("Qwen-Audio-3.1-ASR", await page.Page.Locator("#ep-name").InputValueAsync());
        Assert.Equal("https://dashscope.aliyuncs.com/api/v1", await page.Page.Locator("#ep-url").InputValueAsync());
        Assert.Equal("qwen-audio-3.1-asr-flash-message", await page.Page.Locator("#ep-model").InputValueAsync());
        Assert.Equal("ep-key", await page.Page.EvaluateAsync<string>("document.activeElement.id"));

        await page.Page.Locator("#ep-key").FillAsync("sk-dashscope");
        await page.Page.Locator("#ep-save").ClickAsync();
        Assert.Equal(
            new SaveEndpointIntent(
                "Qwen-Audio-3.1-ASR",
                "https://dashscope.aliyuncs.com/api/v1",
                "qwen-audio-3.1-asr-flash-message",
                "sk-dashscope") { V = 1 },
            await page.LastIntentAsync<SaveEndpointIntent>());

        await page.PushAsync("settings-speech-consent");
        var dialog = page.Page.GetByRole(AriaRole.Dialog);
        Assert.Equal(S("consent.speech"), (await dialog.Locator("h2").InnerTextAsync()).Trim());
        Assert.Contains("麦克风语音", await dialog.InnerTextAsync());
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

    // --- the rest ---------------------------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task RecentDraftsAndLegacyModels_AreOneClickIntents(string engine)
    {
        await using var general = await browsers.OpenSettingsAsync(engine, "settings-general");
        await general.Page.Locator("#recent-clear").ClickAsync();
        await using var models = await browsers.OpenSettingsAsync(engine, "settings-models");
        await models.Page.Locator("#model-tab-storage").ClickAsync();
        await models.Page.Locator("#legacy-remove").ClickAsync();

        Assert.Equal(1, await general.CountAsync<ClearRecentIntent>());
        Assert.Equal(1, await models.CountAsync<RemoveLegacyModelsIntent>());
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task TheUpdateRow_ChecksOnRequest_AndShowsTheDownloadsProgress(string engine)
    {
        await using var idle = await browsers.OpenSettingsAsync(engine, "settings-diagnostics");
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

        await page.Page.Locator("#nav-models").ClickAsync();

        Assert.Equal("models", (await page.LastIntentAsync<OpenSettingsIntent>()).Section);
        Assert.Equal("page", await page.Page.Locator("#nav-general").GetAttributeAsync("aria-current"));

        await page.PushAsync("settings-models");
        Assert.Equal("page", await page.Page.Locator("#nav-models").GetAttributeAsync("aria-current"));
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task TheSettingsWindowNeverReportsASize(string engine)
    {
        await using var page = await browsers.OpenSettingsAsync(engine, "settings-voice");
        await page.PushAsync("settings-models");

        Assert.Equal(0, await page.CountAsync<SizeIntent>());
    }
}
