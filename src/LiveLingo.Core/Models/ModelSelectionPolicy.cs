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

/// <summary>
/// User-configured preferences for the Ollama local daemon. Ollama is a
/// user-managed alternative local runtime – we connect to a running daemon
/// and use pre-pulled model tags, but never manage the daemon lifecycle
/// or download models on the user's behalf.
/// </summary>
public sealed record OllamaPreferences(
    bool Enabled,
    string BaseUrl,
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
        CloudProviderRoutingState? runtimeState = null,
        TranslationRoutingContext? context = null,
        OllamaPreferences? ollama = null)
    {
        // Content-aware escalation: long texts / rare language pairs / high-quality mode
        // promote PreferLocal → PreferCloud when a cloud provider is configured.
        var effectiveMode = ApplyContextEscalation(routingMode, context);
        string? cloudError = null;

        if (effectiveMode == TranslationRoutingMode.CloudOnly)
        {
            return GetRequiredCloudProfile(ModelTaskType.Translation, cloud, runtimeState);
        }

        if (effectiveMode == TranslationRoutingMode.PreferCloud &&
            TryCreateCloudProfile(ModelTaskType.Translation, cloud, runtimeState, out var preferredCloud, out cloudError))
        {
            return preferredCloud;
        }

        // Ollama, when enabled, represents a user-managed local runtime that supplements
        // (not replaces) the built-in llama.cpp backend. An Ollama translation profile
        // takes precedence over the built-in local catalog only when the user explicitly
        // configured an Ollama translation tag.
        if (TryCreateOllamaProfile(ModelTaskType.Translation, ollama, out var ollamaProfile))
        {
            return ollamaProfile;
        }

        var local = TrySelectLocalTranslationProfile(catalog, activeModelId, sourceLanguage, targetLanguage);
        if (local is not null)
            return local;

        var shouldFallbackToCloud = effectiveMode == TranslationRoutingMode.PreferCloud || routeUnsupportedPairsToCloud;
        if (shouldFallbackToCloud &&
            TryCreateCloudProfile(ModelTaskType.Translation, cloud, runtimeState, out var fallbackCloud, out cloudError))
        {
            return fallbackCloud;
        }

        if (shouldFallbackToCloud && IsCloudEnabled(cloud))
            throw new InvalidOperationException(cloudError ?? "Cloud translation provider is not configured.");

        throw new NotSupportedException($"No chat translation model available for {sourceLanguage}→{targetLanguage}.");
    }

    /// <summary>
    /// Builds an ordered list of translation candidates that the invoker will try
    /// in sequence. The first candidate is the primary target picked by
    /// <see cref="SelectTranslationProfile"/>; the remaining candidates are
    /// runtime fallbacks consistent with the user's routing mode.
    ///
    /// Invariants:
    ///   - <see cref="TranslationRoutingMode.LocalOnly"/>: only local/Ollama candidates; never cloud.
    ///   - <see cref="TranslationRoutingMode.CloudOnly"/>: only the cloud candidate; never local.
    ///   - <see cref="TranslationRoutingMode.PreferLocal"/> / <see cref="TranslationRoutingMode.PreferCloud"/>:
    ///     primary follows the user's preference; opposite tier is appended as fallback when configured.
    ///
    /// A candidate is never duplicated in the plan — the primary selection wins its slot.
    /// </summary>
    public static TranslationRoutePlan BuildTranslationRoutePlan(
        IModelCatalog catalog,
        string? activeModelId,
        string sourceLanguage,
        string targetLanguage,
        TranslationRoutingMode routingMode = TranslationRoutingMode.PreferLocal,
        bool routeUnsupportedPairsToCloud = true,
        CloudModelPreferences? cloud = null,
        CloudProviderRoutingState? runtimeState = null,
        TranslationRoutingContext? context = null,
        OllamaPreferences? ollama = null)
    {
        var effectiveMode = ApplyContextEscalation(routingMode, context);
        var primary = SelectTranslationProfile(
            catalog, activeModelId, sourceLanguage, targetLanguage,
            routingMode, routeUnsupportedPairsToCloud, cloud, runtimeState, context, ollama);
        var primaryTier = ClassifyTier(primary);
        var candidates = new List<TranslationRouteCandidate>
        {
            new(primary, primaryTier, FirstTokenBudgetFor(primaryTier))
        };

        // LocalOnly / CloudOnly forbid cross-tier fallbacks by contract.
        if (effectiveMode is TranslationRoutingMode.LocalOnly or TranslationRoutingMode.CloudOnly)
            return new TranslationRoutePlan(candidates);

        // PreferLocal picks a local primary; add Ollama and/or cloud as fallbacks if available.
        // PreferCloud picks a cloud primary; add local/Ollama tiers as degradation targets so a
        // transient cloud outage still yields a result (respecting the user's cloud preference).
        AppendCandidateIfDistinct(
            candidates,
            TryBuildOllamaCandidate(ollama, primary));
        AppendCandidateIfDistinct(
            candidates,
            TryBuildLocalCandidate(catalog, sourceLanguage, targetLanguage, primary, activeModelId));
        AppendCandidateIfDistinct(
            candidates,
            TryBuildCloudCandidate(cloud, runtimeState, primary));

        return new TranslationRoutePlan(candidates);
    }

    private static void AppendCandidateIfDistinct(
        List<TranslationRouteCandidate> candidates,
        TranslationRouteCandidate? candidate)
    {
        if (candidate is null) return;
        if (candidates.Any(c => string.Equals(c.Profile.Id, candidate.Profile.Id, StringComparison.OrdinalIgnoreCase)
                                 && c.Tier == candidate.Tier))
            return;
        candidates.Add(candidate);
    }

    private static TranslationRouteCandidate? TryBuildOllamaCandidate(
        OllamaPreferences? ollama,
        ModelProfile primary)
    {
        if (primary.RuntimeKind == ModelRuntimeKind.Ollama) return null;
        return TryCreateOllamaProfile(ModelTaskType.Translation, ollama, out var profile)
            ? new TranslationRouteCandidate(profile, TranslationRouteTier.Ollama, FirstTokenBudgetFor(TranslationRouteTier.Ollama))
            : null;
    }

    private static TranslationRouteCandidate? TryBuildLocalCandidate(
        IModelCatalog catalog,
        string sourceLanguage,
        string targetLanguage,
        ModelProfile primary,
        string? activeModelId)
    {
        if (primary.RuntimeKind == ModelRuntimeKind.LlamaServer) return null;
        var local = TrySelectLocalTranslationProfile(catalog, activeModelId, sourceLanguage, targetLanguage);
        return local is null
            ? null
            : new TranslationRouteCandidate(local, TranslationRouteTier.Local, FirstTokenBudgetFor(TranslationRouteTier.Local));
    }

    private static TranslationRouteCandidate? TryBuildCloudCandidate(
        CloudModelPreferences? cloud,
        CloudProviderRoutingState? runtimeState,
        ModelProfile primary)
    {
        if (primary.RuntimeKind == ModelRuntimeKind.RemoteHttp) return null;
        return TryCreateCloudProfile(ModelTaskType.Translation, cloud, runtimeState, out var profile, out _)
            ? new TranslationRouteCandidate(profile, TranslationRouteTier.Cloud, FirstTokenBudgetFor(TranslationRouteTier.Cloud))
            : null;
    }

    private static TranslationRouteTier ClassifyTier(ModelProfile profile) => profile.RuntimeKind switch
    {
        ModelRuntimeKind.LlamaServer => TranslationRouteTier.Local,
        ModelRuntimeKind.Ollama => TranslationRouteTier.Ollama,
        ModelRuntimeKind.RemoteHttp => TranslationRouteTier.Cloud,
        _ => TranslationRouteTier.Local
    };

    private static TimeSpan FirstTokenBudgetFor(TranslationRouteTier tier) => tier switch
    {
        // Local models may cold-load multi-GB GGUF weights; Ollama has similar warm-up.
        // Cloud is network-bound and normally replies within ~1-2s.
        TranslationRouteTier.Local => TimeSpan.FromSeconds(8),
        TranslationRouteTier.Ollama => TimeSpan.FromSeconds(8),
        TranslationRouteTier.Cloud => TimeSpan.FromSeconds(4),
        _ => TimeSpan.FromSeconds(8)
    };

    /// <summary>
    /// Applies content-aware routing escalation on top of the user's preference.
    /// <see cref="TranslationRoutingMode.LocalOnly"/> and
    /// <see cref="TranslationRoutingMode.CloudOnly"/> are never overridden.
    /// </summary>
    private static TranslationRoutingMode ApplyContextEscalation(
        TranslationRoutingMode mode,
        TranslationRoutingContext? context)
    {
        if (context is null) return mode;
        if (mode is TranslationRoutingMode.LocalOnly or TranslationRoutingMode.CloudOnly) return mode;

        if (context.TextLength > 600 || context.IsRareLanguagePair || context.IsHighQualityMode)
            return TranslationRoutingMode.PreferCloud;

        return mode;
    }

    public static ModelProfile SelectPostProcessingProfile(
        IModelCatalog catalog,
        string? activeModelId,
        string defaultTargetLanguage,
        TranslationRoutingMode routingMode = TranslationRoutingMode.PreferLocal,
        bool routePostProcessingToCloud = false,
        CloudModelPreferences? cloud = null,
        CloudProviderRoutingState? runtimeState = null,
        OllamaPreferences? ollama = null)
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

        if (!shouldPreferCloud &&
            TryCreateOllamaProfile(ModelTaskType.PostProcessing, ollama, out var ollamaProfile))
        {
            return ollamaProfile;
        }

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

        var fallback = catalog.FindById(ModelRegistry.Gemma4_26B_A4B.Id)
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

    private static bool TryCreateOllamaProfile(
        ModelTaskType taskType,
        OllamaPreferences? ollama,
        out ModelProfile profile)
    {
        profile = default!;
        if (ollama is not { Enabled: true })
            return false;

        var modelId = taskType == ModelTaskType.PostProcessing
            ? ollama.ResolvePostProcessingModelId()
            : ollama.TranslationModelId;
        if (string.IsNullOrWhiteSpace(modelId))
            return false;

        modelId = modelId.Trim();
        var descriptorType = taskType == ModelTaskType.PostProcessing
            ? ModelType.PostProcessing
            : ModelType.Translation;
        var displayName = taskType == ModelTaskType.PostProcessing
            ? $"Ollama {modelId} (Post-processing)"
            : $"Ollama {modelId}";

        profile = new ModelProfile(
            modelId,
            displayName,
            taskType,
            ModelProviderKind.Ollama,
            ModelRuntimeKind.Ollama,
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
