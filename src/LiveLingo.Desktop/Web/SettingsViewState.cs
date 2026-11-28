using System.Globalization;
using LiveLingo.Core.Inference;
using LiveLingo.Core.Models;
using LiveLingo.Desktop.Settings;
using LiveLingo.Desktop.Services.Update;

namespace LiveLingo.Desktop.Web;

/// <summary>One application the composer remembers a choice for; the summary reads like the output menu ("Translate to English").</summary>
internal sealed record AppChoiceOptionState(string Value, string Label);

internal sealed record AppPreferenceState(
    string Id,
    string RawName,
    string Name,
    string Summary,
    string Choice,
    string ModeAria,
    string Used);

/// <param name="Theme">One of <see cref="SettingsViewModel.Themes"/>.</param>
internal sealed record GeneralSettingsState(
    string Hotkey,
    bool HotkeyInvalid,
    string CommitKey,
    string Language,
    string Theme,
    bool ReduceMotion,
    bool RememberChoices,
    IReadOnlyList<AppPreferenceState> Apps,
    IReadOnlyList<AppChoiceOptionState> AppChoices);

internal sealed record TermState(int I, string Source, string Target);

internal sealed record CorrectionState(int I, string Spoken, string Canonical);

internal sealed record PolishPresetItemState(
    string Id,
    string Name,
    string Description,
    string Gate,
    string GateBadge,
    bool BuiltIn,
    bool Enabled,
    bool IsDefault,
    bool Customized,
    bool CanToggle,
    string ToggleAria,
    string DefaultInstructions,
    string EffectiveInstructions);

internal sealed record StarterTemplateState(
    string Id,
    string Name,
    string Description,
    string Gate,
    string Instructions);

internal sealed record PresetFormModalState(
    string? Id,
    string Title,
    bool BuiltIn,
    string Name,
    string Description,
    string Gate,
    string Instructions,
    string? Error);

internal sealed record DeletePresetModalState(
    string Id,
    string Name);

/// <param name="Microphones">Device names; the first, empty one is the system default.</param>
/// <param name="DefaultOutput">One of <see cref="SettingsViewModel.DefaultOutputs"/>.</param>
/// <param name="PolishPrompt">What the polish requirements box shows (the built-in text where nothing is customized).</param>
/// <param name="PolishPromptIsDefault">True while the built-in requirements are used.</param>
/// <param name="PolishPromptInvalid">The text the user typed is not usable and was not saved.</param>
internal sealed record VoiceSettingsState(
    IReadOnlyList<string> Microphones, string Microphone, string Direction, bool Cleanup, string DefaultOutput, bool AutoDictate,
    bool PreloadSpeech, bool BuiltInGlossary, IReadOnlyList<TermState> Terms, IReadOnlyList<CorrectionState> Corrections,
    string PolishPrompt, bool PolishPromptIsDefault, bool PolishPromptInvalid,
    string DefaultPolishMode, bool SmartHostPolish,
    IReadOnlyList<PolishPresetItemState> Presets,
    IReadOnlyList<StarterTemplateState> StarterTemplates,
    PresetFormModalState? PresetForm,
    DeletePresetModalState? DeletePreset);

internal sealed record ConsentModalState(
    CloudStep Step,
    string EndpointId,
    string EndpointName,
    string Host,
    string? PreviousEndpointName);

internal sealed record DeleteEndpointModalState(
    string Id,
    string Name,
    string? AffectedSteps);

/// <param name="Text">What the old models are and how much room they take; empty when there are none.</param>
internal sealed record LegacyModelsState(int Count, string Text);

/// <summary>One line of the status the user can read out when asking for help; values never contain what the user wrote.</summary>
internal sealed record DiagnosticLine(string Label, string Value);

internal sealed record HardwareSummaryState(
    string Summary,
    string Residency,
    int BudgetPercent,
    bool OverBudget,
    string ModeNote,
    string DiskUsed,
    bool AnyCloud,
    string PrivacyStatus,
    bool IsRecommended,
    bool IsRecommendedDownloading,
    string? RecommendActionLabel);

