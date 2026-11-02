namespace LiveLingo.Core.Drafting;

/// <summary>Why the fidelity gate refused a cleanup result. The interface language turns this into a sentence.</summary>
public enum GateReason
{
    Empty,
    MultiLine,
    Brackets,
    NumberLost,
    NumberInvented,
    TermLost,
    TermNotInGlossary,
    TooShort,
    TooLong,
    Added,
    Deleted,

    /// <summary>The model's answer was cut off (it ran past its output limit), so it cannot be trusted as a whole.</summary>
    Incomplete
}

/// <summary>
/// A refused cleanup. <paramref name="Detail"/> is the offending fragment of what the user said (or a ratio or count):
/// it may be shown to the user, but it is their text and must never be logged.
/// </summary>
public readonly record struct GateRejection(GateReason Reason, string Detail = "");
