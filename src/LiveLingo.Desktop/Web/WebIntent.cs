using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LiveLingo.Core.Inference;
using LiveLingo.Core.LocalHost;
using LiveLingo.Core.Models;
using LiveLingo.Desktop.Composer;
using LiveLingo.Desktop.Settings;

namespace LiveLingo.Desktop.Web;

/// <summary>
/// What the page may ask for. A closed set: anything else (unknown name, missing or extra field, wrong type, out of
/// range, too large) is dropped before it reaches a view model. The page never sends a decision, only an intent.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "name", UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FailSerialization)]
[JsonDerivedType(typeof(ReadyIntent), "ready")]
[JsonDerivedType(typeof(SizeIntent), "size")]
[JsonDerivedType(typeof(DragIntent), "drag")]
[JsonDerivedType(typeof(ResizeIntent), "resize")]
[JsonDerivedType(typeof(ZoomIntent), "zoom")]
[JsonDerivedType(typeof(ClippedIntent), "clipped")]
[JsonDerivedType(typeof(SourceIntent), "source")]
[JsonDerivedType(typeof(MicToggleIntent), "micToggle")]
[JsonDerivedType(typeof(OutputIntent), "output")]
[JsonDerivedType(typeof(PolishModeIntent), "polishMode")]
[JsonDerivedType(typeof(SwapLanguageIntent), "swapLanguage")]
[JsonDerivedType(typeof(RestoreDefaultsIntent), "restoreDefaults")]
[JsonDerivedType(typeof(CommitIntent), "commit")]
[JsonDerivedType(typeof(CopyIntent), "copy")]
[JsonDerivedType(typeof(CancelIntent), "cancel")]
[JsonDerivedType(typeof(HideIntent), "hide")]
[JsonDerivedType(typeof(RetryIntent), "retry")]
[JsonDerivedType(typeof(EditIntent), "edit")]
[JsonDerivedType(typeof(EditDocumentIntent), "editDocument")]
[JsonDerivedType(typeof(RetryDocumentIntent), "retryDocument")]
[JsonDerivedType(typeof(AcceptPreviewIntent), "acceptPreview")]
[JsonDerivedType(typeof(EditTextIntent), "editText")]
[JsonDerivedType(typeof(SaveEditIntent), "saveEdit")]
[JsonDerivedType(typeof(SaveEditAndRememberIntent), "saveEditAndRemember")]
[JsonDerivedType(typeof(CancelEditIntent), "cancelEdit")]
[JsonDerivedType(typeof(UseOriginalIntent), "useOriginal")]
[JsonDerivedType(typeof(NewDraftIntent), "newDraft")]
[JsonDerivedType(typeof(RestoreRecentIntent), "restoreRecent")]
[JsonDerivedType(typeof(ClearRecentIntent), "clearRecent")]
[JsonDerivedType(typeof(ImportIntent), "import")]
[JsonDerivedType(typeof(ExportIntent), "export")]
[JsonDerivedType(typeof(WideIntent), "wide")]
[JsonDerivedType(typeof(WholeIntent), "whole")]
[JsonDerivedType(typeof(CopyOnlyIntent), "copyOnly")]
[JsonDerivedType(typeof(BannerIntent), "banner")]
[JsonDerivedType(typeof(OpenSettingsIntent), "openSettings")]
[JsonDerivedType(typeof(SettingIntent), "setting")]
[JsonDerivedType(typeof(AddTermIntent), "addTerm")]
[JsonDerivedType(typeof(RemoveTermIntent), "removeTerm")]
[JsonDerivedType(typeof(AddCorrectionIntent), "addCorrection")]
[JsonDerivedType(typeof(RemoveCorrectionIntent), "removeCorrection")]
[JsonDerivedType(typeof(SelectCloudModelIntent), "selectCloudModel")]
[JsonDerivedType(typeof(UseLocalStageIntent), "useLocalStage")]
[JsonDerivedType(typeof(DisableAllCloudIntent), "disableAllCloud")]
[JsonDerivedType(typeof(ConsentIntent), "consent")]
[JsonDerivedType(typeof(OpenEndpointFormIntent), "openEndpointForm")]
[JsonDerivedType(typeof(CloseEndpointFormIntent), "closeEndpointForm")]
[JsonDerivedType(typeof(SaveEndpointIntent), "saveEndpoint")]
[JsonDerivedType(typeof(DeleteEndpointIntent), "deleteEndpoint")]
[JsonDerivedType(typeof(EndpointAnswerIntent), "endpointAnswer")]
[JsonDerivedType(typeof(SelectModelIntent), "selectModel")]
[JsonDerivedType(typeof(DownloadModelIntent), "downloadModel")]
[JsonDerivedType(typeof(CancelModelDownloadIntent), "cancelModelDownload")]
[JsonDerivedType(typeof(DeleteModelIntent), "deleteModel")]
[JsonDerivedType(typeof(ApplyRecommendedModelsIntent), "applyRecommendedModels")]
[JsonDerivedType(typeof(RemoveLegacyModelsIntent), "removeLegacyModels")]
[JsonDerivedType(typeof(CheckUpdatesIntent), "checkUpdates")]
[JsonDerivedType(typeof(RestartToUpdateIntent), "restartToUpdate")]
[JsonDerivedType(typeof(OpenDownloadsIntent), "openDownloads")]
[JsonDerivedType(typeof(ForgetAppIntent), "forgetApp")]
[JsonDerivedType(typeof(ClearAppsIntent), "clearApps")]
[JsonDerivedType(typeof(SetAppChoiceIntent), "setAppChoice")]
[JsonDerivedType(typeof(OpenPresetFormIntent), "openPresetForm")]
[JsonDerivedType(typeof(ClosePresetFormIntent), "closePresetForm")]
[JsonDerivedType(typeof(SavePresetIntent), "savePreset")]
[JsonDerivedType(typeof(ResetPresetIntent), "resetPreset")]
[JsonDerivedType(typeof(TogglePresetIntent), "togglePreset")]
[JsonDerivedType(typeof(DeletePresetIntent), "deletePreset")]
[JsonDerivedType(typeof(PresetAnswerIntent), "presetAnswer")]
[JsonDerivedType(typeof(RefreshLocalHostsIntent), "refreshLocalHosts")]
[JsonDerivedType(typeof(SelectLocalHostModelIntent), "selectLocalHostModel")]
[JsonDerivedType(typeof(ApplyHotLocalHostModelIntent), "applyHotLocalHostModel")]
[JsonDerivedType(typeof(DownloadLocalHostModelIntent), "downloadLocalHostModel")]
[JsonDerivedType(typeof(StartLocalHostServerIntent), "startLocalHostServer")]
[JsonDerivedType(typeof(OpenLocalHostAppIntent), "openLocalHostApp")]
[JsonDerivedType(typeof(SaveLocalHostConfigIntent), "saveLocalHostConfig")]
[JsonDerivedType(typeof(AddCustomLocalHostIntent), "addCustomLocalHost")]
[JsonDerivedType(typeof(RemoveCustomLocalHostIntent), "removeCustomLocalHost")]
internal abstract record WebIntent
{
    public const int Version = 1;

