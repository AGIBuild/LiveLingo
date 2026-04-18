namespace TranslationBenchmark.Models;

public sealed record ModelEndpointConfig(
    string Name,
    string BaseUrl,
    string? ApiKey = null,
    string? ModelId = null,
    int TimeoutSeconds = 120,
    /// <summary>Prompt variant: "default" | "gemma" | "gemma4-tagged" | "gemma4-concise" | "gemma4-structured" | "minimal"</summary>
    string PromptVariant = "default",
    /// <summary>If true, call the streaming endpoint and measure first-token latency alongside total latency.</summary>
    bool Streaming = false
);
