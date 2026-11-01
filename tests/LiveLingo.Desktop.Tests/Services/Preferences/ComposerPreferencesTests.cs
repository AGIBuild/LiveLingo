using LiveLingo.Core.Translation;
using LiveLingo.Desktop.Composer;
using LiveLingo.Desktop.Platform;
using LiveLingo.Desktop.Services.Configuration;
using LiveLingo.Desktop.Services.Preferences;
using LiveLingo.Desktop.Tests.Composer;

namespace LiveLingo.Desktop.Tests.Services.Preferences;

public sealed class MemoryPreferencesStore : IPreferencesStore
{
    public PreferencesData Data { get; set; } = new();
    public int Saves { get; private set; }

    public PreferencesData Load() => Data;

    public void Save(PreferencesData data)
    {
        Data = data;
        Saves++;
    }
}

public class ComposerPreferencesTests
{
    private static readonly ComposerOutput[] AllOutputs = [ComposerOutput.Clean, ComposerOutput.Translate, ComposerOutput.Original];
    private static readonly ComposerOutput[] NoCleanup = [ComposerOutput.Translate, ComposerOutput.Original];

    private static TargetWindowInfo Slack => new(1, 0, "Slack", "chat", 0, 0, 800, 600, "com.tinyspeck.slackmacgap");
    private static TargetWindowInfo Cursor => new(2, 0, "Cursor", "agent", 0, 0, 800, 600, "com.todesktop.cursor");

    private sealed class Rig
    {
        public MemoryPreferencesStore Store { get; } = new();
        public FakeClock Clock { get; } = new();
        public SettingsModel Settings { get; } = new();
        public ComposerPreferences Sut { get; }

        public Rig()
        {
            var service = NSubstitute.Substitute.For<ISettingsService>();
            NSubstitute.SubstituteExtensions.Returns(service.Current, _ => Settings);
            Sut = new ComposerPreferences(Store, service, Clock);
        }
    }

    // ---- starting point -------------------------------------------------------------

    [Fact]
    public void AnApplicationWithoutMemory_StartsWithTheSettingsDefaults()
    {
        var rig = new Rig();
        rig.Settings.Composer.DefaultOutput = "Translate";
        rig.Settings.Composer.TargetLanguage = "Chinese";

        var start = rig.Sut.Resolve(Slack, AllOutputs);

        Assert.Equal(ComposerOutput.Translate, start.Output);
        Assert.Equal(TargetLanguage.Chinese, start.Target);
        Assert.Equal((ComposerOutput.Translate, TargetLanguage.Chinese), (start.DefaultOutput, start.DefaultTarget));
        Assert.False(start.Remember!.Applied);
    }

    [Fact]
    public void ADefaultOutputThatIsNotOffered_FallsBackToTheOriginalWording()
    {
        var rig = new Rig();
        rig.Settings.Composer.DefaultOutput = "Clean";

        Assert.Equal(ComposerOutput.Original, rig.Sut.Resolve(Slack, NoCleanup).Output);
    }

    [Fact]
    public void WhatWasChosenInAnApplication_IsUsedTheNextTimeForThatApplicationOnly()
    {
        var rig = new Rig();
        rig.Sut.Remember(Slack, ComposerOutput.Translate, TargetLanguage.English);

        var slack = rig.Sut.Resolve(Slack, AllOutputs);
        var cursor = rig.Sut.Resolve(Cursor, AllOutputs);

        Assert.Equal((ComposerOutput.Translate, TargetLanguage.English), (slack.Output, slack.Target));
        Assert.True(slack.Remember!.Applied);
        Assert.Equal("Slack", slack.Remember.Name);
        Assert.Equal(ComposerOutput.Clean, cursor.Output);
        Assert.False(cursor.Remember!.Applied);
    }

    [Fact]
    public void AMemoryThatEqualsTheDefaults_IsNotAnnounced()
    {
        var rig = new Rig();
        rig.Sut.Remember(Slack, ComposerOutput.Clean, TargetLanguage.English);

        Assert.False(rig.Sut.Resolve(Slack, AllOutputs).Remember!.Applied);
    }

