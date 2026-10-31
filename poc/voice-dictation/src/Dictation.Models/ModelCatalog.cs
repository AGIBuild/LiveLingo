using Dictation.Asr.Sherpa;
using Dictation.Cleanup.Llama;
using Dictation.Core;

namespace Dictation.Models;

/// <summary>Maps short names used on the command line to pinned model files.</summary>
public static class ModelCatalog
{
    public static readonly string Root = Environment.GetEnvironmentVariable("VOICE_POC_MODELS")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "voice-poc", "models");

    private static string Dir(string name) => Path.Combine(Root, name);

    public static ILiveRecognizer Live(string key) => key switch
    {
        "xasr-160" => new SherpaLiveRecognizer(Dir("sherpa-onnx-x-asr-160ms-streaming-zipformer-transducer-zh-en-punct-int8-2026-06-05"), tailPaddingMs: 500),
        "xasr-480" => new SherpaLiveRecognizer(Dir("sherpa-onnx-x-asr-480ms-streaming-zipformer-transducer-zh-en-punct-int8-2026-06-05"), tailPaddingMs: 800),
        "xasr-960" => new SherpaLiveRecognizer(Dir("sherpa-onnx-x-asr-960ms-streaming-zipformer-transducer-zh-en-punct-int8-2026-06-05"), tailPaddingMs: 1300),
        _ => throw new ArgumentException($"Unknown live recognizer '{key}'"),
    };

    /// <summary>Speech probability above which Silero VAD calls a frame speech. Lower finds quieter and far-field speech.</summary>
    public const float DefaultVadThreshold = 0.35f;

    public static IVoiceActivityDetector Vad(float minSilenceSeconds, float threshold = DefaultVadThreshold) =>
        new SherpaVad(Path.Combine(Root, "silero_vad.onnx"), minSilenceSeconds, threshold);

    /// <summary>Second-pass recognizers that can be selected by key, "none" meaning live text only.</summary>
    public static readonly IReadOnlyList<string> SecondPassKeys = ["xasr", "qwen3", "funasr", "sensevoice", "none"];

    public const string DefaultSecondPass = "qwen3";

    public static ISegmentRecognizer? SecondPass(string key, string hotwords = "") => key switch
    {
        "none" => null,
        "xasr" => SherpaOfflineRecognizer.XAsr(Dir("sherpa-onnx-x-asr-zipformer-transducer-zh-en-punct-int8-2026-06-03")),
        "qwen3" => SherpaOfflineRecognizer.Qwen3Asr(Dir("sherpa-onnx-qwen3-asr-0.6B-int8-2026-03-25"), hotwords: hotwords),
        "funasr" => SherpaOfflineRecognizer.FunAsrNano(Dir("sherpa-onnx-funasr-nano-int8-2025-12-30")),
        "sensevoice" => SherpaOfflineRecognizer.SenseVoice(Dir("sherpa-onnx-sense-voice-zh-en-ja-ko-yue-int8-2025-09-09")),
        _ => throw new ArgumentException($"Unknown second pass '{key}'"),
    };

    public static ITextCleaner? Cleaner(string key, int maxTokensCap = 320, int contextSize = 2048) => key switch
    {
        "none" => null,
        "qwen35-4b" => new LlamaCleaner(new LlamaCleanerOptions
        {
            ModelPath = Path.Combine(Root, "Qwen3.5-4B-Q4_K_M.gguf"),
            EmptyThinkBlock = true,
            MaxTokensCap = maxTokensCap,
            ContextSize = contextSize,
        }),
        "qwen3-4b-2507" => new LlamaCleaner(new LlamaCleanerOptions
        {
            ModelPath = Path.Combine(Root, "Qwen3-4B-Instruct-2507-Q4_K_M.gguf"),
            EmptyThinkBlock = false,
            MaxTokensCap = maxTokensCap,
            ContextSize = contextSize,
        }),
        _ => throw new ArgumentException($"Unknown cleaner '{key}'"),
    };
}
