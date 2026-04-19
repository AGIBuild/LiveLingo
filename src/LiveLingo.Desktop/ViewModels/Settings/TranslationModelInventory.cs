using LiveLingo.Core.Models;

namespace LiveLingo.Desktop.ViewModels.Settings;

internal sealed class TranslationModelInventory : ITranslationModelInventory
{
    private readonly IModelManager? _modelManager;
    private readonly ISettingsLocalizationHelper _localization;

    public TranslationModelInventory(IModelManager? modelManager, ISettingsLocalizationHelper localization)
    {
        _modelManager = modelManager;
        _localization = localization;
    }

    public IReadOnlyList<TranslationModelOption> Snapshot()
    {
        var installed = _modelManager?.ListInstalled() ?? [];
        return installed
            .Where(m => m.Type == ModelType.Translation)
            .Select(CreateInstalledTranslationModelOption)
            .Where(option => option is not null)
            .Select(option => option!)
            .DistinctBy(o => o.Id, StringComparer.OrdinalIgnoreCase)
            .OrderBy(o => o.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public string BuildPairLabel(ModelType type, string? source, string? target) =>
        type == ModelType.Translation && !string.IsNullOrWhiteSpace(source) && !string.IsNullOrWhiteSpace(target)
            ? $"{source}→{target}"
            : type == ModelType.PostProcessing
                ? _localization.Translate("settings.translation.pair.postProcessing", "Post-processing")
                : type.ToString();

    public bool TryParseLanguagePairFromModelId(string modelId, out string sourceLanguage, out string targetLanguage)
    {
        sourceLanguage = string.Empty;
        targetLanguage = string.Empty;

        const string prefix = "opus-mt-";
        if (!modelId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        var pairPart = modelId[prefix.Length..];
        var parts = pairPart.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
            return false;

        sourceLanguage = parts[0];
        targetLanguage = parts[1];
        return true;
    }

    private TranslationModelOption? CreateInstalledTranslationModelOption(InstalledModel installed)
    {
        if (installed.Type != ModelType.Translation)
            return null;

        string? source = null;
        string? target = null;
        if (TryParseLanguagePairFromModelId(installed.Id, out var parsedSource, out var parsedTarget))
        {
            source = parsedSource;
            target = parsedTarget;
        }

        return new TranslationModelOption(
            installed.Id,
            installed.DisplayName,
            ModelType.Translation,
            source,
            target,
            BuildPairLabel(ModelType.Translation, source, target));
    }
}
