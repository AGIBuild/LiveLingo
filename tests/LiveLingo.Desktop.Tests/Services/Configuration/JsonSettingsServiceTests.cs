using LiveLingo.Desktop.Services.Configuration;

namespace LiveLingo.Desktop.Tests.Services.Configuration;

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
        await svc.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal("Ctrl+Alt+T", svc.Current.Hotkeys.OverlayToggle);
    }

    [Fact]
    public async Task SaveAndLoad_Roundtrip()
    {
        var svc = new JsonSettingsService(_settingsPath);
        await svc.LoadAsync(TestContext.Current.CancellationToken);
        var replacement = svc.CloneCurrent();
        replacement.Hotkeys.OverlayToggle = "Alt+X";
        svc.Replace(replacement);

        var svc2 = new JsonSettingsService(_settingsPath);
        await svc2.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal("Alt+X", svc2.Current.Hotkeys.OverlayToggle);
    }

    [Fact]
    public async Task LoadAsync_HandlesCorruptJson()
    {
        await File.WriteAllTextAsync(_settingsPath, "{{{{corrupt json!!", TestContext.Current.CancellationToken);

        var svc = new JsonSettingsService(_settingsPath);
        await svc.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal("Ctrl+Alt+T", svc.Current.Hotkeys.OverlayToggle);
    }

    [Fact]
    public void Replace_RaisesSettingsChanged()
    {
        var initial = new SettingsModel();
        var svc = new JsonSettingsService(_settingsPath);
        var raised = false;
        svc.SettingsChanged += () => raised = true;

        svc.Replace(initial);

        Assert.True(raised);
    }

    [Fact]
    public async Task SaveAsync_CreatesDirectory()
    {
        var nestedPath = Path.Combine(_tempDir, "sub", "deep", "settings.json");
        var svc = new JsonSettingsService(nestedPath);
        await svc.SaveAsync(TestContext.Current.CancellationToken);

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
        await svc.SaveAsync(TestContext.Current.CancellationToken);
        Assert.True(svc.SettingsFileExists());
    }

    [Fact]
    public async Task Current_PersistsAfterReplace()
    {
        var svc = new JsonSettingsService(_settingsPath);
        await svc.LoadAsync(TestContext.Current.CancellationToken);
        var replacement = svc.CloneCurrent();
        replacement.Translation.DefaultSourceLanguage = "ja";
        svc.Replace(replacement);

        var svc2 = new JsonSettingsService(_settingsPath);
        await svc2.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal("ja", svc2.Current.Translation.DefaultSourceLanguage);
    }

    [Fact]
    public async Task SaveAsync_WritesValidJson()
    {
        var svc = new JsonSettingsService(_settingsPath);
        await svc.SaveAsync(TestContext.Current.CancellationToken);

        var json = await File.ReadAllTextAsync(_settingsPath, TestContext.Current.CancellationToken);
        Assert.Contains("overlayToggle", json);
    }

    [Fact]
    public async Task LoadAsync_DeserializesCustomValues()
    {
        var json = """
        {
            "schemaVersion": 2,
            "hotkeys": { "overlayToggle": "Alt+X" },
            "translation": { "defaultSourceLanguage": "ja" }
        }
        """;
        await File.WriteAllTextAsync(_settingsPath, json, TestContext.Current.CancellationToken);

        var svc = new JsonSettingsService(_settingsPath);
        await svc.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal("Alt+X", svc.Current.Hotkeys.OverlayToggle);
        Assert.Equal("ja", svc.Current.Translation.DefaultSourceLanguage);
    }

    [Fact]
    public async Task LoadAsync_ResetsDefaults_WhenSchemaVersionMissing()
    {
        var json = """
        {
            "hotkeys": { "overlayToggle": "Alt+X" },
            "translation": { "defaultSourceLanguage": "ja" }
        }
        """;
        await File.WriteAllTextAsync(_settingsPath, json, TestContext.Current.CancellationToken);

        var svc = new JsonSettingsService(_settingsPath);
        await svc.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(SettingsModel.CurrentSchemaVersion, svc.Current.SchemaVersion);
        Assert.Equal("Ctrl+Alt+T", svc.Current.Hotkeys.OverlayToggle);
        Assert.Equal("zh", svc.Current.Translation.DefaultSourceLanguage);

        var persisted = await File.ReadAllTextAsync(_settingsPath, TestContext.Current.CancellationToken);
        Assert.Contains("\"schemaVersion\": 2", persisted);
    }

    [Fact]
    public void Replace_ReplacesCurrent()
    {
        var svc = new JsonSettingsService(_settingsPath);
        var replacement = new SettingsModel();
        replacement.Hotkeys.OverlayToggle = "Alt+Z";

        svc.Replace(replacement);

        Assert.Equal("Alt+Z", svc.Current.Hotkeys.OverlayToggle);
    }

    [Fact]
    public async Task CloneCurrent_Isolation_Works()
    {
        var svc = new JsonSettingsService(_settingsPath);
        await svc.LoadAsync(TestContext.Current.CancellationToken);

        var clone = svc.CloneCurrent();
        clone.UI.OverlayOpacity = 0.5;

        Assert.NotEqual(clone.UI.OverlayOpacity, svc.Current.UI.OverlayOpacity);
    }

    [Fact]
    public void Replace_RejectsNull()
    {
        var svc = new JsonSettingsService(_settingsPath);
        Assert.Throws<ArgumentNullException>(() => svc.Replace(null!));
    }

    [Fact]
    public async Task Replace_IsThreadSafe_UnderConcurrency()
    {
        var svc = new JsonSettingsService(_settingsPath);
        await svc.LoadAsync(TestContext.Current.CancellationToken);

        var tasks = Enumerable.Range(0, 16).Select(async i =>
        {
            await Task.Yield();
            var next = svc.CloneCurrent();
            next.Advanced.InferenceThreads = i;
            svc.Replace(next);
        });
        await Task.WhenAll(tasks);

        var svc2 = new JsonSettingsService(_settingsPath);
        await svc2.LoadAsync(TestContext.Current.CancellationToken);
        Assert.InRange(svc2.Current.Advanced.InferenceThreads, 0, 15);
    }

    [Fact]
    public void Replace_DoesNotDeadlock_OnBlockingSyncContext()
    {
        var svc = new JsonSettingsService(_settingsPath);
        var finished = false;
        var ex = default(Exception);
        var thread = new Thread(() =>
        {
            try
            {
                SynchronizationContext.SetSynchronizationContext(new BlockingSynchronizationContext());
                var model = new SettingsModel();
                model.Translation.ActiveTranslationModelId = "opus-mt-zh-en";
                svc.Replace(model);
                finished = true;
            }
            catch (Exception e)
            {
                ex = e;
            }
        })
        {
            IsBackground = true
        };

        thread.Start();
        var joined = thread.Join(TimeSpan.FromSeconds(2));

        Assert.True(joined, "Replace should not block UI-like synchronization context.");
        Assert.Null(ex);
        Assert.True(finished);
    }

    private sealed class BlockingSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state)
        {
            // Intentionally drop queued continuations to simulate UI deadlock conditions.
        }
    }

    [Fact]
    public async Task SaveAsync_RootPath_NoParentDir()
    {
        var rootFile = Path.Combine(_tempDir, "settings.json");
        var svc = new JsonSettingsService(rootFile);
        await svc.SaveAsync(TestContext.Current.CancellationToken);

        Assert.True(File.Exists(rootFile));
    }

    [Fact]
    public async Task LoadAsync_EmptyFile_ReturnsDefaults()
    {
        await File.WriteAllTextAsync(_settingsPath, "", TestContext.Current.CancellationToken);

        var svc = new JsonSettingsService(_settingsPath);
        await svc.LoadAsync(TestContext.Current.CancellationToken);

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
