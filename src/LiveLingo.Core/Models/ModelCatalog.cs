namespace LiveLingo.Core.Models;

public enum TranslatorTier
{
    Fast,
    Quality
}

/// <summary>
/// The only place that lists what the app may download. Every URL pins an immutable revision
/// (a release tag or a commit hash) and every asset carries its SHA256.
/// </summary>
public static class ModelCatalog
{
    public const string LlamaRuntimeId = "llama-cpp-b11980";

    private const string SherpaRelease = "https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/";
    private const string SpeechLiveArchive = "sherpa-onnx-x-asr-480ms-streaming-zipformer-transducer-zh-en-punct-int8-2026-06-05";
    private const string SpeechSecondPassArchive = "sherpa-onnx-qwen3-asr-0.6B-int8-2026-03-25";

    private const string LlamaRelease = "https://github.com/ggml-org/llama.cpp/releases/download/b11980/";

    public static readonly ModelDescriptor TranslatorFast = new(
        "hy-mt2-1.8b-q4km",
        "Hy-MT2 1.8B (fast translation)",
        ModelType.Translation,
        "Apache-2.0",
        [
            new ModelAsset(
                "Hy-MT2-1.8B-Q4_K_M.gguf",
                "https://huggingface.co/tencent/Hy-MT2-1.8B-GGUF/resolve/a0c709d9fac510f2c807aa3af52872340dc37a4a/Hy-MT2-1.8B-Q4_K_M.gguf",
                1_133_080_448,
                "dc5f44fcf1fa496ee7ad725982c0c8c553a4de00259b53af84c4b89fb0c06699")
        ])
    {
        ShortName = "Hy-MT2-1.8B"
    };

    public static readonly ModelDescriptor TranslatorQuality = new(
        "hy-mt2-7b-q4km",
        "Hy-MT2 7B (quality translation)",
        ModelType.Translation,
        "Apache-2.0",
        [
            new ModelAsset(
                "Hy-MT2-7B-Q4_K_M.gguf",
                "https://huggingface.co/tencent/Hy-MT2-7B-GGUF/resolve/ab8472660ac61fac25f1af43fac2599d52a8a775/Hy-MT2-7B-Q4_K_M.gguf",
                4_624_648_896,
                "9f96256500f3fc1ab4d64336b58f52a949a95ad7516b0c229476eef782f9f77b")
        ])
    {
        ShortName = "Hy-MT2-7B"
    };

    public static readonly ModelDescriptor Cleaner = new(
        "qwen3.5-4b-q4km",
        "Qwen3.5 4B (text cleanup)",
        ModelType.PostProcessing,
        "Apache-2.0",
        [
            new ModelAsset(
                "Qwen3.5-4B-Q4_K_M.gguf",
                "https://huggingface.co/unsloth/Qwen3.5-4B-GGUF/resolve/e87f176479d0855a907a41277aca2f8ee7a09523/Qwen3.5-4B-Q4_K_M.gguf",
                2_740_937_888,
                "00fe7986ff5f6b463e62455821146049db6f9313603938a70800d1fb69ef11a4")
        ])
    {
        ShortName = "Qwen3.5 4B"
    };

    /// <summary>Streaming X-ASR (480 ms chunks) that produces the live text while the user speaks.</summary>
    public static readonly ModelDescriptor SpeechLive = new(
        "x-asr-480ms-streaming-int8",
        "X-ASR 480 ms streaming (live text)",
        ModelType.SpeechToText,
        "Apache-2.0",
        [
            new ModelAsset(
                SpeechLiveArchive + ".tar.bz2",
                SherpaRelease + SpeechLiveArchive + ".tar.bz2",
                133_895_136,
                "fa5f63d618e5a01526e275a358bb7772e403f84808a4769fba52cffd8160bf74")
        ])
    {
        ShortName = "X-ASR",
        ArchiveType = ModelArchiveType.TarBz2,
        ExtractedFiles = ["encoder.int8.onnx", "decoder.onnx", "joiner.int8.onnx", "tokens.txt"]
    };

