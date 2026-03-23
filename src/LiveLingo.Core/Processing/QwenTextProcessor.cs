using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace LiveLingo.Core.Processing;

public abstract class QwenTextProcessor : ITextProcessor
{
    private readonly QwenModelHost _host;
    private readonly HttpClient _http;
    private readonly ILogger _logger;

    public abstract string Name { get; }
    protected abstract string SystemPrompt { get; }

    protected QwenTextProcessor(QwenModelHost host, HttpClient http, ILogger logger)
    {
        _host = host;
        _http = http;
        _logger = logger;
    }

    public async Task<string> ProcessAsync(string text, string language, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        try
        {
            var endpoint = await _host.GetOrStartServerAsync(ct);
            var url = $"{endpoint}/v1/chat/completions";

            var requestBody = new
            {
                messages = new[]
                {
                    new { role = "system", content = $"{SystemPrompt} Do not use <think> tags." },
                    new { role = "user", content = text }
                },
                max_tokens = 512,
                temperature = 0.3f,
                top_p = 0.9f,
                stop = new[] { "</s>", "<|im_end|>", "</think>" },
                stream = false
            };

            var response = await _http.PostAsJsonAsync(url, requestBody, ct);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var result = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString()?.Trim() ?? string.Empty;

            // Clean up <think> tags if the model still generated them
            if (result.Contains("</think>"))
            {
                var parts = result.Split("</think>");
                result = parts.Last().Trim();
            }
            else if (result.StartsWith("<think>"))
            {
                // Model generated <think> but didn't finish it
                result = string.Empty;
            }

            if (string.IsNullOrWhiteSpace(result))
            {
                _logger.LogWarning("{Processor} returned empty output, using original text", Name);
                return text;
            }

            return result;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{Processor} failed, falling back to original text", Name);
            return text;
        }
    }

    public void Dispose() { }
}