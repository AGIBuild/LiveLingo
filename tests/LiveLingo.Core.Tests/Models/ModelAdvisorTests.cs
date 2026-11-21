using LiveLingo.Core.Inference;
using LiveLingo.Core.Models;

namespace LiveLingo.Core.Tests.Models;

public sealed class ModelAdvisorTests
{
    private const long GiB = 1024L * 1024 * 1024;

    private static HardwareProfile AppleMetal(int ramGb, int cores = 8) =>
        new($"Apple M-Series", ComputeBackend.AppleMetal, ramGb * GiB, cores);

    private static HardwareProfile CpuHost(int ramGb, int cores = 8) =>
        new($"{cores}-core CPU", ComputeBackend.CpuOnly, ramGb * GiB, cores);

    private static HardwareProfile DiscreteGpuHost(int ramGb, int vramGb, int cores = 8) =>
        new("Workstation", ComputeBackend.DiscreteGpu, ramGb * GiB, cores, vramGb * GiB);

    [Theory]
    [InlineData(8, "x-asr-offline-int8", "qwen3.5-2b-q4km", "hy-mt2-1.8b-q4km")]
    [InlineData(16, "qwen3-asr-0.6b-int8", "qwen3.5-4b-q4km", "hy-mt2-1.8b-q4km")]
    [InlineData(24, "qwen3-asr-0.6b-int8", "qwen3.5-4b-q4km", "hy-mt2-7b-q4km")]
    [InlineData(32, "qwen3-asr-1.7b-int8", "qwen3.5-9b-q4km", "hy-mt2-7b-q4km")]
    [InlineData(36, "qwen3-asr-1.7b-int8", "qwen3.5-9b-q4km", "hy-mt2-7b-q4km")]
    [InlineData(64, "qwen3-asr-1.7b-int8", "qwen3.5-9b-q4km", "hy-mt2-7b-q4km")]
    public void AppleSiliconMatrix_SelectsBestMatchGroundedInResidencyBudget(
        int ramGb, string expectedSpeech, string expectedCleaner, string expectedTranslator)
    {
        var recommended = ModelAdvisor.Recommend(AppleMetal(ramGb));

        Assert.Equal(new ModelSelection(expectedSpeech, expectedCleaner, expectedTranslator), recommended);
    }

    [Fact]
    public void Apple16GbBaseline_MarksBestMatches_Allows17BSpeechAnd7BTranslatorAsHeavy_AndBlocks9BCleaner()
    {
        var hw = AppleMetal(16);

        var speech = ModelAdvisor.EvaluateStage(ModelStage.Speech, hw);
        Assert.Equal(
            [
                (ModelCatalog.SpeechQuality.Id, ModelFit.Heavy),
                (ModelCatalog.SpeechSecondPass.Id, ModelFit.Recommended),
                (ModelCatalog.SpeechFast.Id, ModelFit.Supported)
            ],
            speech.Select(e => (e.Model.Id, e.Fit)));

        var cleaners = ModelAdvisor.EvaluateStage(ModelStage.Cleaner, hw);
        Assert.Equal(
            [
                (ModelCatalog.CleanerQuality.Id, ModelFit.Unsupported),
                (ModelCatalog.Cleaner.Id, ModelFit.Recommended),
                (ModelCatalog.CleanerFast.Id, ModelFit.Supported)
            ],
            cleaners.Select(e => (e.Model.Id, e.Fit)));
        Assert.Equal(ModelUnsupportedReason.InsufficientMemory, cleaners[0].UnsupportedReason);
        Assert.Equal(24, cleaners[0].RequiredMemoryGb);

        var translators = ModelAdvisor.EvaluateStage(ModelStage.Translator, hw);
        Assert.Equal(
            [
                (ModelCatalog.TranslatorQuality.Id, ModelFit.Heavy),
                (ModelCatalog.TranslatorFast.Id, ModelFit.Recommended)
            ],
            translators.Select(e => (e.Model.Id, e.Fit)));

        var defaultPlan = ModelAdvisor.AssessPlan(ModelAdvisor.Recommend(hw), hw);
        Assert.True(defaultPlan.CanCoResideBothLlms);
        Assert.False(defaultPlan.ExceedsSafeBudget);

        var stretchPlan = ModelAdvisor.AssessPlan(
            ModelAdvisor.Recommend(hw) with { TranslatorModelId = ModelCatalog.TranslatorQuality.Id }, hw);
        Assert.False(stretchPlan.CanCoResideBothLlms);
        Assert.True(stretchPlan.ExceedsSafeBudget);
    }

