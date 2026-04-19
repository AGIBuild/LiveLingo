using LiveLingo.Desktop.Services.Configuration;

namespace LiveLingo.Desktop.ViewModels.Settings;

internal sealed class CloudProviderPresetCoordinator : ICloudProviderPresetCoordinator
{
    private bool _isApplyingPreset;
    private bool _isInferringPreset;

    public event Action? PresentationChanged;

    public bool IsRewritingPresetFields => _isApplyingPreset || _isInferringPreset;

    public void ApplyPreset(CloudProviderSettings cloudProvider)
    {
        if (_isApplyingPreset || _isInferringPreset)
        {
            PresentationChanged?.Invoke();
            return;
        }

        var preset = CloudProviderPresetCatalog.FindById(cloudProvider.PresetId);
        PresentationChanged?.Invoke();
        if (string.Equals(preset.Id, CloudProviderPresetCatalog.Custom.Id, StringComparison.OrdinalIgnoreCase))
            return;

        _isApplyingPreset = true;
        try
        {
            cloudProvider.ProviderType = "OpenAICompatible";
            cloudProvider.BaseUrl = preset.BaseUrl;
            cloudProvider.TranslationModelId = preset.TranslationModelPlaceholder;
            cloudProvider.PostProcessingModelId = preset.PostProcessingModelPlaceholder;
        }
        finally
        {
            _isApplyingPreset = false;
        }

        PresentationChanged?.Invoke();
    }

    public void SyncPresetFromBaseUrl(CloudProviderSettings cloudProvider)
    {
        PresentationChanged?.Invoke();
        if (_isApplyingPreset)
            return;

        var inferredPresetId = CloudProviderPresetCatalog.InferFromBaseUrl(cloudProvider.BaseUrl).Id;
        if (string.Equals(cloudProvider.PresetId, inferredPresetId, StringComparison.OrdinalIgnoreCase))
            return;

        _isInferringPreset = true;
        try
        {
            cloudProvider.PresetId = inferredPresetId;
        }
        finally
        {
            _isInferringPreset = false;
        }
    }

    public CloudProviderPreset GetSelectedPreset(CloudProviderSettings cloudProvider) =>
        CloudProviderPresetCatalog.FindById(cloudProvider.PresetId);
}
