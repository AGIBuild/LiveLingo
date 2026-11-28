using System.Globalization;
using LiveLingo.Core.Cloud;
using LiveLingo.Core.Inference;
using LiveLingo.Core.Models;
using LiveLingo.Core.Translation;
using LiveLingo.Desktop.Composer;
using LiveLingo.Desktop.Services.Localization;
using LiveLingo.Desktop.Services.Update;

namespace LiveLingo.Desktop.Settings;

/// <summary>Turns the settings state into localized text, so the page only draws strings.</summary>
public sealed class SettingsTexts(ILocalizationService loc)
{
    private readonly ComposerTexts _composer = new(loc);

    /// <summary>The static labels of the page, by their key without the "settings." prefix.</summary>
    private static readonly string[] Keys =
    [
        "title", "nav.aria", "nav.general", "nav.voice", "nav.models", "nav.about",
        "hotkey", "hotkey.desc", "hotkey.invalid", "hotkey.prompt",
        "commit", "commit.desc", "commit.enter", "commit.ctrlEnter",
        "language", "language.desc",
        "theme", "theme.desc", "theme.auto", "theme.light", "theme.dark",
        "motion", "motion.desc",
        "mic", "mic.desc", "mic.default",
        "cleanup", "cleanup.desc",
        "direction", "direction.desc", "direction.zhEn", "direction.enZh",
        "output", "output.desc", "output.clean", "output.translate", "output.original",
        "autoDictate", "autoDictate.desc", "preload", "preload.desc", "builtInTerms", "builtInTerms.desc", "remember", "remember.desc",
        "defaultPolish", "defaultPolish.desc", "smartHostPolish", "smartHostPolish.desc",
        "presets", "presets.desc", "presets.add", "presets.builtIn", "presets.custom", "presets.defaultBadge", "presets.tunedBadge",
        "presets.gate.verbatim", "presets.gate.factAnchor", "presets.inspect", "presets.edit", "presets.reset", "presets.delete",
        "presets.form.add", "presets.form.starters", "presets.form.name", "presets.form.nameExample",
        "presets.form.desc", "presets.form.descExample", "presets.form.gate", "presets.form.gate.factAnchor", "presets.form.gate.verbatim",
        "presets.form.instructions", "presets.form.note",
        "presets.error.nameMissing", "presets.error.nameTaken", "presets.error.descInvalid", "presets.error.instructionsInvalid",
        "presets.delete.title", "presets.delete.body",
        "polish", "polish.desc", "polish.reset", "polish.custom", "polish.invalid", "polish.save",
        "apps", "apps.desc", "apps.empty", "apps.forget", "apps.clear", "apps.followDefault",
        "corrections", "corrections.spoken", "corrections.canonical", "corrections.spokenExample", "corrections.canonicalExample",
        "corrections.add", "corrections.remove", "corrections.hint",
        "terms", "terms.source", "terms.target", "terms.sourceExample", "terms.targetExample", "terms.add", "terms.remove", "terms.hint",
        "privacy.badge.alwaysLocal", "privacy.badge.local", "privacy.badge.cloud", "privacy.routing.allLocal",
        "cloud.speech", "cloud.translate", "cloud.clean",
        "cloud.baseUrl", "cloud.baseUrl.desc", "cloud.speech.baseUrl.desc",
        "cloud.model", "cloud.model.desc", "cloud.speech.model.desc",
        "cloud.apiKey", "cloud.apiKey.desc",
        "cloud.keyStored", "cloud.invalid",
        "cloud.edit", "cloud.delete", "cloud.keySaved", "cloud.keyMissing",
        "cloud.name", "cloud.name.example", "cloud.speech.name.example",
        "cloud.preset.qwenAsrHint", "cloud.preset.qwenAsr",
        "cloud.form.add", "cloud.form.edit", "cloud.form.save", "cloud.form.create", "cloud.form.cancel",
        "cloud.error.nameMissing", "cloud.error.nameTaken", "cloud.error.urlInvalid", "cloud.error.modelMissing", "cloud.error.keyMissing",
        "cloud.delete.title", "cloud.delete.body", "cloud.delete.steps", "cloud.delete.keep", "cloud.delete.confirm",
        "consent.speech", "consent.translate", "consent.clean",
        "consent.body", "consent.switchBody", "consent.speechBody", "consent.speechSwitchBody",
        "consent.keepLocal", "consent.keepCurrent", "consent.allow",
        "recent", "recent.desc", "recent.clear",
        "models.hw.title", "models.hw.matched", "models.hw.preparing", "models.hw.gaugeAria",
        "models.speech.localOnly", "models.group.cloud.title", "models.group.cloud.speechTitle", "models.group.local.title",
        "models.cloud.empty", "models.cloud.speechEmpty", "models.cloud.add", "models.cloud.badge",
        "models.cloud.footprint", "models.cloud.speechFootprint", "models.cloud.incomplete", "models.cloud.useLocal",
        "models.tabs.aria", "models.dl.title",
        "models.storage.title", "models.storage.desc", "models.storage.total", "models.storage.path",
        "models.storage.listTitle", "models.storage.empty",
        "models.metric.quality", "models.metric.speed",
        "models.state.activeInstalled", "models.state.selected", "models.state.activating", "models.state.installed",
        "models.action.enable", "models.action.cancel", "models.action.delete", "models.action.retry",
        "about.subtitle",
        "legacy", "legacy.remove",
        "version", "version.desc",
        "update", "update.desc", "update.check", "update.restart", "update.download",
        "diag", "diag.desc", "diag.open", "diag.title"
    ];

