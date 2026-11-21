using LiveLingo.Core.Cloud;
using LiveLingo.Core.Dictation;
using LiveLingo.Core.Models;
using LiveLingo.Core.Speech.Native;
using LiveLingo.Core.Translation;
using SherpaOnnx;

namespace LiveLingo.Core.Speech;

public enum SpeechHotwordMode
{
    /// <summary>Model does not accept runtime prompt/vocabulary hotwords (e.g. greedy Zipformer transducer).</summary>
    None,

    /// <summary>Comma-separated system-prompt hotwords bounded under Sherpa Qwen3-ASR's 48-token head threshold.</summary>
    BoundedPromptHead,

    /// <summary>Two-tier DashScope/Bailian P0 (weight 50) + P1 (weight 4) vocabulary up to 2,000 terms.</summary>
    TieredCloudVocabulary,

    /// <summary>Plain-text system/prompt term hint for OpenAI-compatible audio endpoints (such as vLLM Qwen/Qwen3-ASR-1.7B or Whisper).</summary>
    PlainTextHint
}

/// <summary>
/// Model-specific optimization profile governing chunking, endpointing, hotword injection, loop protection and
/// downstream cleanup handoff. Isolates workarounds needed by one model architecture so they never act as negative
/// optimizations on another.
/// </summary>
public sealed record SpeechOptimizationProfile(
    string StrategyId,
    SpeechHotwordMode HotwordMode,
    bool SupportsNativePolish,
    bool SupportsRollingContext,
    int MaxSegmentSeconds,
    bool StripChunkBoundaryPeriod,
    bool HoldDanglingClausePeriod,
    double? MaxCharsPerSecondGuard,
    DictationOptions Dictation)
{
    public bool SupportsHotwords => HotwordMode != SpeechHotwordMode.None;
}

/// <summary>Strategy contract for per-model speech recognition tuning across local ONNX and cloud endpoints.</summary>
public interface ISpeechOptimizationStrategy
{
    string Id { get; }
    SpeechOptimizationProfile Profile { get; }

    IReadOnlyList<string> SelectPromptHotwords(Glossary? glossary, IReadOnlyList<string> fallbackHotwords);

    IReadOnlyDictionary<string, int> SelectCloudVocabulary(Glossary? glossary, IReadOnlyList<string> fallbackHotwords);
}

/// <summary>Extension of <see cref="ISpeechOptimizationStrategy"/> for local <see cref="OfflineRecognizer"/> second-pass models.</summary>
internal interface ILocalSecondPassStrategy : ISpeechOptimizationStrategy
{
    int ResolveThreads(int configuredThreads, int logicalCores);
    OfflineRecognizer Load(string modelDir, string hotwords, int threads);
}

public static class SpeechOptimizationStrategies
{
    private static readonly ILocalSecondPassStrategy Qwen3Asr17B = new Qwen3Asr17BLocalStrategy();
    private static readonly ILocalSecondPassStrategy Qwen3Asr06B = new Qwen3Asr06BLocalStrategy();
    private static readonly ILocalSecondPassStrategy XAsrOffline = new XAsrOfflineLocalStrategy();

    private static readonly IReadOnlyDictionary<string, ILocalSecondPassStrategy> LocalById =
        new[] { Qwen3Asr17B, Qwen3Asr06B, XAsrOffline }
            .ToDictionary(s => s.Id, StringComparer.Ordinal);

    private static readonly ISpeechOptimizationStrategy CloudQwen31Message = new CloudStrategy(
        "qwen-audio-3.1-asr-message",
        new SpeechOptimizationProfile(
            StrategyId: "qwen-audio-3.1-asr-message",
            HotwordMode: SpeechHotwordMode.TieredCloudVocabulary,
            SupportsNativePolish: true,
            SupportsRollingContext: true,
            MaxSegmentSeconds: 0, // Full-duplex WebSocket streaming: client-side slicing is a negative optimization.
            StripChunkBoundaryPeriod: false,
            HoldDanglingClausePeriod: false,
            MaxCharsPerSecondGuard: null,
            Dictation: new DictationOptions { HoldDanglingClausePeriod = false, SecondPassMaxCharsPerSecond = 0 }));

