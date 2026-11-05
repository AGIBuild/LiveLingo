using LiveLingo.Core.Models;
using LiveLingo.Core.Cleanup;
using LiveLingo.Core.Translation;
using LiveLingo.Desktop.Composer;
using LiveLingo.Desktop.Platform;
using LiveLingo.Desktop.Services.Configuration;
using LiveLingo.Desktop.Services.Preferences;
using LiveLingo.Desktop.Settings;
using NSubstitute;

namespace LiveLingo.Desktop.Tests.Settings;

public class SettingsViewModelTests
{
    internal sealed class Fixture
    {
        public SettingsModel Current { get; }
        public List<SettingsModel> Saved { get; } = [];
        public ISecretStore Secrets { get; } = Substitute.For<ISecretStore>();
        public IModelManager Models { get; } = Substitute.For<IModelManager>();
        public IMicrophone Microphone { get; } = Substitute.For<IMicrophone>();
        public RecentDrafts Recent { get; } = new();
        public Services.Preferences.MemoryPreferencesStore PreferenceStore { get; } = new();
        public SettingsViewModel Vm { get; }

        public Fixture(SettingsModel? current = null, string? storedKey = null, PreferencesData? remembered = null)
        {
            Current = current ?? new SettingsModel();
            PreferenceStore.Data = remembered ?? new PreferencesData();
            var service = Substitute.For<ISettingsService>();
            service.Current.Returns(_ => Saved.Count > 0 ? Saved[^1] : Current);
            service.CloneCurrent().Returns(_ => Saved.Count > 0 ? Saved[^1].DeepClone() : Current.DeepClone());
            service.When(s => s.Replace(Arg.Any<SettingsModel>())).Do(call => Saved.Add(call.Arg<SettingsModel>()));
            Secrets.GetSecretAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(storedKey);
            Microphone.InputDevices().Returns(["USB Mic", "Built-in"]);
            Vm = new SettingsViewModel(service, Secrets, Models, Recent, Microphone, new ComposerPreferences(PreferenceStore, service));
        }

        public SettingsModel Last => Saved[^1];
    }

    [Fact]
    public void StartsFromTheCurrentSettings()
    {
        var model = new SettingsModel();
        model.Hotkeys.Summon = "Alt+Space";
        model.Appearance.Language = "en-US";
        model.Appearance.Theme = "Dark";
        model.Appearance.Animations = false;
        model.Composer.CommitKey = "CtrlEnter";
        model.Composer.TargetLanguage = "Chinese";
        model.Composer.TranslatorTier = "Fast";
        model.Composer.CleanupEnabled = false;
        model.Composer.Glossary = "Redis\n缓存穿透=cache penetration";
        model.Cloud.Endpoints = CloudTestData.WithEndpoints("main", translate: true).Cloud.Endpoints;
        model.Cloud.ActiveEndpointId = "main";
        model.Cloud.Translate = true;

        var vm = new Fixture(model).Vm;

        Assert.False(vm.CleanupEnabled);
        Assert.Equal("Alt+Space", vm.SummonHotkey);
        Assert.Equal(1, vm.LanguageIndex);
        Assert.Equal(2, vm.ThemeIndex);
        Assert.True(vm.ReduceMotion);
        Assert.Equal(1, vm.CommitKeyIndex);
        Assert.Equal(1, vm.TargetIndex);
        Assert.Equal(1, vm.TierIndex);
        Assert.Equal([new TermEntry("Redis", "Redis"), new TermEntry("缓存穿透", "cache penetration")], vm.Terms);
        Assert.Equal(["Main", "Other"], vm.Endpoints.Select(endpoint => endpoint.Name));
        Assert.Equal("Main", vm.ActiveEndpoint?.Name);
        Assert.True(vm.CloudTranslate);
        Assert.False(vm.CloudClean);
    }

