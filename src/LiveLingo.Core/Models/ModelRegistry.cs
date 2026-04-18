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
    /// Matryoshka effective-4B fallback (~5.4 GB). Used when the 26B MoE cannot load on the device.
    /// </summary>
    public static readonly ModelDescriptor Gemma4_E4B = new(
        "gemma4-e4b",
        "Gemma 4 E4B (GGUF Q4_K_M)",
        "https://huggingface.co/bartowski/google_gemma-4-E4B-it-GGUF/resolve/main/google_gemma-4-E4B-it-Q4_K_M.gguf",
        5_405_167_904,
        ModelType.Translation)
    {
        ChatTemplate = LocalModelChatTemplate.Gemma,
    };

    /// <summary>
    /// Primary translation GGUF: Gemma 4 26B Mixture-of-Experts with 4B activated parameters
    /// (~17 GB on disk, ~4B inference cost). Falls back to <see cref="Gemma4_E4B"/> on low-RAM devices.
    /// MoE gives ~26B-class translation quality at ~4B-class latency — ideal for real-time captions.
    /// </summary>
    public static readonly ModelDescriptor Gemma4_26B_A4B = new(
        "gemma4-26b-a4b",
        "Gemma 4 26B-A4B MoE (GGUF Q4_K_M)",
        "https://huggingface.co/bartowski/google_gemma-4-26B-A4B-it-GGUF/resolve/main/google_gemma-4-26B-A4B-it-Q4_K_M.gguf",
        17_035_037_632,
        ModelType.Translation)
    {
        ChatTemplate = LocalModelChatTemplate.Gemma,
        LoadFailureFallback = Gemma4_E4B,
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
    /// Kept for users who had Qwen3.5-9B installed; new installs default to Gemma 4 26B-A4B MoE.
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
        [Gemma4_26B_A4B, Gemma4_E4B, Qwen35_9B, Qwen25_7B, MarianZhEn, MarianEnZh, MarianJaEn];

    public static IReadOnlyList<ModelDescriptor> RequiredModels { get; } =
        [Gemma4_26B_A4B];

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
        [Gemma4_E4B, Qwen25_15B, WhisperBase, SileroVad];

    public static IReadOnlyList<ModelDescriptor> AllModels { get; } =
    [
        Gemma4_26B_A4B, Gemma4_E4B,
        Qwen35_9B, Qwen25_7B,
        MarianZhEn, MarianEnZh, MarianJaEn,
        FastTextLid, Qwen25_15B, WhisperBase, SileroVad
    ];

    public static ModelDescriptor? FindTranslationModel(string sourceLanguage, string targetLanguage) =>
        Gemma4_26B_A4B;

    public static IReadOnlyList<ModelDescriptor> GetRequiredModelsForLanguagePair(
        string? sourceLanguage,
        string? targetLanguage) => [Gemma4_26B_A4B];
}
