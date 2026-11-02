using System.Text.Json;

namespace Dictation.Audio;

public sealed record SentenceRecord(string Raw, string? Cleaned, string State, string? RejectReason, string? RejectedText);

/// <summary>Everything needed to judge and reproduce one take next to its audio.</summary>
public sealed record TakeRecord(
    string Id,
    DateTime StartedAt,
    int DurationMs,
    string Microphone,
    string SecondPass,
    IReadOnlyList<string> Glossary,
    string LiveText,
    string RawText,
    string FinalText,
    double CommitWaitMs,
    IReadOnlyList<SentenceRecord> Sentences,
    string Reference);

/// <summary>
/// Keeps every take as <c>id.wav</c> + <c>id.json</c> in the user's profile, outside the repository.
/// <c>Reference</c> is what the speaker really said, typed by the user after the take; it turns a take into a test case.
/// </summary>
public sealed class RecordingStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public RecordingStore(string directory)
    {
        Directory = directory;
        System.IO.Directory.CreateDirectory(directory);
    }

    public static string DefaultDirectory =>
        Environment.GetEnvironmentVariable("VOICE_POC_RECORDINGS")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "VoicePoc", "recordings");

    public string Directory { get; }

    public string WavPath(string id) => Path.Combine(Directory, id + ".wav");

    public string JsonPath(string id) => Path.Combine(Directory, id + ".json");

    /// <summary>Opens the audio file of a new take; the id sorts by start time.</summary>
    public (string Id, WavWriter Writer) Begin(DateTime startedAt)
    {
        var id = startedAt.ToString("yyyyMMdd-HHmmss");
        return (id, new WavWriter(WavPath(id)));
    }

    public void Save(TakeRecord take) =>
        File.WriteAllText(JsonPath(take.Id), JsonSerializer.Serialize(take, Json));

    public TakeRecord Load(string id) =>
        JsonSerializer.Deserialize<TakeRecord>(File.ReadAllText(JsonPath(id)), Json)
        ?? throw new InvalidDataException("Empty take file: " + JsonPath(id));

    public void SetReference(string id, string reference) => Save(Load(id) with { Reference = reference });

    /// <summary>Ids of all takes that have both audio and a record, oldest first.</summary>
    public IReadOnlyList<string> Ids() =>
        System.IO.Directory.GetFiles(Directory, "*.json")
            .Select(f => Path.GetFileNameWithoutExtension(f))
            .Where(id => File.Exists(WavPath(id)))
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();
}