    /// <summary>The interface language, for the page's lang attribute.</summary>
    public string Locale => loc.CurrentCulture.Name;

    public string Title => loc.T("settings.title");

    public IReadOnlyDictionary<string, string> WebStrings()
    {
        var strings = Keys.ToDictionary(key => key, key => loc.T("settings." + key));
        strings["appName"] = loc.T("app.name");
        strings["closeAria"] = loc.T("composer.close");
        return strings;
    }

    /// <summary>A label by its key without the "settings." prefix.</summary>
    public string Label(string key) => loc.T("settings." + key);

    public string OnOff(bool on) => loc.T(on ? "settings.diag.on" : "settings.diag.off");

    public string Stored(bool stored) => loc.T(stored ? "settings.diag.stored" : "settings.diag.notStored");

    /// <summary>What the old models still on disk are and how much room they take.</summary>
    public string LegacyDescription(int count, long bytes) =>
        loc.T("settings.legacy.desc", count, FormatDecimalGb(bytes));

    public string HardwareSummary(HardwareProfile hardware) => hardware.Backend switch
    {
        ComputeBackend.AppleMetal => loc.T("settings.models.hw.metal", hardware.DeviceName, hardware.TotalMemoryGb),
        ComputeBackend.DiscreteGpu => loc.T("settings.models.hw.dgpu", hardware.DeviceName, hardware.TotalMemoryGb, hardware.DedicatedVideoMemoryGb ?? 0),
        _ => loc.T("settings.models.hw.cpu", hardware.DeviceName, hardware.TotalMemoryGb)
    };

    public string BackendDiagnostic(HardwareProfile hardware) => hardware.Backend switch
    {
        ComputeBackend.AppleMetal => loc.T("settings.diag.backend.metal", hardware.TotalMemoryGb),
        ComputeBackend.DiscreteGpu => loc.T("settings.diag.backend.dgpu", hardware.DedicatedVideoMemoryGb ?? hardware.TotalMemoryGb),
        _ => loc.T("settings.diag.backend.cpu", hardware.TotalMemoryGb)
    };

    public string ResidencySummary(ResidencyAssessment assessment) =>
        loc.T("settings.models.hw.residency", FormatGib(assessment.ActiveCoResidentBytes), FormatGib(assessment.SafeBudgetBytes));

    public string ResidencyMode(ResidencyAssessment assessment) =>
        loc.T(assessment.CanCoResideBothLlms ? "settings.models.hw.mode.dual" : "settings.models.hw.mode.swap");

    public string DiskUsedSummary(long bytes) =>
        bytes > 0 ? loc.T("settings.models.hw.disk.some", FormatSize(bytes)) : loc.T("settings.models.hw.disk.none");

    public string RecommendActionLabel(long missingBytes) =>
        missingBytes > 0 ? loc.T("settings.models.hw.downloadApply", FormatSize(missingBytes)) : loc.T("settings.models.hw.apply");

    public string StageTabTitle(ModelStage stage) => loc.T($"settings.models.tab.{StageKey(stage)}");

