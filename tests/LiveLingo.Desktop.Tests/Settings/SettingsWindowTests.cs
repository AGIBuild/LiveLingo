using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using LiveLingo.Core.Models;
using LiveLingo.Desktop.Platform;
using LiveLingo.Desktop.Services.Configuration;
using LiveLingo.Desktop.Tests.Composer;
using LiveLingo.Desktop.Services.Localization;
using LiveLingo.Desktop.Settings;
using NSubstitute;

namespace LiveLingo.Desktop.Tests.Settings;

public class SettingsWindowTests
{
    [AvaloniaFact]
    public async Task ShowsTheCurrentGlossary_AndSavingWritesTheEditsAndClosesTheWindow()
    {
        var model = new SettingsModel();
        model.Composer.Glossary = "Avalonia";
        var saved = new List<SettingsModel>();
        var service = Substitute.For<ISettingsService>();
        service.CloneCurrent().Returns(_ => model.DeepClone());
        service.When(s => s.Replace(Arg.Any<SettingsModel>())).Do(call => saved.Add(call.Arg<SettingsModel>()));
        var loc = Substitute.For<ILocalizationService>();
        loc.T(Arg.Any<string>()).Returns(call => call.Arg<string>());
        var vm = new SettingsViewModel(service, Substitute.For<ISecretStore>(), Substitute.For<IModelManager>());
        var window = new SettingsWindow(vm, loc);
        window.Show();

        var glossary = window.FindControl<TextBox>("GlossaryBox")!;
        Assert.Equal("Avalonia", glossary.Text);
        glossary.Text = "Avalonia\nRedis";
        window.FindControl<Button>("SaveButton")!.Command!.Execute(null);
        await Wait.Until(() => !window.IsVisible);

        Assert.Equal("Avalonia\nRedis", Assert.Single(saved).Composer.Glossary);
    }

    [AvaloniaFact]
    public async Task CloudKeyBoxIsMasked_AndTheForgetButtonShowsOnlyWhenAKeyIsStored()
    {
        var service = Substitute.For<ISettingsService>();
        service.CloneCurrent().Returns(_ => new SettingsModel());
        var secrets = Substitute.For<ISecretStore>();
        secrets.GetSecretAsync(CloudSettings.ApiKeySecretId, Arg.Any<CancellationToken>()).Returns("sk");
        var loc = Substitute.For<ILocalizationService>();
        loc.T(Arg.Any<string>()).Returns(call => call.Arg<string>());
        var window = new SettingsWindow(new SettingsViewModel(service, secrets, Substitute.For<IModelManager>()), loc);
        window.Show();

        Assert.Equal('•', window.FindControl<TextBox>("CloudApiKeyBox")!.PasswordChar);
        Assert.Equal("settings.cloud.apiKey", Avalonia.Automation.AutomationProperties.GetName(window.FindControl<TextBox>("CloudApiKeyBox")!));
        await Wait.Until(() => window.FindControl<Button>("CloudForgetButton")!.IsEffectivelyVisible);
    }
}