    private static readonly ISpeechOptimizationStrategy CloudQwen31Streaming = new CloudStrategy(
        "qwen-audio-3.1-asr-streaming",
        new SpeechOptimizationProfile(
            StrategyId: "qwen-audio-3.1-asr-streaming",
            HotwordMode: SpeechHotwordMode.TieredCloudVocabulary,
            SupportsNativePolish: false,
            SupportsRollingContext: false,
            MaxSegmentSeconds: 0,
            StripChunkBoundaryPeriod: false,
            HoldDanglingClausePeriod: false,
            MaxCharsPerSecondGuard: null,
            Dictation: new DictationOptions { HoldDanglingClausePeriod = false, SecondPassMaxCharsPerSecond = 0 }));

    private static readonly ISpeechOptimizationStrategy CloudQwen31Flash = new CloudStrategy(
        "qwen-audio-3.1-asr-flash",
        new SpeechOptimizationProfile(
            StrategyId: "qwen-audio-3.1-asr-flash",
            HotwordMode: SpeechHotwordMode.TieredCloudVocabulary,
            SupportsNativePolish: true,
            SupportsRollingContext: true,
            MaxSegmentSeconds: 45, // Fits comfortably inside 7,168 input audio tokens.
            StripChunkBoundaryPeriod: false,
            HoldDanglingClausePeriod: false,
            MaxCharsPerSecondGuard: null,
            Dictation: new DictationOptions { HoldDanglingClausePeriod = false, SecondPassMaxCharsPerSecond = 0 }));

    private static readonly ISpeechOptimizationStrategy CloudQwen30Legacy = new CloudStrategy(
        "qwen-audio-3.0-asr",
        new SpeechOptimizationProfile(
            StrategyId: "qwen-audio-3.0-asr",
            HotwordMode: SpeechHotwordMode.TieredCloudVocabulary,
            SupportsNativePolish: false,
            SupportsRollingContext: false,
            MaxSegmentSeconds: 45,
            StripChunkBoundaryPeriod: false,
            HoldDanglingClausePeriod: false,
            MaxCharsPerSecondGuard: null,
            Dictation: new DictationOptions { HoldDanglingClausePeriod = false, SecondPassMaxCharsPerSecond = 0 }));

    private static readonly ISpeechOptimizationStrategy CloudOpenAiCompatible = new CloudStrategy(
        "openai-compatible-asr",
        new SpeechOptimizationProfile(
            StrategyId: "openai-compatible-asr",
            HotwordMode: SpeechHotwordMode.PlainTextHint,
            SupportsNativePolish: false, // Verbatim ASR (including vLLM Qwen/Qwen3-ASR-1.7B, qwen3-asr-flash, Whisper) keeps LLM cleanup active.
            SupportsRollingContext: true,
            MaxSegmentSeconds: 45,
            StripChunkBoundaryPeriod: false,
            HoldDanglingClausePeriod: false,
            MaxCharsPerSecondGuard: null,
            Dictation: new DictationOptions { HoldDanglingClausePeriod = false, SecondPassMaxCharsPerSecond = 0 }));

    public static ISpeechOptimizationStrategy ForModel(string modelId) =>
        LocalById.TryGetValue(modelId, out var local) ? local : Qwen3Asr06B;

    internal static ILocalSecondPassStrategy ForLocal(string modelId) =>
        LocalById.TryGetValue(modelId, out var local) ? local : Qwen3Asr06B;

    public static ISpeechOptimizationStrategy ForCloud(SpeechCloudProtocol protocol, string wireModel)
    {
        var lower = wireModel.Trim().ToLowerInvariant();
        if (protocol == SpeechCloudProtocol.QwenAudioWebSocket)
        {
            if (lower.Contains("message", StringComparison.Ordinal) && lower.Contains("3.1", StringComparison.Ordinal))
                return CloudQwen31Message;
            return CloudQwen31Streaming;
        }

        if (protocol == SpeechCloudProtocol.QwenAudioHttp)
        {
            return lower.Contains("3.1", StringComparison.Ordinal) ? CloudQwen31Flash : CloudQwen30Legacy;
        }

        return CloudOpenAiCompatible;
    }