    [Fact]
    public void Opening_ApplyingNothing_DoesNotSave()
    {
        var f = new Fixture();

        Assert.Empty(f.Saved);
    }

    [Fact]
    public void PolishRequirements_AreSaved_AndInvalidEditsKeepThePreviousValue()
    {
        var f = new Fixture();
        Assert.Equal(PolishStyle.Default, f.Vm.PolishPromptShown);
        Assert.True(f.Vm.PolishPromptIsDefault);
        f.Vm.PolishPrompt = "Keep the original tone and use paragraphs.";
        Assert.Equal(f.Vm.PolishPrompt, f.Last.Composer.PolishPrompt);
        Assert.Equal(f.Last.Composer.PolishPrompt, f.Last.DeepClone().Composer.PolishPrompt);
        f.Vm.PolishPrompt = "bad";
        Assert.True(f.Vm.PolishPromptInvalid);
        Assert.Equal("bad", f.Vm.PolishPromptShown);
        Assert.Equal("Keep the original tone and use paragraphs.", f.Last.Composer.PolishPrompt);
        f.Vm.PolishPrompt = "";
        Assert.False(f.Vm.PolishPromptInvalid);
        Assert.True(f.Vm.PolishPromptIsDefault);
        Assert.Equal("", f.Last.Composer.PolishPrompt);
    }

    [Fact]
    public void DamagedStoredRequirements_ShowTheDefault()
    {
        var model = new SettingsModel();
        model.Composer.PolishPrompt = "bad";
        var f = new Fixture(model);
        Assert.True(f.Vm.PolishPromptIsDefault);
        Assert.Equal(PolishStyle.Default, f.Vm.PolishPromptShown);
        Assert.Empty(f.Saved);
    }

    [Fact]
    public void EveryChange_IsAppliedAtOnce()
    {
        var f = new Fixture();

        f.Vm.SummonHotkey = " Ctrl+Shift+L ";
        f.Vm.LanguageIndex = 1;
        f.Vm.ThemeIndex = 1;
        f.Vm.CommitKeyIndex = 1;
        f.Vm.ReduceMotion = true;
        f.Vm.TargetIndex = 1;
        f.Vm.TierIndex = 1;
        f.Vm.CleanupEnabled = false;

        var model = f.Last;
        Assert.False(model.Composer.CleanupEnabled);
        Assert.Equal("Ctrl+Shift+L", model.Hotkeys.Summon);
        Assert.Equal("en-US", model.Appearance.Language);
        Assert.Equal("Light", model.Appearance.Theme);
        Assert.Equal("CtrlEnter", model.Composer.CommitKey);
        Assert.False(model.Appearance.Animations);
        Assert.Equal("Chinese", model.Composer.TargetLanguage);
        Assert.Equal("Fast", model.Composer.TranslatorTier);
    }

    [Fact]
    public void ThePreferencesOfTheComposer_StartFromTheSettingsAndAreAppliedAtOnce()
    {
        var model = new SettingsModel();
        Assert.True(model.Composer.RememberChoices);
        Assert.False(model.Composer.AutoDictate);
        Assert.True(model.Composer.PreloadSpeech);
        Assert.Equal("Clean", model.Composer.DefaultOutput);

        var f = new Fixture();
        f.Vm.RememberChoices = false;
        f.Vm.AutoDictate = true;
        f.Vm.PreloadSpeech = false;
        f.Vm.DefaultOutputIndex = SettingsViewModel.DefaultOutputs.ToList().IndexOf("Translate");

        Assert.False(f.Last.Composer.RememberChoices);
        Assert.True(f.Last.Composer.AutoDictate);
        Assert.False(f.Last.Composer.PreloadSpeech);
        Assert.Equal("Translate", f.Last.Composer.DefaultOutput);
    }

