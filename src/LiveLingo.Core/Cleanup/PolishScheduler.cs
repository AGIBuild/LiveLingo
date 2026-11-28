using LiveLingo.Core.Dictation;
using LiveLingo.Core.Drafting;
using LiveLingo.Core.Inference;
using LiveLingo.Core.Translation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LiveLingo.Core.Cleanup;

/// <summary>
/// The polish of a finished dictation. While the user speaks, each passage is cleaned on its own
/// (<see cref="CleanupScheduler"/>), so a thought that the recognizer cut at a pause stays cut at the passage seam.
/// When recording stops, the whole dictation is polished as one text: runs of neighbouring passages (up to
/// <see cref="MaxGroupChars"/> of original text; usually the whole dictation) go to the polisher, and a result that passes
/// the <see cref="FidelityGate"/> replaces the run with a single unit (its original text is the passages' joined, so the
/// user can still go back to it). The polish starts at once, from the original text, without waiting for the
/// per-passage cleanups, which pause meanwhile.
/// <list type="bullet">
/// <item>The dictation is summarized first; every run reads that summary alongside the original text.</item>
/// <item>An invalid custom result is retried once with default requirements. No recursive model retries.</item>
/// <item>Until the polish is done the draft counts as processing (<see cref="Draft.Polishing"/>, set in the very step that
/// recording stops), so writing waits for it. A polish that fails or arrives after the draft was frozen changes nothing.</item>
/// </list>
/// </summary>
public sealed class PolishScheduler : IAsyncDisposable
{
    /// <summary>The most original text one polish handles; it still fits the model's context and output limit.</summary>
    public const int MaxGroupChars = 800;

    private readonly DraftSession _session;
    private readonly IDictationPolisher _polisher;
    private readonly IReadOnlyList<string> _glossary;
    private readonly IReadOnlyList<GlossaryCorrection> _corrections;
    private readonly string _style;
    private readonly bool _usePresets;
    private readonly int _contextChars;
    private readonly ILogger<PolishScheduler> _logger;

    private readonly object _gate = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly HashSet<int> _polished = [];
    private readonly HashSet<string> _tried = [];
    private IReadOnlyList<PolishPreset> _presets;

    private string? _summary;
    private string? _cachedDocSummarySource;
    private string? _cachedDocSummary;
    private string? _warmedKey;
    private string? _warmingKey;
    private CancellationTokenSource? _warmCts;
    private Task _warmTask = Task.CompletedTask;
    private int _revision = -1;
    private int _sourceRevision = -1;
    private CancellationTokenSource? _flight;
    private bool _running;
    private bool _disposed;
    private Task _worker = Task.CompletedTask;
    private readonly DocumentScheduler _documents;

    /// <param name="style">The polish requirements, already resolved (<see cref="PolishStyle.Resolve"/>).</param>
    public PolishScheduler(
        DraftSession session, IDictationPolisher polisher, IReadOnlyList<string> glossary, string style, int contextChars,
        ILogger<PolishScheduler>? logger = null,
        IReadOnlyList<GlossaryCorrection>? corrections = null,
        TimeProvider? time = null)
    {
        _session = session;
        _polisher = polisher;
        _glossary = glossary;
        _corrections = corrections ?? [];
        _style = PolishStyle.Resolve(style);
        _usePresets = false;
        _presets = PolishPresets.BuiltIns;
        _contextChars = contextChars;
        _logger = logger ?? NullLogger<PolishScheduler>.Instance;
        _documents = new DocumentScheduler(session, UnitKind.Clean, (work, ct) => PolishDocumentAsync(work, null, ct), time, logger: _logger);
        _session.Mutate(draft => draft.PolishExpected = true);
        _session.Changed += OnChanged;
        OnChanged();
    }