    /// <summary>A message may carry a whole pasted document; nothing legitimate is larger.</summary>
    public const int MaxBytes = 1 << 20;

    private static readonly string[] SettingsSections = ["general", "voice", "polish", "glossary", "models", "about"];
    private static readonly string[] ModelsTabs = ["speech", "cleaner", "translator", "storage"];

    public required int V { get; init; }

    protected virtual bool IsValid => true;

    protected static bool IsIndex(int value) => value is >= 0 and < 10_000;

    protected static bool IsExtent(int value) => value is >= 1 and <= 4_000;

    protected static bool IsSection(string value) => SettingsSections.Contains(value);

    protected static bool IsModelsTab(string value) => ModelsTabs.Contains(value);

    protected static bool IsEndpointId(string value) => value.Length is >= 1 and <= 64;

    protected static bool IsPresetId(string value) =>
        value.Length is >= 1 and <= 64 && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    protected static bool IsHostId(string value) =>
        value.Length is >= 1 and <= 64 && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or ':');

    protected static bool IsSelectableModelId(string value) => ModelCatalog.FindSelectable(value) is not null;

    protected static bool IsCatalogModelId(string value) =>
        value.Length is >= 1 and <= 64 && ModelCatalog.FindAny(value) is not null;

    /// <summary>
    /// Returns the intent, or null with a <paramref name="reason"/> that is safe to log: it names the kind of failure
    /// and never echoes anything from the message.
    /// </summary>
    public static WebIntent? Parse(string json, out string reason)
    {
        reason = "";
        if (json.Length > MaxBytes || Encoding.UTF8.GetByteCount(json) > MaxBytes)
        {
            reason = "too large";
            return null;
        }

        WebIntent? intent;
        try
        {
            intent = JsonSerializer.Deserialize<WebIntent>(json, WebJson.Wire);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            // A missing discriminator is NotSupportedException; both mean "not a message of the closed set".
            reason = nameof(JsonException);
            return null;
        }

        if (intent is null)
        {
            reason = "invalid";
            return null;
        }

        if (intent.V != Version)
        {
            reason = "version";
            return null;
        }

        if (!intent.IsValid)
        {
            reason = "invalid";
            return null;
        }

        return intent;
    }
}

