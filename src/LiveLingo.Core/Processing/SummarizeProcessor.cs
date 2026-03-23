using Microsoft.Extensions.Logging;

namespace LiveLingo.Core.Processing;

public sealed class SummarizeProcessor : QwenTextProcessor
{
    public override string Name => "summarize";

    protected override string SystemPrompt =>
        "You are a concise text summarizer. Shorten the given text to its key points while preserving the original meaning. Output ONLY the shortened text, no explanations.";

    public SummarizeProcessor(QwenModelHost host, HttpClient http, ILogger<SummarizeProcessor> logger)
        : base(host, http, logger) { }
}