internal sealed record DownloadItemState(
    string Id,
    string Category,
    string Name,
    string StatusText,
    int? Percent,
    bool Queued,
    bool Failed,
    bool CanCancel,
    bool CanRetry);

internal sealed record ModelOptionState(
    string Id,
    int Rank,
    string Name,
    string Summary,
    int Quality,
    int Speed,
    string Footprint,
    ModelFit Fit,
    string FitBadge,
    string? FitDetail,
    bool Active,
    bool PendingActivation,
    bool Installed,
    bool Downloading,
    int? DownloadPercent,
    string? DownloadText,
    string? Error,
    bool CanSelectImmediate,
    bool CanDownload,
    string? DownloadActionLabel,
    bool CanDelete,
    string? DeleteActionLabel);

/// <summary>One saved cloud endpoint shown in a text stage's radio list; never exposes the stored API key.</summary>
internal sealed record StageCloudOptionState(
    string Id,
    string Name,
    string Url,
    string Host,
    string Model,
    bool HasKey,
    bool Usable,
    bool Active,
    string? PeerBadge);

internal sealed record ModelStageGroupState(
    string Stage,
    string TabTitle,
    string TabSummary,
    bool Busy,
    string Title,
    string Desc,
    bool LocalOnly,
    string? Notice,
    CloudStep? CloudStep,
    bool CloudRouted,
    bool CloudInvalid,
    IReadOnlyList<StageCloudOptionState> CloudOptions,
    IReadOnlyList<ModelOptionState> Options);

internal sealed record InstalledModelItemState(
    string Id,
    string Name,
    string Category,
    string SizeText,
    bool InUse,
    string StatusBadge,
    bool CanActivate,
    bool CanDelete,
    string? DeleteActionLabel);

internal sealed record ModelStorageState(
    string TabTitle,
    string TabSummary,
    string TotalSize,
    string Breakdown,
    string Path,
    IReadOnlyList<InstalledModelItemState> Items,
    LegacyModelsState Legacy);

internal sealed record ModelsSettingsState(
    string Tab,
    string? NavBadge,
    HardwareSummaryState Hardware,
    IReadOnlyList<DownloadItemState> Downloads,
    IReadOnlyList<ModelStageGroupState> Stages,
    ModelStorageState Storage,
    EndpointForm? Form,
    DeleteEndpointModalState? DeleteConfirm,
    ConsentModalState? Consent);

internal sealed record AboutSettingsState(
    string Version,
    UpdateViewState Update,
    IReadOnlyList<DiagnosticLine> Diagnostics);

/// <summary>What the update row draws: a sentence, the download progress while there is one, and which buttons make sense.</summary>
/// <param name="Percent">The download progress; null when nothing is being downloaded.</param>
/// <param name="Busy">A check or download is under way; the page shows it and offers nothing else.</param>
/// <param name="CanDownload">The copy cannot update itself and the releases page is where to get a new installer.</param>
internal sealed record UpdateViewState(string Status, int? Percent, bool Busy, bool CanCheck, bool CanRestart, bool CanDownload)
{
    public static UpdateViewState From(UpdateState state, bool hasDownloadPage, SettingsTexts texts, string current) => new(
        texts.UpdateStatus(state, current),
        state.Phase == UpdatePhase.Downloading ? state.Percent : null,
        state.Busy,
        state.Phase is UpdatePhase.Idle or UpdatePhase.UpToDate or UpdatePhase.Failed,
        state.Phase == UpdatePhase.Ready,
        state.Phase == UpdatePhase.Unsupported && hasDownloadPage);
}

/// <summary>What the host knows about itself: the app version and the system it runs on.</summary>
internal sealed record AppInfo(string Version, string Platform);

/// <param name="Theme">One of <see cref="SettingsViewModel.Themes"/>.</param>
internal sealed record SettingsOptionsState(bool Animations, string Theme);

