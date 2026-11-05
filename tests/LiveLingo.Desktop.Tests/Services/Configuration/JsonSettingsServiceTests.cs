using LiveLingo.Desktop.Services.Configuration;
using LiveLingo.Core.Cleanup;

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

        Assert.Equal("Ctrl+Alt+T", svc.Current.Hotkeys.Summon);
    }

    [Fact]
    public async Task SaveAndLoad_Roundtrip()
    {
        var svc = new JsonSettingsService(_settingsPath);
        await svc.LoadAsync(TestContext.Current.CancellationToken);
        var replacement = svc.CloneCurrent();
        replacement.Hotkeys.Summon = "Alt+X";
        replacement.Composer.PolishPrompt = "Keep my tone without inventing facts.";
        svc.Replace(replacement);

        var svc2 = new JsonSettingsService(_settingsPath);
        await svc2.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal("Alt+X", svc2.Current.Hotkeys.Summon);
        Assert.Equal(replacement.Composer.PolishPrompt, svc2.Current.Composer.PolishPrompt);
    }

    [Fact]
    public async Task CloudEndpoints_AreSavedAndLoadedWithTheEnabledOne()
    {
        var svc = new JsonSettingsService(_settingsPath);
        await svc.LoadAsync(TestContext.Current.CancellationToken);
        svc.Replace(Tests.Settings.CloudTestData.WithEndpoints("other", translate: true));

        var svc2 = new JsonSettingsService(_settingsPath);
        await svc2.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["main", "other"], svc2.Current.Cloud.Endpoints.Select(endpoint => endpoint.Id));
        Assert.Equal("other", svc2.Current.Cloud.Active?.Id);
        Assert.True(svc2.Current.Cloud.Translate);

        using var document = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(_settingsPath, TestContext.Current.CancellationToken));
        Assert.False(document.RootElement.GetProperty("cloud").TryGetProperty("baseUrl", out _));
    }

    [Fact]
    public async Task TheSingleEndpointOfEarlierVersions_BecomesTheFirstEndpoint_KeepsItsKey_AndStaysEnabledIfAStepWasOn()
    {
        await File.WriteAllTextAsync(_settingsPath, """
            { "schemaVersion": 3, "hotkeys": { "summon": "Alt+X" },
              "cloud": { "baseUrl": " https://api.example.com/v1 ", "model": "gpt-deploy", "translate": true, "clean": false } }
            """, TestContext.Current.CancellationToken);
        var svc = new JsonSettingsService(_settingsPath);

        await svc.LoadAsync(TestContext.Current.CancellationToken);

        var endpoint = Assert.Single(svc.Current.Cloud.Endpoints);
        Assert.Equal("https://api.example.com/v1", endpoint.BaseUrl);
        Assert.Equal("gpt-deploy", endpoint.Model);
        Assert.Equal(CloudSettings.LegacyApiKeySecretId, endpoint.SecretId);
        Assert.Equal(endpoint.Id, svc.Current.Cloud.ActiveEndpointId);
        Assert.True(svc.Current.Cloud.Translate);
        Assert.Equal("Alt+X", svc.Current.Hotkeys.Summon);

        // Migrated once: the file no longer holds the old fields, and a second load keeps the same endpoint.
        var json = await File.ReadAllTextAsync(_settingsPath, TestContext.Current.CancellationToken);
        Assert.Contains($"\"id\": \"{endpoint.Id}\"", json);
        var again = new JsonSettingsService(_settingsPath);
        await again.LoadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(endpoint.Id, Assert.Single(again.Current.Cloud.Endpoints).Id);
    }

    [Fact]
    public async Task TheSingleEndpointOfEarlierVersions_IsNotEnabled_WhenNoCloudStepWasOn()
    {
        await File.WriteAllTextAsync(_settingsPath, """
            { "schemaVersion": 3, "cloud": { "baseUrl": "https://api.example.com/v1", "model": "m", "translate": false, "clean": false } }
            """, TestContext.Current.CancellationToken);
        var svc = new JsonSettingsService(_settingsPath);

        await svc.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Single(svc.Current.Cloud.Endpoints);
        Assert.Equal("", svc.Current.Cloud.ActiveEndpointId);
        Assert.Null(svc.Current.Cloud.Active);
    }

    [Fact]
    public async Task AnEmptyCloudOfEarlierVersions_MigratesToNoEndpoints()
    {
        await File.WriteAllTextAsync(_settingsPath, """
            { "schemaVersion": 3, "cloud": { "baseUrl": "", "model": "", "translate": true, "clean": false } }
            """, TestContext.Current.CancellationToken);
        var svc = new JsonSettingsService(_settingsPath);

        await svc.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Empty(svc.Current.Cloud.Endpoints);
        Assert.False(svc.Current.Cloud.Translate);
    }

    [Fact]
    public async Task CorruptSettings_FallBackToBuiltInPolishRequirements()
    {
        await File.WriteAllTextAsync(_settingsPath, "{broken", TestContext.Current.CancellationToken);
        var svc = new JsonSettingsService(_settingsPath);
        await svc.LoadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(PolishStyle.Default, PolishStyle.Resolve(svc.Current.Composer.PolishPrompt));
    }

    [Fact]
    public async Task LoadAsync_ReplacesThePlaceholderUpdateUrlOfEarlierVersions_AndStoresTheFix()
    {
        var saved = new SettingsModel();
        saved.Update.UpdateUrl = UpdateSettings.PlaceholderUpdateUrl;
        saved.Hotkeys.Summon = "Alt+X";
        await File.WriteAllTextAsync(_settingsPath, System.Text.Json.JsonSerializer.Serialize(saved, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)), TestContext.Current.CancellationToken);

        var svc = new JsonSettingsService(_settingsPath);
        await svc.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(UpdateSettings.DefaultUpdateUrl, svc.Current.Update.UpdateUrl);
        Assert.Equal("Alt+X", svc.Current.Hotkeys.Summon);
        Assert.Contains(UpdateSettings.DefaultUpdateUrl, await File.ReadAllTextAsync(_settingsPath, TestContext.Current.CancellationToken));
        Assert.DoesNotContain(UpdateSettings.PlaceholderUpdateUrl, await File.ReadAllTextAsync(_settingsPath, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("")]
    [InlineData("https://example.com/releases")]
    public async Task LoadAsync_KeepsAnUpdateUrlTheUserSet_AndAnEmptyOneTurnsUpdatesOff(string url)
    {
        var saved = new SettingsModel();
        saved.Update.UpdateUrl = url;
        await File.WriteAllTextAsync(_settingsPath, System.Text.Json.JsonSerializer.Serialize(saved, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)), TestContext.Current.CancellationToken);

        var svc = new JsonSettingsService(_settingsPath);
        await svc.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(url, svc.Current.Update.UpdateUrl);
    }

    [Fact]
    public async Task LoadAsync_HandlesCorruptJson()
    {
        await File.WriteAllTextAsync(_settingsPath, "{{{{corrupt json!!", TestContext.Current.CancellationToken);

        var svc = new JsonSettingsService(_settingsPath);
        await svc.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal("Ctrl+Alt+T", svc.Current.Hotkeys.Summon);
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
        replacement.Appearance.Language = "zh-CN";
        svc.Replace(replacement);

        var svc2 = new JsonSettingsService(_settingsPath);
        await svc2.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal("zh-CN", svc2.Current.Appearance.Language);
    }

    [Fact]
    public async Task SaveAsync_WritesValidJson()
    {
        var svc = new JsonSettingsService(_settingsPath);
        await svc.SaveAsync(TestContext.Current.CancellationToken);

        var json = await File.ReadAllTextAsync(_settingsPath, TestContext.Current.CancellationToken);
        Assert.Contains("summon", json);
    }

    [Fact]
    public async Task LoadAsync_DeserializesCustomValues()
    {
        var json = """
        {
            "schemaVersion": 3,
            "hotkeys": { "summon": "Alt+X" },
            "appearance": { "language": "zh-CN" }
        }
        """;
        await File.WriteAllTextAsync(_settingsPath, json, TestContext.Current.CancellationToken);

        var svc = new JsonSettingsService(_settingsPath);
        await svc.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal("Alt+X", svc.Current.Hotkeys.Summon);
        Assert.Equal("zh-CN", svc.Current.Appearance.Language);
    }

    [Fact]
    public async Task LoadAsync_ResetsDefaults_WhenSchemaVersionMissing()
    {
        var json = """
        {
            "hotkeys": { "summon": "Alt+X" },
            "appearance": { "language": "zh-CN" }
        }
        """;
        await File.WriteAllTextAsync(_settingsPath, json, TestContext.Current.CancellationToken);

        var svc = new JsonSettingsService(_settingsPath);
        await svc.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(SettingsModel.CurrentSchemaVersion, svc.Current.SchemaVersion);
        Assert.Equal("Ctrl+Alt+T", svc.Current.Hotkeys.Summon);
        Assert.Equal("en-US", svc.Current.Appearance.Language);

        var persisted = await File.ReadAllTextAsync(_settingsPath, TestContext.Current.CancellationToken);
        Assert.Contains("\"schemaVersion\": 3", persisted);
    }

    [Fact]
    public void Replace_ReplacesCurrent()
    {
        var svc = new JsonSettingsService(_settingsPath);
        var replacement = new SettingsModel();
        replacement.Hotkeys.Summon = "Alt+Z";

        svc.Replace(replacement);

        Assert.Equal("Alt+Z", svc.Current.Hotkeys.Summon);
    }

    [Fact]
    public async Task CloneCurrent_Isolation_Works()
    {
        var svc = new JsonSettingsService(_settingsPath);
        await svc.LoadAsync(TestContext.Current.CancellationToken);

        var clone = svc.CloneCurrent();
        clone.Appearance.Animations = false;

        Assert.True(svc.Current.Appearance.Animations);
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
                model.Advanced.LogLevel = "Debug";
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

        Assert.Equal("Ctrl+Alt+T", svc.Current.Hotkeys.Summon);
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