    /// <summary>Scenario-preset constructor used by the multi-scenario Composer.</summary>
    public PolishScheduler(
        DraftSession session,
        IDictationPolisher polisher,
        IReadOnlyList<string> glossary,
        IReadOnlyList<PolishPreset> presets,
        int contextChars,
        ILogger<PolishScheduler>? logger = null,
        IReadOnlyList<GlossaryCorrection>? corrections = null,
        TimeProvider? time = null)
    {
        _session = session;
        _polisher = polisher;
        _glossary = glossary;
        _corrections = corrections ?? [];
        _style = PolishStyle.Default;
        _usePresets = true;
        _presets = presets is { Count: > 0 } ? presets : PolishPresets.BuiltIns;
        _contextChars = contextChars;
        _logger = logger ?? NullLogger<PolishScheduler>.Instance;
        _documents = new DocumentScheduler(
            session,
            UnitKind.Clean,
            PolishDocumentAsync,
            time,
            holistic: draft => ResolvePreset(draft.PolishModeId).Route == PolishRouteKind.Holistic,
            logger: _logger);
        _session.Mutate(draft => draft.PolishExpected = true);
        _session.Changed += OnChanged;
        OnChanged();
    }

    /// <summary>Updates the configured preset catalog when the user edits or adds presets in Settings.</summary>
    public void UpdatePresets(IReadOnlyList<PolishPreset> presets)
    {
        lock (_gate)
            _presets = presets is { Count: > 0 } ? presets : PolishPresets.BuiltIns;
    }

    private PolishPreset ResolvePreset(string modeId)
    {
        lock (_gate)
            return PolishPresets.Resolve(modeId, _presets);
    }

    private static string BasePresetStyle(PolishPreset preset) =>
        preset.EffectiveInstructions + (preset.Gate == PolishGateKind.FactAnchor
            ? "\n代码、URL、占位符和技术标识符必须逐字保留。"
            : "\n保留显式段落、缩进和列表结构；代码、URL、占位符和技术标识符必须逐字保留。");

    private static string PresetCacheKey(PolishPreset preset) =>
        $"{preset.Gate}:{BasePresetStyle(preset)}";

    private void OnChanged()
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            var (due, recording, cleanSelected, frozen, modeId) = _session.Read(draft => (
                Due: Observe(draft),
                Recording: _usePresets && draft.DocumentMode && draft.Recording,
                CleanSelected: draft.Output == OutputKind.Clean,
                Frozen: draft.Snapshot is not null,
                ModeId: draft.PolishModeId));

            UpdatePromptWarmupLocked(recording, cleanSelected, frozen, modeId);

            if (_running || !due)
                return;