internal sealed record ReadyIntent : WebIntent;

/// <summary>The page content size in CSS pixels; the window follows it.</summary>
internal sealed record SizeIntent(int Width, int Height) : WebIntent
{
    protected override bool IsValid => IsExtent(Width) && IsExtent(Height);
}

/// <summary>
/// The user started dragging the composer by its header. The page cannot move a native window, and the pointer's
/// screen coordinates are not reliable across web engines, so the page only says the drag began (after a small
/// threshold, while the button is held) and the system moves the window.
/// </summary>
internal sealed record DragIntent : WebIntent;

/// <summary>
/// The user is dragging an edge or the corner of the composer. It carries the window size the pointer asks for, in CSS
/// pixels including the shadow margin; a dimension the dragged edge does not change is null. The window decides what it
/// can be (the screen's working area, the smallest usable size).
/// </summary>
internal sealed record ResizeIntent(int? Width, int? Height) : WebIntent
{
    protected override bool IsValid =>
        (Width is not null || Height is not null)
        && (Width is null || IsExtent(Width.Value))
        && (Height is null || IsExtent(Height.Value));
}

/// <summary>Toggles the window between the size that follows its content and a large one that shows as much text as the screen allows.</summary>
internal sealed record ZoomIntent : WebIntent;

/// <summary>
/// A diagnostic, not an action: the page's visible area stayed smaller than the page's content (it is cut off).
/// Sizes in CSS pixels. The host only logs it, with the window's own geometry next to it.
/// </summary>
internal sealed record ClippedIntent(int ViewportWidth, int ViewportHeight, int ContentWidth, int ContentHeight) : WebIntent
{
    protected override bool IsValid =>
        ViewportWidth is >= 0 and <= 100_000 && ViewportHeight is >= 0 and <= 100_000
        && ContentWidth is >= 0 and <= 100_000 && ContentHeight is >= 0 and <= 100_000;
}

/// <summary>The source text and the page's own revision of it (design 5.3).</summary>
internal sealed record SourceIntent(string Text, int Rev) : WebIntent
{
    protected override bool IsValid => Rev >= 0;
}