/// <summary>
/// Everything the settings page draws, as plain data (design 5.2). Like the composer's, it is replaced wholesale by
/// each snapshot. Built only by <see cref="From"/>.
/// </summary>
internal sealed record SettingsViewState(
    int Rev,
    string Section,
    GeneralSettingsState General,
    VoiceSettingsState Voice,
    ModelsSettingsState Models,
    AboutSettingsState About,
    SettingsOptionsState Options)
{
    public static SettingsViewState From(
        SettingsViewModel vm,
        SettingsTexts texts,
        string section,
        AppInfo app,
        UpdateViewState update,
        string modelsTab = "speech")
    {
        var theme = SettingsViewModel.Themes[vm.ThemeIndex];
        var presets = vm.Presets;
        var enabledPresets = presets.Where(p => p.Enabled).ToArray();
        var appChoices = BuildAppChoices(enabledPresets, texts);
        var presetItems = presets.Select(p =>
        {
            var name = texts.PresetDisplayName(p);
            var isDefault = string.Equals(p.Id, vm.DefaultPolishMode, StringComparison.Ordinal);
            var gateBadge = p.Gate == LiveLingo.Core.Cleanup.PolishGateKind.FactAnchor
                ? texts.Label("presets.gate.factAnchor")
                : texts.Label("presets.gate.verbatim");
            return new PolishPresetItemState(
                p.Id,
                name,
                texts.PresetDescription(p),
                p.Gate.ToString(),
                gateBadge,
                p.BuiltIn,
                p.Enabled,
                isDefault,
                p.IsCustomized,
                CanToggle: !isDefault,
                texts.PresetToggleAria(name),
                p.DefaultInstructions,
                p.EffectiveInstructions);
        }).ToArray();

        PresetFormModalState? presetForm = null;
        if (vm.ActivePresetForm is { } form)
        {
            var editing = form.Id is null ? null : presets.FirstOrDefault(p => p.Id == form.Id);
            var editingName = editing is null ? "" : texts.PresetDisplayName(editing);
            var errorKey = form.Error switch
            {
                PresetFormError.NameMissing => "nameMissing",
                PresetFormError.NameTaken => "nameTaken",
                PresetFormError.DescriptionInvalid => "descInvalid",
                PresetFormError.InstructionsInvalid => "instructionsInvalid",
                _ => null
            };
            presetForm = new PresetFormModalState(
                form.Id,
                editing is null ? texts.Label("presets.form.add") : texts.PresetEditTitle(editingName),
                editing?.BuiltIn == true,
                editing is null ? "" : editingName,
                editing is null ? "" : texts.PresetDescription(editing),
                editing?.Gate.ToString() ?? nameof(LiveLingo.Core.Cleanup.PolishGateKind.FactAnchor),
                editing?.EffectiveInstructions ?? "",
                errorKey);
        }

        DeletePresetModalState? deletePreset = vm.PendingDeletePreset is { } deletingPreset
            ? new DeletePresetModalState(deletingPreset.Id, deletingPreset.Name)
            : null;

        return new SettingsViewState(
            Rev: 0,
            section,
            new GeneralSettingsState(
                vm.SummonHotkey,
                vm.HotkeyInvalid,
                SettingsViewModel.CommitKeys[vm.CommitKeyIndex],
                SettingsViewModel.Languages[vm.LanguageIndex],
                theme,
                vm.ReduceMotion,
                vm.RememberChoices,
                [.. vm.Apps.Select(a =>
                {
                    var displayName = texts.AppName(a.Name);
                    var resolvedMode = string.IsNullOrWhiteSpace(a.PolishMode) || !enabledPresets.Any(p => p.Id == a.PolishMode)
                        ? vm.DefaultPolishMode
                        : a.PolishMode;
                    var preset = presets.FirstOrDefault(p => p.Id == resolvedMode);
                    var choice = a.Output == LiveLingo.Desktop.Composer.ComposerOutput.Translate
                        ? "translate:" + a.Target
                        : "clean:" + resolvedMode;
                    return new AppPreferenceState(
                        a.AppId,
                        a.Name,
                        displayName,
                        texts.AppSummary(a.Output, a.Target, preset),
                        choice,
                        texts.AppModeAria(displayName),
                        texts.Date(a.UsedAt));
                })],
                appChoices),
            new VoiceSettingsState(
                vm.MicrophoneNames,
                vm.MicrophoneNames[vm.MicrophoneIndex],
                SettingsViewModel.Targets[vm.TargetIndex],
                vm.CleanupEnabled,
                SettingsViewModel.DefaultOutputs[vm.DefaultOutputIndex],
                vm.AutoDictate,
                vm.PreloadSpeech,
                vm.BuiltInGlossary,
                [.. vm.Terms.Select((term, i) => new TermState(i, term.Source, term.Target))],
                [.. vm.Corrections.Select((corr, i) => new CorrectionState(i, corr.Spoken, corr.Canonical))],
                vm.PolishPromptShown,
                vm.PolishPromptIsDefault,
                vm.PolishPromptInvalid,
                vm.DefaultPolishMode,
                vm.SmartHostPolish,
                presetItems,
                [.. LiveLingo.Core.Cleanup.PolishPresets.StarterTemplates.Select(t => new StarterTemplateState(
                    t.Id,
                    texts.StarterTemplateName(t.Id),
                    texts.StarterTemplateDesc(t.Id),
                    t.Gate.ToString(),
                    t.Instructions))],
                presetForm,
                deletePreset),
            BuildModelsState(vm, texts, modelsTab),
            new AboutSettingsState(app.Version, update, Diagnostics(vm, texts, app)),
            new SettingsOptionsState(!vm.ReduceMotion, theme));
    }

    private static IReadOnlyList<AppChoiceOptionState> BuildAppChoices(
        IReadOnlyList<LiveLingo.Core.Cleanup.PolishPreset> enabledPresets,
        SettingsTexts texts)
    {
        var list = new List<AppChoiceOptionState>(enabledPresets.Count + 3);
        foreach (var preset in enabledPresets)
            list.Add(new AppChoiceOptionState("clean:" + preset.Id, texts.PresetDisplayName(preset)));
        list.Add(new AppChoiceOptionState("translate:English", texts.TranslateChoiceLabel(LiveLingo.Core.Translation.TargetLanguage.English)));
        list.Add(new AppChoiceOptionState("translate:Chinese", texts.TranslateChoiceLabel(LiveLingo.Core.Translation.TargetLanguage.Chinese)));
        list.Add(new AppChoiceOptionState("default", texts.Label("apps.followDefault")));
        return list;
    }

    private static ModelsSettingsState BuildModelsState(
        SettingsViewModel vm,
        SettingsTexts texts,
        string modelsTab)
    {
        var coordinator = vm.ModelCoordinator;
        var hardware = coordinator.Hardware;
        var active = coordinator.ActiveSelection;
        var assessment = ModelAdvisor.AssessPlan(
            active,
            hardware,
            cleanInCloud: vm.CloudClean,
            translateInCloud: vm.CloudTranslate,
            cleanupEnabled: vm.CleanupEnabled,
            speechInCloud: vm.CloudSpeech);
        var budgetPercent = assessment.SafeBudgetBytes > 0
            ? Math.Clamp((int)Math.Round(assessment.ActiveCoResidentBytes * 100d / assessment.SafeBudgetBytes), 0, 100)
            : 100;

        var inventory = coordinator.GetInstalledInventory();
        var catalogDiskBytes = inventory.Sum(item => item.SizeBytes);
        var totalDiskBytes = catalogDiskBytes + vm.LegacyBytes;

        var anyCloud = vm.CloudSpeech || vm.CloudClean || vm.CloudTranslate;
        var isRecommended = coordinator.IsRecommendedActive;
        var isRecommendedDownloading = coordinator.IsRecommendedDownloading;
        var hardwareView = new HardwareSummaryState(
            texts.HardwareSummary(hardware),
            texts.ResidencySummary(assessment),
            budgetPercent,
            assessment.ExceedsSafeBudget,
            texts.ResidencyMode(assessment),
            texts.DiskUsedSummary(totalDiskBytes),
            anyCloud,
            texts.ModelsPrivacyStatus(vm.CloudClean, vm.CloudTranslate, vm.CloudSpeech),
            isRecommended,
            isRecommendedDownloading,
            isRecommended || isRecommendedDownloading ? null : texts.RecommendActionLabel(coordinator.MissingBytesForRecommended()));

        var activeDownloads = coordinator.GetActiveDownloads();
        var downloadRows = activeDownloads.Select(task =>
        {
            var isConnectingOrQueued = task.Queued || (task.State.IsDownloading && task.State.BytesDownloaded <= 0 && task.State.Percentage < 0.5);
            int? percent = task.State.IsDownloading && !isConnectingOrQueued
                ? Math.Clamp((int)Math.Round(task.State.Percentage), 0, 100)
                : null;
            var statusText = task.State.IsFailed
                ? texts.DownloadError(task.State) ?? ""
                : texts.DownloadProgressLabel(task.State, task.Model.SizeBytes, task.Queued);

            return new DownloadItemState(
                task.Model.Id,
                texts.CategoryBadge(task.Model, task.Stage),
                texts.CatalogArtifactName(task.Model),
                statusText,
                percent,
                task.Queued,
                task.State.IsFailed,
                CanCancel: task.State.IsDownloading || task.Queued,
                CanRetry: task.State.IsFailed);
        }).ToArray();

        var primaryRunning = downloadRows.FirstOrDefault(row => !row.Failed && row.Percent is not null);
        var anyBusyDownload = downloadRows.Any(row => !row.Failed);
        var navBadge = texts.NavDownloadBadge(primaryRunning?.Percent, anyBusyDownload);

        ModelStage[] stages = [ModelStage.Speech, ModelStage.Cleaner, ModelStage.Translator];
        var groups = stages.Select(stage =>
        {
            var evaluations = ModelAdvisor.EvaluateStage(
                stage,
                hardware,
                active,
                cleanInCloud: vm.CloudClean,
                translateInCloud: vm.CloudTranslate,
                speechInCloud: vm.CloudSpeech);
            var activeDescriptor = active.DescriptorFor(stage);
            var activeId = activeDescriptor.Id;
            var pendingId = coordinator.PendingModelId(stage);
            var currentStageInstalled = coordinator.IsInstalled(activeDescriptor);

            CloudStep? cloudStep = stage switch
            {
                ModelStage.Speech => CloudStep.Speech,
                ModelStage.Cleaner => CloudStep.Clean,
                ModelStage.Translator => CloudStep.Translate,
                _ => null
            };
            var stageEndpoint = stage switch
            {
                ModelStage.Speech => vm.SpeechEndpoint,
                ModelStage.Cleaner => vm.CleanerEndpoint,
                ModelStage.Translator => vm.TranslatorEndpoint,
                _ => null
            };
            var cloudRouted = stage switch
            {
                ModelStage.Speech => vm.CloudSpeech,
                ModelStage.Cleaner => vm.CloudClean,
                ModelStage.Translator => vm.CloudTranslate,
                _ => false
            };
            var cloudOptions = cloudStep is null
                ? []
                : vm.Endpoints.Select(endpoint =>
                {
                    var activeHere = cloudRouted && endpoint.Id == stageEndpoint?.Id;
                    var usableHere = stage == ModelStage.Speech ? endpoint.SpeechUsable : endpoint.Usable;
                    return new StageCloudOptionState(
                        endpoint.Id,
                        endpoint.Name,
                        endpoint.BaseUrl,
                        HostOf(endpoint.BaseUrl),
                        endpoint.Model,
                        endpoint.HasKey,
                        usableHere,
                        Active: activeHere,
                        texts.PeerStageBadge(stage, activeHere, endpoint.UsedForClean, endpoint.UsedForTranslate, endpoint.UsedForSpeech));
                }).ToArray();

            var notice = texts.StageNotice(stage, vm.CleanupEnabled, vm.CloudSpeech, vm.SpeechEndpoint?.Model, activeId, cloudRouted);
            var hasWorkingCurrent = cloudRouted || currentStageInstalled;

            ModelDownloadState? stageRunningDownload = null;
            var stageQueued = false;
            var options = evaluations.Select(eval =>
            {
                var model = eval.Model;
                var profile = model.Profile!;
                var download = coordinator.GetOptionDownloadState(model);
                var isQueued = coordinator.IsQueued(model.Id);
                var isActive = !cloudRouted && model.Id == activeId;
                var isInstalled = download.IsInstalled;
                var isDownloading = download.IsDownloading;
                if (isDownloading && stageRunningDownload is null)
                {
                    stageRunningDownload = download;
                    stageQueued = isQueued;
                }

                var isIndeterminate = isDownloading && (isQueued || (download.BytesDownloaded <= 0 && download.Percentage < 0.5));
                var canSelectImmediate = eval.IsSelectable && !isActive && (isInstalled || !hasWorkingCurrent);
                var canDownload = eval.IsSelectable && !isInstalled && !isDownloading;
                var canDelete = isInstalled && coordinator.CanDeleteModel(model.Id);
                var installedSize = inventory.FirstOrDefault(i => i.Model.Id == model.ArtifactId)?.SizeBytes ?? model.SizeBytes;

                return new ModelOptionState(
                    model.Id,
                    profile.Rank,
                    texts.OptionName(model),
                    texts.OptionDescription(model),
                    profile.QualityStars,
                    profile.SpeedStars,
                    texts.OptionFootprint(model),
                    eval.Fit,
                    texts.FitBadge(eval.Fit),
                    texts.FitDetail(eval, hardware),
                    isActive,
                    PendingActivation: model.Id == pendingId,
                    isInstalled,
                    isDownloading,
                    isDownloading && !isIndeterminate ? Math.Clamp((int)Math.Round(download.Percentage), 0, 100) : null,
                    isDownloading ? texts.DownloadProgressLabel(download, model.SizeBytes, isQueued) : null,
                    texts.DownloadError(download),
                    canSelectImmediate,
                    canDownload,
                    canDownload ? texts.DownloadActionLabel(model, isActive, download.IsFailed) : null,
                    canDelete,
                    canDelete ? texts.DeleteWithSizeLabel(installedSize) : null);
            }).ToArray();

            return new ModelStageGroupState(
                stage.ToString().ToLowerInvariant(),
                texts.StageTabTitle(stage),
                texts.StageTabSummary(
                    stage,
                    activeDescriptor,
                    currentStageInstalled,
                    stageRunningDownload,
                    stageQueued,
                    vm.CleanupEnabled,
                    cloudRouted,
                    stageEndpoint?.Name),
                Busy: stageRunningDownload is not null,
                texts.StageTitle(stage),
                texts.StageDescription(stage),
                LocalOnly: false,
                notice,
                cloudStep,
                cloudRouted,
                CloudInvalid: cloudStep is not null && vm.CloudInvalid,
                cloudOptions,
                options);
        }).ToArray();

        var inUseBytes = inventory.Where(i => i.InUse).Sum(i => i.SizeBytes);
        var idleBytes = inventory.Where(i => !i.InUse).Sum(i => i.SizeBytes) + vm.LegacyBytes;
        var totalItemCount = inventory.Count + vm.LegacyCount;
        var storageItems = inventory.Select(item => new InstalledModelItemState(
            item.Model.Id,
            texts.CatalogArtifactName(item.Model),
            texts.CategoryBadge(item.Model, item.Stage),
            texts.FormatSize(item.SizeBytes),
            item.InUse,
            texts.StorageStatusBadge(item.InUse),
            item.CanActivate,
            item.CanDelete,
            item.CanDelete ? texts.DeleteWithSizeLabel(item.SizeBytes) : null)).ToArray();

        var storageView = new ModelStorageState(
            texts.StorageTabTitle,
            texts.StorageTabSummary(totalItemCount, totalDiskBytes),
            texts.FormatSize(totalDiskBytes),
            texts.StorageBreakdown(totalItemCount, inUseBytes, idleBytes),
            coordinator.StorageDirectory,
            storageItems,
            new LegacyModelsState(vm.LegacyCount, vm.HasLegacyModels ? texts.LegacyDescription(vm.LegacyCount, vm.LegacyBytes) : ""));

        ConsentModalState? consentModal = null;
        if (vm.PendingConsent is { } pending
            && vm.Endpoints.FirstOrDefault(item => item.Id == pending.EndpointId) is { } targetEndpoint)
        {
            var previousName = pending.PreviousEndpointId is null
                ? null
                : vm.Endpoints.FirstOrDefault(item => item.Id == pending.PreviousEndpointId)?.Name;
            consentModal = new ConsentModalState(
                pending.Step,
                targetEndpoint.Id,
                targetEndpoint.Name,
                HostOf(targetEndpoint.BaseUrl),
                previousName);
        }

        DeleteEndpointModalState? deleteModal = null;
        if (vm.PendingDeleteEndpoint is { } deleting)
        {
            deleteModal = new DeleteEndpointModalState(
                deleting.Id,
                deleting.Name,
                texts.AffectedCloudSteps(deleting.UsedForClean, deleting.UsedForTranslate, deleting.UsedForSpeech));
        }

        return new ModelsSettingsState(
            modelsTab,
            navBadge,
            hardwareView,
            downloadRows,
            groups,
            storageView,
            vm.Form,
            deleteModal,
            consentModal);
    }

    private static string HostOf(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Authority : url;

    /// <summary>Only settings that are switches or closed choices: terms, endpoints, device names and keys never go in.</summary>
    private static DiagnosticLine[] Diagnostics(SettingsViewModel vm, SettingsTexts texts, AppInfo app)
    {
        var active = vm.ActiveModels;
        var activeCloudEndpoints = new[] { vm.SpeechEndpoint, vm.CleanerEndpoint, vm.TranslatorEndpoint }.Where(e => e is not null).ToArray();
        var keyStored = activeCloudEndpoints.Length > 0
            ? activeCloudEndpoints.All(e => e!.HasKey)
            : vm.Endpoints.Any(e => e.HasKey);
        return
        [
            new(texts.Label("version"), app.Version),
            new(texts.Label("diag.platform"), app.Platform),
            new(texts.Label("diag.backend"), texts.BackendDiagnostic(vm.Hardware)),
            new(texts.Label("language"), SettingsViewModel.Languages[vm.LanguageIndex]),
            new(texts.Label("diag.model.speech"), active.Speech.ShortName),
            new(texts.Label("diag.model.cleaner"), active.Cleaner.ShortName),
            new(texts.Label("diag.model.translator"), active.Translator.ShortName),
            new(texts.Label("cleanup"), texts.OnOff(vm.CleanupEnabled)),
            new(texts.Label("cloud.speech"), texts.OnOff(vm.CloudSpeech)),
            new(texts.Label("cloud.translate"), texts.OnOff(vm.CloudTranslate)),
            new(texts.Label("cloud.clean"), texts.OnOff(vm.CloudClean)),
            new(texts.Label("cloud.endpoints"), vm.Endpoints.Count.ToString(CultureInfo.InvariantCulture)),
            new(texts.Label("cloud.apiKey"), texts.Stored(keyStored)),
            new(texts.Label("legacy"), vm.LegacyCount.ToString(CultureInfo.InvariantCulture))
        ];
    }
}
