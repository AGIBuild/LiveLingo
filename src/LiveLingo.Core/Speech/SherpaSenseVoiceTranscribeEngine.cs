using LiveLingo.Core.Models;
using Microsoft.Extensions.Logging;
using SherpaOnnx;

namespace LiveLingo.Core.Speech;

/// <summary>
/// Speech-to-text engine backed by sherpa-onnx running the SenseVoice Small int8 bundle
/// (中 / 粤 / 英 / 日 / 韩 + on-model language identification).
///
/// Lifecycle, audio plumbing and disposal live in <see cref="SherpaOfflineRecognizerEngineBase"/>.
/// SenseVoice's language is bound at recognizer-creation time. We initialise it in <c>auto</c>
/// mode so a single recognizer can serve every supported language without reloading weights —
/// the .NET binding does not surface the detected language back, so the caller-supplied hint
/// is echoed through to <see cref="SpeechTranscriptionResult.Language"/>.
/// </summary>
internal sealed class SherpaSenseVoiceTranscribeEngine : SherpaOfflineRecognizerEngineBase
{
    public SherpaSenseVoiceTranscribeEngine(
        IModelManager modelManager,
        CoreOptions coreOptions,
        ILogger<SherpaSenseVoiceTranscribeEngine>? logger = null)
        : base(modelManager, coreOptions, logger)
    {
    }

    public override IReadOnlyCollection<string> SupportedModelIds { get; } =
        [ModelRegistry.SherpaSenseVoiceSmallInt8.Id];

    protected override ModelDescriptor Descriptor => ModelRegistry.SherpaSenseVoiceSmallInt8;

    protected override void ConfigureRecognizer(ref OfflineRecognizerConfig config, string modelDir)
    {
        config.ModelConfig.SenseVoice.Model = Path.Combine(modelDir, "model.int8.onnx");
        // "auto" lets SenseVoice pick from {zh, en, ja, ko, yue} per utterance — required so
        // a single recognizer can serve every supported language without re-loading weights.
        config.ModelConfig.SenseVoice.Language = "auto";
        // ITN turns on punctuation (commas, full stops, question marks) — matches the
        // formatting downstream translation expects.
        config.ModelConfig.SenseVoice.UseInverseTextNormalization = 1;
    }
}
