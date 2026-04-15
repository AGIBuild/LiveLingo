namespace LiveLingo.Core.Models;

public enum ModelTaskType
{
    Translation,
    PostProcessing,
    LanguageDetection,
    SpeechToText,
    VoiceActivityDetection
}

public enum ModelProviderKind
{
    LlamaServer,
    OpenAICompatible,
    MarianOnnx,
    FastText,
    WhisperCpp,
    SileroVad
}

public enum ModelRuntimeKind
{
    LlamaServer,
    RemoteHttp,
    OnnxRuntime,
    InProcess
}

public enum ModelExecutionKind
{
    ChatCompletions,
    OnnxTranslation,
    Classification,
    SpeechToText,
    VoiceActivityDetection
}

public sealed record ModelProfile(
    string Id,
    string DisplayName,
    ModelTaskType TaskType,
    ModelProviderKind ProviderKind,
    ModelRuntimeKind RuntimeKind,
    ModelExecutionKind ExecutionKind,
    IReadOnlyList<string> Languages,
    ModelDescriptor Descriptor,
    bool SupportsAllLanguages = false);
