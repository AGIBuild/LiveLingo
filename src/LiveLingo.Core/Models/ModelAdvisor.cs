using LiveLingo.Core.Inference;

namespace LiveLingo.Core.Models;

/// <summary>How well a catalog model fits the host machine.</summary>
public enum ModelFit
{
    /// <summary>The highest-utility option for this stage that fits the host's concurrent residency and compute budget.</summary>
    Recommended,

    /// <summary>Runs fluently and co-resides within the host's safe memory budget.</summary>
    Supported,

    /// <summary>Runnable one role at a time, but exceeds the concurrent residency or CPU fast-turnaround budget.</summary>
    Heavy,

    /// <summary>Exceeds the host's single-role memory ceiling or requires GPU acceleration unavailable on this host.</summary>
    Unsupported
}

public enum ModelUnsupportedReason
{
    InsufficientMemory,
    InsufficientVideoMemory,
    RequiresGpuAcceleration
}

public sealed record ModelEvaluation(
    ModelDescriptor Model,
    ModelFit Fit,
    ModelUnsupportedReason? UnsupportedReason = null,
    int? RequiredMemoryGb = null)
{
    public bool IsSelectable => Fit != ModelFit.Unsupported;
}

/// <summary>
/// Combined memory accounting for an active <see cref="ModelSelection"/> on a given <see cref="HardwareProfile"/>,
/// shared by the Settings UI gauge and <c>ModelWarmup</c>'s background co-residency decision.
/// </summary>
public sealed record ResidencyAssessment(
    long ActiveCoResidentBytes,
    long SafeBudgetBytes,
    bool CanCoResideBothLlms,
    bool ExceedsSafeBudget);

/// <summary>
/// Evaluates catalog models against a <see cref="HardwareProfile"/> using composable compatibility specifications
/// and selects the optimal multi-stage <see cref="ModelSelection"/> under the host's residency and compute constraints.
/// </summary>
public static class ModelAdvisor
{
    private static readonly int[] StandardRamTiersGb = [8, 12, 16, 24, 32, 48, 64, 96, 128];
    private static readonly int[] StandardVramTiersGb = [6, 8, 10, 12, 16, 24, 32, 48];

    private static readonly IModelCompatibilityRule[] Rules =
    [
        new ComputeAccelerationRule(),
        new VramCeilingRule(),
        new SystemMemoryCeilingRule(),
        new CoResidencyRule()
    ];

    /// <summary>
    /// Selects the highest-utility <see cref="ModelSelection"/> whose individual models pass all hard ceilings
    /// and whose combined resident footprint stays within <see cref="HardwareProfile.CoResidentBudgetBytes"/>
    /// (or minimizes budget overshoot on ultra-constrained devices). Stages offloaded to the cloud contribute zero
    /// local resident memory, freeing budget for the remaining local stages while keeping a valid local fallback ID.
    /// </summary>
    public static ModelSelection Recommend(
        HardwareProfile hardware,
        bool cleanInCloud = false,
        bool translateInCloud = false)
    {
        var allLocal = RecommendAllLocal(hardware);
        if (!cleanInCloud && !translateInCloud)
            return allLocal;

        var speeches = SelectableOrLightest(ModelStage.Speech, hardware);
        var cleaners = cleanInCloud ? [allLocal.Cleaner] : SelectableOrLightest(ModelStage.Cleaner, hardware);
        var translators = translateInCloud ? [allLocal.Translator] : SelectableOrLightest(ModelStage.Translator, hardware);

        return PickBestCombination(speeches, cleaners, translators, hardware, cleanInCloud, translateInCloud);
    }

    private static ModelSelection RecommendAllLocal(HardwareProfile hardware)
    {
        var speeches = SelectableOrLightest(ModelStage.Speech, hardware);
        var cleaners = SelectableOrLightest(ModelStage.Cleaner, hardware);
        var translators = SelectableOrLightest(ModelStage.Translator, hardware);
        return PickBestCombination(speeches, cleaners, translators, hardware, cleanInCloud: false, translateInCloud: false);
    }

