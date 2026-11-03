using LiveLingo.Core.Models;
using LiveLingo.Core.Translation;
using LiveLingo.Desktop.Composer;
using LiveLingo.Desktop.Services.Configuration;
using LiveLingo.Desktop.Services.Preferences;
using LiveLingo.Desktop.Services.Update;
using LiveLingo.Desktop.Settings;
using LiveLingo.Desktop.Tests.Composer;
using LiveLingo.Desktop.Tests.Settings;
using LiveLingo.Desktop.Web;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace LiveLingo.Desktop.Tests.Web;

public sealed class SettingsIntentsTests
{
    private sealed class Rig
    {
        public SettingsViewModelTests.Fixture F { get; }
        public ListLogger Log { get; } = new();
        public List<string> Opened { get; } = [];
        public SettingsIntents Sut { get; }

        public Rig(
            SettingsModel? model = null, string? storedKey = null, string section = "general", PreferencesData? remembered = null,
            UpdateChecks? updates = null)
        {
            F = new SettingsViewModelTests.Fixture(model, storedKey, remembered);
            Sut = new SettingsIntents(F.Vm, section, updates, Opened.Add, Log);
        }

        public SettingsViewModel Vm => F.Vm;

        public void Do(WebIntent intent) => Sut.Handle(intent);

        public void Set(string key, string value) => Do(new SettingIntent(key, value) { V = 1 });
    }

    private static SettingsModel ConfiguredCloud()
    {
        var model = new SettingsModel();
        model.Cloud.BaseUrl = "https://api.example.com/v1";
        model.Cloud.Model = "m";
        return model;
    }

    [Fact]
    public void EachSetting_ReachesItsViewModelProperty_AndIsStored()
    {
        var rig = new Rig();

        rig.Set(SettingIntent.Names.Hotkey, "Ctrl+Shift+K");
        rig.Set(SettingIntent.Names.CommitKey, "CtrlEnter");
        rig.Set(SettingIntent.Names.Language, "en-US");
        rig.Set(SettingIntent.Names.Theme, "Dark");
        rig.Set(SettingIntent.Names.Direction, "Chinese");
        rig.Set(SettingIntent.Names.Tier, "Fast");
        rig.Set(SettingIntent.Names.ReduceMotion, "true");

        var stored = rig.F.Last;
        Assert.Equal("Ctrl+Shift+K", stored.Hotkeys.Summon);
        Assert.Equal("CtrlEnter", stored.Composer.CommitKey);
        Assert.Equal("en-US", stored.Appearance.Language);
        Assert.Equal("Dark", stored.Appearance.Theme);
        Assert.Equal("Chinese", stored.Composer.TargetLanguage);
        Assert.Equal("Fast", stored.Composer.TranslatorTier);
        Assert.False(stored.Appearance.Animations);
    }

    [Fact]
    public void TheCleanupSwitch_IsStored()
    {
        var rig = new Rig();

        rig.Set(SettingIntent.Names.Cleanup, "false");

        Assert.False(rig.Vm.CleanupEnabled);
        Assert.False(rig.F.Last.Composer.CleanupEnabled);

        rig.Set(SettingIntent.Names.Cleanup, "true");

        Assert.True(rig.F.Last.Composer.CleanupEnabled);
    }

    [Fact]
    public void ThePolishPrompt_IsStoredOrFlagged_AndCanBeReset()
    {
        var rig = new Rig();
        rig.Set(SettingIntent.Names.PolishPrompt, "Keep my tone without inventing facts.");
        Assert.Equal("Keep my tone without inventing facts.", rig.F.Last.Composer.PolishPrompt);
        rig.Set(SettingIntent.Names.PolishPrompt, "bad");
        Assert.True(rig.Vm.PolishPromptInvalid);
        Assert.Equal("Keep my tone without inventing facts.", rig.F.Last.Composer.PolishPrompt);
        rig.Set(SettingIntent.Names.PolishPrompt, "");
        Assert.Equal("", rig.F.Last.Composer.PolishPrompt);
    }

    [Fact]
    public void ACloudEndpoint_IsStored()
    {
        var rig = new Rig();

        rig.Set(SettingIntent.Names.CloudBaseUrl, "https://api.example.com/v1");
        rig.Set(SettingIntent.Names.CloudModel, "gpt-deploy");

        Assert.Equal("https://api.example.com/v1", rig.Vm.CloudBaseUrl);
        Assert.Equal("gpt-deploy", rig.Vm.CloudModel);
        Assert.Equal("gpt-deploy", rig.F.Last.Cloud.Model);
    }

    [Fact]
    public void AnUnusableHotkey_IsFlaggedByTheViewModel_AndNotStored()
    {
        var rig = new Rig();

        rig.Set(SettingIntent.Names.Hotkey, "Ctrl+");

        Assert.True(rig.Vm.HotkeyInvalid);
        Assert.NotEqual("Ctrl+", rig.F.Last.Hotkeys.Summon);
    }

