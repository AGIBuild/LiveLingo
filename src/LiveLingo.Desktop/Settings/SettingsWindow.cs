using Avalonia.Controls;
using LiveLingo.Desktop.Web;
using Microsoft.Extensions.Logging;

namespace LiveLingo.Desktop.Settings;

/// <summary>The window around the settings page: a title bar and a web view. Everything inside is the page's.</summary>
internal sealed class SettingsWindow : Window
{
    private readonly SettingsWebSession _session;

    public SettingsWindow(SettingsViewModel vm, SettingsTexts texts, string section, ILogger logger)
    {
        var web = new WebSurface(WebPage.SettingsEntry, logger);
        Title = texts.Title;
        Width = 920;
        Height = 660;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Content = web.View;

        _session = new SettingsWebSession(
            vm, texts, section, typeof(SettingsWindow).Assembly.GetName().Version?.ToString(3) ?? "", web,
            SynchronizationContext.Current ?? throw new InvalidOperationException("The settings are shown from the UI thread."),
            TimeProvider.System, logger);
        Closed += (_, _) => _session.Dispose();
    }

    public void ShowSection(string section) => _session.ShowSection(section);
}
