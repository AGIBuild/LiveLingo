namespace LiveLingo.Core.Models;

/// <summary>
/// Ordered list of translation candidates that an <see cref="ITranslationInvoker"/>
/// will try in sequence. The first candidate is the primary choice; subsequent
/// candidates are runtime fallbacks invoked when the primary fails, times out,
/// or produces output that fails a post-run quality guard.
/// </summary>
public sealed record TranslationRoutePlan(IReadOnlyList<TranslationRouteCandidate> Candidates)
{
    public TranslationRouteCandidate Primary => Candidates.Count > 0
        ? Candidates[0]
        : throw new InvalidOperationException("Route plan contains no candidates.");

    public bool HasCandidates => Candidates.Count > 0;
}

public sealed record TranslationRouteCandidate(
    ModelProfile Profile,
    TranslationRouteTier Tier,
    TimeSpan FirstTokenBudget);

/// <summary>
/// Coarse classification used for telemetry, logging, and deciding first-token
/// budgets. A plan normally contains at most one candidate per tier but is not
/// required to — e.g. a user may configure a cloud-to-cloud fallback later.
/// </summary>
public enum TranslationRouteTier
{
    /// <summary>Built-in llama.cpp catalog model (local GGUF).</summary>
    Local,

    /// <summary>User-managed Ollama daemon.</summary>
    Ollama,

    /// <summary>Remote OpenAI-compatible cloud provider.</summary>
    Cloud
}
