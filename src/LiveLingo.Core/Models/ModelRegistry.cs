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

    // ── Gemma 4 ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Lightweight fallback (4 B, ~3 GB). Used when Gemma 4 12B cannot load on the device.
    /// </summary>
    public static readonly ModelDescriptor Gemma4_4B = new(
        "gemma4-4b",
        "Gemma 4 4B (GGUF Q4_K_M)",
        "https://huggingface.co/bartowski/google_gemma-4-4b-it-GGUF/resolve/main/google_gemma-4-4b-it-Q4_K_M.gguf",
        2_640_000_000,
        ModelType.Translation)
    {
        ChatTemplate = LocalModelChatTemplate.Gemma,
    };

    /// <summary>
    /// Primary translation GGUF (12 B). Falls back to <see cref="Gemma4_4B"/> on low-RAM devices.
    /// </summary>
    public static readonly ModelDescriptor Gemma4_12B = new(
        "gemma4-12b",
        "Gemma 4 12B (GGUF Q4_K_M)",
        "https://huggingface.co/bartowski/google_gemma-4-12b-it-GGUF/resolve/main/google_gemma-4-12b-it-Q4_K_M.gguf",
        7_270_000_000,
        ModelType.Translation)
    {
        ChatTemplate = LocalModelChatTemplate.Gemma,
        LoadFailureFallback = Gemma4_4B,
    };

    // ── Qwen (retained for existing installs and post-processing) ────────────

    public static readonly ModelDescriptor Qwen25_15B = new(
        "qwen25-1.5b",
        "Qwen2.5-1.5B-Instruct (GGUF Q4_K_M)",
        "https://huggingface.co/Qwen/Qwen2.5-1.5B-Instruct-GGUF/resolve/main/qwen2.5-1.5b-instruct-q4_k_m.gguf",
        1_117_320_736,
        ModelType.PostProcessing)
    {
        ChatTemplate = LocalModelChatTemplate.Qwen,
    };

    /// <summary>
    /// Kept for users who had Qwen3.5-9B installed; new installs default to Gemma 4 12B.
    /// </summary>
    public static readonly ModelDescriptor Qwen35_9B = new(
        "qwen35-9b",
        "Qwen3.5-9B Abliterated (GGUF Q4_K_M)",
        "https://huggingface.co/Abhiray/Qwen3.5-9B-abliterated-GGUF/resolve/main/Qwen3.5-9B-abliterated-Q4_K_M.gguf",
        5_627_044_704,
        ModelType.Translation)
    {
        ChatTemplate = LocalModelChatTemplate.Qwen,
        LoadFailureFallback = Qwen25_15B,
    };

    public static readonly ModelDescriptor Qwen25_7B = new(
        "qwen25-7b",
        "Qwen2.5-7B-Instruct (GGUF Q4_K_M)",
        "https://huggingface.co/bartowski/Qwen2.5-7B-Instruct-GGUF/resolve/main/Qwen2.5-7B-Instruct-Q4_K_M.gguf",
        4_683_074_240,
        ModelType.Translation)
    {
        ChatTemplate = LocalModelChatTemplate.Qwen,
        LoadFailureFallback = Qwen25_15B,
    };

    public static IReadOnlyList<ModelDescriptor> TranslationModels { get; } =
        [Gemma4_12B, Gemma4_4B, Qwen35_9B, Qwen25_7B, MarianZhEn, MarianEnZh, MarianJaEn];

    public static IReadOnlyList<ModelDescriptor> RequiredModels { get; } =
        [Gemma4_12B];

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
        [Gemma4_4B, Qwen25_15B, WhisperBase, SileroVad];

    public static IReadOnlyList<ModelDescriptor> AllModels { get; } =
    [
        Gemma4_12B, Gemma4_4B,
        Qwen35_9B, Qwen25_7B,
        MarianZhEn, MarianEnZh, MarianJaEn,
        FastTextLid, Qwen25_15B, WhisperBase, SileroVad
    ];

    public static ModelDescriptor? FindTranslationModel(string sourceLanguage, string targetLanguage) =>
        Gemma4_12B;

    public static IReadOnlyList<ModelDescriptor> GetRequiredModelsForLanguagePair(
        string? sourceLanguage,
        string? targetLanguage) => [Gemma4_12B];
}
