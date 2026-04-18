using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace LiveLingo.Core.Models;

/// <summary>
/// Parses OpenAI-compatible SSE (Server-Sent Events) streams from
/// <c>/v1/chat/completions</c> with <c>stream=true</c>.
///
/// Yields raw content deltas. The stream terminates on <c>data: [DONE]</c>
/// or when the HTTP response body ends.
/// </summary>
internal static class SseStreamReader
{
    /// <summary>
    /// Reads the response stream and yields each non-empty content delta.
    /// </summary>
    public static async IAsyncEnumerable<string> ReadDeltasAsync(
        Stream responseStream,
        [EnumeratorCancellation] CancellationToken ct)
    {
        using var reader = new StreamReader(responseStream, leaveOpen: true);

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (line is null) yield break;
            if (!line.StartsWith("data: ", StringComparison.Ordinal)) continue;

            var data = line["data: ".Length..];
            if (data == "[DONE]") yield break;

            var delta = ExtractDelta(data);
            if (!string.IsNullOrEmpty(delta))
                yield return delta;
        }
    }

    private static string? ExtractDelta(string sseData)
    {
        try
        {
            using var doc = JsonDocument.Parse(sseData);
            var choices = doc.RootElement.GetProperty("choices");
            if (choices.GetArrayLength() == 0) return null;

            var choice = choices[0];
            if (!choice.TryGetProperty("delta", out var delta)) return null;
            if (!delta.TryGetProperty("content", out var content)) return null;
            if (content.ValueKind != JsonValueKind.String) return null;

            return content.GetString();
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
