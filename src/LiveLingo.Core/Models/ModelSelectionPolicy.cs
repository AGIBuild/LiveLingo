namespace LiveLingo.Core.Models;

public sealed record CloudModelPreferences(
    bool Enabled,
    string? BaseUrl,
    string? ApiKey,
    string? TranslationModelId,
    string? PostProcessingModelId)
{
    public string? ResolvePostProcessingModelId() =>
        string.IsNullOrWhiteSpace(PostProcessingModelId) ? TranslationModelId : PostProcessingModelId;
}

public static class ModelSelectionPolicy
{
    public static ModelProfile SelectTranslationProfile(
        IModelCatalog catalog,
        string? activeModelId,
        string sourceLanguage,
        string targetLanguage,
        TranslationRoutingMode routingMode = TranslationRoutingMode.PreferLocal,
        bool routeUnsupportedPairsToCloud = true,
        CloudModelPreferences? cloud = null,
        CloudProviderRoutingState? runtimeState = null)
    {
        string? cloudError = null;

        if (routingMode == TranslationRoutingMode.CloudOnly)
        {
            return GetRequiredCloudProfile(ModelTaskType.Translation, cloud, runtimeState);
        }

        if (routingMode == TranslationRoutingMode.PreferCloud &&
            TryCreateCloudProfile(ModelTaskType.Translation, cloud, runtimeState, out var preferredCloud, out cloudError))
        {
            return preferredCloud;
        }

        var local = TrySelectLocalTranslationProfile(catalog, activeModelId, sourceLanguage, targetLanguage);
        if (local is not null)
            return local;

        var shouldFallbackToCloud = routingMode == TranslationRoutingMode.PreferCloud || routeUnsupportedPairsToCloud;
        if (shouldFallbackToCloud &&
            TryCreateCloudProfile(ModelTaskType.Translation, cloud, runtimeState, out var fallbackCloud, out cloudError))
        {
            return fallbackCloud;
        }

        if (shouldFallbackToCloud && IsCloudEnabled(cloud))
            throw new InvalidOperationException(cloudError ?? "Cloud translation provider is not configured.");

        throw new NotSupportedException($"No chat translation model available for {sourceLanguage}→{targetLanguage}.");
    }

    public static ModelProfile SelectPostProcessingProfile(
        IModelCatalog catalog,
        string? activeModelId,
        string defaultTargetLanguage,
        TranslationRoutingMode routingMode = TranslationRoutingMode.PreferLocal,
        bool routePostProcessingToCloud = false,
        CloudModelPreferences? cloud = null,
        CloudProviderRoutingState? runtimeState = null)
    {
        string? cloudError = null;
        var shouldPreferCloud = routingMode is TranslationRoutingMode.CloudOnly or TranslationRoutingMode.PreferCloud
            || routePostProcessingToCloud;
        if (shouldPreferCloud &&
            TryCreateCloudProfile(ModelTaskType.PostProcessing, cloud, runtimeState, out var cloudProfile, out cloudError))
        {
            return cloudProfile;
        }

        if (routingMode == TranslationRoutingMode.CloudOnly && IsCloudEnabled(cloud))
            throw new InvalidOperationException(cloudError ?? "Cloud post-processing provider is not configured.");

        var active = FindProfileById(catalog, activeModelId, cloud);
        if (active is not null &&
            active.ExecutionKind == ModelExecutionKind.ChatCompletions &&
            active.TaskType is ModelTaskType.Translation or ModelTaskType.PostProcessing)
        {
            return active;
        }

        if (active is null)
            return SelectTranslationProfile(
                catalog,
                null,
                "zh",
                defaultTargetLanguage,
                TranslationRoutingMode.LocalOnly,
                routeUnsupportedPairsToCloud: false);

        return catalog.FindById(ModelRegistry.Qwen25_15B.Id)
            ?? SelectTranslationProfile(
                catalog,
                null,
                "zh",
                defaultTargetLanguage,
                TranslationRoutingMode.LocalOnly,
                routeUnsupportedPairsToCloud: false);
    }

    public static ModelProfile? FindProfileById(
        IModelCatalog catalog,
        string? profileId,
        CloudModelPreferences? cloud = null)
    {
        if (string.IsNullOrWhiteSpace(profileId))
            return null;

        return catalog.FindById(profileId)
            ?? TryFindCloudProfileById(profileId, cloud);
    }

    private static ModelProfile? TrySelectLocalTranslationProfile(
        IModelCatalog catalog,
        string? activeModelId,
        string sourceLanguage,
        string targetLanguage)
    {
        var active = FindProfileById(catalog, activeModelId);
        if (active is not null &&
            active.TaskType == ModelTaskType.Translation &&
            active.ExecutionKind == ModelExecutionKind.ChatCompletions &&
            SupportsLanguagePair(active, sourceLanguage, targetLanguage))
        {
            return active;
        }

        var fallback = catalog.FindById(ModelRegistry.Gemma4_12B.Id)
            ?? catalog.FindById(ModelRegistry.Qwen35_9B.Id)
            ?? throw new InvalidOperationException("Default translation profile is missing from the model catalog.");

        return SupportsLanguagePair(fallback, sourceLanguage, targetLanguage)
            ? fallback
            : null;
    }

