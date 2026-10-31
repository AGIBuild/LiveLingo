using LiveLingo.Desktop.Platform;
using LiveLingo.Desktop.Services.Configuration;
using LiveLingo.Desktop.Settings;
using NSubstitute;

namespace LiveLingo.Desktop.Tests.Settings;

public class SettingsViewModelTests
{
    private static (SettingsViewModel Vm, ISecretStore Secrets, List<SettingsModel> Saved) Create(
        SettingsModel? current = null, string? storedKey = null)
    {
        var model = current ?? new SettingsModel();
        var saved = new List<SettingsModel>();
        var service = Substitute.For<ISettingsService>();
        service.CloneCurrent().Returns(_ => model.DeepClone());
        service.When(s => s.Replace(Arg.Any<SettingsModel>())).Do(call => saved.Add(call.Arg<SettingsModel>()));
        var secrets = Substitute.For<ISecretStore>();
        secrets.GetSecretAsync(CloudSettings.ApiKeySecretId, Arg.Any<CancellationToken>()).Returns(storedKey);
        return (new SettingsViewModel(service, secrets), secrets, saved);
    }

    [Fact]
    public void StartsFromTheCurrentSettings()
    {
        var model = new SettingsModel();
        model.Hotkeys.Summon = "Alt+Space";
        model.Appearance.Language = "zh-CN";
        model.Composer.TranslatorTier = "Quality";
        model.Composer.Glossary = "Redis";
        model.Cloud.BaseUrl = "https://api.example.com/v1";
        model.Cloud.Model = "m";
        model.Cloud.Translate = true;

        var (vm, _, _) = Create(model);

        Assert.Equal("Alt+Space", vm.SummonHotkey);
        Assert.Equal("zh-CN", vm.Language);
        Assert.True(vm.Quality);
        Assert.Equal("Redis", vm.Glossary);
        Assert.Equal("https://api.example.com/v1", vm.CloudBaseUrl);
        Assert.True(vm.CloudTranslate);
        Assert.False(vm.CloudClean);
    }

    [Fact]
    public async Task Save_PersistsEveryEditedValue_AndClosesTheWindow()
    {
        var (vm, _, saved) = Create();
        var closed = 0;
        vm.CloseRequested += () => closed++;

        vm.SummonHotkey = " Ctrl+Shift+L ";
        vm.Language = "zh-CN";
        vm.Quality = true;
        vm.Glossary = "Avalonia\n缓存穿透=cache penetration";
        await vm.SaveCommand.ExecuteAsync(null);

        var model = Assert.Single(saved);
        Assert.Equal("Ctrl+Shift+L", model.Hotkeys.Summon);
        Assert.Equal("zh-CN", model.Appearance.Language);
        Assert.Equal("Quality", model.Composer.TranslatorTier);
        Assert.Equal("Avalonia\n缓存穿透=cache penetration", model.Composer.Glossary);
        Assert.Equal(1, closed);
    }

