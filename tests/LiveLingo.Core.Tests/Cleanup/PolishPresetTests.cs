using LiveLingo.Core.Cleanup;

namespace LiveLingo.Core.Tests.Cleanup;

public sealed class PolishPresetTests
{
    [Fact]
    public void BuiltIns_HaveFiveOrthogonalPresetsInShortcutOrder()
    {
        var ids = PolishPresets.BuiltIns.Select(p => p.Id).ToArray();
        Assert.Equal(["ai-task", "natural", "formal", "todo", "report"], ids);
        Assert.Equal(["1", "2", "3", "4", "5"], PolishPresets.BuiltIns.Select(p => p.ShortcutKey).ToArray());
        Assert.All(PolishPresets.BuiltIns, p => Assert.True(PolishStyle.IsUsable(p.EffectiveInstructions)));
    }

    [Fact]
    public void BuiltInPreset_UsesShippedInstructionsUntilCustomized()
    {
        var natural = PolishPresets.FindBuiltIn(PolishPresets.NaturalId);
        Assert.True(natural.IsDefaultNatural);
        Assert.False(natural.IsCustomized);
        Assert.Equal(PolishStyle.Default, natural.EffectiveInstructions);

        var tuned = natural with { CustomInstructions = "保留全部口吻，仅修正明显错别字并删除重复语气词。" };
        Assert.True(tuned.IsCustomized);
        Assert.False(tuned.IsDefaultNatural);
        Assert.Equal("保留全部口吻，仅修正明显错别字并删除重复语气词。", tuned.EffectiveInstructions);
    }

    [Fact]
    public void Resolve_FallsBackToNaturalWhenUnknownOrBlank()
    {
        Assert.Equal(PolishPresets.NaturalId, PolishPresets.Resolve(null).Id);
        Assert.Equal(PolishPresets.NaturalId, PolishPresets.Resolve("non-existent").Id);
        Assert.Equal(PolishPresets.AiTaskId, PolishPresets.Resolve(PolishPresets.AiTaskId).Id);
    }

    [Fact]
    public void Validation_RejectsBlankOverlongOrControlCharsInNameAndDescription()
    {
        Assert.False(PolishPresets.IsValidName(""));
        Assert.False(PolishPresets.IsValidName("   "));
        Assert.False(PolishPresets.IsValidName(new string('字', PolishPresets.MaxNameLength + 1)));
        Assert.False(PolishPresets.IsValidName("Bad\u0007Name"));
        Assert.True(PolishPresets.IsValidName("Bug 缺陷与复现"));

        Assert.True(PolishPresets.IsValidDescription(null));
        Assert.True(PolishPresets.IsValidDescription(""));
        Assert.False(PolishPresets.IsValidDescription(new string('a', PolishPresets.MaxDescriptionLength + 1)));
        Assert.False(PolishPresets.IsValidDescription("bad\nline"));
    }

    [Fact]
    public void StarterTemplates_AreUsableOutOfTheBox()
    {
        Assert.Equal(3, PolishPresets.StarterTemplates.Count);
        Assert.All(PolishPresets.StarterTemplates, tpl =>
        {
            Assert.True(PolishPresets.IsValidName(tpl.DefaultName));
            Assert.True(PolishPresets.IsValidDescription(tpl.DefaultDescription));
            Assert.True(PolishStyle.IsUsable(tpl.Instructions));
        });
    }
}
