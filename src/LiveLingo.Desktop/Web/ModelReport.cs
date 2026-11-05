using LiveLingo.Core.Inference;

namespace LiveLingo.Desktop.Web;

/// <summary>
/// What the models of one composer window actually did, as an immutable copy for a snapshot: the latest use of each stage
/// and the latest one a provider confirmed (the model that was really loaded, or the name a cloud service returned).
/// </summary>
/// <param name="CloudUsed">A request of this window went to a cloud model; it stays true for the rest of the window.</param>
internal sealed record ModelReport(
    IReadOnlyDictionary<ModelStage, ModelUse> Used,
    IReadOnlyDictionary<ModelStage, ModelUse> Confirmed,
    bool CloudUsed)
{
    public static ModelReport None { get; } = new(new Dictionary<ModelStage, ModelUse>(), new Dictionary<ModelStage, ModelUse>(), false);
}
