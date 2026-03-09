namespace LiveLingo.Core.Models;

public static class ModelRegistry
{
    public static readonly ModelDescriptor MarianZhEn = new(
        "opus-mt-zh-en",
        "MarianMT Chinese→English",
        "https://huggingface.co/Xenova/opus-mt-zh-en/resolve/main/onnx/encoder_model.onnx",
        447_388_663,
        ModelType.Translation)
    {
        Assets =
        [
            new("onnx/encoder_model.onnx", "https://huggingface.co/Xenova/opus-mt-zh-en/resolve/main/onnx/encoder_model.onnx", 209_938_220),
            new("onnx/decoder_model_merged.onnx", "https://huggingface.co/Xenova/opus-mt-zh-en/resolve/main/onnx/decoder_model_merged.onnx", 235_839_236),
            new("source.spm", "https://huggingface.co/Xenova/opus-mt-zh-en/resolve/main/source.spm", 804_677),
            new("target.spm", "https://huggingface.co/Xenova/opus-mt-zh-en/resolve/main/target.spm", 806_530),
            new("vocab.json", "https://huggingface.co/Xenova/opus-mt-zh-en/resolve/main/vocab.json", 1_617_902),
            new("config.json", "https://huggingface.co/Xenova/opus-mt-zh-en/resolve/main/config.json", 0),
            new("generation_config.json", "https://huggingface.co/Xenova/opus-mt-zh-en/resolve/main/generation_config.json", 0),
        ]
    };

    public static readonly ModelDescriptor MarianEnZh = new(
        "opus-mt-en-zh",
        "MarianMT English→Chinese",
        "https://huggingface.co/Xenova/opus-mt-en-zh/resolve/main/onnx/encoder_model.onnx",
        447_388_663,
        ModelType.Translation)
    {
        Assets =
        [
            new("onnx/encoder_model.onnx", "https://huggingface.co/Xenova/opus-mt-en-zh/resolve/main/onnx/encoder_model.onnx", 209_938_220),
            new("onnx/decoder_model_merged.onnx", "https://huggingface.co/Xenova/opus-mt-en-zh/resolve/main/onnx/decoder_model_merged.onnx", 235_839_236),
            new("source.spm", "https://huggingface.co/Xenova/opus-mt-en-zh/resolve/main/source.spm", 804_677),
            new("target.spm", "https://huggingface.co/Xenova/opus-mt-en-zh/resolve/main/target.spm", 806_530),
            new("vocab.json", "https://huggingface.co/Xenova/opus-mt-en-zh/resolve/main/vocab.json", 1_617_902),
            new("config.json", "https://huggingface.co/Xenova/opus-mt-en-zh/resolve/main/config.json", 0),
            new("generation_config.json", "https://huggingface.co/Xenova/opus-mt-en-zh/resolve/main/generation_config.json", 0),
        ]
    };

    public static readonly ModelDescriptor MarianJaEn = new(
        "opus-mt-ja-en",
        "MarianMT Japanese→English",
        "https://huggingface.co/Xenova/opus-mt-ja-en/resolve/main/onnx/encoder_model.onnx",
        447_388_663,
        ModelType.Translation)
    {
        Assets =
        [
            new("onnx/encoder_model.onnx", "https://huggingface.co/Xenova/opus-mt-ja-en/resolve/main/onnx/encoder_model.onnx", 209_938_220),
            new("onnx/decoder_model_merged.onnx", "https://huggingface.co/Xenova/opus-mt-ja-en/resolve/main/onnx/decoder_model_merged.onnx", 235_839_236),
            new("source.spm", "https://huggingface.co/Xenova/opus-mt-ja-en/resolve/main/source.spm", 804_677),
            new("target.spm", "https://huggingface.co/Xenova/opus-mt-ja-en/resolve/main/target.spm", 806_530),
            new("vocab.json", "https://huggingface.co/Xenova/opus-mt-ja-en/resolve/main/vocab.json", 1_617_902),
            new("config.json", "https://huggingface.co/Xenova/opus-mt-ja-en/resolve/main/config.json", 0),
            new("generation_config.json", "https://huggingface.co/Xenova/opus-mt-ja-en/resolve/main/generation_config.json", 0),
        ]
    };

    public static readonly ModelDescriptor FastTextLid = new(
        "lid.176.ftz",
        "FastText Language Detection",
        "https://dl.fbaipublicfiles.com/fasttext/supervised-models/lid.176.ftz",
        938_013,
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
        [MarianZhEn];

    public static readonly ModelDescriptor WhisperBase = new(
        "whisper-base",
        "Whisper Base (Speech-to-Text)",
        "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.bin",
        147_951_465,
        ModelType.SpeechToText);

    public static readonly ModelDescriptor SileroVad = new(
        "silero-vad",
        "Silero VAD v5 (Voice Activity Detection)",
        "https://huggingface.co/runanywhere/silero-vad-v5/resolve/main/silero_vad.onnx",
        2_440_000,
        ModelType.VoiceActivityDetection);

    public static IReadOnlyList<ModelDescriptor> OptionalModels { get; } =
        [Qwen25_15B, WhisperBase, SileroVad];

    public static IReadOnlyList<ModelDescriptor> AllModels { get; } =
        [MarianZhEn, MarianEnZh, MarianJaEn, FastTextLid, Qwen25_15B, WhisperBase, SileroVad];

    public static ModelDescriptor? FindTranslationModel(string sourceLanguage, string targetLanguage)
    {
        var id = $"opus-mt-{sourceLanguage}-{targetLanguage}";
        return TranslationModels.FirstOrDefault(m => m.Id == id);
    }

    public static IReadOnlyList<ModelDescriptor> GetRequiredModelsForLanguagePair(
        string? sourceLanguage,
        string? targetLanguage)
    {
        var src = string.IsNullOrWhiteSpace(sourceLanguage) ? "zh" : sourceLanguage;
        var tgt = string.IsNullOrWhiteSpace(targetLanguage) ? "en" : targetLanguage;
        var translationModel = FindTranslationModel(src, tgt) ?? MarianZhEn;
        return [translationModel];
    }
}
