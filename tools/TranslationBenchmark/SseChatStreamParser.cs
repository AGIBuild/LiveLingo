using System.Text.Json.Nodes;

namespace TranslationBenchmark;

/// <summary>
/// Pure-function parser for OpenAI-compatible SSE chat completion streams.
/// Extracted so the streaming path in <see cref="TranslationClient"/> can be
/// unit-tested without a live HTTP endpoint.
///
/// Protocol (per OpenAI / llama.cpp / Ollama OpenAI-compat layer):
///   data: {"choices":[{"delta":{"content":"Hello"}}]}\n\n
///   data: {"choices":[{"delta":{"content":" world"}}]}\n\n
///   data: [DONE]\n\n
/// </summary>
public static class SseChatStreamParser
{
    /// <summary>
    /// Returns the incremental content deltas from an OpenAI-compatible SSE
    /// chat stream, in arrival order. Lines that are not <c>data:</c> payloads,
    /// empty keep-alives, or <c>[DONE]</c> markers are skipped; malformed
    /// payloads raise <see cref="InvalidOperationException"/>.
    /// </summary>
    public static async IAsyncEnumerable<string> EnumerateDeltasAsync(
        Stream stream,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        using var reader = new StreamReader(stream);
        while (true)
        {
            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (line is null) break;
            if (line.Length == 0) continue;
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;

            var payload = line.AsSpan(5).TrimStart().ToString();
            if (payload.Length == 0 || payload == "[DONE]") continue;

            JsonNode? node;
            try
            {
                node = JsonNode.Parse(payload);
            }
            catch (System.Text.Json.JsonException ex)
            {
                throw new InvalidOperationException($"Malformed SSE chunk: {payload}", ex);
            }

            var delta = node?["choices"]?[0]?["delta"]?["content"]?.GetValue<string?>();
            if (!string.IsNullOrEmpty(delta))
                yield return delta;
        }
    }
}