    [Fact]
    public void AMemoryOfAnOutputThatIsNoLongerOffered_IsIgnored()
    {
        var rig = new Rig();
        rig.Sut.Remember(Slack, ComposerOutput.Clean, TargetLanguage.Chinese);

        var start = rig.Sut.Resolve(Slack, NoCleanup);

        Assert.Equal(ComposerOutput.Original, start.Output);
        Assert.Equal(TargetLanguage.English, start.Target);
        Assert.False(start.Remember!.Applied);
    }

    [Fact]
    public void WithRememberingOff_NothingIsUsedAndNothingIsRecorded()
    {
        var rig = new Rig();
        rig.Sut.Remember(Slack, ComposerOutput.Translate, TargetLanguage.English);
        rig.Settings.Composer.RememberChoices = false;

        var start = rig.Sut.Resolve(Slack, AllOutputs);
        rig.Sut.Remember(Cursor, ComposerOutput.Translate, TargetLanguage.Chinese);

        Assert.Equal(ComposerOutput.Clean, start.Output);
        Assert.Null(start.Remember);
        Assert.Single(rig.Sut.Apps);
    }

    [Fact]
    public void ATargetTheSystemCannotIdentify_GetsNoMemory()
    {
        var rig = new Rig();
        var unknown = Slack with { AppId = "" };

        rig.Sut.Remember(unknown, ComposerOutput.Translate, TargetLanguage.English);
        var start = rig.Sut.Resolve(unknown, AllOutputs);

        Assert.Empty(rig.Sut.Apps);
        Assert.Null(start.Remember);
    }

    [Fact]
    public void WithoutAnInputBox_TheClipboardIsOneContextOfItsOwn()
    {
        var rig = new Rig();
        rig.Sut.Remember(null, ComposerOutput.Translate, TargetLanguage.Chinese);

        var clipboard = rig.Sut.Resolve(null, AllOutputs);
        var slack = rig.Sut.Resolve(Slack, AllOutputs);

        Assert.Equal((ComposerOutput.Translate, TargetLanguage.Chinese), (clipboard.Output, clipboard.Target));
        Assert.Equal("", clipboard.Remember!.Name);
        Assert.Equal(ComposerOutput.Clean, slack.Output);
    }

    // ---- recording --------------------------------------------------------------------

    [Fact]
    public void ChoosingTheOriginalWording_IsNotRemembered_ButTheDirectionIs()
    {
        var rig = new Rig();
        rig.Sut.Remember(Slack, ComposerOutput.Translate, TargetLanguage.English);

        rig.Sut.Remember(Slack, ComposerOutput.Original, TargetLanguage.Chinese);

        var start = rig.Sut.Resolve(Slack, AllOutputs);
        Assert.Equal((ComposerOutput.Translate, TargetLanguage.Chinese), (start.Output, start.Target));
    }

    [Fact]
    public void ChoosingTheOriginalWording_WhereNothingWasRemembered_KeepsTheDefaultOutput()
    {
        var rig = new Rig();

        rig.Sut.Remember(Slack, ComposerOutput.Original, TargetLanguage.English);

        Assert.Equal(ComposerOutput.Clean, rig.Sut.Resolve(Slack, AllOutputs).Output);
    }

    [Fact]
    public void ARepeatedChoice_UpdatesTheEntryInsteadOfAddingOne()
    {
        var rig = new Rig();
        rig.Sut.Remember(Slack, ComposerOutput.Translate, TargetLanguage.English);
        rig.Clock.Advance(TimeSpan.FromMinutes(5));

        rig.Sut.Remember(Slack, ComposerOutput.Clean, TargetLanguage.English);

        var entry = Assert.Single(rig.Sut.Apps);
        Assert.Equal(ComposerOutput.Clean, entry.Output);
        Assert.Equal(rig.Clock.GetUtcNow(), entry.UsedAt);
    }