    [Fact]
    public async Task AMicrophone_IsChosenByName_AndOneThatIsGoneChangesNothing()
    {
        var rig = new Rig();
        await rig.Vm.InitializeAsync();

        rig.Set(SettingIntent.Names.Microphone, "Built-in");
        Assert.Equal(2, rig.Vm.MicrophoneIndex);

        rig.Set(SettingIntent.Names.Microphone, "Unplugged");
        Assert.Equal(2, rig.Vm.MicrophoneIndex);

        rig.Set(SettingIntent.Names.Microphone, "");
        Assert.Equal(0, rig.Vm.MicrophoneIndex);
    }

    [Fact]
    public void AGlossaryTerm_IsAddedWithItsTranslation_AndRemovedByPosition()
    {
        var rig = new Rig();

        rig.Do(new AddTermIntent("缓存穿透", "cache penetration") { V = 1 });
        rig.Do(new AddTermIntent("Redis", "") { V = 1 });

        Assert.Equal([new TermEntry("缓存穿透", "cache penetration"), new TermEntry("Redis", "Redis")], rig.Vm.Terms);

        rig.Do(new RemoveTermIntent(0) { V = 1 });

        Assert.Equal([new TermEntry("Redis", "Redis")], rig.Vm.Terms);
        Assert.Equal("Redis", rig.F.Last.Composer.Glossary);
    }

    [Fact]
    public void RemovingATermThatIsNotThere_ChangesNothing()
    {
        var rig = new Rig();
        rig.Do(new AddTermIntent("Redis", "Redis") { V = 1 });

        rig.Do(new RemoveTermIntent(7) { V = 1 });

        Assert.Single(rig.Vm.Terms);
    }

    [Fact]
    public async Task ACloudStep_NeedsTheUsersAnswerBeforeItTurnsOn()
    {
        var rig = new Rig(ConfiguredCloud(), storedKey: "sk");
        await rig.Vm.InitializeAsync();

        rig.Do(new CloudToggleIntent(CloudStep.Translate, On: true) { V = 1 });
        Assert.Equal(CloudStep.Translate, rig.Vm.PendingConsent);
        Assert.False(rig.Vm.CloudTranslate);

        rig.Do(new ConsentIntent(Allow: true) { V = 1 });
        Assert.Null(rig.Vm.PendingConsent);
        Assert.True(rig.F.Last.Cloud.Translate);

        rig.Do(new CloudToggleIntent(CloudStep.Translate, On: false) { V = 1 });
        Assert.False(rig.F.Last.Cloud.Translate);
    }

