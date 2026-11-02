using System.Text.Json;
using LiveLingo.Desktop.Services.Configuration;

namespace LiveLingo.Desktop.Tests.Services.Configuration;

public class SettingsMigrationTests : IDisposable
{
    private const string LegacyFile = """
        {
          "schemaVersion": 2,
          "hotkeys": { "overlayToggle": "Ctrl+Shift+K" },
          "translation": { "defaultTargetLanguage": "ja", "cloudProvider": { "endpoint": "https://old.example.com" } },
          "processing": { "enabled": true },
          "ui": { "overlayOpacity": 0.8, "defaultInjectionMode": "PasteAndSend", "language": "zh-CN" },
          "update": { "updateUrl": "https://example.com/releases", "checkIntervalHours": 12 },
          "advanced": { "modelStoragePath": "/data/models", "inferenceThreads": 6, "logLevel": "Debug", "huggingFaceMirror": "https://mirror.example.com" }
        }
        """;

    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"LiveLingoMigration_{Guid.NewGuid():N}");

    public SettingsMigrationTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void WhatStillMeansTheSameThing_IsKept()
    {
        var model = SettingsMigration.FromLegacy(JsonDocument.Parse(LegacyFile).RootElement);

        Assert.Equal(SettingsModel.CurrentSchemaVersion, model.SchemaVersion);
        Assert.Equal("Ctrl+Shift+K", model.Hotkeys.Summon);
        Assert.Equal("zh-CN", model.Appearance.Language);
        Assert.Equal("https://example.com/releases", model.Update.UpdateUrl);
        Assert.Equal(12, model.Update.CheckIntervalHours);
        Assert.Equal("/data/models", model.Advanced.ModelStoragePath);
        Assert.Equal(6, model.Advanced.InferenceThreads);
        Assert.Equal("Debug", model.Advanced.LogLevel);
        Assert.Equal("https://mirror.example.com", model.Advanced.HuggingFaceMirror);
    }

    [Fact]
    public void WhatBelongedToTheOldDesign_IsDropped_AndCloudStaysOff()
    {
        var model = SettingsMigration.FromLegacy(JsonDocument.Parse(LegacyFile).RootElement);

        Assert.False(model.Cloud.Translate);
        Assert.False(model.Cloud.Clean);
        Assert.Equal("", model.Cloud.BaseUrl);
        Assert.Equal("Fast", model.Composer.TranslatorTier);
        Assert.Equal("", model.Composer.Glossary);
        Assert.DoesNotContain("old.example.com", JsonSerializer.Serialize(model));
    }

    [Fact]
    public void AnInvalidOrMissingValue_FallsBackToTheDefault_ForThatValueOnly()
    {
        var root = JsonDocument.Parse("""
            { "schemaVersion": 2, "hotkeys": { "overlayToggle": "Ctrl+Alt+" },
              "ui": { "language": 5 }, "update": { "checkIntervalHours": 0 },
              "advanced": { "inferenceThreads": -2, "logLevel": "  ", "modelStoragePath": "/d" } }
            """).RootElement;

        var model = SettingsMigration.FromLegacy(root);

        var defaults = SettingsModel.CreateDefault();
        Assert.Equal(defaults.Hotkeys.Summon, model.Hotkeys.Summon);
        Assert.Equal(defaults.Appearance.Language, model.Appearance.Language);
        Assert.Equal(defaults.Update.CheckIntervalHours, model.Update.CheckIntervalHours);
        Assert.Equal(defaults.Advanced.InferenceThreads, model.Advanced.InferenceThreads);
        Assert.Equal(defaults.Advanced.LogLevel, model.Advanced.LogLevel);
        Assert.Equal("/d", model.Advanced.ModelStoragePath);
    }

    [Fact]
    public void PropertyNamesAreMatchedIgnoringCase_AndNonObjectSectionsAreIgnored()
    {
        var model = SettingsMigration.FromLegacy(JsonDocument.Parse("""
            { "schemaVersion": 2, "Hotkeys": { "OverlayToggle": "Alt+Z" }, "ui": "oops", "advanced": [] }
            """).RootElement);

        Assert.Equal("Alt+Z", model.Hotkeys.Summon);
        Assert.Equal("en-US", model.Appearance.Language);
    }

    [Fact]
    public async Task LoadingASchema2File_MigratesIt_AndPersistsTheNewSchema()
    {
        var path = Path.Combine(_dir, "settings.json");
        await File.WriteAllTextAsync(path, LegacyFile, TestContext.Current.CancellationToken);

        var service = new JsonSettingsService(path);
        await service.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal("Ctrl+Shift+K", service.Current.Hotkeys.Summon);
        var persisted = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
        Assert.Contains("\"schemaVersion\": 3", persisted);
        Assert.DoesNotContain("overlayToggle", persisted);

        var reloaded = new JsonSettingsService(path);
        await reloaded.LoadAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Ctrl+Shift+K", reloaded.Current.Hotkeys.Summon);
        Assert.Equal("/data/models", reloaded.Current.Advanced.ModelStoragePath);
    }

    [Theory]
    [InlineData("""{ "schemaVersion": 1, "hotkeys": { "overlayToggle": "Alt+Z" } }""")]
    [InlineData("""{ "schemaVersion": 99, "hotkeys": { "summon": "Alt+Z" } }""")]
    [InlineData("""{ "schemaVersion": "2", "hotkeys": { "overlayToggle": "Alt+Z" } }""")]
    public async Task AnyOtherSchema_StartsFromDefaults(string json)
    {
        var path = Path.Combine(_dir, "settings.json");
        await File.WriteAllTextAsync(path, json, TestContext.Current.CancellationToken);

        var service = new JsonSettingsService(path);
        await service.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal("Ctrl+Alt+T", service.Current.Hotkeys.Summon);
    }
}