    public string StorageTabTitle => loc.T("settings.models.tab.storage");

    public string CategoryBadge(ModelStage? stage) => stage is { } s
        ? StageTabTitle(s)
        : loc.T("settings.models.tab.shared");

    public string StageTabSummary(
        ModelStage stage,
        ModelDescriptor activeModel,
        bool installed,
        ModelDownloadState? activeStageDownload,
        bool queued,
        bool cleanupEnabled,
        bool cloudRouted,
        string? endpointName)
    {
        if (activeStageDownload is { IsDownloading: true } dl)
        {
            return queued || (dl.BytesDownloaded == 0 && dl.Percentage < 0.5)
                ? loc.T("settings.models.tab.summary.queued")
                : loc.T("settings.models.tab.summary.downloading", (int)Math.Round(dl.Percentage));
        }

        if (stage == ModelStage.Cleaner && !cleanupEnabled)
            return loc.T("settings.models.tab.summary.off");
        if (cloudRouted && !string.IsNullOrEmpty(endpointName))
            return loc.T("settings.models.tab.summary.cloud", endpointName);

        return installed
            ? activeModel.ShortName
            : loc.T("settings.models.tab.summary.pending", activeModel.ShortName);
    }

    public string StorageTabSummary(int itemCount, long totalBytes) =>
        itemCount > 0 && totalBytes > 0
            ? loc.T("settings.models.tab.summary.storage", itemCount, FormatSize(totalBytes))
            : loc.T("settings.models.tab.summary.storageEmpty");

    public string StorageBreakdown(int itemCount, long inUseBytes, long reclaimableBytes) =>
        itemCount == 0
            ? loc.T("settings.models.storage.breakdown.empty")
            : reclaimableBytes > 0
                ? loc.T("settings.models.storage.breakdown.reclaimable", FormatSize(inUseBytes), FormatSize(reclaimableBytes))
                : loc.T("settings.models.storage.breakdown.allActive", itemCount);

    public string StorageStatusBadge(bool inUse) =>
        loc.T(inUse ? "settings.models.state.activeInstalled" : "settings.models.state.idle");

    public string DeleteWithSizeLabel(long sizeBytes) =>
        loc.T("settings.models.action.deleteSize", FormatSize(sizeBytes));

    public string? NavDownloadBadge(int? activePercent, bool anyQueuedOrConnecting) =>
        activePercent is { } pct
            ? loc.T("settings.models.nav.downloading", pct)
            : anyQueuedOrConnecting
                ? loc.T("settings.models.nav.queued")
                : null;

    public string DownloadProgressLabel(ModelDownloadState state, long fallbackTotalBytes, bool queued)
    {
        var total = state.TotalBytes > 0 ? state.TotalBytes : fallbackTotalBytes;
        if (queued)
            return loc.T("settings.models.dl.queued", FormatSize(total));

        var downloaded = state.BytesDownloaded > 0
            ? state.BytesDownloaded
            : (long)Math.Round(total * Math.Clamp(state.Percentage, 0, 100) / 100d);

        if (downloaded <= 0 && state.Percentage < 0.5)
            return loc.T("settings.models.dl.connecting", FormatSize(total));
        if (total > 0 && downloaded >= total && state.Percentage >= 99.5)
            return loc.T("settings.models.dl.verifying", FormatSize(total), FormatSize(total));

        return loc.T(
            "settings.models.dl.progress",
            FormatSize(downloaded),
            FormatSize(total),
            (int)Math.Round(state.Percentage));
    }

    public string StageTitle(ModelStage stage) => loc.T($"settings.models.stage.{StageKey(stage)}.title");

    public string StageDescription(ModelStage stage) => loc.T($"settings.models.stage.{StageKey(stage)}.desc");

    public string? StageNotice(
        ModelStage stage,
        bool cleanupEnabled,
        bool cloudSpeech = false,
        string? speechModel = null,
        string? cleanerModelId = null,
        bool cleanInCloud = false) => stage switch
    {
        ModelStage.Cleaner when !cleanupEnabled => loc.T("settings.models.notice.cleanupOff"),
        ModelStage.Cleaner when !cleanInCloud && string.Equals(cleanerModelId, ModelCatalog.CleanerFast.Id, StringComparison.Ordinal) =>
            loc.T("settings.models.notice.cleaner2bQuality"),
        ModelStage.Cleaner when cloudSpeech && SpeechCloudRoute.SupportsNativePolishForModel(speechModel) => loc.T("settings.models.notice.speechNativePolish"),
        _ => null
    };