    /// <summary>Qwen3-ASR 0.6B that re-decodes each closed segment for the settled text.</summary>
    public static readonly ModelDescriptor SpeechSecondPass = new(
        "qwen3-asr-0.6b-int8",
        "Qwen3-ASR 0.6B (settled text)",
        ModelType.SpeechToText,
        "Apache-2.0",
        [
            new ModelAsset(
                SpeechSecondPassArchive + ".tar.bz2",
                SherpaRelease + SpeechSecondPassArchive + ".tar.bz2",
                878_702_423,
                "393f8a14e2f5fb96746aaab342997a40641001fbd5bf9592a080a8329178ee96")
        ])
    {
        ShortName = "Qwen3-ASR",
        ArchiveType = ModelArchiveType.TarBz2,
        ExtractedFiles =
        [
            "conv_frontend.onnx", "encoder.int8.onnx", "decoder.int8.onnx",
            "tokenizer/merges.txt", "tokenizer/tokenizer_config.json", "tokenizer/vocab.json"
        ]
    };

    public static readonly ModelDescriptor SpeechVad = new(
        "silero-vad",
        "Silero VAD (speech boundaries)",
        ModelType.VoiceActivityDetection,
        "MIT",
        [
            new ModelAsset(
                "silero_vad.onnx",
                SherpaRelease + "silero_vad.onnx",
                643_854,
                "9e2449e1087496d8d4caba907f23e0bd3f78d91fa552479bb9c23ac09cbb1fd6")
        ]);

    /// <summary>Everything voice dictation needs besides the cleaner and the translator.</summary>
    public static IReadOnlyList<ModelDescriptor> Speech { get; } = [SpeechLive, SpeechSecondPass, SpeechVad];

    /// <summary>What importing a recording needs: no live recognizer, since the whole file is known.</summary>
    public static IReadOnlyList<ModelDescriptor> Transcription { get; } = [SpeechSecondPass, SpeechVad];

    private static readonly ModelDescriptor LlamaMacArm64 = new(
        LlamaRuntimeId + "-osx-arm64",
        "llama.cpp b11980 (macOS arm64)",
        ModelType.Runtime,
        "MIT",
        [
            new ModelAsset(
                "llama-b11980-bin-macos-arm64.tar.gz",
                LlamaRelease + "llama-b11980-bin-macos-arm64.tar.gz",
                16_463_598,
                "b7d060dd22924984dcb924fdea9858bd5bec2638217b6435fd16da55524f5524")
        ])
    {
        ArchiveType = ModelArchiveType.TarGz,
        ExtractedFiles = ["llama-server"]
    };

    // The CPU build is the baseline; the Vulkan build is evaluated on real Windows hardware before it becomes the default.
    private static readonly ModelDescriptor LlamaWinX64 = new(
        LlamaRuntimeId + "-win-x64",
        "llama.cpp b11980 (Windows x64 CPU)",
        ModelType.Runtime,
        "MIT",
        [
            new ModelAsset(
                "llama-b11980-bin-win-cpu-x64.zip",
                LlamaRelease + "llama-b11980-bin-win-cpu-x64.zip",
                24_408_593,
                "f56c3a8a145bc08cbd5824bc0c4b8aacf91ba8fc5984ed5cdf913f91fd68a1fb")
        ])
    {
        ArchiveType = ModelArchiveType.Zip,
        ExtractedFiles = ["llama-server.exe"]
    };

    public static ModelDescriptor Translator(TranslatorTier tier) => tier switch
    {
        TranslatorTier.Fast => TranslatorFast,
        TranslatorTier.Quality => TranslatorQuality,
        _ => throw new ArgumentOutOfRangeException(nameof(tier))
    };

    /// <param name="runtimeIdentifier">.NET runtime identifier, for example <c>osx-arm64</c> or <c>win-x64</c>.</param>
    public static ModelDescriptor LlamaRuntimeFor(string runtimeIdentifier) => runtimeIdentifier switch
    {
        "osx-arm64" => LlamaMacArm64,
        "win-x64" => LlamaWinX64,
        _ => throw new PlatformNotSupportedException($"No pinned llama.cpp runtime for '{runtimeIdentifier}'.")
    };

    public static ModelDescriptor LlamaRuntime =>
        LlamaRuntimeFor(System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier);

    public static IReadOnlyList<ModelDescriptor> All { get; } =
        [TranslatorFast, TranslatorQuality, Cleaner, SpeechLive, SpeechSecondPass, SpeechVad, LlamaMacArm64, LlamaWinX64];
}
