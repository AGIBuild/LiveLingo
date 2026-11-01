using LiveLingo.Core.Translation;
using LiveLingo.Desktop.Composer;
using LiveLingo.Desktop.Services.Preferences;

namespace LiveLingo.Desktop.Tests.Services.Preferences;

public sealed class JsonPreferencesStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"livelingo-prefs-{Guid.NewGuid():N}");
    private string File_ => Path.Combine(_directory, "nested", "preferences.json");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void NothingStored_LoadsAsEmpty()
    {
        var data = new JsonPreferencesStore(File_).Load();

        Assert.Empty(data.Apps);
        Assert.Null(data.ExportFormat);
    }

    [Fact]
    public void WhatWasSaved_IsLoadedBack_AsNamesNotNumbers()
    {
        var store = new JsonPreferencesStore(File_);
        var at = new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

        store.Save(new PreferencesData
        {
            Apps = [new AppChoice("com.slack", "Slack", ComposerOutput.Translate, TargetLanguage.Chinese, at)],
            ExportFormat = ExportFormat.Markdown
        });
        var loaded = store.Load();

        Assert.Equal(new AppChoice("com.slack", "Slack", ComposerOutput.Translate, TargetLanguage.Chinese, at), Assert.Single(loaded.Apps));
        Assert.Equal(ExportFormat.Markdown, loaded.ExportFormat);
        var text = System.IO.File.ReadAllText(File_);
        Assert.Contains("\"translate\"", text);
        Assert.Contains("\"markdown\"", text);
        Assert.False(System.IO.File.Exists(File_ + ".tmp"));
    }

    [Fact]
    public void AFileThatCannotBeRead_LoadsAsEmpty_InsteadOfStoppingTheApp()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(File_)!);
        System.IO.File.WriteAllText(File_, "{ this is not json");

        Assert.Empty(new JsonPreferencesStore(File_).Load().Apps);
    }

    [Fact]
    public void AFileOfAnotherVersion_LoadsAsEmpty()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(File_)!);
        System.IO.File.WriteAllText(File_, """{ "schemaVersion": 99, "apps": [{ "appId": "x", "name": "X", "output": "clean", "target": "English", "usedAt": "2026-11-01T00:00:00+00:00" }] }""");

        Assert.Empty(new JsonPreferencesStore(File_).Load().Apps);
    }

    [Fact]
    public void WhatIsStored_HasNoTitlesTextsOrKeys()
    {
        var store = new JsonPreferencesStore(File_);
        store.Save(new PreferencesData { Apps = [new AppChoice("a", "A", ComposerOutput.Clean, TargetLanguage.English, DateTimeOffset.UnixEpoch)] });

        var names = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(File_)).RootElement
            .GetProperty("apps")[0].EnumerateObject().Select(p => p.Name).Order().ToArray();

        Assert.Equal(["appId", "name", "output", "target", "usedAt"], names);
    }
}