    public string PresetDisplayName(LiveLingo.Core.Cleanup.PolishPreset preset) =>
        preset.BuiltIn ? loc.T($"composer.polish.{preset.Id}.name") : preset.Name;

    public string PresetDescription(LiveLingo.Core.Cleanup.PolishPreset preset) =>
        preset.BuiltIn ? loc.T($"composer.polish.{preset.Id}.desc") : preset.Description;

    public string PresetToggleAria(string name) => loc.T("settings.presets.toggleAria", name);

    public string PresetEditTitle(string name) => loc.T("settings.presets.form.edit", name);

    public string AppModeAria(string appName) => loc.T("settings.apps.modeAria", appName);

    public string StarterTemplateName(string id) => loc.T($"settings.presets.starter.{id}.name");

    public string StarterTemplateDesc(string id) => loc.T($"settings.presets.starter.{id}.desc");

    public string? PeerStageBadge(
        ModelStage currentStage,
        bool activeHere,
        bool usedForClean,
        bool usedForTranslate,
        bool usedForSpeech = false)
    {
        var peers = new List<string>(2);
        if (currentStage != ModelStage.Speech && usedForSpeech)
            peers.Add(loc.T("settings.models.tab.speech"));
        if (currentStage != ModelStage.Cleaner && usedForClean)
            peers.Add(loc.T("settings.output.clean"));
        if (currentStage != ModelStage.Translator && usedForTranslate)
            peers.Add(loc.T("settings.output.translate"));
        if (peers.Count == 0)
            return null;

        var joined = string.Join(loc.T("settings.cloud.stepJoin"), peers);
        return loc.T(activeHere ? "settings.cloud.alsoUsedBy" : "settings.cloud.usedBy", joined);
    }

    public string ModelsPrivacyStatus(bool cloudClean, bool cloudTranslate, bool cloudSpeech = false)
    {
        var steps = AffectedCloudSteps(cloudClean, cloudTranslate, cloudSpeech);
        return steps is null
            ? loc.T("settings.models.privacy.allLocal")
            : loc.T(cloudSpeech ? "settings.models.privacy.cloudSpeech" : "settings.models.privacy.cloud", steps);
    }

    public string? AffectedCloudSteps(bool usedForClean, bool usedForTranslate, bool usedForSpeech = false)
    {
        var list = new List<string>(3);
        if (usedForSpeech)
            list.Add(loc.T("settings.cloud.speech"));
        if (usedForClean)
            list.Add(loc.T("settings.cloud.clean"));
        if (usedForTranslate)
            list.Add(loc.T("settings.cloud.translate"));
        return list.Count == 0 ? null : string.Join(loc.T("settings.cloud.stepJoin"), list);
    }

    public string CatalogArtifactName(ModelDescriptor descriptor) => descriptor.Profile is not null
        ? OptionName(descriptor)
        : descriptor.Type == ModelType.Runtime
            ? loc.T("settings.models.shared.runtime.name")
            : loc.T($"settings.models.shared.{descriptor.Id}.name");

    public string OptionName(ModelDescriptor option) => loc.T($"settings.models.opt.{option.Id}.name");

    public string OptionDescription(ModelDescriptor option) => loc.T($"settings.models.opt.{option.Id}.desc");

    public string OptionFootprint(ModelDescriptor option) =>
        loc.T("settings.models.footprint", FormatSize(option.SizeBytes), FormatGib(option.Profile!.ResidentBytes));

    public string FitBadge(ModelFit fit) => fit switch
    {
        ModelFit.Recommended => loc.T("settings.models.fit.recommended"),
        ModelFit.Supported => loc.T("settings.models.fit.supported"),
        ModelFit.Heavy => loc.T("settings.models.fit.heavy"),
        ModelFit.Unsupported => loc.T("settings.models.fit.unsupported"),
        _ => throw new ArgumentOutOfRangeException(nameof(fit))
    };

