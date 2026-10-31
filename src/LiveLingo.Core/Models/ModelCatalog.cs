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
        ]);

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
        ]);

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
        [TranslatorFast, TranslatorQuality, LlamaMacArm64, LlamaWinX64];
}