    [Fact]
    public void EveryChoice_IsSavedAtOnce()
    {
        var rig = new Rig();

        rig.Sut.Remember(Slack, ComposerOutput.Translate, TargetLanguage.English);

        Assert.Equal(1, rig.Store.Saves);
        Assert.Equal("com.tinyspeck.slackmacgap", Assert.Single(rig.Store.Data.Apps).AppId);
    }

    [Fact]
    public void TheMostRecentlyChosenApplicationIsFirst_AndTheOldestIsDroppedBeyondTheCapacity()
    {
        var rig = new Rig();
        for (var i = 0; i < ComposerPreferences.Capacity + 3; i++)
        {
            rig.Sut.Remember(Slack with { AppId = $"app.{i}", ProcessName = $"App {i}" }, ComposerOutput.Translate, TargetLanguage.English);
            rig.Clock.Advance(TimeSpan.FromMinutes(1));
        }

        Assert.Equal(ComposerPreferences.Capacity, rig.Sut.Apps.Count);
        Assert.Equal($"app.{ComposerPreferences.Capacity + 2}", rig.Sut.Apps[0].AppId);
        Assert.DoesNotContain(rig.Sut.Apps, a => a.AppId == "app.0");
    }

    [Fact]
    public void ThereIsNoExpiry_AnOldChoiceIsStillUsed()
    {
        var rig = new Rig();
        rig.Sut.Remember(Slack, ComposerOutput.Translate, TargetLanguage.English);

        rig.Clock.Advance(TimeSpan.FromDays(400));

        Assert.Equal(ComposerOutput.Translate, rig.Sut.Resolve(Slack, AllOutputs).Output);
    }

    // ---- forgetting -------------------------------------------------------------------

    [Fact]
    public void ForgettingAnApplication_RemovesOnlyIt()
    {
        var rig = new Rig();
        rig.Sut.Remember(Slack, ComposerOutput.Translate, TargetLanguage.English);
        rig.Sut.Remember(Cursor, ComposerOutput.Translate, TargetLanguage.English);

        rig.Sut.Forget(Slack.AppId);

        Assert.Equal(Cursor.AppId, Assert.Single(rig.Sut.Apps).AppId);
        Assert.Equal(ComposerOutput.Clean, rig.Sut.Resolve(Slack, AllOutputs).Output);
    }

    [Fact]
    public void ClearingEverything_ForgetsAllApplications_AndTheExportFormat()
    {
        var rig = new Rig();
        rig.Sut.Remember(Slack, ComposerOutput.Translate, TargetLanguage.English);
        rig.Sut.RememberExportFormat(ExportFormat.Markdown);

        rig.Sut.Clear();

        Assert.Empty(rig.Sut.Apps);
        Assert.Null(rig.Sut.LastExportFormat);
        Assert.Empty(rig.Store.Data.Apps);
    }

    // ---- files ------------------------------------------------------------------------

    [Fact]
    public void TheLastExportFormat_IsRememberedForAllApplications()
    {
        var rig = new Rig();
        Assert.Null(rig.Sut.LastExportFormat);

        rig.Sut.RememberExportFormat(ExportFormat.Srt);

        Assert.Equal(ExportFormat.Srt, rig.Sut.LastExportFormat);
        Assert.Equal(ExportFormat.Srt, rig.Store.Data.ExportFormat);
    }

    // ---- loading ----------------------------------------------------------------------

    [Fact]
    public void WhatWasStored_IsLoadedAtStartup()
    {
        var store = new MemoryPreferencesStore
        {
            Data = new PreferencesData
            {
                Apps = [new AppChoice("a.b", "Ab", ComposerOutput.Translate, TargetLanguage.Chinese, DateTimeOffset.UnixEpoch)],
                ExportFormat = ExportFormat.Markdown
            }
        };
        var service = NSubstitute.Substitute.For<ISettingsService>();
        NSubstitute.SubstituteExtensions.Returns(service.Current, new SettingsModel());

        var sut = new ComposerPreferences(store, service);

        Assert.Equal("a.b", Assert.Single(sut.Apps).AppId);
        Assert.Equal(ExportFormat.Markdown, sut.LastExportFormat);
    }
}
