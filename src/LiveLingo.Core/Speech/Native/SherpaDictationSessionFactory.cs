using LiveLingo.Core.Dictation;
using LiveLingo.Core.Drafting;
using LiveLingo.Core.Models;

namespace LiveLingo.Core.Speech.Native;

/// <summary>Builds the validated pipeline: X-ASR 480 ms live text, Silero VAD, Qwen3-ASR second pass.</summary>
public sealed class SherpaDictationSessionFactory(IModelManager models) : IDictationSessionFactory
{
    /// <summary>The detector reports a span after this much silence; shorter than the hard pause so pauses are seen early.</summary>
    private const float VadMinSilenceSeconds = 0.3f;

    public Task<DictationSession> CreateAsync(DraftSession draft, IReadOnlyList<string> glossary, CancellationToken ct) =>
        Task.Run(() =>
        {
            foreach (var model in ModelCatalog.Speech)
            {
                if (!models.HasAllExpectedLocalAssets(model))
                    throw new ModelNotReadyException(model.Type, model.Id, $"'{model.DisplayName}' is not installed.", "Download it first.");
            }

            var live = new SherpaLiveRecognizer(models.GetModelDirectory(ModelCatalog.SpeechLive.Id));
            var vad = new SherpaSileroVad(
                Path.Combine(models.GetModelDirectory(ModelCatalog.SpeechVad.Id), "silero_vad.onnx"),
                VadMinSilenceSeconds);
            var secondPass = new SherpaQwen3Recognizer(
                models.GetModelDirectory(ModelCatalog.SpeechSecondPass.Id), hotwords: string.Join("\n", glossary));
            return new DictationSession(live, vad, secondPass, draft);
        }, ct);
}
