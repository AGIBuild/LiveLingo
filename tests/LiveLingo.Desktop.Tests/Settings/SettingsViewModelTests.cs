using LiveLingo.Desktop.Services.Configuration;
using LiveLingo.Desktop.Settings;
using NSubstitute;

namespace LiveLingo.Desktop.Tests.Settings;

public class SettingsViewModelTests
{
    private static (SettingsViewModel Vm, ISettingsService Service, List<SettingsModel> Saved) Create(SettingsModel? current = null)
    {
        var model = current ?? new SettingsModel();
        var saved = new List<SettingsModel>();
        var service = Substitute.For<ISettingsService>();
        service.CloneCurrent().Returns(_ => model.DeepClone());
        service.When(s => s.Replace(Arg.Any<SettingsModel>())).Do(call => saved.Add(call.Arg<SettingsModel>()));
        return (new SettingsViewModel(service), service, saved);
    }

    [Fact]
    public void StartsFromTheCurrentSettings()
    {
        var model = new SettingsModel();
        model.Hotkeys.Summon = "Alt+Space";
        model.Appearance.Language = "zh-CN";
        model.Composer.TranslatorTier = "Quality";
        model.Composer.Glossary = "Redis";

        var (vm, _, _) = Create(model);

        Assert.Equal("Alt+Space", vm.SummonHotkey);
        Assert.Equal("zh-CN", vm.Language);
        Assert.True(vm.Quality);
        Assert.Equal("Redis", vm.Glossary);
    }

    [Fact]
    public void Save_PersistsEveryEditedValue_AndClosesTheWindow()
    {
        var (vm, _, saved) = Create();
        var closed = 0;
        vm.CloseRequested += () => closed++;

        vm.SummonHotkey = " Ctrl+Shift+L ";
        vm.Language = "zh-CN";
        vm.Quality = true;
        vm.Glossary = "Avalonia\n缓存穿透=cache penetration";
        vm.SaveCommand.Execute(null);

        var model = Assert.Single(saved);
        Assert.Equal("Ctrl+Shift+L", model.Hotkeys.Summon);
        Assert.Equal("zh-CN", model.Appearance.Language);
        Assert.Equal("Quality", model.Composer.TranslatorTier);
        Assert.Equal("Avalonia\n缓存穿透=cache penetration", model.Composer.Glossary);
        Assert.Equal(1, closed);
    }

    [Fact]
    public void Save_WithoutQuality_SelectsTheFastTier()
    {
        var model = new SettingsModel();
        model.Composer.TranslatorTier = "Quality";
        var (vm, _, saved) = Create(model);

        vm.Quality = false;
        vm.SaveCommand.Execute(null);

        Assert.Equal("Fast", saved[0].Composer.TranslatorTier);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Ctrl+Alt+")]
    [InlineData("   ")]
    public void Save_WithAnInvalidHotkey_ChangesNothing_AndFlagsIt(string hotkey)
    {
        var (vm, _, saved) = Create();
        var closed = 0;
        vm.CloseRequested += () => closed++;

        vm.SummonHotkey = hotkey;
        vm.SaveCommand.Execute(null);

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
}
