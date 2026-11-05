using LiveLingo.Desktop.Services.Update;
using LiveLingo.Desktop.Settings;
using Microsoft.Extensions.Logging;

namespace LiveLingo.Desktop.Web;

/// <summary>
/// Maps the settings page's intents onto <see cref="SettingsViewModel"/>. Like <see cref="ComposerIntents"/> it decides
/// nothing: what a valid hotkey is, when a cloud step may be turned on and what a consent means stay in the view model.
/// </summary>
internal sealed class SettingsIntents : IIntentHandler
{
    private readonly SettingsViewModel _vm;
    private readonly UpdateChecks? _updates;
    private readonly Action<string> _openUrl;
    private readonly ILogger _logger;

    public SettingsIntents(SettingsViewModel vm, string section, UpdateChecks? updates, Action<string> openUrl, ILogger logger)
    {
        _vm = vm;
        Section = section;
        _updates = updates;
        _openUrl = openUrl;
        _logger = logger;
    }

    /// <summary>The group the page shows. Owned here, so the window can also ask for one (the composer's "settings" item).</summary>
    public string Section { get; set; }

    /// <summary>The intent may have changed what the page shows; not every change is a property of the view model.</summary>
    public event Action? Changed;

    public void Handle(WebIntent intent)
    {
        switch (intent)
        {
            case OpenSettingsIntent open: Section = open.Section; break;
            case SettingIntent setting: Apply(setting); break;
            case AddTermIntent add:
                _vm.NewTermSource = add.Source;
                _vm.NewTermTarget = add.Target;
                _vm.AddTermCommand.Execute(null);
                break;
            case RemoveTermIntent remove:
                if (remove.Index < _vm.Terms.Count)
                    _vm.RemoveTermCommand.Execute(_vm.Terms[remove.Index]);
                break;
            case CloudToggleIntent toggle: _vm.SetCloud(toggle.Step, toggle.On); break;
            case ConsentIntent consent: _vm.AnswerConsent(consent.Allow); break;
            case OpenEndpointFormIntent open: _vm.OpenEndpointForm(open.Id); break;
            case CloseEndpointFormIntent: _vm.CloseEndpointForm(); break;
            case SaveEndpointIntent save: Run(() => _vm.SaveEndpointAsync(save.Label, save.BaseUrl, save.Model, save.ApiKey)); break;
            case EnableEndpointIntent enable: _vm.SetEndpointEnabled(enable.Id, enable.On); break;
            case DeleteEndpointIntent delete: _vm.DeleteEndpoint(delete.Id); break;
            case EndpointAnswerIntent answer: Run(() => _vm.AnswerChangeAsync(answer.Allow)); break;
            case ClearRecentIntent: _vm.ClearRecentCommand.Execute(null); break;
            case ForgetAppIntent forget: _vm.ForgetApp(forget.AppId); break;
            case ClearAppsIntent: _vm.ClearApps(); break;
            case RemoveLegacyModelsIntent: Run(() => _vm.RemoveLegacyModelsCommand.ExecuteAsync(null)); break;
            case CheckUpdatesIntent:
                if (_updates is not null)
                    Run(() => _updates.CheckNowAsync());
                break;
            case RestartToUpdateIntent: _updates?.ApplyAndRestart(); break;
            case OpenDownloadsIntent:
                if (_updates?.DownloadPage is { } page)
                    _openUrl(page);
                break;
            default: throw new ArgumentOutOfRangeException(nameof(intent), intent.GetType().Name, "Not a settings intent.");
        }

        Changed?.Invoke();
    }

    private void Apply(SettingIntent setting)
    {
        var value = setting.Value;
        switch (setting.Key)
        {
            case SettingIntent.Names.Hotkey: _vm.SummonHotkey = value; break;
            case SettingIntent.Names.CommitKey: _vm.CommitKeyIndex = IndexOf(SettingsViewModel.CommitKeys, value); break;
            case SettingIntent.Names.Language: _vm.LanguageIndex = IndexOf(SettingsViewModel.Languages, value); break;
            case SettingIntent.Names.Theme: _vm.ThemeIndex = IndexOf(SettingsViewModel.Themes, value); break;
            case SettingIntent.Names.Direction: _vm.TargetIndex = IndexOf(SettingsViewModel.Targets, value); break;
            case SettingIntent.Names.Tier: _vm.TierIndex = IndexOf(SettingsViewModel.Tiers, value); break;
            case SettingIntent.Names.DefaultOutput: _vm.DefaultOutputIndex = IndexOf(SettingsViewModel.DefaultOutputs, value); break;
            case SettingIntent.Names.AutoDictate: _vm.AutoDictate = value == "true"; break;
            case SettingIntent.Names.PreloadSpeech: _vm.PreloadSpeech = value == "true"; break;
            case SettingIntent.Names.RememberChoices: _vm.RememberChoices = value == "true"; break;
            case SettingIntent.Names.PolishPrompt: _vm.PolishPrompt = value; break;
            case SettingIntent.Names.ReduceMotion: _vm.ReduceMotion = value == "true"; break;
            case SettingIntent.Names.Cleanup: _vm.CleanupEnabled = value == "true"; break;
            case SettingIntent.Names.Microphone:
                // A device that is not in the list (unplugged since the snapshot) changes nothing.
                var index = _vm.MicrophoneNames.ToList().IndexOf(value);
                if (index >= 0)
                    _vm.MicrophoneIndex = index;
                break;
            default: throw new ArgumentOutOfRangeException(nameof(setting), setting.Key, "Not a setting.");
        }
    }

    // The intent was validated against these lists when it was parsed.
    private static int IndexOf(IReadOnlyList<string> choices, string value) => choices.ToList().IndexOf(value);

    private void Run(Func<Task> action) => Observed.Run(action, _logger, "A settings action");
}
