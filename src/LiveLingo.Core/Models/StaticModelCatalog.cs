namespace LiveLingo.Core.Models;

public sealed class StaticModelCatalog : IModelCatalog
{
    private static readonly string[] LlamaSupportedLanguages =
        ["zh", "en", "ja", "ko", "fr", "de", "es", "ru", "ar", "pt"];

    private readonly IReadOnlyList<ModelProfile> _profiles = ModelRegistry.AllModels
        .Select(MapDescriptor)
        .ToArray();

    public IReadOnlyList<ModelProfile> AllProfiles => _profiles;

    public IReadOnlyList<ModelProfile> GetProfiles(ModelTaskType taskType) =>
        _profiles.Where(p => p.TaskType == taskType).ToArray();

    public ModelProfile? FindById(string id) =>
        _profiles.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

    private static ModelProfile MapDescriptor(ModelDescriptor descriptor)
    {
        if (descriptor.Id.StartsWith("opus-mt-", StringComparison.OrdinalIgnoreCase))
        {
            var languages = descriptor.Id["opus-mt-".Length..]
                .Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            return new ModelProfile(
                descriptor.Id,
                descriptor.DisplayName,
                ModelTaskType.Translation,
                ModelProviderKind.MarianOnnx,
                ModelRuntimeKind.OnnxRuntime,
                ModelExecutionKind.OnnxTranslation,
                languages,
                descriptor);
        }

        return descriptor.Type switch
        {
            ModelType.Translation when IsLocalLlamaModel(descriptor) =>
                new ModelProfile(
                    descriptor.Id,
                    descriptor.DisplayName,
                    ModelTaskType.Translation,
                    ModelProviderKind.LlamaServer,
                    ModelRuntimeKind.LlamaServer,
                    ModelExecutionKind.ChatCompletions,
                    LlamaSupportedLanguages,
                    descriptor),

            ModelType.PostProcessing when IsLocalLlamaModel(descriptor) =>
                new ModelProfile(
                    descriptor.Id,
                    descriptor.DisplayName,
                    ModelTaskType.PostProcessing,
                    ModelProviderKind.LlamaServer,
                    ModelRuntimeKind.LlamaServer,
                    ModelExecutionKind.ChatCompletions,
                    LlamaSupportedLanguages,
                    descriptor),

            ModelType.LanguageDetection =>
                new ModelProfile(
                    descriptor.Id,
                    descriptor.DisplayName,
                    ModelTaskType.LanguageDetection,
                    ModelProviderKind.FastText,
                    ModelRuntimeKind.InProcess,
                    ModelExecutionKind.Classification,
                    [],
                    descriptor),

            ModelType.SpeechToText =>
                new ModelProfile(
                    descriptor.Id,
                    descriptor.DisplayName,
                    ModelTaskType.SpeechToText,
                    ModelProviderKind.SherpaOnnx,
                    ModelRuntimeKind.OnnxRuntime,
                    ModelExecutionKind.SpeechToText,
                    [],
                    descriptor),

            ModelType.VoiceActivityDetection =>
                new ModelProfile(
                    descriptor.Id,
                    descriptor.DisplayName,
                    ModelTaskType.VoiceActivityDetection,
                    ModelProviderKind.SileroVad,
                    ModelRuntimeKind.OnnxRuntime,
                    ModelExecutionKind.VoiceActivityDetection,
                    [],
                    descriptor),

            _ => new ModelProfile(
                descriptor.Id,
                descriptor.DisplayName,
                ModelTaskType.Translation,
                ModelProviderKind.LlamaServer,
                ModelRuntimeKind.LlamaServer,
                ModelExecutionKind.ChatCompletions,
                LlamaSupportedLanguages,
                descriptor)
        };
    }

    /// <summary>
    /// Returns true for any GGUF-backed model that runs through llama-server
    /// (Gemma, Qwen, Llama, Mistral, …). Excludes Marian ONNX and other dedicated runtimes.
    /// </summary>
    private static bool IsLocalLlamaModel(ModelDescriptor descriptor) =>
        descriptor.ChatTemplate != LocalModelChatTemplate.Generic
        || descriptor.DownloadUrl.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase);
}
