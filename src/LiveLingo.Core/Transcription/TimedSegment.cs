namespace LiveLingo.Core.Transcription;

/// <summary>One recognized stretch of speech with its position in the audio file.</summary>
public sealed record TimedSegment(TimeSpan Start, TimeSpan End, string Text);
