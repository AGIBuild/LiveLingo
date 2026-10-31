namespace LiveLingo.Core.Drafting;

/// <summary>One model-produced view of a unit (cleanup or translation).</summary>
public sealed class UnitResult
{
    /// <summary>Null while the result is still pending.</summary>
    public string? Text { get; internal set; }

    /// <summary>True once the user edited the text; model results are never allowed to replace it.</summary>
    public bool IsOwned { get; internal set; }

    public string Error { get; internal set; } = "";
    public string Warning { get; internal set; } = "";

    internal void Clear()
    {
        Text = null;
        IsOwned = false;
        Error = "";
        Warning = "";
    }
}

public sealed class DraftUnit
{
    internal DraftUnit(int id, SourceUnit source)
    {
        Id = id;
        Apply(source);
    }

    public int Id { get; }
    public string Raw { get; private set; } = "";
    public string Lead { get; private set; } = "";
    public string Trail { get; private set; } = "";
    public bool IsComplete { get; private set; } = true;
    public UnitResult Clean { get; } = new();
    public UnitResult Translation { get; } = new();

    internal UnitResult Result(UnitKind kind) => kind == UnitKind.Clean ? Clean : Translation;

    /// <summary>Takes the layout and completeness of a source unit whose body is unchanged.</summary>
    internal void Apply(SourceUnit source)
    {
        Raw = source.Body;
        Lead = source.Lead;
        Trail = source.Trail;
        IsComplete = source.IsComplete;
    }
}
