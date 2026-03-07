namespace LiveLingo.Core.Models;

public static class ModelRegistry
{
    public static readonly ModelDescriptor MarianZhEn = new(
        "opus-mt-zh-en",
        "MarianMT Chinese→English",
        "https://huggingface.co/Helsinki-NLP/opus-mt-zh-en/resolve/main/pytorch_model.bin",
        312_500_000,
        ModelType.Translation);

    public static readonly ModelDescriptor MarianEnZh = new(
        "opus-mt-en-zh",
        "MarianMT English→Chinese",
        "https://huggingface.co/Helsinki-NLP/opus-mt-en-zh/resolve/main/pytorch_model.bin",
        312_500_000,
        ModelType.Translation);

    public static readonly ModelDescriptor MarianJaEn = new(
        "opus-mt-ja-en",
        "MarianMT Japanese→English",
        "https://huggingface.co/Helsinki-NLP/opus-mt-ja-en/resolve/main/pytorch_model.bin",
        312_500_000,
        ModelType.Translation);

    public static readonly ModelDescriptor FastTextLid = new(
        "lid.176.ftz",
        "FastText Language Detection",
        "https://dl.fbaipublicfiles.com/fasttext/supervised-models/lid.176.ftz",
        917_391,
        ModelType.LanguageDetection);

    public static readonly ModelDescriptor Qwen25_15B = new(
        "qwen25-1.5b",
        "Qwen2.5-1.5B-Instruct (GGUF Q4_K_M)",
        "https://huggingface.co/Qwen/Qwen2.5-1.5B-Instruct-GGUF/resolve/main/qwen2.5-1.5b-instruct-q4_k_m.gguf",
        1_073_741_824,
        ModelType.PostProcessing);

    public static IReadOnlyList<ModelDescriptor> TranslationModels { get; } =
        [MarianZhEn, MarianEnZh, MarianJaEn];

    public static IReadOnlyList<ModelDescriptor> RequiredModels { get; } =
        [Qwen25_15B];

    public static IReadOnlyList<ModelDescriptor> AllModels { get; } =
        [MarianZhEn, MarianEnZh, MarianJaEn, FastTextLid, Qwen25_15B];

    public static ModelDescriptor? FindTranslationModel(string sourceLanguage, string targetLanguage)
    {
        var id = $"opus-mt-{sourceLanguage}-{targetLanguage}";
        return TranslationModels.FirstOrDefault(m => m.Id == id);
    }
}
