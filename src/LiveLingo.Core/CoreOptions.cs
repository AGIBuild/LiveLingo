using LiveLingo.Core.Models;

namespace LiveLingo.Core;

public class CoreOptions
{
    public string ModelStoragePath { get; set; } = Path.Combine(UserData.Directory, "models");

    /// <summary>Where short-lived runtime state (pid records of started servers) is kept.</summary>
    public string RuntimeStatePath { get; set; } = Path.Combine(UserData.Directory, "run");

    public int InferenceThreads { get; set; }

    /// <summary>How long the loaded speech models stay in memory after they were last used or prepared.</summary>
    public TimeSpan SpeechIdleTimeout { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>Which translation model to run; the fast tier is the default for 16 GB machines.</summary>
    public TranslatorTier TranslatorTier { get; set; } = TranslatorTier.Fast;

    /// <summary>
    /// Mirror base URL for huggingface.co downloads (e.g. "https://hf-mirror.com").
    /// When set, all huggingface.co URLs are rewritten to use this mirror.
    /// </summary>
    public string? HuggingFaceMirror { get; set; }

    /// <summary>
    /// Optional Hugging Face access token for gated models (sent as Authorization: Bearer).
    /// </summary>
    public string? HuggingFaceToken { get; set; }
}
