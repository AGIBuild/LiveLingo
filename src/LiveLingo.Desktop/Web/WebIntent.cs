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
[JsonDerivedType(typeof(SourceIntent), "source")]
[JsonDerivedType(typeof(MicDownIntent), "micDown")]
[JsonDerivedType(typeof(MicUpIntent), "micUp")]
[JsonDerivedType(typeof(MicToggleIntent), "micToggle")]
[JsonDerivedType(typeof(OutputIntent), "output")]
[JsonDerivedType(typeof(SwapLanguageIntent), "swapLanguage")]
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
[JsonDerivedType(typeof(SaveKeyIntent), "saveKey")]
[JsonDerivedType(typeof(ForgetKeyIntent), "forgetKey")]
[JsonDerivedType(typeof(RemoveLegacyModelsIntent), "removeLegacyModels")]
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

/// <summary>The source text and the page's own revision of it (design 5.3).</summary>
internal sealed record SourceIntent(string Text, int Rev) : WebIntent
{
    protected override bool IsValid => Rev >= 0;
}

internal sealed record MicDownIntent : WebIntent;

internal sealed record MicUpIntent : WebIntent;

internal sealed record MicToggleIntent : WebIntent;

internal sealed record OutputIntent(ComposerOutput Value) : WebIntent;

internal sealed record SwapLanguageIntent : WebIntent;

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
        public const string CloudBaseUrl = "cloudBaseUrl";
        public const string CloudModel = "cloudModel";
    }

    protected override bool IsValid => Key switch
    {
        // Free text is bounded here; whether it is a usable hotkey or endpoint is the view model's call.
        Names.Hotkey => Value.Length <= 64,
        Names.Microphone or Names.CloudModel => Value.Length <= 256,
        Names.CloudBaseUrl => Value.Length <= 2_048,
        Names.CommitKey => SettingsViewModel.CommitKeys.Contains(Value),
        Names.Language => SettingsViewModel.Languages.Contains(Value),
        Names.Theme => SettingsViewModel.Themes.Contains(Value),
        Names.Direction => SettingsViewModel.Targets.Contains(Value),
        Names.Tier => SettingsViewModel.Tiers.Contains(Value),
        Names.ReduceMotion or Names.Cleanup => Value is "true" or "false",
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

/// <summary>The API key the user typed. It crosses once, here, and is never part of a snapshot or a log line.</summary>
internal sealed record SaveKeyIntent(string Value) : WebIntent
{
    protected override bool IsValid => Value.Length is >= 1 and <= 4_096;

    public override string ToString() => nameof(SaveKeyIntent);
}

internal sealed record ForgetKeyIntent : WebIntent;

internal sealed record RemoveLegacyModelsIntent : WebIntent;
