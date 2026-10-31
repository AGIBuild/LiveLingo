using LiveLingo.Desktop.Services.Configuration;

namespace LiveLingo.Desktop.Tests.Services.Configuration;

public class SettingsModelTests
{
    [Fact]
    public void Defaults_MatchTheComposerSchema()
    {
        var s = new SettingsModel();

        Assert.Equal(3, SettingsModel.CurrentSchemaVersion);
        Assert.Equal(SettingsModel.CurrentSchemaVersion, s.SchemaVersion);
        Assert.Equal("Ctrl+Alt+T", s.Hotkeys.Summon);
        Assert.Equal("en-US", s.Appearance.Language);
        Assert.True(s.Appearance.Animations);
        Assert.Equal(UpdateSettings.DefaultUpdateUrl, s.Update.UpdateUrl);
        Assert.Equal(4, s.Update.CheckIntervalHours);
        Assert.Null(s.Advanced.ModelStoragePath);
        Assert.Equal("Information", s.Advanced.LogLevel);
    }

    [Fact]
    public void DeepClone_IsIndependent()
    {
        var original = new SettingsModel();
        var clone = original.DeepClone();

        clone.Hotkeys.Summon = "Alt+Space";
        clone.Advanced.HuggingFaceMirror = "https://hf-mirror.com";

        Assert.Equal("Ctrl+Alt+T", original.Hotkeys.Summon);
        Assert.Null(original.Advanced.HuggingFaceMirror);
    }
}
