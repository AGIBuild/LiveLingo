namespace LiveLingo.Core;

public enum TranslationRoutingMode
{
    LocalOnly,
    PreferLocal,
    PreferCloud,
    CloudOnly
}

public class CoreOptions
{
    public string ModelStoragePath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LiveLingo", "models");

    public string DefaultTargetLanguage { get; set; } = "en";

    /// <summary>
    /// The currently selected model id for translation.
    /// </summary>
    public string? ActiveTranslationModelId { get; set; }

    /// <summary>
    /// Selects whether translation stays local-first or routes through the configured cloud model.
    /// </summary>
    public TranslationRoutingMode TranslationRoutingMode { get; set; } = TranslationRoutingMode.PreferLocal;

    /// <summary>
    /// When true, unsupported local language pairs are routed to the configured cloud model.
    /// </summary>
    public bool RouteUnsupportedLanguagePairsToCloud { get; set; } = true;

    /// <summary>
    /// When true, post-processing uses the configured cloud model instead of the local Qwen model.
    /// </summary>
    public bool RoutePostProcessingToCloud { get; set; }

    /// <summary>
    /// Enables the OpenAI-compatible remote translation provider.
    /// </summary>
    public bool CloudProviderEnabled { get; set; }

    /// <summary>
    /// Base URL for the OpenAI-compatible API, typically ending with /v1.
    /// </summary>
    public string? CloudProviderBaseUrl { get; set; }

    /// <summary>
    /// API key used for the OpenAI-compatible provider.
    /// </summary>
    public string? CloudProviderApiKey { get; set; }

    /// <summary>
    /// Remote model id used for translation requests.
    /// </summary>
    public string? CloudTranslationModelId { get; set; }

    /// <summary>
    /// Optional dedicated remote model id used for post-processing requests.
    /// Falls back to <see cref="CloudTranslationModelId"/> when empty.
    /// </summary>
    public string? CloudPostProcessingModelId { get; set; }

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