/// <summary>The microphone button or its shortcut: starts or stops listening.</summary>
internal sealed record MicToggleIntent : WebIntent;

internal sealed record OutputIntent(ComposerOutput Value) : WebIntent;

internal sealed record PolishModeIntent(string Id) : WebIntent
{
    protected override bool IsValid => IsPresetId(Id);
}

internal sealed record SwapLanguageIntent : WebIntent;

/// <summary>"Restore defaults" of the remembered-choice note: back to the settings' output and direction, and forget the memory.</summary>
internal sealed record RestoreDefaultsIntent : WebIntent;

internal sealed record CommitIntent(bool FromKey) : WebIntent;

internal sealed record CopyIntent : WebIntent;

internal sealed record CancelIntent : WebIntent;

internal sealed record HideIntent : WebIntent;

internal sealed record RetryIntent(int I) : WebIntent
{
    protected override bool IsValid => IsIndex(I);
}

internal sealed record EditIntent(int I) : WebIntent
{
    protected override bool IsValid => IsIndex(I);
}

internal sealed record EditTextIntent(string Text) : WebIntent;

internal sealed record EditDocumentIntent : WebIntent;

internal sealed record RetryDocumentIntent : WebIntent;

internal sealed record AcceptPreviewIntent : WebIntent;

internal sealed record SaveEditIntent : WebIntent;

internal sealed record SaveEditAndRememberIntent : WebIntent;

internal sealed record CancelEditIntent : WebIntent;

internal sealed record UseOriginalIntent(int I) : WebIntent
{
    protected override bool IsValid => IsIndex(I);
}

internal sealed record NewDraftIntent : WebIntent;

internal sealed record RestoreRecentIntent(int Index) : WebIntent
{
    protected override bool IsValid => Index >= 0 && Index < RecentDrafts.Capacity;
}

internal sealed record ClearRecentIntent : WebIntent;

internal sealed record ImportIntent : WebIntent;

internal sealed record ExportIntent(ExportFormat Format) : WebIntent;

internal sealed record WideIntent(bool On) : WebIntent;

internal sealed record WholeIntent(bool On) : WebIntent;

internal sealed record CopyOnlyIntent(bool On) : WebIntent;

internal sealed record BannerIntent(BannerAction Action) : WebIntent;

internal sealed record OpenSettingsIntent(string Section, string? Tab = null) : WebIntent
{
    protected override bool IsValid => IsSection(Section) && (Tab is null || (Section == "models" && IsModelsTab(Tab)));
}

/// <summary>One value of the settings changed. Unknown keys, and values outside a closed choice, are dropped here.</summary>
internal sealed record SettingIntent(string Key, string Value) : WebIntent
{
    public static class Names
    {
        public const string Hotkey = "hotkey";
        public const string CommitKey = "commitKey";
        public const string Language = "language";
        public const string Theme = "theme";
        public const string ReduceMotion = "reduceMotion";
        public const string Microphone = "microphone";
        public const string Direction = "direction";
        public const string Cleanup = "cleanup";
        public const string DefaultOutput = "defaultOutput";
        public const string AutoDictate = "autoDictate";
        public const string PreloadSpeech = "preloadSpeech";
        public const string BuiltInGlossary = "builtInGlossary";
        public const string RememberChoices = "rememberChoices";
        public const string DefaultPolishMode = "defaultPolishMode";
        public const string SmartHostPolish = "smartHostPolish";
        public const string PolishPrompt = "polishPrompt";
    }

