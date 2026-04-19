using LiveLingo.Core.Models;
using Microsoft.Extensions.Logging;
using SherpaOnnx;

namespace LiveLingo.Core.Speech;

/// <summary>
/// Speech-to-text engine backed by sherpa-onnx running the Cohere Transcribe 14-language int8
/// bundle. Lifecycle, audio plumbing and disposal live in
/// <see cref="SherpaOfflineRecognizerEngineBase"/>; this class only declares the descriptor
/// it serves and how to wire encoder/decoder into <see cref="OfflineRecognizerConfig"/>.
/// </summary>
internal sealed class SherpaCohereTranscribeEngine : SherpaOfflineRecognizerEngineBase
{
    public SherpaCohereTranscribeEngine(
        IModelManager modelManager,
        CoreOptions coreOptions,
        ILogger<SherpaCohereTranscribeEngine>? logger = null)
        : base(modelManager, coreOptions, logger)
    {
    }

    public override IReadOnlyCollection<string> SupportedModelIds { get; } =
        [ModelRegistry.SherpaCohereTranscribe14LangInt8.Id];

    protected override ModelDescriptor Descriptor => ModelRegistry.SherpaCohereTranscribe14LangInt8;

    protected override void ConfigureRecognizer(ref OfflineRecognizerConfig config, string modelDir)
    {
        config.ModelConfig.CohereTranscribe.Encoder = Path.Combine(modelDir, "encoder.int8.onnx");
        config.ModelConfig.CohereTranscribe.Decoder = Path.Combine(modelDir, "decoder.int8.onnx");
        // Cohere Transcribe ships built-in punctuation + inverse text normalization;
        // both are off by default and cost nothing at inference time, so always enable
        // them to deliver readable, formatted text to translation downstream.
        config.ModelConfig.CohereTranscribe.UsePunct = 1;
        config.ModelConfig.CohereTranscribe.UseItn = 1;
    }

    /// <summary>
    /// Cohere accepts a per-utterance language hint via stream option; pass it through when set.
    /// </summary>
    protected override void ApplyLanguageHint(OfflineStream stream, string? language)
    {
        var code = NormalizeLanguageCode(language);
        if (code.Length == 0)
            return;
        stream.SetOption("language", code);
    }
}
