using LiveLingo.App.Services.Configuration;

namespace LiveLingo.App.Tests.Services.Configuration;

public class UserSettingsTests
{
    [Fact]
    public void Defaults_AreReasonable()
    {
        var s = new UserSettings();
        Assert.Equal("Ctrl+Alt+T", s.Hotkeys.OverlayToggle);
        Assert.Equal("zh", s.Translation.DefaultSourceLanguage);
        Assert.Equal("en", s.Translation.DefaultTargetLanguage);
        Assert.Single(s.Translation.LanguagePairs);
        Assert.Equal("Off", s.Processing.DefaultMode);
        Assert.Equal(0.95, s.UI.OverlayOpacity);
        Assert.Equal("PasteAndSend", s.UI.DefaultInjectionMode);
        Assert.Null(s.UI.LastOverlayPosition);
        Assert.Equal("", s.Update.UpdateUrl);
        Assert.Equal(4, s.Update.CheckIntervalHours);
        Assert.Null(s.Advanced.ModelStoragePath);
        Assert.Equal(0, s.Advanced.InferenceThreads);
        Assert.Equal("Information", s.Advanced.LogLevel);
    }

    [Fact]
    public void LanguagePair_Equality()
    {
        var a = new LanguagePair("zh", "en");
        var b = new LanguagePair("zh", "en");
        Assert.Equal(a, b);
    }

    [Fact]
    public void OverlayPosition_Equality()
    {
        var a = new OverlayPosition(100, 200);
        var b = new OverlayPosition(100, 200);
        Assert.Equal(a, b);
    }

    [Fact]
    public void UserSettings_WithOverrides()
    {
        var s = new UserSettings
        {
            Hotkeys = new HotkeySettings { OverlayToggle = "Ctrl+Shift+L" },
            Translation = new TranslationSettings
            {
                DefaultSourceLanguage = "ja",
                DefaultTargetLanguage = "zh",
                LanguagePairs = [new("ja", "zh"), new("en", "zh")]
            },
            Processing = new ProcessingSettings { DefaultMode = "Summarize" },
            UI = new UISettings
            {
                OverlayOpacity = 0.8,
                DefaultInjectionMode = "PasteOnly",
                LastOverlayPosition = new OverlayPosition(500, 300)
            },
            Advanced = new AdvancedSettings
            {
                ModelStoragePath = "/custom",
                InferenceThreads = 4,
                LogLevel = "Debug"
            }
        };

        Assert.Equal("Ctrl+Shift+L", s.Hotkeys.OverlayToggle);
        Assert.Equal(2, s.Translation.LanguagePairs.Count);
        Assert.Equal("Summarize", s.Processing.DefaultMode);
        Assert.Equal(0.8, s.UI.OverlayOpacity);
        Assert.Equal(500, s.UI.LastOverlayPosition!.X);
        Assert.Equal("/custom", s.Advanced.ModelStoragePath);
    }
}
