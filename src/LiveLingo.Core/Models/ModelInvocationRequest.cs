namespace LiveLingo.Core.Models;

public sealed record ModelChatMessage(string Role, string Content);

public sealed record ModelInvocationOptions(
    int MaxTokens,
    float Temperature,
    float TopP,
    IReadOnlyList<string> StopSequences,
    bool Stream)
{
    public static readonly string[] DefaultStopSequences = ["</s>", "<|im_end|>", "</think>"];

    public static ModelInvocationOptions CreateTranslationDefaults() =>
        new(
            MaxTokens: 512,
            Temperature: 0.1f,
            TopP: 0.95f,
            StopSequences: DefaultStopSequences,
            Stream: false);

    public static ModelInvocationOptions CreateTextProcessingDefaults() =>
        new(
            MaxTokens: 512,
            Temperature: 0.3f,
            TopP: 0.9f,
            StopSequences: DefaultStopSequences,
            Stream: false);
}

public sealed record ModelInvocationRequest(
    ModelProfile Profile,
    ModelTaskType TaskType,
    IReadOnlyList<ModelChatMessage> Messages,
    ModelInvocationOptions Options);

public sealed record ModelInvocationResult(string Text);

public sealed record ModelRuntimeSession(
    ModelProfile Profile,
    ModelTaskType TaskType,
    string Endpoint);
