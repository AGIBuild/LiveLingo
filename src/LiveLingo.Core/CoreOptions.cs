using LiveLingo.Core.Models;

namespace LiveLingo.Core;

public class CoreOptions
{
    public string ModelStoragePath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LiveLingo", "models");

    /// <summary>Where short-lived runtime state (pid records of started servers) is kept.</summary>
    public string RuntimeStatePath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LiveLingo", "run");

    public int InferenceThreads { get; set; }

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