    [Fact]
    public async Task Save_WithoutQuality_SelectsTheFastTier()
    {
        var model = new SettingsModel();
        model.Composer.TranslatorTier = "Quality";
        var (vm, _, saved) = Create(model);

        vm.Quality = false;
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal("Fast", saved[0].Composer.TranslatorTier);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Ctrl+Alt+")]
    [InlineData("   ")]
    public async Task Save_WithAnInvalidHotkey_ChangesNothing_AndFlagsIt(string hotkey)
    {
        var (vm, _, saved) = Create();
        var closed = 0;
        vm.CloseRequested += () => closed++;

        vm.SummonHotkey = hotkey;
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Empty(saved);
        Assert.True(vm.HotkeyInvalid);
        Assert.Equal(0, closed);

        vm.SummonHotkey = "Ctrl+Alt+T";
        Assert.False(vm.HotkeyInvalid);
    }

    [Fact]
    public void Cancel_ClosesWithoutSaving()
    {
        var (vm, _, saved) = Create();
        var closed = 0;
        vm.CloseRequested += () => closed++;

        vm.Glossary = "changed";
        vm.CancelCommand.Execute(null);

        Assert.Empty(saved);
        Assert.Equal(1, closed);
    }

    [Fact]
    public void EditsAreMadeOnACopy_SoTheLiveSettingsAreUntouchedUntilSave()
    {
        var model = new SettingsModel();
        var (vm, _, _) = Create(model);

        vm.Glossary = "edited";

        Assert.Equal("", model.Composer.Glossary);
    }

    // ---- cloud ----------------------------------------------------------------

    [Fact]
    public async Task Cloud_IsOffByDefault_AndSavingWithoutItNeedsNoEndpointOrKey()
    {
        var (vm, secrets, saved) = Create();

        await vm.SaveCommand.ExecuteAsync(null);

        var cloud = Assert.Single(saved).Cloud;
        Assert.False(cloud.Translate);
        Assert.False(cloud.Clean);
        await secrets.DidNotReceiveWithAnyArgs().SetSecretAsync(default!, default!, default);
    }

    [Fact]
    public async Task EnablingCloud_StoresTheKeyInTheSecretStore_NotInTheSettings()
    {
        var (vm, secrets, saved) = Create();

        vm.CloudBaseUrl = " https://example.openai.azure.com/openai/v1 ";
        vm.CloudModel = " gpt-4.1-mini ";
        vm.CloudApiKey = " sk-secret ";
        vm.CloudTranslate = true;
        await vm.SaveCommand.ExecuteAsync(null);

        await secrets.Received(1).SetSecretAsync(CloudSettings.ApiKeySecretId, "sk-secret", Arg.Any<CancellationToken>());
        var cloud = Assert.Single(saved).Cloud;
        Assert.Equal("https://example.openai.azure.com/openai/v1", cloud.BaseUrl);
        Assert.Equal("gpt-4.1-mini", cloud.Model);
        Assert.True(cloud.Translate);
        Assert.DoesNotContain("sk-secret", System.Text.Json.JsonSerializer.Serialize(saved[0]));
        Assert.Equal("", vm.CloudApiKey);
        Assert.True(vm.HasStoredKey);
    }

    [Theory]
    [InlineData("", "m", "key")]
    [InlineData("http://api.example.com/v1", "m", "key")]
    [InlineData("https://api.example.com/v1", "", "key")]
    [InlineData("https://api.example.com/v1", "m", "")]
    public async Task EnablingCloud_WithAnIncompleteConfiguration_SavesNothing_AndFlagsIt(string url, string model, string key)
    {
        var (vm, secrets, saved) = Create();

        vm.CloudBaseUrl = url;
        vm.CloudModel = model;
        vm.CloudApiKey = key;
        vm.CloudClean = true;
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Empty(saved);
        Assert.True(vm.CloudInvalid);
        await secrets.DidNotReceiveWithAnyArgs().SetSecretAsync(default!, default!, default);

        vm.CloudModel = "other";
        Assert.False(vm.CloudInvalid);
    }

    [Fact]
    public async Task AStoredKey_SatisfiesTheConfiguration_WithoutRetypingIt()
    {
        var model = new SettingsModel();
        model.Cloud.BaseUrl = "https://api.example.com/v1";
        model.Cloud.Model = "m";
        var (vm, secrets, saved) = Create(model, storedKey: "sk-stored");
        await vm.InitializeAsync();
        Assert.True(vm.HasStoredKey);

        vm.CloudTranslate = true;
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.True(Assert.Single(saved).Cloud.Translate);
        await secrets.DidNotReceiveWithAnyArgs().SetSecretAsync(default!, default!, default);
    }

    [Fact]
    public async Task Initialize_ReportsWhetherAKeyIsStored()
    {
        var (withKey, _, _) = Create(storedKey: "sk");
        var (withoutKey, _, _) = Create(storedKey: null);

        await withKey.InitializeAsync();
        await withoutKey.InitializeAsync();

        Assert.True(withKey.HasStoredKey);
        Assert.False(withoutKey.HasStoredKey);
    }

    [Fact]
    public async Task ForgettingTheKey_DeletesIt_AndSwitchesTheCloudOff()
    {
        var (vm, secrets, _) = Create(storedKey: "sk");
        await vm.InitializeAsync();
        vm.CloudTranslate = true;
        vm.CloudClean = true;

        await vm.ForgetCloudKeyCommand.ExecuteAsync(null);

        await secrets.Received(1).DeleteSecretAsync(CloudSettings.ApiKeySecretId, Arg.Any<CancellationToken>());
        Assert.False(vm.HasStoredKey);
        Assert.False(vm.CloudTranslate);
        Assert.False(vm.CloudClean);
    }
}
