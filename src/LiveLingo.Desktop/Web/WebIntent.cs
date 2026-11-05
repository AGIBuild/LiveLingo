using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
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
[JsonDerivedType(typeof(SwapLanguageIntent), "swapLanguage")]
[JsonDerivedType(typeof(RestoreDefaultsIntent), "restoreDefaults")]
[JsonDerivedType(typeof(CommitIntent), "commit")]
[JsonDerivedType(typeof(CopyIntent), "copy")]
[JsonDerivedType(typeof(CancelIntent), "cancel")]
[JsonDerivedType(typeof(HideIntent), "hide")]
[JsonDerivedType(typeof(RetryIntent), "retry")]
[JsonDerivedType(typeof(EditIntent), "edit")]
[JsonDerivedType(typeof(EditTextIntent), "editText")]
[JsonDerivedType(typeof(SaveEditIntent), "saveEdit")]
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
[JsonDerivedType(typeof(CloudToggleIntent), "cloudToggle")]
[JsonDerivedType(typeof(ConsentIntent), "consent")]
[JsonDerivedType(typeof(OpenEndpointFormIntent), "openEndpointForm")]
[JsonDerivedType(typeof(CloseEndpointFormIntent), "closeEndpointForm")]
[JsonDerivedType(typeof(SaveEndpointIntent), "saveEndpoint")]
[JsonDerivedType(typeof(EnableEndpointIntent), "enableEndpoint")]
[JsonDerivedType(typeof(DeleteEndpointIntent), "deleteEndpoint")]
[JsonDerivedType(typeof(EndpointAnswerIntent), "endpointAnswer")]
[JsonDerivedType(typeof(RemoveLegacyModelsIntent), "removeLegacyModels")]
[JsonDerivedType(typeof(CheckUpdatesIntent), "checkUpdates")]
[JsonDerivedType(typeof(RestartToUpdateIntent), "restartToUpdate")]
[JsonDerivedType(typeof(OpenDownloadsIntent), "openDownloads")]
[JsonDerivedType(typeof(ForgetAppIntent), "forgetApp")]
[JsonDerivedType(typeof(ClearAppsIntent), "clearApps")]
internal abstract record WebIntent
{
    public const int Version = 1;

    /// <summary>A message may carry a whole pasted document; nothing legitimate is larger.</summary>
    public const int MaxBytes = 1 << 20;

    private static readonly string[] SettingsSections = ["general", "voice", "privacy", "models"];

    public required int V { get; init; }

    protected virtual bool IsValid => true;

    protected static bool IsIndex(int value) => value is >= 0 and < 10_000;

    protected static bool IsExtent(int value) => value is >= 1 and <= 4_000;

    protected static bool IsSection(string value) => SettingsSections.Contains(value);

    protected static bool IsEndpointId(string value) => value.Length is >= 1 and <= 64;

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

internal sealed record SaveEditIntent : WebIntent;

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

internal sealed record OpenSettingsIntent(string Section) : WebIntent
{
    protected override bool IsValid => IsSection(Section);
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
        public const string Tier = "tier";
        public const string DefaultOutput = "defaultOutput";
        public const string AutoDictate = "autoDictate";
        public const string PreloadSpeech = "preloadSpeech";
        public const string RememberChoices = "rememberChoices";
        public const string PolishPrompt = "polishPrompt";
    }

    protected override bool IsValid => Key switch
    {
        // Free text is bounded here; whether it is a usable hotkey is the view model's call.
        Names.Hotkey => Value.Length <= 64,
        Names.Microphone => Value.Length <= 256,
        // Bounded only; whether the text is a usable set of requirements is the view model's call (it flags it).
        Names.PolishPrompt => Value.Length <= 4_000,
        Names.CommitKey => SettingsViewModel.CommitKeys.Contains(Value),
        Names.Language => SettingsViewModel.Languages.Contains(Value),
        Names.Theme => SettingsViewModel.Themes.Contains(Value),
        Names.Direction => SettingsViewModel.Targets.Contains(Value),
        Names.Tier => SettingsViewModel.Tiers.Contains(Value),
        Names.DefaultOutput => SettingsViewModel.DefaultOutputs.Contains(Value),
        Names.ReduceMotion or Names.Cleanup or Names.AutoDictate or Names.PreloadSpeech or Names.RememberChoices => Value is "true" or "false",
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

internal sealed record CloudToggleIntent(CloudStep Step, bool On) : WebIntent;

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

internal sealed record EnableEndpointIntent(string Id, bool On) : WebIntent
{
    protected override bool IsValid => IsEndpointId(Id);
}

internal sealed record DeleteEndpointIntent(string Id) : WebIntent
{
    protected override bool IsValid => IsEndpointId(Id);
}

/// <summary>The answer to a switch of the enabled endpoint or a deletion that was asked about.</summary>
internal sealed record EndpointAnswerIntent(bool Allow) : WebIntent;

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
