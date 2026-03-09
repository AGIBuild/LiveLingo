namespace LiveLingo.Core;

public class CoreOptions
{
    public string ModelStoragePath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LiveLingo", "models");

    public string DefaultTargetLanguage { get; set; } = "en";

    public int InferenceThreads { get; set; }

    /// <summary>
    /// Mirror base URL for huggingface.co downloads (e.g. "https://hf-mirror.com").
    /// When set, all huggingface.co URLs are rewritten to use this mirror.
    /// </summary>
    public string? HuggingFaceMirror { get; set; }
}