    protected override bool IsValid => Key switch
    {
        // Free text is bounded here; whether it is a usable hotkey is the view model's call.
        Names.Hotkey => Value.Length <= 64,
        Names.Microphone => Value.Length <= 256,
        // Bounded only; whether the text is a usable set of requirements is the view model's call (it flags it).
        Names.PolishPrompt => Value.Length <= 4_000,
        Names.DefaultPolishMode => IsPresetId(Value),
        Names.CommitKey => SettingsViewModel.CommitKeys.Contains(Value),
        Names.Language => SettingsViewModel.Languages.Contains(Value),
        Names.Theme => SettingsViewModel.Themes.Contains(Value),
        Names.Direction => SettingsViewModel.Targets.Contains(Value),
        Names.DefaultOutput => SettingsViewModel.DefaultOutputs.Contains(Value),
        Names.ReduceMotion or Names.Cleanup or Names.AutoDictate or Names.PreloadSpeech or Names.BuiltInGlossary
            or Names.RememberChoices or Names.SmartHostPolish => Value is "true" or "false",
        _ => false
    };
}

internal sealed record AddTermIntent(string Source, string Target) : WebIntent
{
    protected override bool IsValid => Source.Length <= 200 && Target.Length <= 200;
}

internal sealed record RemoveTermIntent(int Index) : WebIntent
{
    protected override bool IsValid => IsIndex(Index);
}

internal sealed record AddCorrectionIntent(string Spoken, string Canonical) : WebIntent
{
    protected override bool IsValid => Spoken.Length <= 200 && Canonical.Length <= 200;
}

internal sealed record RemoveCorrectionIntent(int Index) : WebIntent
{
    protected override bool IsValid => IsIndex(Index);
}

internal sealed record SelectCloudModelIntent(CloudStep Step, string EndpointId) : WebIntent
{
    protected override bool IsValid => IsEndpointId(EndpointId);
}

internal sealed record UseLocalStageIntent(CloudStep Step) : WebIntent;

internal sealed record DisableAllCloudIntent : WebIntent;

internal sealed record ConsentIntent(bool Allow) : WebIntent;

/// <summary>Opens the form that adds an endpoint (no id) or edits one.</summary>
internal sealed record OpenEndpointFormIntent(string? Id) : WebIntent
{
    protected override bool IsValid => Id is null || IsEndpointId(Id);
}

internal sealed record CloseEndpointFormIntent : WebIntent;

/// <summary>
/// What the user filled in. The API key crosses once, here, and is never part of a snapshot or a log line; an empty key
/// keeps the stored one of an existing endpoint. Whether the values are usable is the view model's call.
/// </summary>
internal sealed record SaveEndpointIntent(string Label, string BaseUrl, string Model, string ApiKey) : WebIntent
{
    protected override bool IsValid => Label.Length <= 40 && BaseUrl.Length <= 2_048 && Model.Length <= 256 && ApiKey.Length <= 4_096;

    public override string ToString() => nameof(SaveEndpointIntent);
}

internal sealed record DeleteEndpointIntent(string Id) : WebIntent
{
    protected override bool IsValid => IsEndpointId(Id);
}

/// <summary>The answer to an endpoint deletion that was asked about.</summary>
internal sealed record EndpointAnswerIntent(bool Allow) : WebIntent;

internal sealed record SelectModelIntent(string ModelId) : WebIntent
{
    protected override bool IsValid => IsSelectableModelId(ModelId);
}

internal sealed record DownloadModelIntent(string ModelId) : WebIntent
{
    protected override bool IsValid => IsCatalogModelId(ModelId);
}

internal sealed record CancelModelDownloadIntent(string ModelId) : WebIntent
{
    protected override bool IsValid => IsCatalogModelId(ModelId);
}

internal sealed record DeleteModelIntent(string ModelId) : WebIntent
{
    protected override bool IsValid => IsSelectableModelId(ModelId) || ModelCatalog.IsSharedStageArtifact(ModelId);
}

internal sealed record ApplyRecommendedModelsIntent : WebIntent;

internal sealed record RemoveLegacyModelsIntent : WebIntent;

/// <summary>"Check for updates" of the settings page.</summary>
internal sealed record CheckUpdatesIntent : WebIntent;

/// <summary>"Restart to update": installs the downloaded version and starts it.</summary>
internal sealed record RestartToUpdateIntent : WebIntent;

