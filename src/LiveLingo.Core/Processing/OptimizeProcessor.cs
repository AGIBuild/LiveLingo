using Microsoft.Extensions.Logging;

namespace LiveLingo.Core.Processing;

public sealed class OptimizeProcessor : QwenTextProcessor
{
    public override string Name => "optimize";

    protected override string SystemPrompt =>
        "You are a text editor. Improve the grammar, clarity, and readability of the given text while preserving the meaning. Output ONLY the improved text, no explanations.";

    public OptimizeProcessor(QwenModelHost host, ILogger<OptimizeProcessor> logger)
        : base(host, logger) { }
}
