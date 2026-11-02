using System.Text;

namespace LiveLingo.Core.Transcription;

/// <summary>Writes subtitles in the SubRip (.srt) format.</summary>
public static class SrtWriter
{
    /// <summary>Cues without text are skipped; cue numbers stay contiguous.</summary>
    public static string Write(IEnumerable<TimedSegment> segments)
    {
        var builder = new StringBuilder();
        var number = 0;
        foreach (var segment in segments)
        {
            var lines = segment.Text
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (lines.Length == 0)
                continue;

            builder.Append(++number).Append('\n')
                .Append(Stamp(segment.Start)).Append(" --> ").Append(Stamp(segment.End)).Append('\n')
                .Append(string.Join('\n', lines)).Append("\n\n");
        }
        return builder.ToString();
    }

    private static string Stamp(TimeSpan time) =>
        $"{(int)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00},{time.Milliseconds:000}";
}
