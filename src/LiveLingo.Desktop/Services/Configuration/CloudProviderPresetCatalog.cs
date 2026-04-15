namespace LiveLingo.Desktop.Services.Configuration;

public sealed record CloudProviderPreset(
    string Id,
    string DisplayName,
    string BaseUrl,
    string TranslationModelPlaceholder,
    string PostProcessingModelPlaceholder);

public static class CloudProviderPresetCatalog
{
    public static readonly CloudProviderPreset Custom = new(
        "Custom",
        "Custom",
        "https://your-gateway.example.com/v1",
        "custom-translation-model",
        "custom-postprocess-model");

    public static readonly CloudProviderPreset OpenAI = new(
        "OpenAI",
        "OpenAI",
        "https://api.openai.com/v1",
        "gpt-4.1-mini",
        "gpt-4.1-nano");

    public static readonly CloudProviderPreset OpenRouter = new(
        "OpenRouter",
        "OpenRouter",
        "https://openrouter.ai/api/v1",
        "openai/gpt-4.1-mini",
        "openai/gpt-4.1-nano");

    public static readonly CloudProviderPreset Groq = new(
        "Groq",
        "Groq",
        "https://api.groq.com/openai/v1",
        "llama-3.3-70b-versatile",
        "llama-3.1-8b-instant");

    public static IReadOnlyList<CloudProviderPreset> All { get; } =
        [Custom, OpenAI, OpenRouter, Groq];

    public static CloudProviderPreset FindById(string? presetId) =>
        All.FirstOrDefault(p => string.Equals(p.Id, presetId, StringComparison.OrdinalIgnoreCase))
        ?? InferFromBaseUrl(null);

    public static CloudProviderPreset InferFromBaseUrl(string? baseUrl)
    {
        var normalized = NormalizeBaseUrl(baseUrl);
        if (string.IsNullOrWhiteSpace(normalized))
            return OpenAI;

        return All.FirstOrDefault(p =>
                   string.Equals(NormalizeBaseUrl(p.BaseUrl), normalized, StringComparison.OrdinalIgnoreCase))
               ?? Custom;
    }

    public static string NormalizeBaseUrl(string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
            return string.Empty;

        return baseUrl.Trim().TrimEnd('/');
    }
}
