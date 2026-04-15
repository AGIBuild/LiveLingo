namespace LiveLingo.Core.Models;

internal static class OpenAICompatibleEndpoints
{
    public static string BuildChatCompletionsEndpoint(string endpoint)
    {
        var trimmed = endpoint.TrimEnd('/');
        return trimmed.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : $"{trimmed}/chat/completions";
    }

    public static string BuildModelsEndpoint(string endpoint)
    {
        var trimmed = endpoint.TrimEnd('/');
        return trimmed.EndsWith("/models", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : $"{trimmed}/models";
    }
}
