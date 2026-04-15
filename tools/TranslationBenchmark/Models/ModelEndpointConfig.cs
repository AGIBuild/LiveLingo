namespace TranslationBenchmark.Models;

public sealed record ModelEndpointConfig(
    string Name,
    string BaseUrl,
    string? ApiKey = null,
    string? ModelId = null,
    int TimeoutSeconds = 120,
    /// <summary>Prompt variant: "default" | "gemma" | "minimal"</summary>
    string PromptVariant = "default"
);
