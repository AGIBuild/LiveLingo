namespace LiveLingo.Core.Models;

/// <summary>
/// Identifies the chat template / inference quirks of a GGUF model.
/// Used by runtime helpers (think-tag stripping, stop token selection) without
/// hard-coding model family names in calling code.
/// </summary>
public enum LocalModelChatTemplate
{
    /// <summary>Generic OpenAI-compatible chat completions – no special post-processing.</summary>
    Generic,

    /// <summary>Qwen series (Qwen2.x / Qwen3.x): may emit &lt;think&gt;…&lt;/think&gt; blocks.</summary>
    Qwen,

    /// <summary>Google Gemma series (Gemma 2 / Gemma 3 / Gemma 4): uses &lt;start_of_turn&gt; chat format.</summary>
    Gemma,
}

public record ModelDescriptor(
    string Id,
    string DisplayName,
    string DownloadUrl,
    long SizeBytes,
    ModelType Type)
{
    public IReadOnlyList<ModelAsset> Assets { get; init; } = [];

    /// <summary>
    /// When set, runtime may switch to this descriptor if the primary model fails to load (e.g. insufficient RAM).
    /// </summary>
    public ModelDescriptor? LoadFailureFallback { get; init; }

    /// <summary>
    /// Chat template / inference quirks for this GGUF.
    /// Defaults to <see cref="LocalModelChatTemplate.Generic"/> (standard chat completions, no post-processing).
    /// </summary>
    public LocalModelChatTemplate ChatTemplate { get; init; } = LocalModelChatTemplate.Generic;
}

public record ModelAsset(
    string RelativePath,
    string DownloadUrl,
    long SizeBytes);

public enum ModelType
{
    Translation,
    PostProcessing,
    LanguageDetection,
    SpeechToText,
    VoiceActivityDetection
}