    private static ModelSelection PickBestCombination(
        IReadOnlyList<ModelDescriptor> speeches,
        IReadOnlyList<ModelDescriptor> cleaners,
        IReadOnlyList<ModelDescriptor> translators,
        HardwareProfile hardware,
        bool cleanInCloud,
        bool translateInCloud)
    {
        ModelSelection? best = null;
        var bestOvershoot = long.MaxValue;
        var bestUtility = int.MinValue;

        foreach (var speech in speeches)
        foreach (var cleaner in cleaners)
        foreach (var translator in translators)
        {
            var overshoot = BudgetOvershoot(speech, cleaner, translator, hardware, cleanInCloud, translateInCloud);
            var utility = EffectiveUtility(speech, hardware)
                        + (cleanInCloud ? 0 : EffectiveUtility(cleaner, hardware))
                        + (translateInCloud ? 0 : EffectiveUtility(translator, hardware));

            if (overshoot < bestOvershoot || (overshoot == bestOvershoot && utility > bestUtility))
            {
                best = new ModelSelection(speech.Id, cleaner.Id, translator.Id);
                bestOvershoot = overshoot;
                bestUtility = utility;
            }
        }

        return best!;
    }

    /// <summary>
    /// Resolves possibly-empty or invalid persisted model IDs against <paramref name="hardware"/>:
    /// empty, unknown or <see cref="ModelFit.Unsupported"/> entries fall back to <see cref="Recommend"/>.
    /// </summary>
    public static ModelSelection Resolve(
        string? speechModelId,
        string? cleanerModelId,
        string? translatorModelId,
        HardwareProfile hardware)
    {
        var recommended = Recommend(hardware);
        return new ModelSelection(
            ResolveStage(ModelStage.Speech, speechModelId, recommended.SpeechModelId, hardware),
            ResolveStage(ModelStage.Cleaner, cleanerModelId, recommended.CleanerModelId, hardware),
            ResolveStage(ModelStage.Translator, translatorModelId, recommended.TranslatorModelId, hardware));
    }

    /// <summary>
    /// Evaluates every local option of <paramref name="stage"/> in catalog rank order (#1 best quality first),
    /// accounting for whether the opposite LLM stage is currently offloaded to the cloud.
    /// </summary>
    public static IReadOnlyList<ModelEvaluation> EvaluateStage(
        ModelStage stage,
        HardwareProfile hardware,
        ModelSelection? activeSelection = null,
        bool cleanInCloud = false,
        bool translateInCloud = false)
    {
        // Evaluating a local option for a stage asks "how well would this option fit if this stage runs locally?"
        var peerCleanInCloud = stage == ModelStage.Cleaner ? false : cleanInCloud;
        var peerTranslateInCloud = stage == ModelStage.Translator ? false : translateInCloud;
        var recommended = Recommend(hardware, peerCleanInCloud, peerTranslateInCloud);
        var peers = activeSelection ?? recommended;
        var recommendedId = recommended.ModelIdFor(stage);

        return
        [
            .. ModelCatalog.OptionsFor(stage)
                .Select(option => EvaluateOption(option, hardware, peers, recommendedId, peerCleanInCloud, peerTranslateInCloud))
        ];
    }

    /// <summary>Evaluates one selectable model option against <paramref name="hardware"/>.</summary>
    public static ModelEvaluation Evaluate(
        ModelDescriptor option,
        HardwareProfile hardware,
        ModelSelection? activeSelection = null,
        bool cleanInCloud = false,
        bool translateInCloud = false)
    {
        var profile = option.Profile
            ?? throw new ArgumentException($"Model '{option.Id}' is not a selectable stage option.", nameof(option));
        var peerCleanInCloud = profile.Stage == ModelStage.Cleaner ? false : cleanInCloud;
        var peerTranslateInCloud = profile.Stage == ModelStage.Translator ? false : translateInCloud;
        var recommended = Recommend(hardware, peerCleanInCloud, peerTranslateInCloud);
        return EvaluateOption(
            option,
            hardware,
            activeSelection ?? recommended,
            recommended.ModelIdFor(profile.Stage),
            peerCleanInCloud,
            peerTranslateInCloud);
    }

