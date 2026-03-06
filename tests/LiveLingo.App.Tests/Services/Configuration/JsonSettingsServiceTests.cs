using LiveLingo.App.Services.Configuration;

namespace LiveLingo.App.Tests.Services.Configuration;

public class JsonSettingsServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _settingsPath;

    public JsonSettingsServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"LiveLingoSettingsTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _settingsPath = Path.Combine(_tempDir, "settings.json");
    }

    [Fact]
    public async Task LoadAsync_ReturnsDefaults_WhenNoFile()
    {
        var svc = new JsonSettingsService(_settingsPath);
        await svc.LoadAsync();

        Assert.Equal("Ctrl+Alt+T", svc.Current.Hotkeys.OverlayToggle);
    }

    [Fact]
    public async Task SaveAndLoad_Roundtrip()
    {
        var svc = new JsonSettingsService(_settingsPath);
        svc.Update(s => s);
        svc.Update(s => s);
        await Task.Delay(100);

        var svc2 = new JsonSettingsService(_settingsPath);
        await svc2.LoadAsync();

        Assert.Equal(svc.Current.Hotkeys.OverlayToggle, svc2.Current.Hotkeys.OverlayToggle);
    }

    [Fact]
    public async Task LoadAsync_HandlesCorruptJson()
    {
        await File.WriteAllTextAsync(_settingsPath, "{{{{corrupt json!!");

        var svc = new JsonSettingsService(_settingsPath);
        await svc.LoadAsync();

        Assert.Equal("Ctrl+Alt+T", svc.Current.Hotkeys.OverlayToggle);
    }

    [Fact]
    public void Update_RaisesSettingsChanged()
    {
        var svc = new JsonSettingsService(_settingsPath);
        UserSettings? received = null;
        svc.SettingsChanged += s => received = s;

        svc.Update(s => s);

        Assert.NotNull(received);
    }

    [Fact]
    public async Task SaveAsync_CreatesDirectory()
    {
        var nestedPath = Path.Combine(_tempDir, "sub", "deep", "settings.json");
        var svc = new JsonSettingsService(nestedPath);
        await svc.SaveAsync();

        Assert.True(File.Exists(nestedPath));
    }

    [Fact]
    public void SettingsFileExists_False_Initially()
    {
        var svc = new JsonSettingsService(_settingsPath);
        Assert.False(svc.SettingsFileExists());
    }

    [Fact]
    public async Task SettingsFileExists_True_AfterSave()
    {
        var svc = new JsonSettingsService(_settingsPath);
        await svc.SaveAsync();
        Assert.True(svc.SettingsFileExists());
    }

    [Fact]
    public async Task Current_PersistsAfterUpdate()
    {
        var svc = new JsonSettingsService(_settingsPath);
        svc.Update(s => s);
        await Task.Delay(100);

        var svc2 = new JsonSettingsService(_settingsPath);
        await svc2.LoadAsync();

        Assert.Equal("zh", svc2.Current.Translation.DefaultSourceLanguage);
    }

    [Fact]
    public async Task SaveAsync_WritesValidJson()
    {
        var svc = new JsonSettingsService(_settingsPath);
        await svc.SaveAsync();

        var json = await File.ReadAllTextAsync(_settingsPath);
        Assert.Contains("overlayToggle", json);
    }

    [Fact]
    public async Task LoadAsync_DeserializesCustomValues()
    {
        var json = """
        {
            "hotkeys": { "overlayToggle": "Alt+X" },
            "translation": { "defaultSourceLanguage": "ja" }
        }
        """;
        await File.WriteAllTextAsync(_settingsPath, json);

        var svc = new JsonSettingsService(_settingsPath);
        await svc.LoadAsync();

        Assert.Equal("Alt+X", svc.Current.Hotkeys.OverlayToggle);
        Assert.Equal("ja", svc.Current.Translation.DefaultSourceLanguage);
    }

    [Fact]
    public void Update_ReplacesCurrent()
    {
        var svc = new JsonSettingsService(_settingsPath);

        svc.Update(s => s with
        {
            Hotkeys = s.Hotkeys with { OverlayToggle = "Alt+Z" }
        });

        Assert.Equal("Alt+Z", svc.Current.Hotkeys.OverlayToggle);
    }

    [Fact]
    public async Task SaveAsync_RootPath_NoParentDir()
    {
        var rootFile = Path.Combine(_tempDir, "settings.json");
        var svc = new JsonSettingsService(rootFile);
        await svc.SaveAsync();

        Assert.True(File.Exists(rootFile));
    }

    [Fact]
    public async Task LoadAsync_EmptyFile_ReturnsDefaults()
    {
        await File.WriteAllTextAsync(_settingsPath, "");

        var svc = new JsonSettingsService(_settingsPath);
        await svc.LoadAsync();

        Assert.Equal("Ctrl+Alt+T", svc.Current.Hotkeys.OverlayToggle);
    }

    [Fact]
    public void Constructor_NoPath_UsesDefault()
    {
        var svc = new JsonSettingsService();
        Assert.NotNull(svc.Current);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }
}
