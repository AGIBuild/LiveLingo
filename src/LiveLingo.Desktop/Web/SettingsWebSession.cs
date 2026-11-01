using LiveLingo.Desktop.Settings;
using Microsoft.Extensions.Logging;

namespace LiveLingo.Desktop.Web;

/// <summary>
/// One open settings window: connects <see cref="SettingsViewModel"/> to a page. The view model changes, the page gets a
/// snapshot; the page acts, the view model is told.
/// </summary>
internal sealed class SettingsWebSession : IDisposable
{
    private readonly WebBridge _bridge;
    private readonly SettingsIntents _intents;
    private readonly SettingsViewModel _vm;
    private readonly ILogger _logger;

    public SettingsWebSession(
        SettingsViewModel vm,
        SettingsTexts texts,
        string section,
        AppInfo app,
        IWebHost host,
        SynchronizationContext ui,
        TimeProvider time,
        ILogger logger)
    {
        _vm = vm;
        _logger = logger;
        _intents = new SettingsIntents(vm, section, logger);
        _bridge = new WebBridge(
            host, "settings",
            rev => SettingsViewState.From(vm, texts, _intents.Section, app) with { Rev = rev },
            () => new StringsData(texts.Locale, texts.WebStrings()),
            _intents, ui, time, logger);

        vm.PropertyChanged += OnViewModelChanged;
        _intents.Changed += _bridge.Invalidate;
        _bridge.Started += ReadSystem;
    }

    /// <summary>Shows another group of settings (the composer asked for one while the window is open).</summary>
    public void ShowSection(string section)
    {
        _intents.Section = section;
        _bridge.Invalidate();
    }

    public void Dispose()
    {
        _vm.PropertyChanged -= OnViewModelChanged;
        _bridge.Dispose();
    }

    private void OnViewModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => _bridge.Invalidate();

    /// <summary>What the system provides (the stored key, the devices, the models on disk) is read once the page is up.</summary>
    private void ReadSystem() => Observed.Run(_vm.InitializeAsync, _logger, "Reading the system state for the settings");
}
