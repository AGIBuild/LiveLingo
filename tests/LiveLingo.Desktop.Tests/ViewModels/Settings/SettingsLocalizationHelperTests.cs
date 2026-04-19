using LiveLingo.Desktop.Services.Configuration;
using LiveLingo.Desktop.Services.Localization;
using LiveLingo.Desktop.ViewModels.Settings;
using NSubstitute;

namespace LiveLingo.Desktop.Tests.ViewModels.Settings;

public sealed class SettingsLocalizationHelperTests
{
    [Fact]
    public void Translate_ReturnsFallback_WhenLocalizerIsNull()
    {
        var helper = new SettingsLocalizationHelper(loc: null);

        var result = helper.Translate("any.key", "fallback text");

        Assert.Equal("fallback text", result);
    }

    [Fact]
    public void Translate_ReturnsLocalizedValue_WhenKeyResolves()
    {
        var loc = Substitute.For<ILocalizationService>();
        loc.TryT("settings.foo", out _).Returns(call =>
        {
            call[1] = "bar";
            return true;
        });
        var helper = new SettingsLocalizationHelper(loc);

        var result = helper.Translate("settings.foo", "FOO");

        Assert.Equal("bar", result);
    }

    [Fact]
    public void Translate_FormatsArgs_UsingLocalizedTemplate()
    {
        var loc = Substitute.For<ILocalizationService>();
        loc.TryT("settings.advanced.migrationFailed", out _).Returns(call =>
        {
            call[1] = "迁移失败：{0}";
            return true;
        });
        var helper = new SettingsLocalizationHelper(loc);

        var result = helper.Translate("settings.advanced.migrationFailed", "Migration failed: {0}", "disk full");

        Assert.Equal("迁移失败：disk full", result);
    }

    [Fact]
    public void Translate_FallsBackOnFormatException_InTemplate()
    {
        var loc = Substitute.For<ILocalizationService>();
        loc.TryT("k", out _).Returns(call =>
        {
            call[1] = "broken {0";
            return true;
        });
        var helper = new SettingsLocalizationHelper(loc);

        var result = helper.Translate("k", "fallback {0}", "x");

        Assert.Equal("broken {0", result);
    }

    [Fact]
    public void BuildSelectableOptions_AllListsPopulated()
    {
        var helper = new SettingsLocalizationHelper(loc: null);

        var options = helper.BuildSelectableOptions();

        Assert.Equal(2, options.InjectionModes.Count);
        Assert.Equal(4, options.PostProcessModes.Count);
        Assert.Equal(4, options.RoutingModes.Count);
        Assert.Equal(3, options.SttRoutingModes.Count);
        Assert.Equal(4, options.CloudProviderPresets.Count);
        Assert.Equal(5, options.LogLevels.Count);
    }

    [Fact]
    public void ResolveCloudPresetDisplayName_KnownPreset_UsesLocalizedKey()
    {
        var loc = Substitute.For<ILocalizationService>();
        loc.TryT("settings.ai.cloudPreset.openai", out _).Returns(call =>
        {
            call[1] = "OpenAI 中文";
            return true;
        });
        var helper = new SettingsLocalizationHelper(loc);

        var result = helper.ResolveCloudPresetDisplayName(CloudProviderPresetCatalog.OpenAI);

        Assert.Equal("OpenAI 中文", result);
    }

    [Fact]
    public void ResolveCloudPresetDisplayName_UnknownPreset_FallsBackToDisplayName()
    {
        var helper = new SettingsLocalizationHelper(loc: null);
        var customPreset = new CloudProviderPreset("XYZ", "Custom Display", "https://x", "t", "p");

        var result = helper.ResolveCloudPresetDisplayName(customPreset);

        Assert.Equal("Custom Display", result);
    }
}
