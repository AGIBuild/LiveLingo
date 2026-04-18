using LiveLingo.Core.Translation;

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

    /// <summary>
    /// Enables the Ollama local daemon provider. User is responsible for running
    /// Ollama (e.g. <c>ollama serve</c>) and pre-pulling the required model tags.
    /// </summary>
    public bool OllamaEnabled { get; set; }

    /// <summary>
    /// Base URL of the Ollama daemon. Defaults to the standard local endpoint.
    /// </summary>
    public string OllamaBaseUrl { get; set; } = "http://localhost:11434";

    /// <summary>
    /// Ollama model tag used for translation (e.g. <c>gemma3:4b</c>, <c>llama3.2:3b</c>).
    /// User must have pulled this tag via <c>ollama pull</c> beforehand.
    /// </summary>
    public string? OllamaTranslationModelId { get; set; }

    /// <summary>
    /// Optional Ollama model tag for post-processing. Falls back to
    /// <see cref="OllamaTranslationModelId"/> when empty.
    /// </summary>
    public string? OllamaPostProcessingModelId { get; set; }

    public int InferenceThreads { get; set; }

    /// <summary>
    /// User-defined term mappings injected into translation prompts when the
    /// source term is found in the source text.
    /// </summary>
    public IReadOnlyList<GlossaryEntry> Glossary { get; set; } = [];

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
