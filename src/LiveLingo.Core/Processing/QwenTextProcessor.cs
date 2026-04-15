using LiveLingo.Core.Models;
using Microsoft.Extensions.Logging;

namespace LiveLingo.Core.Processing;

public abstract class QwenTextProcessor : ITextProcessor
{
    private readonly IModelSelector _selector;
    private readonly IModelInvocationService _invocationService;
    private readonly ILogger _logger;

    public abstract string Name { get; }
    protected abstract string SystemPrompt { get; }

    protected QwenTextProcessor(
        IModelSelector selector,
        IModelInvocationService invocationService,
        ILogger logger)
    {
        _selector = selector;
        _invocationService = invocationService;
        _logger = logger;
    }

    public async Task<string> ProcessAsync(string text, string language, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        try
        {
            var profile = _selector.SelectPostProcessingProfile();
            var request = new ModelInvocationRequest(
                profile,
                ModelTaskType.PostProcessing,
                [
                    new ModelChatMessage("system", $"{SystemPrompt} Do not use <think> tags."),
                    new ModelChatMessage("user", text)
                ],
                ModelInvocationOptions.CreateTextProcessingDefaults());

            var result = (await _invocationService.InvokeAsync(request, ct).ConfigureAwait(false)).Text;

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