    [Fact]
    public void Apple24Gb_Prioritizes7BTranslator_AndOffers9BCleanerAsHeavy()
    {
        var hw = AppleMetal(24);

        var cleaners = ModelAdvisor.EvaluateStage(ModelStage.Cleaner, hw);
        Assert.Equal(ModelFit.Heavy, cleaners.Single(e => e.Model == ModelCatalog.CleanerQuality).Fit);
        Assert.Equal(ModelFit.Recommended, cleaners.Single(e => e.Model == ModelCatalog.Cleaner).Fit);

        var translators = ModelAdvisor.EvaluateStage(ModelStage.Translator, hw);
        Assert.Equal(ModelFit.Recommended, translators.Single(e => e.Model == ModelCatalog.TranslatorQuality).Fit);
        Assert.Equal(ModelFit.Supported, translators.Single(e => e.Model == ModelCatalog.TranslatorFast).Fit);
    }

    [Fact]
    public void CpuOnlyHosts_BlockGpuBoundModels_AndAdaptCleanerRecommendationToCoreCount()
    {
        var standardCpu = CpuHost(16, cores: 8);
        var stdRec = ModelAdvisor.Recommend(standardCpu);
        Assert.Equal(
            new ModelSelection(ModelCatalog.SpeechSecondPass.Id, ModelCatalog.CleanerFast.Id, ModelCatalog.TranslatorFast.Id),
            stdRec);

        var cleanersOn8Core = ModelAdvisor.EvaluateStage(ModelStage.Cleaner, standardCpu);
        Assert.Equal(ModelFit.Unsupported, cleanersOn8Core[0].Fit);
        Assert.Equal(ModelUnsupportedReason.RequiresGpuAcceleration, cleanersOn8Core[0].UnsupportedReason);
        Assert.Equal(ModelFit.Heavy, cleanersOn8Core[1].Fit);
        Assert.Equal(ModelFit.Recommended, cleanersOn8Core[2].Fit);

        var translatorsOn8Core = ModelAdvisor.EvaluateStage(ModelStage.Translator, standardCpu);
        Assert.Equal(ModelFit.Unsupported, translatorsOn8Core[0].Fit);
        Assert.Equal(ModelUnsupportedReason.RequiresGpuAcceleration, translatorsOn8Core[0].UnsupportedReason);
        Assert.Equal(ModelFit.Recommended, translatorsOn8Core[1].Fit);

        var workstationCpu = CpuHost(32, cores: 16);
        var wsRec = ModelAdvisor.Recommend(workstationCpu);
        Assert.Equal(ModelCatalog.SpeechQuality.Id, wsRec.SpeechModelId);
        Assert.Equal(ModelCatalog.Cleaner.Id, wsRec.CleanerModelId);
    }

    [Theory]
    [InlineData(16, 6, "qwen3-asr-0.6b-int8", "qwen3.5-4b-q4km", "hy-mt2-1.8b-q4km")]
    [InlineData(16, 8, "qwen3-asr-0.6b-int8", "qwen3.5-4b-q4km", "hy-mt2-1.8b-q4km")]
    [InlineData(32, 12, "qwen3-asr-1.7b-int8", "qwen3.5-4b-q4km", "hy-mt2-7b-q4km")]
    [InlineData(32, 16, "qwen3-asr-1.7b-int8", "qwen3.5-9b-q4km", "hy-mt2-7b-q4km")]
    public void DiscreteGpuHosts_RespectDedicatedVideoMemoryCeilings(
        int ramGb, int vramGb, string speech, string cleaner, string translator)
    {
        var hw = DiscreteGpuHost(ramGb, vramGb);
        Assert.Equal(new ModelSelection(speech, cleaner, translator), ModelAdvisor.Recommend(hw));

        if (vramGb == 6)
        {
            var eval7B = ModelAdvisor.Evaluate(ModelCatalog.TranslatorQuality, hw);
            Assert.Equal(ModelFit.Unsupported, eval7B.Fit);
            Assert.Equal(ModelUnsupportedReason.InsufficientVideoMemory, eval7B.UnsupportedReason);
            Assert.Equal(8, eval7B.RequiredMemoryGb);
        }
    }

