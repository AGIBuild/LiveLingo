using LiveLingo.Desktop.Services.Localization;

namespace LiveLingo.Desktop.Tests.Services.Localization;

public class LocalizationServiceTests
{
    private static LocalizationService CreateService(
        Dictionary<string, string>? enUs = null,
        Dictionary<string, string>? zhCn = null)
    {
        var resources = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        if (enUs is not null) resources["en-US"] = enUs;
        if (zhCn is not null) resources["zh-CN"] = zhCn;
        return new LocalizationService(resources);
    }

    [Fact]
    public void T_ReturnsValueFromCurrentCulture()
    {
        var sut = CreateService(
            enUs: new() { ["greet"] = "Hello" },
            zhCn: new() { ["greet"] = "你好" });
        sut.SetCulture("zh-CN");

        Assert.Equal("你好", sut.T("greet"));
    }

    [Fact]
    public void T_FallsBackToEnUS_WhenKeyMissingInActiveCulture()
    {
        var sut = CreateService(
            enUs: new() { ["app.title"] = "LiveLingo" },
            zhCn: new());
        sut.SetCulture("zh-CN");

        Assert.Equal("LiveLingo", sut.T("app.title"));
    }

    [Fact]
    public void T_ReturnsKeyLiteral_WhenKeyMissingEverywhere()
    {
        var sut = CreateService(enUs: new());

        Assert.Equal("unknown.key", sut.T("unknown.key"));
    }

    [Fact]
    public void T_ReturnsKeyLiteral_WhenNoCulturesLoaded()
    {
        var sut = CreateService();

        Assert.Equal("missing", sut.T("missing"));
    }

    [Fact]
    public void T_WithArgs_FormatsString()
    {
        var sut = CreateService(
            enUs: new() { ["version"] = "Version: {0}" });

        Assert.Equal("Version: 1.2.3", sut.T("version", "1.2.3"));
    }

    [Fact]
    public void T_WithArgs_ReturnsTemplateOnFormatError()
    {
        var sut = CreateService(
            enUs: new() { ["bad"] = "Value: {0} {1}" });

        var result = sut.T("bad", "only-one");
        Assert.Equal("Value: {0} {1}", result);
    }

    [Fact]
    public void T_WithArgs_MultipleParams()
    {
        var sut = CreateService(
            enUs: new() { ["multi"] = "{0} and {1}" });

        Assert.Equal("A and B", sut.T("multi", "A", "B"));
    }

    [Fact]
    public void SetCulture_ChangesCurrentCulture()
    {
        var sut = CreateService();
        Assert.Equal("en-US", sut.CurrentCulture.Name);

        sut.SetCulture("zh-CN");
        Assert.Equal("zh-CN", sut.CurrentCulture.Name);
    }

    [Fact]
    public void T_UsesEnUS_AsDefaultCulture()
    {
        var sut = CreateService(
            enUs: new() { ["key"] = "english" });

        Assert.Equal("english", sut.T("key"));
    }

    [Fact]
    public void T_CaseInsensitiveCultureLookup()
    {
        var resources = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["en-US"] = new() { ["a"] = "A" }
        };
        var sut = new LocalizationService(resources);
        sut.SetCulture("en-us");

        Assert.Equal("A", sut.T("a"));
    }

    [Fact]
    public void DefaultConstructor_LoadsEmbeddedResources()
    {
        var sut = new LocalizationService();
        var result = sut.T("tray.quit");
        Assert.Equal("Quit", result);
    }

    [Fact]
    public void DefaultConstructor_LoadsChinese()
    {
        var sut = new LocalizationService();
        sut.SetCulture("zh-CN");
        var result = sut.T("tray.quit");
        Assert.Equal("退出", result);
    }

    [Fact]
    public void TryT_ReturnsTrueAndValue_WhenKeyExists()
    {
        var sut = CreateService(enUs: new() { ["a"] = "A" });

        Assert.True(sut.TryT("a", out var value));
        Assert.Equal("A", value);
    }

    [Fact]
    public void TryT_ReturnsFalseAndEmpty_WhenKeyMissingEverywhere()
    {
        var sut = CreateService(enUs: new());

        Assert.False(sut.TryT("missing", out var value));
        Assert.Equal(string.Empty, value);
    }

    [Fact]
    public void TryT_FallsBackToEnUS_WhenMissingInActiveCulture()
    {
        var sut = CreateService(enUs: new() { ["k"] = "english" }, zhCn: new());
        sut.SetCulture("zh-CN");

        Assert.True(sut.TryT("k", out var value));
        Assert.Equal("english", value);
    }

    [Fact]
    public void ShellKeys_AreCoveredByBothLanguages()
    {
        // Guards against shell code adding a T("...") call without entries in en-US.json and zh-CN.json.
        var sut = new LocalizationService();
        string[] criticalKeys =
        [
            "tray.openComposer",
            "tray.checkUpdates",
            "tray.quit",
            "app.name",
            "app.permission.title",
            "app.permission.body",
            "app.permission.openAccessibility",
            "app.permission.openInputMonitoring",
            "app.hotkey.registrationFailed.title",
            "app.hotkey.registrationFailed.body",
            "dialog.update.title",
            "dialog.update.notConfigured",
            "dialog.update.latest",
            "dialog.update.available.title",
            "dialog.update.available.body",
            "dialog.update.available.download",
            "dialog.update.available.later",
            "dialog.update.failed",
            "dialog.ok",
            "composer.placeholder",
            "composer.output.translate",
            "composer.output.original",
            "composer.language.English",
            "composer.language.Chinese",
            "composer.swap",
            "composer.target.some",
            "composer.target.none",
            "composer.status.empty",
            "composer.status.working",
            "composer.status.ready",
            "composer.status.failed",
            "composer.status.queued",
            "composer.copy",
            "composer.write",
            "composer.retry",
            "composer.result.empty",
            "composer.banner.modelMissing",
            "composer.banner.download",
            "composer.banner.downloading",
            "composer.banner.downloadFailed",
            "composer.banner.downloadRetry",
            "composer.banner.targetLost",
            "composer.banner.writeFailed",
            "composer.output.clean",
            "composer.status.listening",
            "composer.status.starting",
            "composer.mic",
            "composer.banner.micDenied",
            "composer.banner.micDeniedAction",
            "composer.banner.noMic",
            "composer.banner.noSound",
            "composer.status.keptOriginal"
        ];
        foreach (var key in criticalKeys)
        {
            Assert.True(sut.TryT(key, out var value), $"en-US missing key: {key}");
            Assert.NotEqual(string.Empty, value);
        }

        sut.SetCulture("zh-CN");
        foreach (var key in criticalKeys)
        {
            Assert.True(sut.TryT(key, out var value), $"zh-CN missing key: {key}");
            Assert.NotEqual(string.Empty, value);
        }
    }
}
