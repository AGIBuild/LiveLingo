using LiveLingo.Core.Dictation;
using LiveLingo.Core.Models;
using LiveLingo.Core.Transcription;

namespace LiveLingo.Core.Speech.Native;

/// <summary>Silero VAD cuts the recording at pauses; Qwen3-ASR recognizes every stretch.</summary>
public sealed class SherpaAudioFileTranscriber(IModelManager models) : IAudioFileTranscriber
{
    /// <summary>A recording is cut at shorter pauses than live dictation so subtitles stay short.</summary>
    private const float VadMinSilenceSeconds = 0.5f;

    public Task<IReadOnlyList<TimedSegment>> TranscribeAsync(
        string path, IReadOnlyList<string> glossary, IProgress<double>? progress, CancellationToken ct) =>
        Task.Run<IReadOnlyList<TimedSegment>>(() =>
        {
            foreach (var model in ModelCatalog.Transcription)
            {
                if (!models.HasAllExpectedLocalAssets(model))
                    throw new ModelNotReadyException(model.Type, model.Id, $"'{model.DisplayName}' is not installed.", "Download it first.");
            }

            var samples = WavReader.ReadFile(path);
            using var vad = new SherpaSileroVad(
                Path.Combine(models.GetModelDirectory(ModelCatalog.SpeechVad.Id), "silero_vad.onnx"), VadMinSilenceSeconds);
            using var recognizer = new SherpaQwen3Recognizer(
                models.GetModelDirectory(ModelCatalog.SpeechSecondPass.Id), hotwords: string.Join("\n", glossary));
            return FileTranscriber.Transcribe(samples, vad, recognizer, progress, ct);
        }, ct);
}