    public string? FitDetail(ModelEvaluation evaluation, HardwareProfile hardware) => evaluation.Fit switch
    {
        ModelFit.Unsupported => evaluation.UnsupportedReason switch
        {
            ModelUnsupportedReason.InsufficientMemory => loc.T("settings.models.reason.ram", evaluation.RequiredMemoryGb ?? 16, hardware.TotalMemoryGb),
            ModelUnsupportedReason.InsufficientVideoMemory => loc.T("settings.models.reason.vram", evaluation.RequiredMemoryGb ?? 8),
            ModelUnsupportedReason.RequiresGpuAcceleration => loc.T("settings.models.reason.gpu"),
            _ => null
        },
        ModelFit.Heavy => hardware.Backend == ComputeBackend.CpuOnly
            ? loc.T("settings.models.heavy.cpu")
            : loc.T("settings.models.heavy.swap"),
        _ => null
    };

    public string DownloadActionLabel(ModelDescriptor option, bool active, bool failed)
    {
        if (failed)
            return loc.T("settings.models.action.retry");
        var size = FormatSize(option.SizeBytes);
        return loc.T(active ? "settings.models.action.download" : "settings.models.action.downloadEnable", size);
    }

    public string? DownloadError(ModelDownloadState state) => !state.IsFailed
        ? null
        : state.ErrorMessage == ModelDownloadErrorCodes.HuggingFaceAuthorization
            ? loc.T("settings.models.error.auth")
            : loc.T("settings.models.error.failed", state.ErrorMessage ?? "");

    public string FormatSize(long bytes) => bytes switch
    {
        >= 1_000_000_000 => $"{(bytes / 1_000_000_000d).ToString("0.00", CultureInfo.InvariantCulture)} GB",
        >= 1_000_000 => $"{Math.Max(1, Math.Round(bytes / 1_000_000d)).ToString("0", CultureInfo.InvariantCulture)} MB",
        > 0 => $"{Math.Max(1, Math.Round(bytes / 1_000d)).ToString("0", CultureInfo.InvariantCulture)} KB",
        _ => "0 MB"
    };

    private static string StageKey(ModelStage stage) => stage switch
    {
        ModelStage.Speech => "speech",
        ModelStage.Cleaner => "cleaner",
        ModelStage.Translator => "translator",
        _ => throw new ArgumentOutOfRangeException(nameof(stage))
    };

    private static string FormatDecimalGb(long bytes) =>
        Math.Max(0.1, bytes / 1_000_000_000d).ToString("0.0", CultureInfo.InvariantCulture);

    private static string FormatGib(long bytes) =>
        Math.Max(0.1, bytes / (1024d * 1024 * 1024)).ToString("0.0", CultureInfo.InvariantCulture);

    /// <summary>The application's name; the context without an input box (copy only) has none.</summary>
    public string AppName(string name) => _composer.ContextName(name);

    /// <summary>The remembered choice in the words of the output menu.</summary>
    public string AppSummary(ComposerOutput output, TargetLanguage target, LiveLingo.Core.Cleanup.PolishPreset? preset = null) =>
        _composer.OutputMenu(output, target, preset);

    public string TranslateChoiceLabel(TargetLanguage target) => _composer.OutputMenu(ComposerOutput.Translate, target);

    public string Date(DateTimeOffset when) => when.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>What the update check is doing or found, as one sentence; the user's installed <paramref name="current"/> version names the "latest".</summary>
    public string UpdateStatus(UpdateState state, string current) => state.Phase switch
    {
        UpdatePhase.Off => loc.T("settings.update.off"),
        UpdatePhase.Unsupported => loc.T("settings.update.unsupported"),
        UpdatePhase.Idle => loc.T("settings.update.idle"),
        UpdatePhase.Checking => loc.T("settings.update.checking"),
        UpdatePhase.Downloading => loc.T("settings.update.downloading", state.Version!, state.Percent),
        UpdatePhase.Ready => loc.T("settings.update.ready", state.Version!),
        UpdatePhase.UpToDate => loc.T("settings.update.latest", current, Moment(state.At!.Value)),
        UpdatePhase.Failed => loc.T("settings.update.failed", Moment(state.At!.Value), state.Error!),
        _ => throw new ArgumentOutOfRangeException(nameof(state), state.Phase, "Not an update phase.")
    };

    private static string Moment(DateTimeOffset when) => when.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
}
