using LiveLingo.Core.Models;

namespace LiveLingo.Core.Tests.Models;

public class ObsoleteModelRegistryTests
{
    [Fact]
    public void Ids_IncludesWhisperBase_SoUpgradeRemovesLegacyAsset()
    {
        Assert.Contains("whisper-base", ObsoleteModelRegistry.Ids);
    }

    [Fact]
    public void Ids_DoNotOverlapActiveRegistry_SoCleanupNeverDeletesLiveModels()
    {
        var activeIds = ModelRegistry.AllModels.Select(m => m.Id).ToHashSet(StringComparer.Ordinal);

        foreach (var id in ObsoleteModelRegistry.Ids)
        {
            Assert.DoesNotContain(id, activeIds);
        }
    }

    [Fact]
    public void Ids_AreUniqueAndNonBlank()
    {
        Assert.All(ObsoleteModelRegistry.Ids, id => Assert.False(string.IsNullOrWhiteSpace(id)));
        Assert.Equal(ObsoleteModelRegistry.Ids.Distinct().Count(), ObsoleteModelRegistry.Ids.Count);
    }
}