            _running = true;
            _worker = Task.Run(RunAsync);
        }
    }

    private void UpdatePromptWarmupLocked(bool recording, bool cleanSelected, bool frozen, string modeId)
    {
        if (!cleanSelected || frozen)
        {
            if (_warmCts is { } active)
            {
                active.Cancel();
                _warmCts = null;
                _warmingKey = null;
            }
            return;
        }

        var preset = PolishPresets.Resolve(modeId, _presets);
        var key = PresetCacheKey(preset);
        if (!recording)
        {
            // Keep an already-running warm-up for the same preset alive across Recording=false so a cold llama-server
            // startup or nearly-finished KV prefill is never aborted and restarted; only cancel mismatched presets.
            if (_warmCts is { } active && !string.Equals(_warmingKey, key, StringComparison.Ordinal))
            {
                active.Cancel();
                _warmCts = null;
                _warmingKey = null;
            }
            return;
        }

        var style = BasePresetStyle(preset);
        if (string.Equals(_warmedKey, key, StringComparison.Ordinal) ||
            string.Equals(_warmingKey, key, StringComparison.Ordinal))
        {
            return;
        }

        _warmCts?.Cancel();
        var cts = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
        _warmCts = cts;
        _warmingKey = key;
        var gate = preset.Gate;
        _warmTask = Task.Run(() => WarmPresetCoreAsync(style, gate, key, cts));
    }

    private async Task WarmPresetCoreAsync(
        string style,
        PolishGateKind gate,
        string key,
        CancellationTokenSource cts)
    {
        try
        {
            await _polisher.WarmPresetAsync(style, gate, _glossary, cts.Token).ConfigureAwait(false);
            lock (_gate)
            {
                if (!cts.IsCancellationRequested && ReferenceEquals(_warmCts, cts))
                {
                    _warmedKey = key;
                    _warmingKey = null;
                    _warmCts = null;
                }
            }
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Skipped prompt KV warm-up for polish preset.");
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_warmCts, cts))
                {
                    _warmingKey = null;
                    _warmCts = null;
                }
            }
            cts.Dispose();
        }
    }

    /// <summary>A recording just stopped and the polish is not done: only for the cleanup output, and not for a frozen draft.</summary>
    private static bool IsDue(Draft draft) =>
        !draft.DocumentMode && draft.Polishing && !draft.Recording && draft.Snapshot is null && draft.Output == OutputKind.Clean;

    private async Task<DocumentOutput> PolishDocumentAsync(
        DocumentWork work,
        Action<string>? onPartial,
        CancellationToken ct)
    {
        if (!_usePresets)
            return new DocumentOutput(await PolishLegacyDocumentAsync(work, ct).ConfigureAwait(false));

        var preset = ResolvePreset(work.Ticket.PolishModeId);
        if (preset.IsDefaultNatural && work.AllNativePolished && !string.IsNullOrWhiteSpace(work.NativePolishedText))
            return new DocumentOutput(work.NativePolishedText);

        var unownedCount = work.Pieces.Count(piece => !piece.Owned);
        var summary = unownedCount > 1
            ? await GetOrSummarizeDocumentAsync(work.Source, ct).ConfigureAwait(false) : "";
        var pieceIndex = 0;
        GateRejection? softWarning = null;
        var assembled = await work.TransformAsync(async (target, preceding, piecePartial, token) =>
        {
            var isContinuation = pieceIndex++ > 0 && preset.Route == PolishRouteKind.Holistic;
            string? turnHint = null;
            if (isContinuation)
                turnHint = "【分段衔接提示：这是长口述的后续分段，前文已包含开头小节，请直接延续要点，不要重复生成顶层总标题。】";
            else if (preset.Id == PolishPresets.AiTaskId && target.Trim().Length <= 90 && !target.Contains('\n'))
                turnHint = "【当前原话有效长度较短：请直接输出 1～2 句平铺直叙的精炼指令，严禁使用 `###` 标题分节！】";

            var request = new PolishRequest(
                target,
                preceding,
                summary,
                _glossary,
                BasePresetStyle(preset),
                _corrections,
                preset.Gate,
                OnPartial: piecePartial,
                TurnHint: turnHint);

            string text = "";
            GateRejection? rejection = null;
            var isHardFailure = false;
            try
            {
                text = (await _polisher.PolishAsync(request, token).ConfigureAwait(false)).Trim();
                lock (_gate)
                    _warmedKey = PresetCacheKey(preset);
            }
            catch (InvalidDataException)
            {
                rejection = new GateRejection(GateReason.Incomplete);
                isHardFailure = true;
            }
            token.ThrowIfCancellationRequested();

            if (rejection is null)
                rejection = ValidatePresetOutput(preset, target, text, work.SpokenSource, out isHardFailure);

            if (rejection is { } firstRejection)
            {
                _logger.LogInformation("Preset '{Preset}' rejected on first pass ({Reason}: {Detail}); running targeted repair.",
                    preset.Id, firstRejection.Reason, firstRejection.Detail);
                if (firstRejection.Reason is GateReason.ExecutionHijack or GateReason.RoleplaySlop or GateReason.MultiLine)
                    _session.MutateIf(draft => draft.ClearStreamingDocument(work.Ticket));
                var repairHint = FormatRepairFeedback(firstRejection);
                try
                {
                    // Run 1x targeted self-repair silently (OnPartial = null) so the right pane never wipes and types twice.
                    text = (await _polisher.PolishAsync(
                        request with { RepairFeedback = repairHint, OnPartial = null },
                        token).ConfigureAwait(false)).Trim();
                    rejection = ValidatePresetOutput(preset, target, text, work.SpokenSource, out isHardFailure);
                }
                catch (InvalidDataException)
                {
                    rejection = new GateRejection(GateReason.Incomplete);
                    isHardFailure = true;
                }
                token.ThrowIfCancellationRequested();
            }

            if (rejection is { } finalRejection)
            {
                // Soft warnings (e.g. spoken ratio conversions, entity restorations like "R星"->"Rockstar", or heavy
                // spoken disfluency compression after 1x repair) preserve the polished document and surface a review badge.
                if (!isHardFailure && !string.IsNullOrWhiteSpace(text))
                {
                    _logger.LogWarning("Preset '{Preset}' completed with soft warning ({Reason}: {Detail}).",
                        preset.Id, finalRejection.Reason, finalRejection.Detail);
                    softWarning ??= finalRejection;
                    return text;
                }

                if (preset.Route == PolishRouteKind.Piecewise)
                {
                    _logger.LogWarning("Piecewise preset '{Preset}' kept source chunk after repair ({Reason}: {Detail}).",
                        preset.Id, finalRejection.Reason, finalRejection.Detail);
                    softWarning ??= finalRejection;
                    return Glossary.ApplyCorrections(target, _corrections);
                }

                throw new InvalidDataException(FormatGateFailure(finalRejection));
            }
            return text;
        }, _contextChars, onPartial, ct).ConfigureAwait(false);

        return new DocumentOutput(assembled, softWarning);
    }

    private async Task<string> GetOrSummarizeDocumentAsync(string source, CancellationToken ct)
    {
        lock (_gate)
        {
            if (_cachedDocSummarySource == source && _cachedDocSummary is not null)
                return _cachedDocSummary;
        }

        var summary = await _polisher.SummarizeAsync(source, ct).ConfigureAwait(false);
        lock (_gate)
        {
            _cachedDocSummarySource = source;
            _cachedDocSummary = summary;
        }
        return summary;
    }

    private GateRejection? ValidatePresetOutput(
        PolishPreset preset,
        string target,
        string candidate,
        bool spokenSource,
        out bool isHardFailure)
    {
        if (preset.Gate == PolishGateKind.FactAnchor)
        {
            var anchor = FactAnchorGate.CheckDetailed(
                target,
                candidate,
                _glossary,
                _corrections,
                preset.ForbidExecution,
                preset.AllowSelectiveExtraction || !preset.BuiltIn,
                spokenSource,
                out isHardFailure);
            return anchor.Rejection;
        }

        if (preset.ForbidExecution && FactAnchorGate.HasHardSafetyViolation(target, candidate.Trim(), spokenSource, out var safetyReason))
        {
            isHardFailure = true;
            return new GateRejection(safetyReason);
        }

        var targetSpans = spokenSource ? TextUnits.NonCodeLiteralSpans(target) : TextUnits.LiteralSpans(target);
        var candidateSpans = spokenSource ? TextUnits.NonCodeLiteralSpans(candidate) : TextUnits.LiteralSpans(candidate);
        var targetLiterals = targetSpans.Select(span => target[span.Start..span.End]);
        var candidateLiterals = candidateSpans.Select(span => candidate[span.Start..span.End]);
        if (!targetLiterals.SequenceEqual(candidateLiterals))
        {
            isHardFailure = true;
            return new GateRejection(GateReason.TermLost, "literal");
        }

        var gate = FidelityGate.Check(
            target,
            candidate,
            _glossary,
            allowParagraphs: true,
            corrections: _corrections,
            relaxedVerbatim: preset.RelaxedVerbatim,
            spokenDocument: spokenSource);
        if (gate.Rejection is { } rej)
        {
            var reason = rej.Reason;
            isHardFailure = !spokenSource
                || reason is GateReason.Empty
                    or GateReason.Incomplete
                    or GateReason.MultiLine
                    or GateReason.Brackets
                    or GateReason.ExecutionHijack
                    or GateReason.RoleplaySlop
                    or GateReason.NumberLost
                    or GateReason.NumberInvented
                || (reason == GateReason.Added
                    && (rej.Detail == "markdown"
                        || rej.Detail.Any(c => "不没别无非未否".Contains(c))))
                || (reason == GateReason.Deleted
                    && rej.Detail.Any(c => "不没别无非未否".Contains(c)));
            return rej;
        }

        isHardFailure = false;
        return null;
    }

    private static string FormatRepairFeedback(GateRejection rejection)
    {
        const string noMetaNote = "只输出修正后的干净正文，绝对禁止在正文中附带“（原文提及…）”、“（删除编造的…）”等任何修改说明或编辑批注。";
        var specific = rejection.Reason switch
        {
            GateReason.ExecutionHijack => "上一版越界回答了问题或编写了实现代码；请只将原话整理为指令文本，绝对不要回答问题或生成代码块。",
            GateReason.RoleplaySlop => "上一版注入了原话没有的角色扮演套话（如“你是一位资深…”）；请删除所有角色套话，直接输出正文。",
            GateReason.NumberLost => $"上一版丢失或改动了生效数字「{rejection.Detail}」；请按原话保留该数字。",
            GateReason.NumberInvented => $"上一版出现了原话未提及的数字「{rejection.Detail}」；请直接使用原话自述的数字与时间表述，不要自行换算或推演新数字。",
            GateReason.TermLost => $"上一版丢失了关键术语或代码标识符「{rejection.Detail}」；请原样保留。",
            GateReason.TermNotInGlossary => $"上一版凭空补全了原话未提及的名称或分类「{rejection.Detail}」；请彻底删除「{rejection.Detail}」，保持原话的泛指表述。",
            GateReason.Incomplete => "上一版输出被截断不完整；请精炼表达并输出完整内容。",
            _ => $"上一版未通过保真校验（{rejection.Reason}: {rejection.Detail}）；请在不增删事实、数字与否定词的前提下重新整理。"
        };
        return specific + noMetaNote;
    }

    private static string FormatGateFailure(GateRejection rejection) =>
        rejection.Detail.Length > 0
            ? $"Final polish refused: {rejection.Reason} ({rejection.Detail})"
            : $"Final polish refused: {rejection.Reason}";

    private async Task<string> PolishLegacyDocumentAsync(DocumentWork work, CancellationToken ct)
    {
        if (_style == PolishStyle.Default && work.AllNativePolished && !string.IsNullOrWhiteSpace(work.NativePolishedText))
            return work.NativePolishedText;

        var summary = work.Pieces.Count(piece => !piece.Owned) > 1
            ? await _polisher.SummarizeAsync(work.Source, ct).ConfigureAwait(false) : "";
        return await work.TransformAsync(async (target, preceding, token) =>
        {
            const string layout = "\n保留显式段落、缩进和列表结构；代码、URL、占位符和技术标识符必须逐字保留。";
            var request = new PolishRequest(target, preceding, summary, _glossary, _style + layout, _corrections);
            string text = "";
            try
            {
                text = (await _polisher.PolishAsync(request, token).ConfigureAwait(false)).Trim();
            }
            catch (InvalidDataException) when (_style != PolishStyle.Default) { }
            token.ThrowIfCancellationRequested();
            string? Refusal(string candidate)
            {
                var gate = FidelityGate.Check(target, candidate, _glossary, allowParagraphs: true, corrections: _corrections);
                if (!gate.Accepted)
                    return "Final polish refused: " + gate.Rejection?.Reason;
                return TextUnits.LiteralSpans(target).Select(span => target[span.Start..span.End])
                    .SequenceEqual(TextUnits.LiteralSpans(candidate).Select(span => candidate[span.Start..span.End]))
                    ? null : "Final polish changed a protected literal.";
            }
            var refusal = Refusal(text);
            if (refusal is not null && _style != PolishStyle.Default)
            {
                text = (await _polisher.PolishAsync(request with { Style = PolishStyle.Default + layout }, token).ConfigureAwait(false)).Trim();
                token.ThrowIfCancellationRequested();
                refusal = Refusal(text);
            }
            if (refusal is not null)
                throw new InvalidDataException(refusal);
            return text;
        }, _contextChars, ct).ConfigureAwait(false);
    }

    private bool Observe(Draft draft)
    {
        if (_revision != draft.DictationRevision || _sourceRevision != draft.SourceRevision)
        {
            _flight?.Cancel();
            _revision = draft.DictationRevision;
            _sourceRevision = draft.SourceRevision;
            _tried.Clear();
            _polished.Clear();
            _summary = null;
        }
        var due = IsDue(draft);
        if (!due)
            _flight?.Cancel();
        return due;
    }

    private abstract record Step;

    private sealed record Summarize(string Text, int Revision, int SourceRevision) : Step;

    private sealed record Polish(List<int> Ids, string Target, string Preceding, int Revision, int SourceRevision) : Step;

    private sealed record Idle : Step;

    private sealed record Done(int Revision, int SourceRevision) : Step;

    private async Task RunAsync()
    {
        while (true)
        {
            Step step;
            CancellationTokenSource cancel;
            lock (_gate)
            {
                step = _disposed ? new Idle() : _session.Read(draft =>
                {
                    Observe(draft);
                    return Next(draft);
                });
                switch (step)
                {
                    case Idle:
                        _summary = null;
                        _running = false;
                        return;
                    case Done:
                        if (Finish(step))
                            return;
                        continue;
                }

                if (step is Polish polish)
                    _tried.Add(Key(polish.Ids));
                cancel = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                _flight = cancel;
            }

            try
            {
                if (step is Summarize summarize)
                {
                    var summary = (await _polisher.SummarizeAsync(summarize.Text, cancel.Token).ConfigureAwait(false)).Trim();
                    lock (_gate)
                        if (!cancel.IsCancellationRequested && _revision == summarize.Revision)
                            _summary = summary;
                }
                else
                    await PolishAsync((Polish)step, cancel.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested)
            {
                // A new recording, source edit or output choice superseded this request.
            }
            catch (Exception ex) when (ServiceFailure.IsUnavailable(ex))
            {
                if (cancel.IsCancellationRequested)
                    continue;
                _logger.LogWarning("Final polish service unavailable ({Failure}); keeping passage results.", ex.GetType().Name);
                Report(PolishOutcome.KeptPreview);
                lock (_gate)
                    if (Finish(step, cancel.Token))
                        return;
                continue;
            }
            catch (InvalidDataException)
            {
                if (cancel.IsCancellationRequested)
                    continue;
                _logger.LogWarning("Incomplete final polish response; keeping passage results.");
                if (step is Summarize)
                {
                    lock (_gate)
                        if (!cancel.IsCancellationRequested)
                            _summary = "";
                }
                else
                    Report(PolishOutcome.KeptPreview);
            }
            catch (Exception ex)
            {
                if (cancel.IsCancellationRequested)
                    continue;
                _logger.LogError("Final polish failed ({Failure}); keeping passage results.", ex.GetType().Name);
                Report(PolishOutcome.KeptPreview);
                lock (_gate)
                    if (Finish(step, cancel.Token))
                        return;
                continue;
            }
            finally
            {
                lock (_gate)
                {
                    if (cancel.IsCancellationRequested && step is Polish cancelled)
                        _tried.Remove(Key(cancelled.Ids));
                    _flight = null;
                }
                cancel.Dispose();
            }
        }
    }

    private async Task PolishAsync(Polish polish, CancellationToken ct)
    {
        var request = new PolishRequest(polish.Target, polish.Preceding, _summary ?? "", _glossary, _style, _corrections);
        string? text = null;
        try
        {
            text = (await _polisher.PolishAsync(request, ct).ConfigureAwait(false)).Trim();
            ct.ThrowIfCancellationRequested();
        }
        catch (InvalidDataException) when (_style != PolishStyle.Default)
        {
            _logger.LogWarning("Custom polish response incomplete; retrying default requirements.");
        }

        var gate = FidelityGate.Check(polish.Target, text ?? "", _glossary, allowParagraphs: true, corrections: _corrections);
        if (!gate.Accepted && _style != PolishStyle.Default)
        {
            _logger.LogWarning("Custom polish refused ({Reason}); retrying default requirements.", gate.Rejection?.Reason);
            Report(PolishOutcome.DefaultRequirements);
            text = (await _polisher.PolishAsync(request with { Style = PolishStyle.Default }, ct).ConfigureAwait(false)).Trim();
            ct.ThrowIfCancellationRequested();
            gate = FidelityGate.Check(polish.Target, text, _glossary, allowParagraphs: true, corrections: _corrections);
        }
        if (!gate.Accepted)
        {
            _logger.LogWarning("Final polish refused ({Reason}); keeping passage results.", gate.Rejection?.Reason);
            Report(PolishOutcome.KeptPreview);
            return;
        }

        var id = _session.Mutate(draft =>
            !ct.IsCancellationRequested && IsDue(draft) && draft.DictationRevision == polish.Revision && draft.SourceRevision == polish.SourceRevision
                ? draft.Collapse(polish.Ids, text!)
                : null) ?? 0;
        if (id != 0)
            lock (_gate)
                _polished.Add(id);
    }

    /// <summary>The polish is over for this stop: the draft no longer waits for it. Called with the gate held.</summary>
    private bool Finish(Step step, CancellationToken ct = default)
    {
        var (revision, sourceRevision) = step switch
        {
            Polish p => (p.Revision, p.SourceRevision),
            Summarize s => (s.Revision, s.SourceRevision),
            Done d => (d.Revision, d.SourceRevision),
            _ => throw new ArgumentOutOfRangeException(nameof(step))
        };
        var finished = _session.Mutate(draft =>
        {
            if (ct.IsCancellationRequested || !IsDue(draft) ||
                draft.DictationRevision != revision || draft.SourceRevision != sourceRevision)
                return false;
            draft.Polishing = false;
            return true;
        });
        if (!finished)
            return false;
        _summary = null;
        _running = false;
        return true;
    }

    private void Report(PolishOutcome outcome) =>
        _session.Mutate(draft =>
        {
            if (_flight is { IsCancellationRequested: false } && IsDue(draft) &&
                draft.DictationRevision == _revision && draft.SourceRevision == _sourceRevision &&
                draft.PolishOutcome != PolishOutcome.KeptPreview)
                draft.PolishOutcome = outcome;
        });

    private Step Next(Draft draft)
    {
        if (!IsDue(draft))
            return new Idle();

        if (_style == PolishStyle.Default
            && draft.Units.Count > 0
            && draft.Units.All(u => u.Clean.IsOwned || (u.IsNativePolished && !string.IsNullOrWhiteSpace(u.Clean.Text))))
        {
            return new Done(draft.DictationRevision, draft.SourceRevision);
        }

        var runs = Runs(draft);
        var source = draft.SourceText();
        var needsSummary = runs.Count > 1 || source.Length > MaxGroupChars;
        foreach (var (first, count) in runs)
        {
            if (Untried(draft, first, count) is not { } polish)
                continue;

            if (needsSummary && _summary is null)
                return new Summarize(source, draft.DictationRevision, draft.SourceRevision);

            return polish;
        }

        return new Done(draft.DictationRevision, draft.SourceRevision);
    }

    /// <summary>
    /// Runs of neighbouring passages that can be polished together: not edited by hand, without layout, not polished yet,
    /// together up to <see cref="MaxGroupChars"/> of original text. A single passage is no run: there is nothing to join.
    /// </summary>
    private List<(int First, int Count)> Runs(Draft draft)
    {
        var runs = new List<(int, int)>();
        var units = draft.Units;
        var first = 0;
        var length = 0;
        for (var i = 0; i <= units.Count; i++)
        {
            var polishable = i < units.Count && IsPolishable(units[i]);
            if (polishable && (i == first || length + units[i].Raw.Length <= MaxGroupChars))
            {
                length += units[i].Raw.Length;
                continue;
            }

            // The run ends before this unit, which starts the next run when it can be polished.
            if (i - first >= 1)
                runs.Add((first, i - first));
            first = polishable ? i : i + 1;
            length = polishable ? units[i].Raw.Length : 0;
        }

        return runs;
    }

    /// <summary>Each unchanged run is attempted only once.</summary>
    private Polish? Untried(Draft draft, int first, int count)
    {
        if (count < 1)
            return null;

        var ids = draft.Units.Skip(first).Take(count).Select(unit => unit.Id).ToList();
        if (!_tried.Contains(Key(ids)))
        {
            var target = TextUnits.Join(draft.Units.Skip(first).Take(count).Select(unit => ("", unit.Raw, "")));
            return new Polish(ids, target, draft.PrecedingText(first, _contextChars), draft.DictationRevision, draft.SourceRevision);
        }

        return null;
    }

    private bool IsPolishable(DraftUnit unit) =>
        !unit.Clean.IsOwned && !unit.Translation.IsOwned && unit.Lead.Length == 0 && unit.Trail.Length == 0
        && unit.Raw.Length <= MaxGroupChars && !_polished.Contains(unit.Id);

    private static string Key(IEnumerable<int> ids) => string.Join(',', ids);

    public async ValueTask DisposeAsync()
    {
        Task worker;
        Task warmTask;
        lock (_gate)
        {
            _disposed = true;
            worker = _worker;
            warmTask = _warmTask;
        }

        _session.Changed -= OnChanged;
        await _documents.DisposeAsync().ConfigureAwait(false);
        await _stop.CancelAsync().ConfigureAwait(false);
        await Task.WhenAll(worker, warmTask).ConfigureAwait(false);
        _stop.Dispose();
    }
}