/// <summary>Opens the page the installers of the latest version are downloaded from.</summary>
internal sealed record OpenDownloadsIntent : WebIntent;

/// <summary>Forgets what was remembered for one application.</summary>
internal sealed record ForgetAppIntent(string AppId) : WebIntent
{
    protected override bool IsValid => AppId.Length is >= 1 and <= 512;
}

internal sealed record ClearAppsIntent : WebIntent;

internal sealed record SetAppChoiceIntent(string AppId, string AppName, string Choice) : WebIntent
{
    protected override bool IsValid => AppId.Length is >= 1 and <= 512 && AppName.Length <= 128 && Choice.Length is >= 1 and <= 80;
}

internal sealed record OpenPresetFormIntent(string? Id) : WebIntent
{
    protected override bool IsValid => Id is null || IsPresetId(Id);
}

internal sealed record ClosePresetFormIntent : WebIntent;

internal sealed record SavePresetIntent(string Label, string Description, string Gate, string Instructions) : WebIntent
{
    protected override bool IsValid =>
        Label.Length <= 80 && Description.Length <= 120
        && Gate is "FactAnchor" or "Verbatim"
        && Instructions.Length <= 4_000;
}

internal sealed record ResetPresetIntent(string Id) : WebIntent
{
    protected override bool IsValid => IsPresetId(Id);
}

internal sealed record TogglePresetIntent(string Id, bool Enabled) : WebIntent
{
    protected override bool IsValid => IsPresetId(Id);
}

internal sealed record DeletePresetIntent(string Id) : WebIntent
{
    protected override bool IsValid => IsPresetId(Id);
}

internal sealed record PresetAnswerIntent(bool Allow) : WebIntent;

internal sealed record RefreshLocalHostsIntent : WebIntent;

internal sealed record SelectLocalHostModelIntent(ModelStage Stage, string HostId, string ModelKey) : WebIntent
{
    protected override bool IsValid =>
        Stage is ModelStage.Speech or ModelStage.Cleaner or ModelStage.Translator
        && IsHostId(HostId)
        && ModelKey.Length is >= 1 and <= 256;
}

internal sealed record ApplyHotLocalHostModelIntent(string HostId, string ModelKey) : WebIntent
{
    protected override bool IsValid => IsHostId(HostId) && ModelKey.Length is >= 1 and <= 256;
}

internal sealed record DownloadLocalHostModelIntent(string HostId, ModelStage Stage, string CatalogModelId) : WebIntent
{
    protected override bool IsValid =>
        IsHostId(HostId)
        && Stage is ModelStage.Cleaner or ModelStage.Translator
        && IsSelectableModelId(CatalogModelId);
}

internal sealed record StartLocalHostServerIntent(string HostId) : WebIntent
{
    protected override bool IsValid => IsHostId(HostId);
}

internal sealed record OpenLocalHostAppIntent(string HostId) : WebIntent
{
    protected override bool IsValid => IsHostId(HostId);
}

internal sealed record SaveLocalHostConfigIntent(string HostId, string BaseUrl, string ApiToken, bool ClearToken) : WebIntent
{
    protected override bool IsValid =>
        IsHostId(HostId)
        && BaseUrl.Length <= 256
        && (BaseUrl.Length == 0 || LocalHostRoute.TryParseLoopbackUri(BaseUrl, out _))
        && ApiToken.Length <= 4_096;

    public override string ToString() => nameof(SaveLocalHostConfigIntent);
}

internal sealed record AddCustomLocalHostIntent(string Label, string BaseUrl) : WebIntent
{
    protected override bool IsValid => Label.Length <= 40 && LocalHostRoute.TryParseLoopbackUri(BaseUrl, out _);
}

internal sealed record RemoveCustomLocalHostIntent(string HostId) : WebIntent
{
    protected override bool IsValid => IsHostId(HostId);
}
