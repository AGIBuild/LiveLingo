namespace LiveLingo.Core;

public class CoreOptions
{
    public string ModelStoragePath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LiveLingo", "models");

    public int InferenceThreads { get; set; }

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