    [Fact]
    public void TheRememberedApps_CanBeForgottenOneByOne_OrAllAtOnce()
    {
        var now = DateTimeOffset.UnixEpoch;
        var data = new PreferencesData
        {
            Apps =
            [
                new AppChoice("slack", "Slack", ComposerOutput.Translate, TargetLanguage.English, now),
                new AppChoice("code", "Code", ComposerOutput.Clean, TargetLanguage.English, now)
            ],
            ExportFormat = ExportFormat.Markdown
        };
        var f = new Fixture(remembered: data);
        var changed = new List<string?>();
        f.Vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        f.Vm.ForgetApp("slack");

        Assert.Equal(["code"], f.Vm.Apps.Select(app => app.AppId));
        Assert.Contains(nameof(SettingsViewModel.Apps), changed);

        f.Vm.ClearApps();

        Assert.Empty(f.Vm.Apps);
        Assert.Null(f.PreferenceStore.Data.ExportFormat);
    }

    [Fact]
    public void AComboBoxThatIsBeingRefilled_IsNotAChoice()
    {
        var f = new Fixture();

        f.Vm.ThemeIndex = -1;

        Assert.Empty(f.Saved);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Ctrl+Alt+")]
    [InlineData("   ")]
    public void AnInvalidHotkey_IsFlagged_AndNotStored(string hotkey)
    {
        var f = new Fixture();

        f.Vm.SummonHotkey = hotkey;

        Assert.True(f.Vm.HotkeyInvalid);
        Assert.Equal(new SettingsModel().Hotkeys.Summon, f.Last.Hotkeys.Summon);

        f.Vm.SummonHotkey = "Ctrl+Alt+T";
        Assert.False(f.Vm.HotkeyInvalid);
        Assert.Equal("Ctrl+Alt+T", f.Last.Hotkeys.Summon);
    }

    // ---- glossary ---------------------------------------------------------------

    [Fact]
    public void AddingATerm_WithoutATranslation_KeepsItAsWritten()
    {
        var f = new Fixture();

        f.Vm.NewTermSource = " Avalonia ";
        f.Vm.AddTermCommand.Execute(null);

        Assert.Equal([new TermEntry("Avalonia", "Avalonia")], f.Vm.Terms);
        Assert.Equal("Avalonia", f.Last.Composer.Glossary);
        Assert.Equal("", f.Vm.NewTermSource);
    }

    [Fact]
    public void AddingATermWithATranslation_StoresThePair_AndRemovingItDropsIt()
    {
        var f = new Fixture();
        f.Vm.NewTermSource = "缓存穿透";
        f.Vm.NewTermTarget = "cache penetration";
        f.Vm.AddTermCommand.Execute(null);
        Assert.Equal("缓存穿透=cache penetration", f.Last.Composer.Glossary);

        f.Vm.RemoveTermCommand.Execute(f.Vm.Terms[0]);

        Assert.Empty(f.Vm.Terms);
        Assert.Equal("", f.Last.Composer.Glossary);
    }

    [Fact]
    public void AddingAnEmptyTerm_DoesNothing()
    {
        var f = new Fixture();

        f.Vm.NewTermSource = "  ";
        f.Vm.AddTermCommand.Execute(null);

        Assert.Empty(f.Vm.Terms);
        Assert.Empty(f.Saved);
    }

    // ---- cloud ------------------------------------------------------------------

    [Fact]
    public void Cloud_IsOffByDefault()
    {
        var vm = new Fixture().Vm;

        Assert.False(vm.CloudTranslate);
        Assert.False(vm.CloudClean);
    }

    [Fact]
    public void EnablingCloud_WithoutAnEnabledEndpoint_IsRefused_AndFlagged()
    {
        var f = new Fixture(CloudTestData.WithEndpoints(active: null), storedKey: "sk");

        Assert.False(f.Vm.TryEnableCloud(translation: true));

        Assert.True(f.Vm.CloudInvalid);
        Assert.False(f.Vm.CloudTranslate);
        Assert.Empty(f.Saved);
    }

    [Fact]
    public async Task AddingAnEndpoint_StoresTheKeyInTheSecretStore_NotInTheSettings_AndDoesNotEnableIt()
    {
        var f = new Fixture();
        f.Vm.OpenEndpointForm(null);

        await f.Vm.SaveEndpointAsync(" Foundry ", " https://example.openai.azure.com/openai/v1/ ", " gpt-4.1-mini ", " sk-secret ");

        var saved = Assert.Single(f.Last.Cloud.Endpoints);
        Assert.Equal("Foundry", saved.Name);
        Assert.Equal("https://example.openai.azure.com/openai/v1", saved.BaseUrl);
        Assert.Equal("gpt-4.1-mini", saved.Model);
        await f.Secrets.Received(1).SetSecretAsync(saved.SecretId, "sk-secret", Arg.Any<CancellationToken>());
        Assert.Equal("", f.Last.Cloud.ActiveEndpointId);
        Assert.Null(f.Vm.ActiveEndpoint);
        Assert.Null(f.Vm.Form);
        Assert.True(Assert.Single(f.Vm.Endpoints).HasKey);
        Assert.DoesNotContain("sk-secret", System.Text.Json.JsonSerializer.Serialize(f.Last));
    }

    [Theory]
    [InlineData("", "https://api.example.com/v1", "m", "sk", EndpointError.NameMissing)]
    [InlineData("main", "https://api.example.com/v1", "m", "sk", EndpointError.NameTaken)]
    [InlineData("New", "http://api.example.com/v1", "m", "sk", EndpointError.UrlInvalid)]
    [InlineData("New", "not a url", "m", "sk", EndpointError.UrlInvalid)]
    [InlineData("New", "https://api.example.com/v1", "", "sk", EndpointError.ModelMissing)]
    [InlineData("New", "https://api.example.com/v1", "m", "", EndpointError.KeyMissing)]
    public async Task AnUnusableEndpoint_KeepsTheFormOpen_WithTheReason_AndStoresNothing(
        string name, string url, string model, string key, EndpointError expected)
    {
        var f = new Fixture(CloudTestData.WithEndpoints(active: null));
        f.Vm.OpenEndpointForm(null);

        await f.Vm.SaveEndpointAsync(name, url, model, key);

        Assert.Equal(expected, f.Vm.Form?.Error);
        Assert.Empty(f.Saved);
        await f.Secrets.DidNotReceiveWithAnyArgs().SetSecretAsync(default!, default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task LocalhostMayUsePlainHttp()
    {
        var f = new Fixture();
        f.Vm.OpenEndpointForm(null);

        await f.Vm.SaveEndpointAsync("Local", "http://localhost:8080/v1", "qwen", "sk");

        Assert.Equal("http://localhost:8080/v1", Assert.Single(f.Last.Cloud.Endpoints).BaseUrl);
    }

    [Fact]
    public async Task EditingAnEndpoint_WithAnEmptyKey_KeepsTheStoredKey_AndItsOwnNameIsNotATaken()
    {
        var f = new Fixture(CloudTestData.WithEndpoints(active: null), storedKey: "sk-stored");
        await f.Vm.InitializeAsync();
        f.Vm.OpenEndpointForm("main");

        await f.Vm.SaveEndpointAsync("Main", CloudTestData.MainUrl, "m2", "");

        Assert.Null(f.Vm.Form);
        Assert.Equal("m2", f.Last.Cloud.Endpoints.Single(endpoint => endpoint.Id == "main").Model);
        await f.Secrets.DidNotReceiveWithAnyArgs().SetSecretAsync(default!, default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task EditingAnEndpoint_WithANewKey_ReplacesItUnderTheSameSecretId()
    {
        var f = new Fixture(CloudTestData.WithEndpoints(active: null), storedKey: "sk-stored");
        await f.Vm.InitializeAsync();
        f.Vm.OpenEndpointForm("other");

        await f.Vm.SaveEndpointAsync("Other", CloudTestData.OtherUrl, "o", " sk-new ");

        await f.Secrets.Received(1).SetSecretAsync(CloudTestData.SecretId("other"), "sk-new", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ChangingTheHostOfTheEnabledEndpoint_SwitchesTheCloudStepsOff()
    {
        var f = new Fixture(CloudTestData.WithEndpoints(translate: true, clean: true), storedKey: "sk");
        await f.Vm.InitializeAsync();
        f.Vm.OpenEndpointForm("main");

        await f.Vm.SaveEndpointAsync("Main", "https://elsewhere.example.net/v1", "m", "");

        Assert.False(f.Vm.CloudTranslate);
        Assert.False(f.Vm.CloudClean);
        Assert.False(f.Last.Cloud.Translate);
        Assert.False(f.Last.Cloud.Clean);
        Assert.Equal("main", f.Last.Cloud.ActiveEndpointId);
    }

    [Fact]
    public async Task ChangingOnlyThePathOrModelOfTheEnabledEndpoint_KeepsTheCloudStepsOn()
    {
        var f = new Fixture(CloudTestData.WithEndpoints(translate: true), storedKey: "sk");
        await f.Vm.InitializeAsync();
        f.Vm.OpenEndpointForm("main");

        await f.Vm.SaveEndpointAsync("Renamed", "https://api.example.com/openai/v1", "m2", "");

        Assert.True(f.Last.Cloud.Translate);
        Assert.Equal("Renamed", f.Last.Cloud.Endpoints[0].Name);
    }

    [Fact]
    public void OpeningTheFormForAnUnknownEndpoint_ShowsNothing()
    {
        var f = new Fixture(CloudTestData.WithEndpoints());

        f.Vm.OpenEndpointForm("gone");

        Assert.Null(f.Vm.Form);
    }

    [Fact]
    public async Task EnablingAnEndpoint_DisablesTheOne_ThatWasEnabled()
    {
        var f = new Fixture(CloudTestData.WithEndpoints(), storedKey: "sk");
        await f.Vm.InitializeAsync();

        f.Vm.SetEndpointEnabled("other", on: true);

        Assert.Equal("other", f.Last.Cloud.ActiveEndpointId);
        Assert.Equal(["other"], f.Vm.Endpoints.Where(endpoint => endpoint.Active).Select(endpoint => endpoint.Id));
        Assert.Null(f.Vm.PendingChange);
    }

    [Fact]
    public async Task EnablingAnEndpoint_WhileAStepIsOn_AsksFirst_AndKeepsTheOldOneUntilAnswered()
    {
        var f = new Fixture(CloudTestData.WithEndpoints(translate: true), storedKey: "sk");
        await f.Vm.InitializeAsync();

        f.Vm.SetEndpointEnabled("other", on: true);

        Assert.Equal(new PendingEndpointChange(EndpointChange.Switch, "other"), f.Vm.PendingChange);
        Assert.Empty(f.Saved);

        await f.Vm.AnswerChangeAsync(allow: false);
        Assert.Null(f.Vm.PendingChange);
        Assert.Empty(f.Saved);

        f.Vm.SetEndpointEnabled("other", on: true);
        await f.Vm.AnswerChangeAsync(allow: true);
        Assert.Equal("other", f.Last.Cloud.ActiveEndpointId);
        Assert.True(f.Last.Cloud.Translate);
    }

    [Fact]
    public async Task SwitchingToAnEndpointWithoutAKey_WhileAStepIsOn_IsRefusedAndFlagged()
    {
        var f = new Fixture(CloudTestData.WithEndpoints(translate: true), storedKey: null);
        await f.Vm.InitializeAsync();

        f.Vm.SetEndpointEnabled("other", on: true);

        Assert.Null(f.Vm.PendingChange);
        Assert.True(f.Vm.CloudInvalid);
        Assert.Empty(f.Saved);
    }

    [Fact]
    public async Task DisablingTheEnabledEndpoint_SwitchesBothCloudStepsOff()
    {
        var f = new Fixture(CloudTestData.WithEndpoints(translate: true, clean: true), storedKey: "sk");
        await f.Vm.InitializeAsync();

        f.Vm.SetEndpointEnabled("main", on: false);

        Assert.Equal("", f.Last.Cloud.ActiveEndpointId);
        Assert.False(f.Last.Cloud.Translate);
        Assert.False(f.Last.Cloud.Clean);
        Assert.False(f.Vm.CloudTranslate);
        Assert.Null(f.Vm.ActiveEndpoint);
    }

    [Fact]
    public async Task ExactlyOneEndpoint_IsEverEnabled()
    {
        var f = new Fixture(CloudTestData.WithEndpoints(active: null), storedKey: "sk");
        await f.Vm.InitializeAsync();

        f.Vm.SetEndpointEnabled("main", on: true);
        f.Vm.SetEndpointEnabled("other", on: true);
        f.Vm.SetEndpointEnabled("main", on: true);

        Assert.Equal(1, f.Vm.Endpoints.Count(endpoint => endpoint.Active));
        Assert.Equal("main", f.Last.Cloud.ActiveEndpointId);
    }

    [Fact]
    public async Task DeletingAnEndpoint_AsksFirst_ThenDeletesItsKey()
    {
        var f = new Fixture(CloudTestData.WithEndpoints(active: "main"), storedKey: "sk");
        await f.Vm.InitializeAsync();

        f.Vm.DeleteEndpoint("other");
        Assert.Equal(new PendingEndpointChange(EndpointChange.Delete, "other"), f.Vm.PendingChange);
        Assert.Empty(f.Saved);

        await f.Vm.AnswerChangeAsync(allow: true);

        await f.Secrets.Received(1).DeleteSecretAsync(CloudTestData.SecretId("other"), Arg.Any<CancellationToken>());
        Assert.Equal(["main"], f.Last.Cloud.Endpoints.Select(endpoint => endpoint.Id));
        Assert.Equal("main", f.Last.Cloud.ActiveEndpointId);
    }

    [Fact]
    public async Task DeletingTheEnabledEndpoint_SwitchesTheCloudStepsOff()
    {
        var f = new Fixture(CloudTestData.WithEndpoints(translate: true, clean: true), storedKey: "sk");
        await f.Vm.InitializeAsync();

        f.Vm.DeleteEndpoint("main");
        await f.Vm.AnswerChangeAsync(allow: true);

        Assert.Equal("", f.Last.Cloud.ActiveEndpointId);
        Assert.False(f.Last.Cloud.Translate);
        Assert.False(f.Last.Cloud.Clean);
        Assert.Equal(["other"], f.Last.Cloud.Endpoints.Select(endpoint => endpoint.Id));
    }

    [Fact]
    public async Task RefusingTheDeletion_ChangesNothing()
    {
        var f = new Fixture(CloudTestData.WithEndpoints(), storedKey: "sk");
        await f.Vm.InitializeAsync();
        f.Vm.DeleteEndpoint("main");

        await f.Vm.AnswerChangeAsync(allow: false);

        Assert.Empty(f.Saved);
        await f.Secrets.DidNotReceiveWithAnyArgs().DeleteSecretAsync(default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task AStoredKey_SatisfiesTheConfiguration_WithoutRetypingIt()
    {
        var f = new Fixture(CloudTestData.WithEndpoints(), storedKey: "sk-stored");
        await f.Vm.InitializeAsync();

        Assert.True(f.Vm.ActiveEndpoint?.HasKey);
        Assert.True(f.Vm.TryEnableCloud(translation: true));
        Assert.True(f.Last.Cloud.Translate);
    }

    [Fact]
    public async Task DisablingCloud_SwitchesOnlyThatStepOff()
    {
        var f = new Fixture(CloudTestData.WithEndpoints(translate: true, clean: true), storedKey: "sk");
        await f.Vm.InitializeAsync();

        f.Vm.DisableCloud(translation: true);

        Assert.False(f.Last.Cloud.Translate);
        Assert.True(f.Last.Cloud.Clean);
    }

    [Fact]
    public async Task Initialize_ReportsWhichEndpointsHaveAKey()
    {
        var withKey = new Fixture(CloudTestData.WithEndpoints(), storedKey: "sk").Vm;
        var withoutKey = new Fixture(CloudTestData.WithEndpoints(), storedKey: null).Vm;

        await withKey.InitializeAsync();
        await withoutKey.InitializeAsync();

        Assert.All(withKey.Endpoints, endpoint => Assert.True(endpoint.HasKey));
        Assert.All(withoutKey.Endpoints, endpoint => Assert.False(endpoint.HasKey));
    }

    // ---- microphone -------------------------------------------------------------

    [Fact]
    public async Task Initialize_ListsTheDevices_AndSelectsTheSavedOne()
    {
        var model = new SettingsModel();
        model.Composer.Microphone = "Built-in";
        var f = new Fixture(model);

        await f.Vm.InitializeAsync();

        Assert.Equal(["", "USB Mic", "Built-in"], f.Vm.MicrophoneNames);
        Assert.Equal(2, f.Vm.MicrophoneIndex);
        Assert.Empty(f.Saved);
    }

    [Fact]
    public async Task ChoosingADevice_StoresItsName_AndSystemDefaultStoresNothing()
    {
        var f = new Fixture();
        await f.Vm.InitializeAsync();

        f.Vm.MicrophoneIndex = 1;
        Assert.Equal("USB Mic", f.Last.Composer.Microphone);

        f.Vm.MicrophoneIndex = 0;
        Assert.Equal("", f.Last.Composer.Microphone);
    }

    [Fact]
    public void BeforeTheDevicesAreKnown_TheSavedMicrophoneIsKept()
    {
        var model = new SettingsModel();
        model.Composer.Microphone = "USB Mic";
        var f = new Fixture(model);

        f.Vm.ReduceMotion = true;

        Assert.Equal("USB Mic", f.Last.Composer.Microphone);
    }

    // ---- recent drafts and models from earlier versions -----------------------------

    [Fact]
    public void ClearingRecentDrafts_EmptiesTheList()
    {
        var f = new Fixture();
        f.Recent.Close("昨天的想法", written: true);

        f.Vm.ClearRecentCommand.Execute(null);

        Assert.Empty(f.Recent.Items);
    }

    private static InstalledModel Installed(string id, long bytes) =>
        new(id, id, "/models/" + id, bytes, ModelType.Translation, DateTime.UnixEpoch);

    [Fact]
    public async Task OnlyModelsOutsideTheCatalog_AreOfferedForRemoval()
    {
        var f = new Fixture();
        f.Models.ListInstalled().Returns([
            Installed(ModelCatalog.TranslatorFast.Id, 1_000_000_000),
            Installed("qwen3.5-9b-old", 5_500_000_000),
            Installed("whisper-large-old", 3_000_000_000)]);
        Assert.False(f.Vm.HasLegacyModels);

        await f.Vm.InitializeAsync();

        Assert.True(f.Vm.HasLegacyModels);
        Assert.Equal(2, f.Vm.LegacyCount);
        Assert.Equal(8_500_000_000, f.Vm.LegacyBytes);
    }

    [Fact]
    public async Task NothingIsDeleted_UntilTheUserAsks()
    {
        var f = new Fixture();
        f.Models.ListInstalled().Returns([Installed("old-a", 10), Installed("old-b", 20)]);

        await f.Vm.InitializeAsync();
        await f.Models.DidNotReceiveWithAnyArgs().CleanObsoleteModelsAsync(default!, TestContext.Current.CancellationToken);

        await f.Vm.RemoveLegacyModelsCommand.ExecuteAsync(null);

        await f.Models.Received(1).CleanObsoleteModelsAsync(
            Arg.Is<IEnumerable<string>>(ids => ids.OrderBy(id => id).SequenceEqual(new[] { "old-a", "old-b" })),
            Arg.Any<CancellationToken>());
        Assert.False(f.Vm.HasLegacyModels);
        Assert.Equal(0, f.Vm.LegacyBytes);
    }

    [Fact]
    public async Task WithOnlyCurrentModels_NothingIsOffered()
    {
        var f = new Fixture();
        f.Models.ListInstalled().Returns([Installed(ModelCatalog.Cleaner.Id, 1)]);

        await f.Vm.InitializeAsync();

        Assert.False(f.Vm.HasLegacyModels);
    }

    // ---- consent ------------------------------------------------------------------------

    private static async Task<Fixture> ConfiguredCloudAsync()
    {
        var f = new Fixture(CloudTestData.WithEndpoints(), storedKey: "sk");
        await f.Vm.InitializeAsync();
        return f;
    }

    [Fact]
    public async Task TurningAStepOn_AsksForConsentAndChangesNothingYet()
    {
        var f = await ConfiguredCloudAsync();

        f.Vm.SetCloud(CloudStep.Clean, on: true);

        Assert.Equal(CloudStep.Clean, f.Vm.PendingConsent);
        Assert.False(f.Vm.CloudClean);
        Assert.Empty(f.Saved);
    }

    [Fact]
    public async Task Consenting_TurnsOnlyThatStepOn()
    {
        var f = await ConfiguredCloudAsync();
        f.Vm.SetCloud(CloudStep.Translate, on: true);

        f.Vm.AnswerConsent(allow: true);

        Assert.Null(f.Vm.PendingConsent);
        Assert.True(f.Last.Cloud.Translate);
        Assert.False(f.Last.Cloud.Clean);
    }

    [Fact]
    public async Task RefusingTheConsent_LeavesTheStepOff()
    {
        var f = await ConfiguredCloudAsync();
        f.Vm.SetCloud(CloudStep.Translate, on: true);

        f.Vm.AnswerConsent(allow: false);

        Assert.Null(f.Vm.PendingConsent);
        Assert.False(f.Vm.CloudTranslate);
        Assert.Empty(f.Saved);
    }

    [Fact]
    public void TurningAStepOn_WithoutAnEnabledEndpoint_IsRefusedAndFlagged_WithoutAskingForConsent()
    {
        var f = new Fixture(CloudTestData.WithEndpoints(active: null), storedKey: "sk");

        f.Vm.SetCloud(CloudStep.Translate, on: true);

        Assert.Null(f.Vm.PendingConsent);
        Assert.False(f.Vm.CloudTranslate);
        Assert.True(f.Vm.CloudInvalid);
    }

    [Fact]
    public async Task TurningAStepOff_NeedsNoConsent()
    {
        var f = new Fixture(CloudTestData.WithEndpoints(clean: true), storedKey: "sk");
        await f.Vm.InitializeAsync();

        f.Vm.SetCloud(CloudStep.Clean, on: false);

        Assert.Null(f.Vm.PendingConsent);
        Assert.False(f.Last.Cloud.Clean);
    }

    [Fact]
    public async Task AStepThatIsAlreadyOn_IsNotAskedAgain()
    {
        var f = new Fixture(CloudTestData.WithEndpoints(translate: true), storedKey: "sk");
        await f.Vm.InitializeAsync();

        f.Vm.SetCloud(CloudStep.Translate, on: true);

        Assert.Null(f.Vm.PendingConsent);
    }

    [Fact]
    public async Task AnAnswerWithoutAQuestion_ChangesNothing()
    {
        var f = await ConfiguredCloudAsync();

        f.Vm.AnswerConsent(allow: true);

        Assert.Empty(f.Saved);
    }
}
