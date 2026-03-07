using LLama;
using LLama.Common;
using LLama.Sampling;
using Microsoft.Extensions.Logging;

namespace LiveLingo.Core.Processing;

public abstract class QwenTextProcessor : ITextProcessor
{
    private readonly QwenModelHost _host;
    private readonly ILogger _logger;

    public abstract string Name { get; }
    protected abstract string SystemPrompt { get; }

    protected QwenTextProcessor(QwenModelHost host, ILogger logger)
    {
        _host = host;
        _logger = logger;
    }

    public async Task<string> ProcessAsync(string text, string language, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        try
        {
            var weights = await _host.GetWeightsAsync(ct);
            var modelParams = new ModelParams(_host.ModelPath) { ContextSize = 2048 };
            var executor = new StatelessExecutor(weights, modelParams);

            var inferenceParams = new InferenceParams
            {
                MaxTokens = 512,
                AntiPrompts = ["</s>", "\n\n", "<|im_end|>"],
                SamplingPipeline = new DefaultSamplingPipeline
                {
                    Temperature = 0.3f,
                    TopP = 0.9f,
                }
            };

            var prompt = $"<|im_start|>system\n{SystemPrompt}<|im_end|>\n<|im_start|>user\n{text}<|im_end|>\n<|im_start|>assistant\n";

            var output = new List<string>();
            await foreach (var token in executor.InferAsync(prompt, inferenceParams, ct))
            {
                output.Add(token);
                if (string.Concat(output).Length > text.Length * 3)
                    break;
            }

            var result = string.Concat(output).Trim();

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
