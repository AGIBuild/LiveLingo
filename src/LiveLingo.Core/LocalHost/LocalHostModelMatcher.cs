using System.Globalization;
using System.Text.RegularExpressions;
using LiveLingo.Core.Inference;
using LiveLingo.Core.Models;

namespace LiveLingo.Core.LocalHost;

/// <summary>
/// Pure specification and ranking strategy that filters out non-text models (audio/music/embedding/base), classifies
/// discovered local host models by suitability for LiveLingo cleanup or translation, prioritizes hot resident instances
/// (zero extra memory), and produces hardware-matched download recommendations when no suitable model is present.
/// </summary>
public static partial class LocalHostModelMatcher
{
    public const int MinimumContextTokens = 4096;
    private const double MinimumBillionParams = 1.5;

    private static readonly string[] ExcludedModalTokens =
    [
        "music", "inspiremusic", "audio", "tts", "asr", "whisper", "voxtral", "phonon",
        "embed", "rerank", "diffusion", "flux", "sdxl", "stable-diffusion", "bark", "kokoro"
    ];

    private static readonly string[] CleanerCatalogFamilies =
    [
        "qwen3.6-35b-a3b", "qwen3.5-9b", "qwen3.5-4b", "qwen3.5-2b"
    ];

    private static readonly string[] TranslatorCatalogFamilies =
    [
        "qwen3.6-35b-a3b", "index-translate-9b", "hy-mt2-7b", "index-translate-2b", "hy-mt2-1.8b"
    ];

    private static readonly string[] FlagshipBilingualFamilies =
    [
        "qwen3.8", "qwen3.6", "qwen3.5", "qwen3", "gemma-4", "gemma4",
        "glm-5", "glm-4", "deepseek-v4", "deepseek-v3", "index-translate", "hy-mt2"
    ];

    /// <summary>True when a model is clearly not a text instruction/chat LLM (for example music, ASR, TTS, embedding, or base checkpoints).</summary>
    public static bool IsExcludedNonTextModel(LocalHostModelInfo model)
    {
        var haystack = Normalize($"{model.Key} {model.DisplayName}");
        foreach (var token in ExcludedModalTokens)
        {
            if (haystack.Contains(token, StringComparison.Ordinal))
                return true;
        }

        var keyLower = model.Key.ToLowerInvariant();
        if (keyLower.EndsWith("-base", StringComparison.Ordinal) || keyLower.EndsWith("-pt", StringComparison.Ordinal))
            return true;

        return false;
    }

    private static readonly string[] SpeechAsrTokens =
    [
        "whisper", "asr", "voxtral", "paraformer", "sensevoice", "funasr",
        "transcribe", "speech", "stt", "faster-whisper", "distil-whisper"
    ];

    private static readonly string[] NonAsrAudioTokens =
    [
        "music", "inspiremusic", "tts", "bark", "kokoro", "embed", "rerank", "diffusion", "flux", "sdxl", "stable-diffusion"
    ];

