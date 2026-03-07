using LiveLingo.App.Services.Localization;

namespace LiveLingo.App.Tests.Services.Localization;

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
        var result = sut.T("tray.settings");
        Assert.Equal("Settings", result);
    }

    [Fact]
    public void DefaultConstructor_LoadsChinese()
    {
        var sut = new LocalizationService();
        sut.SetCulture("zh-CN");
        var result = sut.T("tray.settings");
        Assert.Equal("设置", result);
    }
}
