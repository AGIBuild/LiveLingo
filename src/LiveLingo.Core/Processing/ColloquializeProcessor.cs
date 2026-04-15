using LiveLingo.Core.Models;
using Microsoft.Extensions.Logging;

namespace LiveLingo.Core.Processing;

public sealed class ColloquializeProcessor : QwenTextProcessor
{
    public override string Name => "colloquialize";

    protected override string SystemPrompt =>
        "You are a casual writing assistant. Rewrite the given text in a friendly, informal chat tone suitable for messaging apps like Slack or Discord. Output ONLY the rewritten text, no explanations.";

    public ColloquializeProcessor(
        IModelSelector selector,
        IModelInvocationService invocationService,
        ILogger<ColloquializeProcessor> logger)
        : base(selector, invocationService, logger) { }
}
