namespace LiveLingo.Core.Processing;

public record ProcessingOptions(
    bool Summarize = false,
    bool Optimize = false,
    bool Colloquialize = false);

public enum ProcessingMode
{
    Off,
    Summarize,
    Optimize,
    Colloquialize
}