    /// <summary>
    /// Local Qwen/Qwen3-ASR-1.7B (INT8 ONNX):
    /// - Verbatim ASR (no native disfluency polish -> must NOT bypass CleanupScheduler / PolishScheduler).
    /// - Larger 28-layer Qwen3 LM benefits from intact multi-clause context (6.5s soft / 11s hard / 15s chunk cap = 195 audio tokens &lt;&lt; 512 KV).
    /// - Raises MaxNewTokens to 224 so dense bilingual code-switched segments never truncate at Sherpa's 128-token default.
    /// - Scales CPU threads up to 8 on multi-core hosts to feed the 2.04 GB INT8 decoder bandwidth, with a 25s stop drain timeout.
    /// </summary>
    private sealed class Qwen3Asr17BLocalStrategy : ILocalSecondPassStrategy
    {
        public string Id => ModelCatalog.SpeechQuality.Id;

        public SpeechOptimizationProfile Profile { get; } = new(
            StrategyId: ModelCatalog.SpeechQuality.Id,
            HotwordMode: SpeechHotwordMode.BoundedPromptHead,
            SupportsNativePolish: false,
            SupportsRollingContext: false,
            MaxSegmentSeconds: 15,
            StripChunkBoundaryPeriod: true,
            HoldDanglingClausePeriod: true,
            MaxCharsPerSecondGuard: 15.0,
            Dictation: new DictationOptions
            {
                SoftMaxUtteranceMs = 6_500,
                HardMaxUtteranceMs = 11_000,
                StopTimeoutMs = 25_000,
                SecondPassMaxCharsPerSecond = 15.0,
                HoldDanglingClausePeriod = true
            });

        public int ResolveThreads(int configuredThreads, int logicalCores) =>
            configuredThreads > 0 ? configuredThreads : Math.Clamp(logicalCores / 2, 4, 8);

        public IReadOnlyList<string> SelectPromptHotwords(Glossary? glossary, IReadOnlyList<string> fallbackHotwords) =>
            glossary is not null ? glossary.SpeechHotwords() : Glossary.BoundHotwords(fallbackHotwords);

        public IReadOnlyDictionary<string, int> SelectCloudVocabulary(Glossary? glossary, IReadOnlyList<string> fallbackHotwords) =>
            new Dictionary<string, int>(StringComparer.Ordinal);

        public OfflineRecognizer Load(string modelDir, string hotwords, int threads) =>
            SherpaQwen3Recognizer.Load(modelDir, hotwords, threads, maxNewTokens: 224);
    }

    /// <summary>
    /// Local Qwen/Qwen3-ASR-0.6B (INT8 ONNX):
    /// - Compact 0.6B LM tuned for fast turnaround on 8–16 GB devices (4s soft / 8s hard / 13s chunk cap).
    /// - Bounded 36-token prompt-head hotwords and 192 max_new_tokens within 512 KV.
    /// </summary>
    private sealed class Qwen3Asr06BLocalStrategy : ILocalSecondPassStrategy
    {
        public string Id => ModelCatalog.SpeechSecondPass.Id;

        public SpeechOptimizationProfile Profile { get; } = new(
            StrategyId: ModelCatalog.SpeechSecondPass.Id,
            HotwordMode: SpeechHotwordMode.BoundedPromptHead,
            SupportsNativePolish: false,
            SupportsRollingContext: false,
            MaxSegmentSeconds: 13,
            StripChunkBoundaryPeriod: true,
            HoldDanglingClausePeriod: true,
            MaxCharsPerSecondGuard: 15.0,
            Dictation: new DictationOptions
            {
                SoftMaxUtteranceMs = 4_000,
                HardMaxUtteranceMs = 8_000,
                StopTimeoutMs = 15_000,
                SecondPassMaxCharsPerSecond = 15.0,
                HoldDanglingClausePeriod = true
            });

        public int ResolveThreads(int configuredThreads, int logicalCores) =>
            configuredThreads > 0 ? configuredThreads : Math.Clamp(logicalCores / 2, 4, 6);

        public IReadOnlyList<string> SelectPromptHotwords(Glossary? glossary, IReadOnlyList<string> fallbackHotwords) =>
            glossary is not null ? glossary.SpeechHotwords() : Glossary.BoundHotwords(fallbackHotwords);

