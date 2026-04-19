namespace LiveLingo.Core.Models;

/// <summary>
/// Model identifiers that LiveLingo used to ship but no longer registers in
/// <see cref="ModelRegistry"/>. On every startup the host sweeps these directories
/// from <c>CoreOptions.ModelStoragePath</c> so an upgrade doesn't strand the old
/// payloads on the user's disk indefinitely.
///
/// Add a new entry here whenever a model is retired from <see cref="ModelRegistry"/>;
/// keep the entries forever — removing one only matters if the model id was reused
/// by a different (incompatible) bundle, which we deliberately avoid.
/// </summary>
public static class ObsoleteModelRegistry
{
    public static IReadOnlyList<string> Ids { get; } =
    [
        // Replaced 2026-04 by SherpaCohereTranscribe14LangInt8 when the STT engine
        // moved from Whisper.net to sherpa-onnx. Old install footprint: ~142 MB.
        "whisper-base"
    ];
}
