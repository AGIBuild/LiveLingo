using LiveLingo.Core;
using LiveLingo.Core.Speech;
using LiveLingo.Desktop.Services.Configuration;
using LiveLingo.Desktop.Services.Localization;

namespace LiveLingo.Desktop.ViewModels.Settings;

internal sealed class SettingsLocalizationHelper : ISettingsLocalizationHelper
{
    private readonly ILocalizationService? _loc;

    public SettingsLocalizationHelper(ILocalizationService? loc)
    {
        _loc = loc;
    }

    public string Translate(string key, string fallback) =>
        _loc is not null && _loc.TryT(key, out var value) ? value : fallback;

    public string Translate(string key, string fallback, params object[] args)
    {
        if (_loc is not null && _loc.TryT(key, out var template))
        {
            try { return string.Format(template, args); }
            catch (FormatException) { return template; }
        }
        return string.Format(fallback, args);
    }

    public LocalizedSettingsOptions BuildSelectableOptions()
    {
        var injectionModes = new SelectableOption[]
        {
            new("PasteAndSend", Translate("settings.injectMode.pasteAndSend", "Paste & Send")),
            new("PasteOnly", Translate("settings.injectMode.pasteOnly", "Paste Only")),
        };

        var postProcessModes = new SelectableOption[]
        {
            new("Off", Translate("settings.postMode.off", "Off")),
            new("Summarize", Translate("settings.postMode.summarize", "Summarize")),
            new("Optimize", Translate("settings.postMode.optimize", "Optimize")),
            new("Colloquialize", Translate("settings.postMode.colloquialize", "Colloquialize")),
        };

        var routingModes = new SelectableOption[]
        {
            new(nameof(TranslationRoutingMode.LocalOnly), Translate("settings.routing.localOnly", "Local Only")),
            new(nameof(TranslationRoutingMode.PreferLocal), Translate("settings.routing.preferLocal", "Prefer Local")),
            new(nameof(TranslationRoutingMode.PreferCloud), Translate("settings.routing.preferCloud", "Prefer Cloud")),
            new(nameof(TranslationRoutingMode.CloudOnly), Translate("settings.routing.cloudOnly", "Cloud Only")),
        };

        var sttRoutingModes = new SelectableOption[]
        {
            new(nameof(SttRoutingMode.AccuracyFirst),
                Translate("settings.speech.routing.accuracyFirst", "Accuracy First")),
            new(nameof(SttRoutingMode.StreamingFirst),
                Translate("settings.speech.routing.streamingFirst", "Streaming First")),
            new(nameof(SttRoutingMode.MultilingualFirst),
                Translate("settings.speech.routing.multilingualFirst", "Multilingual First")),
        };

        var cloudPresets = CloudProviderPresetCatalog.All
            .Select(preset => new SelectableOption(preset.Id, ResolveCloudPresetDisplayName(preset)))
            .ToArray();

        var logLevels = new SelectableOption[]
        {
            new("Verbose", Translate("settings.logLevel.verbose", "Verbose")),
            new("Debug", Translate("settings.logLevel.debug", "Debug")),
            new("Information", Translate("settings.logLevel.information", "Information")),
            new("Warning", Translate("settings.logLevel.warning", "Warning")),
            new("Error", Translate("settings.logLevel.error", "Error")),
        };

        return new LocalizedSettingsOptions(
            injectionModes,
            postProcessModes,
            routingModes,
            sttRoutingModes,
            cloudPresets,
            logLevels);
    }

    public string ResolveCloudPresetDisplayName(CloudProviderPreset preset) =>
        preset.Id switch
        {
            "Custom" => Translate("settings.ai.cloudPreset.custom", "Custom"),
            "OpenAI" => Translate("settings.ai.cloudPreset.openai", "OpenAI"),
            "OpenRouter" => Translate("settings.ai.cloudPreset.openrouter", "OpenRouter"),
            "Groq" => Translate("settings.ai.cloudPreset.groq", "Groq"),
            _ => preset.DisplayName,
        };
}