    /// <summary>
    /// Computes the active concurrent memory footprint and whether both local LLM servers can safely stay warm
    /// side-by-side without exceeding the host's co-resident budget.
    /// </summary>
    public static ResidencyAssessment AssessPlan(
        ModelSelection selection,
        HardwareProfile hardware,
        bool cleanInCloud = false,
        bool translateInCloud = false,
        bool cleanupEnabled = true)
    {
        var speechBytes = selection.Speech.Profile!.ResidentBytes;
        var cleanerLocal = cleanupEnabled && !cleanInCloud;
        var translatorLocal = !translateInCloud;
        var cleanerBytes = cleanerLocal ? selection.Cleaner.Profile!.ResidentBytes : 0;
        var translatorBytes = translatorLocal ? selection.Translator.Profile!.ResidentBytes : 0;

        var combinedBytes = speechBytes + cleanerBytes + translatorBytes;
        var llmBytes = cleanerBytes + translatorBytes;

        var fitsSystemRam = combinedBytes <= hardware.CoResidentBudgetBytes;
        var fitsVram = hardware.VramCoResidentBudgetBytes is not { } vramBudget || llmBytes <= vramBudget;
        var cleanerFastOnCpu = !cleanerLocal || hardware.Backend != ComputeBackend.CpuOnly
            || selection.Cleaner.SizeBytes <= hardware.CpuInteractiveWeightCeilingBytes;
        var translatorFastOnCpu = !translatorLocal || hardware.Backend != ComputeBackend.CpuOnly
            || selection.Translator.SizeBytes <= hardware.CpuInteractiveWeightCeilingBytes;

        var withinSafeBudget = fitsSystemRam && fitsVram;
        var canCoResideBoth = withinSafeBudget && cleanerFastOnCpu && translatorFastOnCpu;

        return new ResidencyAssessment(
            combinedBytes,
            hardware.CoResidentBudgetBytes,
            canCoResideBoth,
            ExceedsSafeBudget: !withinSafeBudget);
    }

    private static string ResolveStage(ModelStage stage, string? requestedId, string recommendedId, HardwareProfile hardware)
    {
        if (ModelCatalog.FindOption(stage, requestedId) is not { } found)
            return recommendedId;

        return PassesHardCeilings(found, hardware) ? found.Id : recommendedId;
    }

    private static IReadOnlyList<ModelDescriptor> SelectableOrLightest(ModelStage stage, HardwareProfile hardware)
    {
        var options = ModelCatalog.OptionsFor(stage);
        var passing = options.Where(option => PassesHardCeilings(option, hardware)).ToList();
        return passing.Count > 0 ? passing : [options[^1]];
    }

    private static bool PassesHardCeilings(ModelDescriptor option, HardwareProfile hardware)
    {
        var context = new RuleContext(option, hardware, ModelSelection.Baseline, CleanInCloud: false, TranslateInCloud: false);
        foreach (var rule in Rules)
        {
            if (rule.Evaluate(context).Fit == ModelFit.Unsupported)
                return false;
        }
        return true;
    }

    private static ModelEvaluation EvaluateOption(
        ModelDescriptor option,
        HardwareProfile hardware,
        ModelSelection peers,
        string recommendedId,
        bool cleanInCloud,
        bool translateInCloud)
    {
        var context = new RuleContext(option, hardware, peers, cleanInCloud, translateInCloud);
        var isHeavy = false;

        foreach (var rule in Rules)
        {
            var outcome = rule.Evaluate(context);
            if (outcome.Fit == ModelFit.Unsupported)
                return new ModelEvaluation(option, ModelFit.Unsupported, outcome.Reason, outcome.RequiredMemoryGb);
            if (outcome.Fit == ModelFit.Heavy)
                isHeavy = true;
        }

        if (string.Equals(option.Id, recommendedId, StringComparison.Ordinal))
            return new ModelEvaluation(option, ModelFit.Recommended);

        return new ModelEvaluation(option, isHeavy ? ModelFit.Heavy : ModelFit.Supported);
    }

    private static long BudgetOvershoot(
        ModelDescriptor speech,
        ModelDescriptor cleaner,
        ModelDescriptor translator,
        HardwareProfile hardware,
        bool cleanInCloud,
        bool translateInCloud)
    {
        var cleanerBytes = cleanInCloud ? 0 : cleaner.Profile!.ResidentBytes;
        var translatorBytes = translateInCloud ? 0 : translator.Profile!.ResidentBytes;
        var systemTotal = speech.Profile!.ResidentBytes + cleanerBytes + translatorBytes;
        var systemOvershoot = Math.Max(0, systemTotal - hardware.CoResidentBudgetBytes);
        var vramOvershoot = hardware.VramCoResidentBudgetBytes is { } vramBudget
            ? Math.Max(0, cleanerBytes + translatorBytes - vramBudget)
            : 0;
        return systemOvershoot + vramOvershoot;
    }

    private static int EffectiveUtility(ModelDescriptor option, HardwareProfile hardware)
    {
        var baseUtility = option.Profile!.UtilityScore;
        // On CPU-only hosts, penalize weights exceeding the interactive CPU turnaround threshold so fast models win Best Match.
        if (hardware.Backend == ComputeBackend.CpuOnly
            && option.Profile.Stage != ModelStage.Speech
            && option.SizeBytes > hardware.CpuInteractiveWeightCeilingBytes)
        {
            return baseUtility - 20;
        }
        return baseUtility;
    }