    [Fact]
    public void Resolve_KeepsSupportedOverrides_AndClampsUnknownOrUnsupportedIdsBackToRecommended()
    {
        var hw = AppleMetal(16);

        var resolved = ModelAdvisor.Resolve(
            ModelCatalog.SpeechFast.Id,
            ModelCatalog.CleanerQuality.Id, // unsupported on 16 GB -> clamped to 4B
            ModelCatalog.TranslatorQuality.Id, // heavy but selectable on 16 GB -> kept!
            hw);

        Assert.Equal(ModelCatalog.SpeechFast.Id, resolved.SpeechModelId);
        Assert.Equal(ModelCatalog.Cleaner.Id, resolved.CleanerModelId);
        Assert.Equal(ModelCatalog.TranslatorQuality.Id, resolved.TranslatorModelId);

        var unknown = ModelAdvisor.Resolve("unknown-speech", "", null, hw);
        Assert.Equal(ModelAdvisor.Recommend(hw), unknown);
    }

    [Fact]
    public void AssessPlan_AccountsForCloudOffloadAndDisabledCleanup()
    {
        var hw = AppleMetal(16);
        var heavyLocal = new ModelSelection(
            ModelCatalog.SpeechSecondPass.Id,
            ModelCatalog.Cleaner.Id,
            ModelCatalog.TranslatorQuality.Id);

        Assert.False(ModelAdvisor.AssessPlan(heavyLocal, hw).CanCoResideBothLlms);
        Assert.True(ModelAdvisor.AssessPlan(heavyLocal, hw, translateInCloud: true).CanCoResideBothLlms);
        Assert.True(ModelAdvisor.AssessPlan(heavyLocal, hw, cleanInCloud: true).CanCoResideBothLlms);
        Assert.True(ModelAdvisor.AssessPlan(heavyLocal, hw, cleanupEnabled: false).CanCoResideBothLlms);

        // When cleanup is offloaded to cloud on 16 GB, Hy-MT2 7B fits within the 7.2 GiB co-resident budget alongside speech
        // and upgrades from Heavy to Recommended Best Match.
        Assert.Equal(ModelFit.Heavy, ModelAdvisor.EvaluateStage(ModelStage.Translator, hw)[0].Fit);
        var cloudCleanEvaluations = ModelAdvisor.EvaluateStage(ModelStage.Translator, hw, cleanInCloud: true);
        Assert.Equal(ModelFit.Recommended, cloudCleanEvaluations[0].Fit);
        Assert.Equal(ModelCatalog.TranslatorQuality.Id, ModelAdvisor.Recommend(hw, cleanInCloud: true).TranslatorModelId);

        // When both Cleaner and Translator run in cloud on 16 GB, Qwen3-ASR 1.7B upgrades to Recommended Best Match.
        var cloudLlmSpeech = ModelAdvisor.EvaluateStage(ModelStage.Speech, hw, cleanInCloud: true, translateInCloud: true);
        Assert.Equal(ModelFit.Recommended, cloudLlmSpeech[0].Fit);
        Assert.Equal(ModelCatalog.SpeechQuality.Id, ModelAdvisor.Recommend(hw, cleanInCloud: true, translateInCloud: true).SpeechModelId);
    }

