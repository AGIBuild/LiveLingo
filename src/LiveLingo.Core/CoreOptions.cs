using LiveLingo.Core.Models;

namespace LiveLingo.Core;

public class CoreOptions
{
    private ModelSelection _models = ModelSelection.Baseline;

    public string ModelStoragePath { get; set; } = Path.Combine(UserData.Directory, "models");

    /// <summary>Where short-lived runtime state (pid records of started servers) is kept.</summary>
    public string RuntimeStatePath { get; set; } = Path.Combine(UserData.Directory, "run");

    public int InferenceThreads { get; set; }

    /// <summary>How long the loaded speech models stay in memory after they were last used or prepared.</summary>
    public TimeSpan SpeechIdleTimeout { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>Selected models across the Speech, Cleaner and Translator stages.</summary>
    public ModelSelection Models
    {
        get => _models;
        set => _models = value ?? ModelSelection.Baseline;
    }

    /// <summary>Which translation model tier is selected; maps onto <see cref="Models"/>.</summary>
    public TranslatorTier TranslatorTier
    {
        get => _models.Translator.Id == ModelCatalog.TranslatorQuality.Id ? TranslatorTier.Quality : TranslatorTier.Fast;
        set => _models = _models with { TranslatorModelId = ModelCatalog.Translator(value).Id };
    }

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
