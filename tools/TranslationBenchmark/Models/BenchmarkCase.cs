using System.Text.Json.Serialization;

namespace TranslationBenchmark.Models;

public sealed record BenchmarkCase(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("domain")] string Domain,
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("reference")] string Reference
);