    [Fact]
    public void SystemHardwareProbe_BuildsExpectedProfilesAndDetectsHost()
    {
        var mac = SystemHardwareProbe.FromFacts("osx-arm64", " Apple M2 ", 16 * GiB, 8);
        Assert.Equal(("Apple M2", ComputeBackend.AppleMetal, 16), (mac.DeviceName, mac.Backend, mac.TotalMemoryGb));
        Assert.Null(mac.DedicatedVideoMemoryGb);

        var dgpu = SystemHardwareProbe.FromFacts("win-x64", "RTX PC", 32 * GiB, 16, 12 * GiB);
        Assert.Equal(("RTX PC", ComputeBackend.DiscreteGpu, 32, 12), (dgpu.DeviceName, dgpu.Backend, dgpu.TotalMemoryGb, dgpu.DedicatedVideoMemoryGb));

        var cpu = SystemHardwareProbe.FromFacts("win-x64", null, 16 * GiB, 8);
        Assert.Equal(("8-core CPU", ComputeBackend.CpuOnly, 16), (cpu.DeviceName, cpu.Backend, cpu.TotalMemoryGb));

        var host = new SystemHardwareProbe().Detect();
        Assert.True(host.TotalMemoryBytes >= GiB);
        Assert.True(host.LogicalCores >= 1);
    }

    [Fact]
    public void NonSelectableDescriptor_ThrowsOnDirectEvaluate()
    {
        Assert.Throws<ArgumentException>(() => ModelAdvisor.Evaluate(ModelCatalog.SpeechVad, AppleMetal(16)));
    }