    [Fact]
    public async Task TheKey_GoesToTheSecretStoreOnly()
    {
        var rig = new Rig();

        rig.Do(new SaveKeyIntent("sk-typed") { V = 1 });
        await Wait.Until(() => rig.Vm.HasStoredKey);

        await rig.F.Secrets.Received(1).SetSecretAsync(CloudSettings.ApiKeySecretId, "sk-typed", Arg.Any<CancellationToken>());
        Assert.Equal("", rig.Vm.CloudApiKey);
        Assert.DoesNotContain(rig.Log.Lines, line => line.Contains("sk-typed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ForgettingTheKey_DeletesItAndTurnsTheCloudOff()
    {
        var rig = new Rig(ConfiguredCloud(), storedKey: "sk");
        await rig.Vm.InitializeAsync();

        rig.Do(new ForgetKeyIntent { V = 1 });
        await Wait.Until(() => !rig.Vm.HasStoredKey);

        await rig.F.Secrets.Received(1).DeleteSecretAsync(CloudSettings.ApiKeySecretId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AFailingSecretStore_IsLoggedByTypeOnly()
    {
        var rig = new Rig();
        rig.F.Secrets.SetSecretAsync(default!, default!, TestContext.Current.CancellationToken).ReturnsForAnyArgs(Task.FromException(new InvalidOperationException("sk-secret rejected")));

        rig.Do(new SaveKeyIntent("sk-secret") { V = 1 });
        await Wait.Until(() => rig.Log.Lines.Count > 0);

        Assert.Contains("InvalidOperationException", rig.Log.Lines[0]);
        Assert.DoesNotContain("sk-secret", rig.Log.Lines[0]);
    }

    [Fact]
    public void TheComposerPreferences_ReachTheViewModel_AndTheRememberedAppsAreForgotten()
    {
        var data = new PreferencesData
        {
            Apps = [new AppChoice("slack", "Slack", ComposerOutput.Translate, TargetLanguage.English, DateTimeOffset.UnixEpoch)]
        };
        var rig = new Rig(remembered: data);

        rig.Set(SettingIntent.Names.DefaultOutput, "Original");
        rig.Set(SettingIntent.Names.AutoDictate, "true");
        rig.Set(SettingIntent.Names.PreloadSpeech, "false");
        rig.Set(SettingIntent.Names.RememberChoices, "false");

        Assert.Equal("Original", rig.F.Last.Composer.DefaultOutput);
        Assert.True(rig.F.Last.Composer.AutoDictate);
        Assert.False(rig.F.Last.Composer.PreloadSpeech);
        Assert.False(rig.F.Last.Composer.RememberChoices);

        rig.Do(new ForgetAppIntent("slack") { V = 1 });
        Assert.Empty(rig.Vm.Apps);

        rig.F.PreferenceStore.Data.Apps.Add(new AppChoice("code", "Code", ComposerOutput.Clean, TargetLanguage.English, DateTimeOffset.UnixEpoch));
        rig.Do(new ClearAppsIntent { V = 1 });
        Assert.Empty(rig.Vm.Apps);
    }

    [Fact]
    public void RecentDrafts_AreClearedByTheViewModel()
    {
        var rig = new Rig();
        rig.F.Recent.Close("yesterday", written: true);

        rig.Do(new ClearRecentIntent { V = 1 });

        Assert.Empty(rig.F.Recent.Items);
    }

    [Fact]
    public async Task ModelsFromEarlierVersions_AreRemovedOnRequest()
    {
        var rig = new Rig();
        rig.F.Models.ListInstalled().Returns([new InstalledModel("old", "old", "/m/old", 10, ModelType.Translation, DateTime.UnixEpoch)]);
        await rig.Vm.InitializeAsync();

        rig.Do(new RemoveLegacyModelsIntent { V = 1 });
        await Wait.Until(() => !rig.Vm.HasLegacyModels);

        await rig.F.Models.Received(1).CleanObsoleteModelsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>());
    }

    private static (UpdateChecks Checks, IUpdateService Service) Updates(string? ready = null, string? page = null)
    {
        var service = Substitute.For<IUpdateService>();
        service.CanUpdate.Returns(true);
        service.ReadyVersion.Returns(ready);
        service.DownloadPage.Returns(page);
        service.FindAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<string?>(null));
        return (new UpdateChecks(service, TimeSpan.FromHours(4), NullLogger.Instance), service);
    }

    [Fact]
    public async Task CheckingForUpdates_AsksTheFeed()
    {
        var (checks, service) = Updates();
        var rig = new Rig(updates: checks);

        rig.Do(new CheckUpdatesIntent { V = 1 });

        await Wait.Until(() => checks.State.Phase == UpdatePhase.UpToDate);
        await service.Received(1).FindAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public void RestartToUpdate_InstallsTheDownloadedVersion_AndNothingWithout()
    {
        var (idle, notReady) = Updates();
        new Rig(updates: idle).Do(new RestartToUpdateIntent { V = 1 });
        notReady.DidNotReceive().ApplyAndRestart();

        var (checks, service) = Updates(ready: "2.2.0");
        new Rig(updates: checks).Do(new RestartToUpdateIntent { V = 1 });

        service.Received(1).ApplyAndRestart();
    }

    [Fact]
    public void TheDownloadPage_IsOpenedWhenThereIsOne()
    {
        var rig = new Rig(updates: Updates(page: "https://github.com/o/r/releases/latest").Checks);
        rig.Do(new OpenDownloadsIntent { V = 1 });
        Assert.Equal(["https://github.com/o/r/releases/latest"], rig.Opened);

        var without = new Rig(updates: Updates().Checks);
        without.Do(new OpenDownloadsIntent { V = 1 });
        Assert.Empty(without.Opened);
    }

    [Fact]
    public void WithUpdatesOff_TheUpdateIntentsDoNothing()
    {
        var rig = new Rig();

        rig.Do(new CheckUpdatesIntent { V = 1 });
        rig.Do(new RestartToUpdateIntent { V = 1 });
        rig.Do(new OpenDownloadsIntent { V = 1 });

        Assert.Empty(rig.Opened);
    }

    [Fact]
    public void TheSection_IsOwnedHere_AndChangesWithTheNavigation()
    {
        var rig = new Rig(section: "voice");
        Assert.Equal("voice", rig.Sut.Section);

        rig.Do(new OpenSettingsIntent("privacy") { V = 1 });

        Assert.Equal("privacy", rig.Sut.Section);
    }

    [Fact]
    public void EveryIntent_TellsThePageToLookAgain()
    {
        var rig = new Rig();
        var changes = 0;
        rig.Sut.Changed += () => changes++;

        rig.Do(new OpenSettingsIntent("models") { V = 1 });
        rig.Do(new RemoveTermIntent(3) { V = 1 });

        Assert.Equal(2, changes);
    }

    [Fact]
    public void AnIntentOutsideTheSettingsSet_IsABug()
    {
        var rig = new Rig();

        Assert.Throws<ArgumentOutOfRangeException>(() => rig.Do(new CommitIntent(true) { V = 1 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => rig.Do(new SettingIntent("deleteEverything", "") { V = 1 }));
    }
}