    private static ModelProfile GetRequiredCloudProfile(
        ModelTaskType taskType,
        CloudModelPreferences? cloud,
        CloudProviderRoutingState? runtimeState)
    {
        if (TryCreateCloudProfile(taskType, cloud, runtimeState, out var profile, out var error))
            return profile;

        throw new InvalidOperationException(error ?? BuildCloudConfigurationMessage(taskType));
    }

    private static bool TryCreateCloudProfile(
        ModelTaskType taskType,
        CloudModelPreferences? cloud,
        CloudProviderRoutingState? runtimeState,
        out ModelProfile profile,
        out string? error)
    {
        profile = default!;
        error = null;

        if (!IsCloudEnabled(cloud))
            return false;

        if (string.IsNullOrWhiteSpace(cloud!.BaseUrl) ||
            string.IsNullOrWhiteSpace(cloud.ApiKey))
        {
            error = BuildCloudConfigurationMessage(taskType);
            return false;
        }

        var modelId = taskType == ModelTaskType.PostProcessing
            ? cloud.ResolvePostProcessingModelId()
            : cloud.TranslationModelId;
        if (string.IsNullOrWhiteSpace(modelId))
        {
            error = BuildCloudConfigurationMessage(taskType);
            return false;
        }

        modelId = modelId.Trim();
        if (!PassesRuntimeValidation(taskType, modelId, runtimeState, out error))
            return false;

        var descriptorType = taskType == ModelTaskType.PostProcessing
            ? ModelType.PostProcessing
            : ModelType.Translation;
        var displayName = taskType == ModelTaskType.PostProcessing
            ? $"Cloud {modelId} (Post-processing)"
            : $"Cloud {modelId}";

        profile = new ModelProfile(
            modelId,
            displayName,
            taskType,
            ModelProviderKind.OpenAICompatible,
            ModelRuntimeKind.RemoteHttp,
            ModelExecutionKind.ChatCompletions,
            [],
            new ModelDescriptor(modelId, displayName, string.Empty, 0, descriptorType),
            SupportsAllLanguages: true);
        return true;
    }

    private static bool PassesRuntimeValidation(
        ModelTaskType taskType,
        string modelId,
        CloudProviderRoutingState? runtimeState,
        out string? error)
    {
        error = null;
        if (runtimeState is null || !runtimeState.HasValidation)
            return true;

        if (!runtimeState.IsHealthy)
        {
            error = runtimeState.Message ?? BuildCloudUnavailableMessage(taskType);
            return false;
        }

        if (runtimeState.HasValidatedModels && !runtimeState.IsModelAvailable(modelId))
        {
            error = BuildCloudModelMissingMessage(taskType, modelId);
            return false;
        }

        return true;
    }

    private static ModelProfile? TryFindCloudProfileById(string profileId, CloudModelPreferences? cloud)
    {
        if (!IsCloudEnabled(cloud))
            return null;

        if (string.Equals(profileId, cloud!.ResolvePostProcessingModelId(), StringComparison.OrdinalIgnoreCase) &&
            TryCreateCloudProfile(ModelTaskType.PostProcessing, cloud, runtimeState: null, out var postProcessing, out _))
        {
            return postProcessing;
        }

        if (string.Equals(profileId, cloud.TranslationModelId, StringComparison.OrdinalIgnoreCase) &&
            TryCreateCloudProfile(ModelTaskType.Translation, cloud, runtimeState: null, out var translation, out _))
        {
            return translation;
        }

        return null;
    }

    private static bool SupportsLanguagePair(ModelProfile profile, string sourceLanguage, string targetLanguage) =>
        profile.SupportsAllLanguages ||
        (profile.Languages.Contains(targetLanguage, StringComparer.OrdinalIgnoreCase) &&
         (string.IsNullOrWhiteSpace(sourceLanguage) ||
          profile.Languages.Contains(sourceLanguage, StringComparer.OrdinalIgnoreCase)));

    private static bool IsCloudEnabled(CloudModelPreferences? cloud) => cloud is { Enabled: true };

    private static string BuildCloudConfigurationMessage(ModelTaskType taskType) =>
        taskType == ModelTaskType.PostProcessing
            ? "Cloud post-processing provider is not configured. Set base URL, API key, and a cloud model in Settings."
            : "Cloud translation provider is not configured. Set base URL, API key, and a cloud model in Settings.";

    private static string BuildCloudUnavailableMessage(ModelTaskType taskType) =>
        taskType == ModelTaskType.PostProcessing
            ? "Cloud post-processing provider validation failed."
            : "Cloud translation provider validation failed.";

    private static string BuildCloudModelMissingMessage(ModelTaskType taskType, string modelId) =>
        taskType == ModelTaskType.PostProcessing
            ? $"Configured cloud post-processing model '{modelId}' was not found in the provider model list."
            : $"Configured cloud translation model '{modelId}' was not found in the provider model list.";
}