    private static int SnapTierGb(long requiredBytes, int[] tiers)
    {
        var exactGb = (int)Math.Ceiling(requiredBytes / (double)HardwareProfile.GiB);
        foreach (var tier in tiers)
        {
            if (tier >= exactGb)
                return tier;
        }
        return exactGb;
    }

    // ---- Specification rules ----------------------------------------------------------------

    private readonly record struct RuleContext(
        ModelDescriptor Model,
        HardwareProfile Hardware,
        ModelSelection Peers,
        bool CleanInCloud,
        bool TranslateInCloud);

    private readonly record struct RuleOutcome(
        ModelFit Fit,
        ModelUnsupportedReason? Reason = null,
        int? RequiredMemoryGb = null)
    {
        public static RuleOutcome Supported => new(ModelFit.Supported);
        public static RuleOutcome Heavy => new(ModelFit.Heavy);
        public static RuleOutcome Unsupported(ModelUnsupportedReason reason, int? requiredGb = null) =>
            new(ModelFit.Unsupported, reason, requiredGb);
    }

    private interface IModelCompatibilityRule
    {
        RuleOutcome Evaluate(RuleContext context);
    }

    private sealed class ComputeAccelerationRule : IModelCompatibilityRule
    {
        public RuleOutcome Evaluate(RuleContext context)
        {
            if (context.Hardware.Backend != ComputeBackend.CpuOnly)
                return RuleOutcome.Supported;

            var profile = context.Model.Profile!;
            if (profile.RequiresGpu)
                return RuleOutcome.Unsupported(ModelUnsupportedReason.RequiresGpuAcceleration);

            if (profile.Stage != ModelStage.Speech && context.Model.SizeBytes > context.Hardware.CpuInteractiveWeightCeilingBytes)
                return RuleOutcome.Heavy;

            return RuleOutcome.Supported;
        }
    }

    private sealed class VramCeilingRule : IModelCompatibilityRule
    {
        public RuleOutcome Evaluate(RuleContext context)
        {
            if (context.Model.Profile!.Stage == ModelStage.Speech
                || context.Hardware.VramSingleRoleCeilingBytes is not { } ceiling)
            {
                return RuleOutcome.Supported;
            }

            var needed = context.Model.Profile.ResidentBytes;
            if (needed <= ceiling)
                return RuleOutcome.Supported;

            var requiredVramBytes = needed + HardwareProfile.VramSingleReserveBytes;
            return RuleOutcome.Unsupported(
                ModelUnsupportedReason.InsufficientVideoMemory,
                SnapTierGb(requiredVramBytes, StandardVramTiersGb));
        }
    }

    private sealed class SystemMemoryCeilingRule : IModelCompatibilityRule
    {
        public RuleOutcome Evaluate(RuleContext context)
        {
            var profile = context.Model.Profile!;
            var minSpeech = ModelCatalog.SpeechModels[^1].Profile!.ResidentBytes;
            var minLlm = ModelCatalog.TranslatorModels[^1].Profile!.ResidentBytes;

            var singleSessionBytes = profile.Stage == ModelStage.Speech
                ? profile.ResidentBytes + minLlm
                : minSpeech + profile.ResidentBytes;

            if (singleSessionBytes <= context.Hardware.SingleRoleCeilingBytes)
                return RuleOutcome.Supported;

            var requiredRamBytes = (long)Math.Ceiling(singleSessionBytes / HardwareProfile.SingleRoleCeilingRatio);
            return RuleOutcome.Unsupported(
                ModelUnsupportedReason.InsufficientMemory,
                SnapTierGb(requiredRamBytes, StandardRamTiersGb));
        }
    }

    private sealed class CoResidencyRule : IModelCompatibilityRule
    {
        public RuleOutcome Evaluate(RuleContext context)
        {
            var candidate = context.Peers.With(context.Model.Profile!.Stage, context.Model.Id);
            var speechBytes = candidate.Speech.Profile!.ResidentBytes;
            var cleanerBytes = context.CleanInCloud ? 0 : candidate.Cleaner.Profile!.ResidentBytes;
            var translatorBytes = context.TranslateInCloud ? 0 : candidate.Translator.Profile!.ResidentBytes;

            if (speechBytes + cleanerBytes + translatorBytes > context.Hardware.CoResidentBudgetBytes)
                return RuleOutcome.Heavy;

            if (context.Hardware.VramCoResidentBudgetBytes is { } vramBudget
                && cleanerBytes + translatorBytes > vramBudget)
            {
                return RuleOutcome.Heavy;
            }

            return RuleOutcome.Supported;
        }
    }
}