    /// <summary>True when a discovered model looks like a speech recognition (ASR) model.</summary>
    public static bool IsSpeechAsrModel(LocalHostModelInfo model)
    {
        var haystack = Normalize($"{model.Key} {model.DisplayName}");
        foreach (var token in NonAsrAudioTokens)
        {
            if (haystack.Contains(token, StringComparison.Ordinal))
                return false;
        }
        foreach (var token in SpeechAsrTokens)
        {
            if (haystack.Contains(token, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    /// <summary>Filters, evaluates and ranks discovered models for <paramref name="stage"/>, placing hot zero-extra-RAM models first.</summary>
    public static IReadOnlyList<EvaluatedHostModel> Evaluate(
        ModelStage stage,
        IReadOnlyList<LocalHostModelInfo> models,
        string? activeModelKey = null)
    {
        var evaluated = new List<EvaluatedHostModel>(models.Count);
        foreach (var model in models)
        {
            if (stage == ModelStage.Speech)
            {
                if (!IsSpeechAsrModel(model))
                    continue;
                evaluated.Add(EvaluateSpeechSingle(model));
                continue;
            }

            if (IsExcludedNonTextModel(model))
                continue;
            evaluated.Add(EvaluateSingle(stage, model));
        }

        return evaluated
            .OrderByDescending(e => !string.IsNullOrEmpty(activeModelKey)
                && string.Equals(e.Model.Key, activeModelKey, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(e => e.IsSuitable)
            .ThenByDescending(e => e.IsSuitable && e.Model.IsLoaded)
            .ThenBy(e => SuitabilityPriority(e.Suitability))
            .ThenByDescending(e => string.Equals(e.Model.Format, "mlx", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(e => (e.Model.ParallelSlots ?? 1) >= 2)
            .ThenByDescending(e => ParseBillionParams(e.Model.ParamsString) ?? 0)
            .ThenBy(e => e.Model.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static bool HasSuitableModel(ModelStage stage, IReadOnlyList<LocalHostModelInfo> models) =>
        Evaluate(stage, models).Any(e => e.IsSuitable);

    /// <summary>Returns the top ready-to-reuse hot model across a host, if one is suitable for text processing.</summary>
    public static EvaluatedHostModel? PickBestHotModel(ModelStage stage, IReadOnlyList<LocalHostModelInfo> models) =>
        Evaluate(stage, models).FirstOrDefault(e => e.IsSuitable && e.Model.IsLoaded);

    /// <summary>Builds hardware-tailored model recommendations for downloading into Bionic / LM Studio / Ollama.</summary>
    public static IReadOnlyList<LocalHostRecommendation> RecommendForMissing(ModelStage stage, HardwareProfile hardware)
    {
        if (stage == ModelStage.Speech)
            return [];

        var highMemory = hardware.TotalMemoryBytes >= 32 * HardwareProfile.GiB && hardware.Backend != ComputeBackend.CpuOnly;

        if (stage == ModelStage.Translator)
        {
            return highMemory
                ? [
                    new LocalHostRecommendation(
                        ModelCatalog.TranslatorUnified,
                        "https://huggingface.co/unsloth/Qwen3.6-35B-A3B-GGUF",
                        "UD-IQ4_XS",
                        "lms get unsloth/Qwen3.6-35B-A3B-GGUF",
                        "qwen3:30b-a3b",
                        "unified"),
                    new LocalHostRecommendation(
                        ModelCatalog.TranslatorIndexQuality,
                        "https://huggingface.co/IndexTeam/Index-Translate-9B-GGUF",
                        "Q4_K_M",
                        "lms get IndexTeam/Index-Translate-9B-GGUF",
                        "qwen3:8b",
                        "translatorFlagship")
                ]
                : [
                    new LocalHostRecommendation(
                        ModelCatalog.TranslatorIndexFast,
                        "https://huggingface.co/IndexTeam/Index-Translate-2B-GGUF",
                        "Q4_K_M",
                        "lms get IndexTeam/Index-Translate-2B-GGUF",
                        "qwen3:4b",
                        "translatorFast"),
                    new LocalHostRecommendation(
                        ModelCatalog.Cleaner,
                        "https://huggingface.co/unsloth/Qwen3.5-4B-GGUF",
                        "Q4_K_M",
                        "lms get unsloth/Qwen3.5-4B-GGUF",
                        "qwen3:4b",
                        "unifiedLight")
                ];
        }

        return highMemory
            ? [
                new LocalHostRecommendation(
                    ModelCatalog.CleanerUnified,
                    "https://huggingface.co/unsloth/Qwen3.6-35B-A3B-GGUF",
                    "UD-IQ4_XS",
                    "lms get unsloth/Qwen3.6-35B-A3B-GGUF",
                    "qwen3:30b-a3b",
                    "unified"),
                new LocalHostRecommendation(
                    ModelCatalog.CleanerQuality,
                    "https://huggingface.co/unsloth/Qwen3.5-9B-GGUF",
                    "Q4_K_M",
                    "lms get unsloth/Qwen3.5-9B-GGUF",
                    "qwen3:8b",
                    "cleanerQuality")
            ]
            : [
                new LocalHostRecommendation(
                    ModelCatalog.Cleaner,
                    "https://huggingface.co/unsloth/Qwen3.5-4B-GGUF",
                    "Q4_K_M",
                    "lms get unsloth/Qwen3.5-4B-GGUF",
                    "qwen3:4b",
                    "cleanerBalanced")
            ];
    }

    private static EvaluatedHostModel EvaluateSpeechSingle(LocalHostModelInfo model)
    {
        var normalized = Normalize($"{model.Key} {model.DisplayName}");
        if (normalized.Contains("qwen3-asr", StringComparison.Ordinal)
            || normalized.Contains("qwen-audio", StringComparison.Ordinal)
            || normalized.Contains("whisper-large-v3", StringComparison.Ordinal)
            || normalized.Contains("sensevoice", StringComparison.Ordinal))
        {
            return new EvaluatedHostModel(model, HostModelSuitability.FlagshipBilingual);
        }

        return new EvaluatedHostModel(model, HostModelSuitability.Compatible);
    }

    private static EvaluatedHostModel EvaluateSingle(ModelStage stage, LocalHostModelInfo model)
    {
        var normalizedArch = (model.Architecture ?? "").Trim().ToLowerInvariant();
        if (normalizedArch.EndsWith("exp", StringComparison.Ordinal)
            && string.Equals(model.Format, "gguf", StringComparison.OrdinalIgnoreCase))
        {
            return new EvaluatedHostModel(model, HostModelSuitability.SuboptimalWarning, "experimentalArch");
        }

        if (model.Reasoning is { IsForcedOn: true })
            return new EvaluatedHostModel(model, HostModelSuitability.SuboptimalWarning, "forcedReasoning");

        if (model.EffectiveContextLength is { } ctx and < MinimumContextTokens)
            return new EvaluatedHostModel(model, HostModelSuitability.SuboptimalWarning, "contextTooSmall");

        var billionParams = ParseBillionParams(model.ParamsString);
        if (billionParams is { } b and < MinimumBillionParams)
            return new EvaluatedHostModel(model, HostModelSuitability.SuboptimalWarning, "tooSmall");

        var normalized = Normalize($"{model.Key} {model.DisplayName}");
        var catalogFamilies = stage == ModelStage.Translator ? TranslatorCatalogFamilies : CleanerCatalogFamilies;
        foreach (var family in catalogFamilies)
        {
            if (normalized.Contains(family, StringComparison.Ordinal))
                return new EvaluatedHostModel(model, HostModelSuitability.CatalogVerified);
        }

        foreach (var flagship in FlagshipBilingualFamilies)
        {
            if (normalized.Contains(flagship, StringComparison.Ordinal) && (billionParams is null || billionParams >= 3.5))
                return new EvaluatedHostModel(model, HostModelSuitability.FlagshipBilingual);
        }

        return new EvaluatedHostModel(model, HostModelSuitability.Compatible);
    }

    internal static double? ParseBillionParams(string? paramsString)
    {
        if (string.IsNullOrWhiteSpace(paramsString))
            return null;

        var moeMatch = MoeParamsRegex().Match(paramsString);
        if (moeMatch.Success
            && double.TryParse(moeMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var experts)
            && double.TryParse(moeMatch.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var perExpert))
        {
            return experts * perExpert;
        }

        var match = BillionParamsRegex().Match(paramsString);
        if (match.Success && double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            return value;

        var millionMatch = MillionParamsRegex().Match(paramsString);
        if (millionMatch.Success && double.TryParse(millionMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var millions))
            return millions / 1000.0;

        return null;
    }

    private static string Normalize(string input) =>
        input.ToLowerInvariant().Replace('_', '-').Replace(' ', '-');

    private static int SuitabilityPriority(HostModelSuitability suitability) => suitability switch
    {
        HostModelSuitability.CatalogVerified => 0,
        HostModelSuitability.FlagshipBilingual => 1,
        HostModelSuitability.Compatible => 2,
        _ => 3
    };

    [GeneratedRegex(@"(\d+(?:\.\d+)?)\s*[xX]\s*(\d+(?:\.\d+)?)\s*[bB]", RegexOptions.CultureInvariant)]
    private static partial Regex MoeParamsRegex();

    [GeneratedRegex(@"(\d+(?:\.\d+)?)\s*[bB]", RegexOptions.CultureInvariant)]
    private static partial Regex BillionParamsRegex();

    [GeneratedRegex(@"(\d+(?:\.\d+)?)\s*[mM]", RegexOptions.CultureInvariant)]
    private static partial Regex MillionParamsRegex();
}