    [Fact]
    public void ConstrainedAndPeerDependentBoundaries_EnforceSpeechVramAndCpuCoResidencyRules()
    {
        // 6 GB host: Qwen3-ASR 1.7B (requires 12 GB) and 0.6B (requires 8 GB) are Unsupported; X-ASR Offline is Recommended.
        var ram6Gb = AppleMetal(6);
        var speech6Gb = ModelAdvisor.EvaluateStage(ModelStage.Speech, ram6Gb);
        Assert.False(speech6Gb[0].IsSelectable);
        Assert.Equal((ModelFit.Unsupported, 12), (speech6Gb[0].Fit, speech6Gb[0].RequiredMemoryGb));
        Assert.False(speech6Gb[1].IsSelectable);
        Assert.Equal((ModelFit.Unsupported, 8), (speech6Gb[1].Fit, speech6Gb[1].RequiredMemoryGb));
        Assert.True(speech6Gb[2].IsSelectable);
        Assert.Equal(ModelFit.Recommended, speech6Gb[2].Fit);

        // 4 GB ultra-low host: SelectableOrLightest still produces a valid lightest-rank fallback without crashing.
        var ram4Gb = AppleMetal(4);
        Assert.Equal(
            new ModelSelection(ModelCatalog.SpeechFast.Id, ModelCatalog.CleanerFast.Id, ModelCatalog.TranslatorFast.Id),
            ModelAdvisor.Recommend(ram4Gb));

        // Peer selection changes co-residency evaluation: on 24 GB Apple Metal, Hy-MT2 1.8B is Supported alongside 4B cleaner,
        // and becomes Heavy if the user explicitly switched cleaner to 9B (1350 + 6170 + 5056 > 11059 MiB for 7B).
        var ram24Gb = AppleMetal(24);
        var with9BCleaner = new ModelSelection(
            ModelCatalog.SpeechSecondPass.Id,
            ModelCatalog.CleanerQuality.Id,
            ModelCatalog.TranslatorFast.Id);
        var translatorsWith9B = ModelAdvisor.EvaluateStage(ModelStage.Translator, ram24Gb, with9BCleaner);
        Assert.Equal(ModelFit.Recommended, translatorsWith9B[0].Fit);
        Assert.Equal(ModelFit.Supported, translatorsWith9B[1].Fit);

        // On 8 GB VRAM + 32 GB RAM discrete GPU, 4B cleaner + 7B translator (8075 MiB) exceeds 6656 MiB co-resident VRAM -> 4B is Heavy alongside 7B.
        var dgpu8Vram = DiscreteGpuHost(ramGb: 32, vramGb: 8);
        var with7BTranslator = new ModelSelection(
            ModelCatalog.SpeechSecondPass.Id,
            ModelCatalog.Cleaner.Id,
            ModelCatalog.TranslatorQuality.Id);
        var cleanersOn8Vram = ModelAdvisor.EvaluateStage(ModelStage.Cleaner, dgpu8Vram, with7BTranslator);
        Assert.Equal(ModelFit.Heavy, cleanersOn8Vram.Single(c => c.Model == ModelCatalog.Cleaner).Fit);
        Assert.False(ModelAdvisor.AssessPlan(with7BTranslator, dgpu8Vram).CanCoResideBothLlms);
        Assert.True(ModelAdvisor.AssessPlan(ModelSelection.Baseline, dgpu8Vram).CanCoResideBothLlms);

        // On CPU-only 8-core host, AssessPlan allows co-residing 2B + 1.8B, and disables co-residency when 4B cleaner or 7B translator is selected.
        var cpu8 = CpuHost(16, cores: 8);
        var cpuRecPlan = ModelAdvisor.AssessPlan(ModelAdvisor.Recommend(cpu8), cpu8);
        Assert.True(cpuRecPlan.CanCoResideBothLlms);
        Assert.Equal(
            ModelCatalog.SpeechSecondPass.Profile!.ResidentBytes
            + ModelCatalog.CleanerFast.Profile!.ResidentBytes
            + ModelCatalog.TranslatorFast.Profile!.ResidentBytes,
            cpuRecPlan.ActiveCoResidentBytes);
        Assert.Equal(cpu8.CoResidentBudgetBytes, cpuRecPlan.SafeBudgetBytes);
        Assert.False(ModelAdvisor.AssessPlan(ModelSelection.Baseline, cpu8).CanCoResideBothLlms);
        Assert.True(ModelAdvisor.AssessPlan(ModelSelection.Baseline, cpu8, cleanInCloud: true).CanCoResideBothLlms);

        var cpuWith7B = new ModelSelection(
            ModelCatalog.SpeechSecondPass.Id,
            ModelCatalog.CleanerFast.Id,
            ModelCatalog.TranslatorQuality.Id);
        Assert.False(ModelAdvisor.AssessPlan(cpuWith7B, cpu8).CanCoResideBothLlms);
        Assert.True(ModelAdvisor.AssessPlan(cpuWith7B, cpu8, translateInCloud: true).CanCoResideBothLlms);

        // Exact inclusive memory/VRAM ceilings (<= rather than <).
        var exactCoResidentBytes = ModelSelection.Baseline.Speech.Profile!.ResidentBytes
                                 + ModelSelection.Baseline.Cleaner.Profile!.ResidentBytes
                                 + ModelSelection.Baseline.Translator.Profile!.ResidentBytes;
        var exactRamBytes = (long)Math.Ceiling(exactCoResidentBytes / HardwareProfile.CoResidentMemoryRatio);
        var exactHost = new HardwareProfile("Exact", ComputeBackend.AppleMetal, exactRamBytes, 8);
        Assert.True(ModelAdvisor.AssessPlan(ModelSelection.Baseline, exactHost).CanCoResideBothLlms);
        Assert.Equal(ModelFit.Supported, ModelAdvisor.Evaluate(ModelCatalog.SpeechFast, exactHost, ModelSelection.Baseline).Fit);

        var exactLlmBytes = ModelSelection.Baseline.Cleaner.Profile!.ResidentBytes
                          + ModelSelection.Baseline.Translator.Profile!.ResidentBytes;
        var exactVramHost = new HardwareProfile(
            "ExactVram",
            ComputeBackend.DiscreteGpu,
            32 * GiB,
            8,
            exactLlmBytes + 1_536L * 1024 * 1024);
        Assert.True(ModelAdvisor.AssessPlan(ModelSelection.Baseline, exactVramHost).CanCoResideBothLlms);
        Assert.Equal(ModelFit.Supported, ModelAdvisor.Evaluate(ModelCatalog.CleanerFast, exactVramHost, ModelSelection.Baseline).Fit);
    }
}
