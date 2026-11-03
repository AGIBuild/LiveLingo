using LiveLingo.Desktop.Services.Update;
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
    private readonly UpdateChecks? _updates;
    private readonly SynchronizationContext _ui;
    private readonly ILogger _logger;

    public SettingsWebSession(
        SettingsViewModel vm,
        SettingsTexts texts,
        string section,
        AppInfo app,
        UpdateChecks? updates,
        Action<string> openUrl,
        IWebHost host,
        SynchronizationContext ui,
        TimeProvider time,
        ILogger logger)
    {
        _vm = vm;
        _updates = updates;
        _ui = ui;
        _logger = logger;
        _intents = new SettingsIntents(vm, section, updates, openUrl, logger);
        _bridge = new WebBridge(
            host, "settings",
            rev => SettingsViewState.From(vm, texts, _intents.Section, app, UpdateView(texts, app)) with { Rev = rev },
            () => new StringsData(texts.Locale, texts.WebStrings()),
            _intents, ui, time, logger);

        vm.PropertyChanged += OnViewModelChanged;
        _intents.Changed += _bridge.Invalidate;
        _bridge.Started += ReadSystem;
        if (updates is not null)
            updates.StateChanged += OnUpdateChanged;
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
        if (_updates is not null)
            _updates.StateChanged -= OnUpdateChanged;
        _bridge.Dispose();
    }

    private UpdateViewState UpdateView(SettingsTexts texts, AppInfo app) =>
        UpdateViewState.From(_updates?.State ?? UpdateState.Off, _updates?.DownloadPage is not null, texts, app.Version);

    private void OnViewModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => _bridge.Invalidate();

    // The check runs on a background thread; the bridge is the UI thread's.
    private void OnUpdateChanged() => _ui.Post(_ => _bridge.Invalidate(), null);

    /// <summary>What the system provides (the stored key, the devices, the models on disk) is read once the page is up.</summary>
    private void ReadSystem() => Observed.Run(_vm.InitializeAsync, _logger, "Reading the system state for the settings");
}