        public IReadOnlyDictionary<string, int> SelectCloudVocabulary(Glossary? glossary, IReadOnlyList<string> fallbackHotwords) =>
            new Dictionary<string, int>(StringComparer.Ordinal);

        public OfflineRecognizer Load(string modelDir, string hotwords, int threads) =>
            SherpaQwen3Recognizer.Load(modelDir, hotwords, threads, maxNewTokens: 192);
    }

    /// <summary>
    /// Local X-ASR Offline (INT8 Zipformer Transducer):
    /// - Frame-synchronous non-autoregressive transducer: has NO 512-token KV cache cliff and cannot enter LLM token loops.
    /// - Avoids 0.6B's aggressive 4s/13s slicing, chunk-period stripping and chars/sec LLM loop rejection.
    /// </summary>
    private sealed class XAsrOfflineLocalStrategy : ILocalSecondPassStrategy
    {
        public string Id => ModelCatalog.SpeechFast.Id;

        public SpeechOptimizationProfile Profile { get; } = new(
            StrategyId: ModelCatalog.SpeechFast.Id,
            HotwordMode: SpeechHotwordMode.None,
            SupportsNativePolish: false,
            SupportsRollingContext: false,
            MaxSegmentSeconds: 30,
            StripChunkBoundaryPeriod: false,
            HoldDanglingClausePeriod: true,
            MaxCharsPerSecondGuard: null,
            Dictation: new DictationOptions
            {
                SoftMaxUtteranceMs = 8_000,
                HardMaxUtteranceMs = 15_000,
                StopTimeoutMs = 10_000,
                SecondPassMaxCharsPerSecond = 0, // Disable autoregressive repetition-loop false positives.
                HoldDanglingClausePeriod = true
            });

        public int ResolveThreads(int configuredThreads, int logicalCores) =>
            configuredThreads > 0 ? configuredThreads : Math.Clamp(logicalCores / 4, 2, 4);

        public IReadOnlyList<string> SelectPromptHotwords(Glossary? glossary, IReadOnlyList<string> fallbackHotwords) => [];

        public IReadOnlyDictionary<string, int> SelectCloudVocabulary(Glossary? glossary, IReadOnlyList<string> fallbackHotwords) =>
            new Dictionary<string, int>(StringComparer.Ordinal);

        public OfflineRecognizer Load(string modelDir, string hotwords, int threads)
        {
            var config = new OfflineRecognizerConfig();
            config.FeatConfig.SampleRate = AudioFormat.SampleRate;
            config.FeatConfig.FeatureDim = 80;
            config.ModelConfig.NumThreads = threads;
            config.ModelConfig.Provider = "cpu";
            config.DecodingMethod = "greedy_search";
            config.ModelConfig.Transducer.Encoder = Path.Combine(modelDir, "encoder-epoch-99-avg-1.int8.onnx");
            config.ModelConfig.Transducer.Decoder = Path.Combine(modelDir, "decoder-epoch-99-avg-1.onnx");
            config.ModelConfig.Transducer.Joiner = Path.Combine(modelDir, "joiner-epoch-99-avg-1.int8.onnx");
            config.ModelConfig.Tokens = Path.Combine(modelDir, "tokens.txt");
            return new OfflineRecognizer(config);
        }
    }

    private sealed class CloudStrategy(string id, SpeechOptimizationProfile profile) : ISpeechOptimizationStrategy
    {
        public string Id { get; } = id;
        public SpeechOptimizationProfile Profile { get; } = profile;

        public IReadOnlyList<string> SelectPromptHotwords(Glossary? glossary, IReadOnlyList<string> fallbackHotwords)
        {
            if (Profile.HotwordMode == SpeechHotwordMode.None)
                return [];
            var source = glossary?.Terms ?? fallbackHotwords;
            return source.Count <= 80 ? source : [.. source.Take(80)];
        }

        public IReadOnlyDictionary<string, int> SelectCloudVocabulary(Glossary? glossary, IReadOnlyList<string> fallbackHotwords)
        {
            if (Profile.HotwordMode != SpeechHotwordMode.TieredCloudVocabulary)
                return new Dictionary<string, int>(StringComparer.Ordinal);
            return glossary is not null
                ? glossary.CloudSpeechVocabulary()
                : Glossary.BuildCloudVocabulary(fallbackHotwords, []);
        }
    }
}
