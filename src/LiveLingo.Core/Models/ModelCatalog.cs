using LiveLingo.Core.Inference;

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

    private const long MiB = 1024L * 1024;
    private const string SherpaRelease = "https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/";
    private const string SpeechLiveArchive = "sherpa-onnx-x-asr-480ms-streaming-zipformer-transducer-zh-en-punct-int8-2026-06-05";
    private const string SpeechSecondPassArchive = "sherpa-onnx-qwen3-asr-0.6B-int8-2026-03-25";
    private const string SpeechFastArchive = "sherpa-onnx-x-asr-zipformer-transducer-zh-en-punct-int8-2026-06-03";
    private const string Qwen3Asr17BResolve =
        "https://huggingface.co/thieunv-asilla/sherpa-onnx-qwen3-asr-1.7B-int8/resolve/69eb686fd94a4a865bb5340a3d6ac0d7f1fec0d5/";

    private const string LlamaRelease = "https://github.com/ggml-org/llama.cpp/releases/download/b11980/";

    /// <summary>
    /// Shared GGUF package for Qwen3.6 35B-A3B MoE (~3B active parameters per token). Stored once on disk and served by
    /// a single shared <c>llama-server</c> process when selected for both cleanup/polish and translation.
    /// </summary>
    public static readonly ModelDescriptor UnifiedQwen36MoE = new(
        "qwen3.6-35b-a3b-iq4xs",
        "Qwen3.6 35B-A3B MoE (unified polish & translation)",
        ModelType.PostProcessing,
        "Apache-2.0",
        [
            new ModelAsset(
                "Qwen3.6-35B-A3B-UD-IQ4_XS.gguf",
                "https://huggingface.co/unsloth/Qwen3.6-35B-A3B-GGUF/resolve/a483e9e6cbd595906af30beda3187c2663a1118c/Qwen3.6-35B-A3B-UD-IQ4_XS.gguf",
                17_730_509_792,
                "649d7508507b84638732c4f52c24c8b15843c6dca2f3ff793ae07c14a67ebbb3")
        ])
    {
        ShortName = "Qwen3.6 35B-A3B"
    };

    public static readonly ModelDescriptor TranslatorUnified = new(
        "qwen3.6-35b-a3b-translator",
        "Qwen3.6 35B-A3B MoE (unified flagship translation)",
        ModelType.Translation,
        "Apache-2.0",
        UnifiedQwen36MoE.Assets)
    {
        ShortName = "Qwen3.6 35B-A3B",
        SharedArtifact = UnifiedQwen36MoE,
        Profile = new ModelProfile(ModelStage.Translator, Rank: 1, QualityStars: 3, SpeedStars: 2, ResidentBytes: 18_800 * MiB, RequiresGpu: true, UtilityScore: 54)
    };

    public static readonly ModelDescriptor TranslatorIndexQuality = new(
        "index-translate-9b-q4km",
        "Index-Translate 9B (flagship translation)",
        ModelType.Translation,
        "Apache-2.0",
        [
            new ModelAsset(
                "Index-Translate-9B.Q4_K_M.gguf",
                "https://huggingface.co/IndexTeam/Index-Translate-9B-GGUF/resolve/645939fbab839a656f1bc39975a872bc8ad749f8/Index-Translate-9B.Q4_K_M.gguf",
                5_780_090_304,
                "9cc6758b0007f4cbf42ea3400768cbff91ae74ef8d640e467158348426d33713")
        ])
    {
        ShortName = "Index-Translate 9B",
        Profile = new ModelProfile(ModelStage.Translator, Rank: 2, QualityStars: 3, SpeedStars: 2, ResidentBytes: 6_270 * MiB, RequiresGpu: true, UtilityScore: 49)
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
        ShortName = "Hy-MT2-7B",
        Profile = new ModelProfile(ModelStage.Translator, Rank: 3, QualityStars: 3, SpeedStars: 2, ResidentBytes: 5_056 * MiB, RequiresGpu: true, UtilityScore: 45)
    };

    public static readonly ModelDescriptor TranslatorIndexFast = new(
        "index-translate-2b-q4km",
        "Index-Translate 2B (fast translation)",
        ModelType.Translation,
        "Apache-2.0",
        [
            new ModelAsset(
                "Index-Translate-2B.Q4_K_M.gguf",
                "https://huggingface.co/IndexTeam/Index-Translate-2B-GGUF/resolve/3a0eca6ce931d88d00614d427ab3ed8ff0743902/Index-Translate-2B.Q4_K_M.gguf",
                1_312_164_352,
                "044b313d29342bd3b2c77cbb64023ca0d209bd9b3247763f9d162767ef2d746a")
        ])
    {
        ShortName = "Index-Translate 2B",
        Profile = new ModelProfile(ModelStage.Translator, Rank: 4, QualityStars: 2, SpeedStars: 3, ResidentBytes: 1_630 * MiB, RequiresGpu: false, UtilityScore: 28)
    };

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
        ShortName = "Hy-MT2-1.8B",
        Profile = new ModelProfile(ModelStage.Translator, Rank: 5, QualityStars: 2, SpeedStars: 3, ResidentBytes: 1_448 * MiB, RequiresGpu: false, UtilityScore: 25)
    };

    public static readonly ModelDescriptor CleanerUnified = new(
        "qwen3.6-35b-a3b-cleaner",
        "Qwen3.6 35B-A3B MoE (unified flagship polish)",
        ModelType.PostProcessing,
        "Apache-2.0",
        UnifiedQwen36MoE.Assets)
    {
        ShortName = "Qwen3.6 35B-A3B",
        SharedArtifact = UnifiedQwen36MoE,
        Profile = new ModelProfile(ModelStage.Cleaner, Rank: 1, QualityStars: 3, SpeedStars: 2, ResidentBytes: 18_800 * MiB, RequiresGpu: true, UtilityScore: 54)
    };

    public static readonly ModelDescriptor CleanerQuality = new(
        "qwen3.5-9b-q4km",
        "Qwen3.5 9B (deep text cleanup)",
        ModelType.PostProcessing,
        "Apache-2.0",
        [
            new ModelAsset(
                "Qwen3.5-9B-Q4_K_M.gguf",
                "https://huggingface.co/unsloth/Qwen3.5-9B-GGUF/resolve/3885219b6810b007914f3a7950a8d1b469d598a5/Qwen3.5-9B-Q4_K_M.gguf",
                5_680_522_464,
                "03b74727a860a56338e042c4420bb3f04b2fec5734175f4cb9fa853daf52b7e8")
        ])
    {
        ShortName = "Qwen3.5 9B",
        Profile = new ModelProfile(ModelStage.Cleaner, Rank: 2, QualityStars: 3, SpeedStars: 1, ResidentBytes: 6_170 * MiB, RequiresGpu: true, UtilityScore: 42)
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
        ShortName = "Qwen3.5 4B",
        Profile = new ModelProfile(ModelStage.Cleaner, Rank: 3, QualityStars: 2, SpeedStars: 2, ResidentBytes: 3_019 * MiB, RequiresGpu: false, UtilityScore: 30)
    };

    public static readonly ModelDescriptor CleanerFast = new(
        "qwen3.5-2b-q4km",
        "Qwen3.5 2B (light text cleanup)",
        ModelType.PostProcessing,
        "Apache-2.0",
        [
            new ModelAsset(
                "Qwen3.5-2B-Q4_K_M.gguf",
                "https://huggingface.co/unsloth/Qwen3.5-2B-GGUF/resolve/f6d5376be1edb4d416d56da11e5397a961aca8ae/Qwen3.5-2B-Q4_K_M.gguf",
                1_280_835_840,
                "aaf42c8b7c3cab2bf3d69c355048d4a0ee9973d48f16c731c0520ee914699223")
        ])
    {
        ShortName = "Qwen3.5 2B",
        Profile = new ModelProfile(ModelStage.Cleaner, Rank: 4, QualityStars: 1, SpeedStars: 3, ResidentBytes: 1_600 * MiB, RequiresGpu: false, UtilityScore: 15)
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

    /// <summary>Qwen/Qwen3-ASR-1.7B INT8 ONNX: highest-accuracy local speech recognizer for multilingual, dialectal and code-switched dictation.</summary>
    public static readonly ModelDescriptor SpeechQuality = new(
        "qwen3-asr-1.7b-int8",
        "Qwen3-ASR 1.7B (flagship settled text)",
        ModelType.SpeechToText,
        "Apache-2.0",
        [
            new ModelAsset(
                "conv_frontend.onnx",
                Qwen3Asr17BResolve + "conv_frontend.onnx",
                48_080_441,
                "3cb27a9fe94d95c938e476f2012b21aba2ec0bfceef33b0e58acd208946bafdd"),
            new ModelAsset(
                "encoder.int8.onnx",
                Qwen3Asr17BResolve + "encoder.int8.onnx",
                314_222_162,
                "a5deedae034ece715de8ed204378d8c77f889af3a60c2566581135e84cced7cd"),
            new ModelAsset(
                "decoder.int8.onnx",
                Qwen3Asr17BResolve + "decoder.int8.onnx",
                2_037_458_645,
                "c43c853fa6e97d08365cb8a5502b360b595cd43c00dc60e4d8ca7cc18cad460b"),
            new ModelAsset(
                "tokenizer/merges.txt",
                Qwen3Asr17BResolve + "tokenizer/merges.txt",
                1_671_853,
                "8831e4f1a044471340f7c0a83d7bd71306a5b867e95fd870f74d0c5308a904d5"),
            new ModelAsset(
                "tokenizer/tokenizer_config.json",
                Qwen3Asr17BResolve + "tokenizer/tokenizer_config.json",
                12_487,
                "4942d005604266809309cabc9f4e9cb89ce855d59b14681fdc0e1cc62ea26c4c"),
            new ModelAsset(
                "tokenizer/vocab.json",
                Qwen3Asr17BResolve + "tokenizer/vocab.json",
                2_776_833,
                "ca10d7e9fb3ed18575dd1e277a2579c16d108e32f27439684afa0e10b1440910")
        ])
    {
        ShortName = "Qwen3-ASR 1.7B",
        Profile = new ModelProfile(ModelStage.Speech, Rank: 1, QualityStars: 3, SpeedStars: 1, ResidentBytes: 3_050 * MiB, RequiresGpu: false, UtilityScore: 36)
    };

    /// <summary>Qwen3-ASR 0.6B that re-decodes each closed segment for the settled text with hotword biasing.</summary>
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
        ],
        Profile = new ModelProfile(ModelStage.Speech, Rank: 2, QualityStars: 2, SpeedStars: 2, ResidentBytes: 1_350 * MiB, RequiresGpu: false, UtilityScore: 30)
    };

    /// <summary>Lightweight offline X-ASR Zipformer transducer for fast, low-memory settled speech recognition.</summary>
    public static readonly ModelDescriptor SpeechFast = new(
        "x-asr-offline-int8",
        "X-ASR Offline (light settled text)",
        ModelType.SpeechToText,
        "Apache-2.0",
        [
            new ModelAsset(
                SpeechFastArchive + ".tar.bz2",
                SherpaRelease + SpeechFastArchive + ".tar.bz2",
                136_396_739,
                "5d02c36d7b44e886b7c8f0d8e051f8713acab96c264bb6ef9e718be39a6a2224")
        ])
    {
        ShortName = "X-ASR Offline",
        ArchiveType = ModelArchiveType.TarBz2,
        ExtractedFiles =
        [
            "encoder-epoch-99-avg-1.int8.onnx", "decoder-epoch-99-avg-1.onnx",
            "joiner-epoch-99-avg-1.int8.onnx", "tokens.txt"
        ],
        Profile = new ModelProfile(ModelStage.Speech, Rank: 3, QualityStars: 1, SpeedStars: 3, ResidentBytes: 768 * MiB, RequiresGpu: false, UtilityScore: 15)
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

    /// <summary>Selectable speech recognition models, ordered strictly from highest quality (#1) to lightest.</summary>
    public static IReadOnlyList<ModelDescriptor> SpeechModels { get; } = [SpeechQuality, SpeechSecondPass, SpeechFast];

    /// <summary>Selectable cleanup and polish models, ordered strictly from highest quality (#1) to lightest.</summary>
    public static IReadOnlyList<ModelDescriptor> CleanerModels { get; } = [CleanerUnified, CleanerQuality, Cleaner, CleanerFast];

    /// <summary>Selectable translation models, ordered strictly from highest quality (#1) to lightest.</summary>
    public static IReadOnlyList<ModelDescriptor> TranslatorModels { get; } =
        [TranslatorUnified, TranslatorIndexQuality, TranslatorQuality, TranslatorIndexFast, TranslatorFast];

    /// <summary>Every user-selectable stage option, ordered by stage and then from best quality (#1) to lightest.</summary>
    public static IReadOnlyList<ModelDescriptor> Selectable { get; } = [.. SpeechModels, .. CleanerModels, .. TranslatorModels];

    public static IReadOnlyList<ModelDescriptor> OptionsFor(ModelStage stage) => stage switch
    {
        ModelStage.Speech => SpeechModels,
        ModelStage.Cleaner => CleanerModels,
        ModelStage.Translator => TranslatorModels,
        _ => throw new ArgumentOutOfRangeException(nameof(stage))
    };

    public static ModelDescriptor? FindOption(ModelStage stage, string? modelId) =>
        string.IsNullOrWhiteSpace(modelId)
            ? null
            : OptionsFor(stage).FirstOrDefault(model => string.Equals(model.Id, modelId, StringComparison.Ordinal));

    public static ModelDescriptor? FindSelectable(string? modelId) =>
        string.IsNullOrWhiteSpace(modelId)
            ? null
            : Selectable.FirstOrDefault(model => string.Equals(model.Id, modelId, StringComparison.Ordinal));

    /// <summary>Finds either a selectable stage option or a physical catalog artifact by ID.</summary>
    public static ModelDescriptor? FindAny(string? modelId) =>
        FindSelectable(modelId) ?? (string.IsNullOrWhiteSpace(modelId)
            ? null
            : All.FirstOrDefault(model => string.Equals(model.Id, modelId, StringComparison.Ordinal)));

    /// <summary>True when <paramref name="modelId"/> is a shared multi-stage LLM package that the user may manage/delete in Storage.</summary>
    public static bool IsSharedStageArtifact(string? modelId) =>
        string.Equals(modelId, UnifiedQwen36MoE.Id, StringComparison.Ordinal);

    /// <summary>Everything voice dictation needs for the default speech recognizer besides the cleaner and the translator.</summary>
    public static IReadOnlyList<ModelDescriptor> Speech { get; } = SpeechFor(SpeechSecondPass);

    /// <summary>What importing a recording needs for the default speech recognizer: no live recognizer, since the whole file is known.</summary>
    public static IReadOnlyList<ModelDescriptor> Transcription { get; } = TranscriptionFor(SpeechSecondPass);

    public static IReadOnlyList<ModelDescriptor> SpeechFor(ModelDescriptor secondPass) => [SpeechLive, secondPass, SpeechVad];

    public static IReadOnlyList<ModelDescriptor> TranscriptionFor(ModelDescriptor secondPass) => [secondPass, SpeechVad];

    public static bool IsSpeechAsset(string modelId) =>
        modelId == SpeechLive.Id || modelId == SpeechVad.Id || SpeechModels.Any(model => model.Id == modelId);

    /// <summary>All artifacts (including shared runtime or companion speech assets) required to run <paramref name="option"/> locally.</summary>
    public static IReadOnlyList<ModelDescriptor> BundleFor(ModelDescriptor option, ModelDescriptor? llamaRuntime = null) =>
        option.Profile?.Stage switch
        {
            ModelStage.Speech => SpeechFor(option),
            ModelStage.Cleaner or ModelStage.Translator =>
                (llamaRuntime ?? LlamaRuntimeOrNull) is { } runtime ? [runtime, option.ResolveArtifact()] : [option.ResolveArtifact()],
            _ => [option.ResolveArtifact()]
        };

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

    public static ModelDescriptor? LlamaRuntimeOrNull =>
        System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier switch
        {
            "osx-arm64" => LlamaMacArm64,
            "win-x64" => LlamaWinX64,
            _ => null
        };

    public static IReadOnlyList<ModelDescriptor> All { get; } =
    [
        UnifiedQwen36MoE,
        TranslatorIndexQuality, TranslatorQuality, TranslatorIndexFast, TranslatorFast,
        CleanerQuality, Cleaner, CleanerFast,
        SpeechLive, SpeechQuality, SpeechSecondPass, SpeechFast, SpeechVad,
        LlamaMacArm64, LlamaWinX64
    ];
}
