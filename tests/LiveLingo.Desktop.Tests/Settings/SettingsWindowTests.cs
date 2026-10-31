using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using LiveLingo.Desktop.Services.Configuration;
using LiveLingo.Desktop.Services.Localization;
using LiveLingo.Desktop.Settings;
using NSubstitute;

namespace LiveLingo.Desktop.Tests.Settings;

public class SettingsWindowTests
{
    [AvaloniaFact]
    public void ShowsTheCurrentGlossary_AndSavingWritesTheEditsAndClosesTheWindow()
    {
        var model = new SettingsModel();
        model.Composer.Glossary = "Avalonia";
        var saved = new List<SettingsModel>();
        var service = Substitute.For<ISettingsService>();
        service.CloneCurrent().Returns(_ => model.DeepClone());
        service.When(s => s.Replace(Arg.Any<SettingsModel>())).Do(call => saved.Add(call.Arg<SettingsModel>()));
        var loc = Substitute.For<ILocalizationService>();
        loc.T(Arg.Any<string>()).Returns(call => call.Arg<string>());
        var vm = new SettingsViewModel(service);
        var window = new SettingsWindow(vm, loc);
        window.Show();

        var glossary = window.FindControl<TextBox>("GlossaryBox")!;
        Assert.Equal("Avalonia", glossary.Text);
        glossary.Text = "Avalonia\nRedis";
        window.FindControl<Button>("SaveButton")!.Command!.Execute(null);

        Assert.Equal("Avalonia\nRedis", Assert.Single(saved).Composer.Glossary);
        Assert.False(window.IsVisible);
    }
}
