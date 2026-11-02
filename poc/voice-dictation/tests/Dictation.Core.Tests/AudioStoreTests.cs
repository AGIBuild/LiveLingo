using Dictation.Audio;

namespace Dictation.Core.Tests;

public sealed class AudioStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "voicepoc-store-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Wav_is_valid_after_every_append_so_a_crash_loses_nothing_recorded()
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "a.wav");
        using var writer = new WavWriter(path);

        writer.Append([0.5f, -0.5f, 0f]);
        var afterFirst = File.ReadAllBytes(path); // read while the writer is still open
        writer.Append([0.25f]);

        var samples = WavFile.ReadMono16k(path);
        Assert.Equal(4, samples.Length);
        Assert.Equal(0.5f, samples[0], 3);
        Assert.Equal(-0.5f, samples[1], 3);
        Assert.Equal(0.25f, samples[3], 3);
        Assert.Equal(WavFile.HeaderBytes + 6, afterFirst.Length);
        Assert.Equal(4, writer.Samples);
    }

    [Fact]
    public void Wav_clips_instead_of_wrapping_around()
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "clip.wav");
        using (var writer = new WavWriter(path)) writer.Append([2f, -2f]);

        var samples = WavFile.ReadMono16k(path);
        Assert.True(samples[0] > 0.99f);
        Assert.True(samples[1] < -0.99f);
    }

    [Fact]
    public void Store_lists_only_complete_takes_oldest_first_and_keeps_the_reference()
    {
        var store = new RecordingStore(_dir);
        var (first, writerA) = store.Begin(new DateTime(2026, 1, 2, 3, 4, 5));
        var (second, writerB) = store.Begin(new DateTime(2026, 1, 2, 3, 4, 9));
        writerA.Dispose();
        writerB.Dispose();
        var (orphan, writerC) = store.Begin(new DateTime(2026, 1, 3, 0, 0, 0)); // audio without a record: a take that never finished
        writerC.Dispose();

        store.Save(Record(second));
        store.Save(Record(first));
        store.SetReference(first, "I really said this");

        Assert.Equal([first, second], store.Ids());
        Assert.Equal("I really said this", store.Load(first).Reference);
        Assert.Equal("", store.Load(second).Reference);
        Assert.DoesNotContain(orphan, store.Ids());
    }

    private static TakeRecord Record(string id) =>
        new(id, DateTime.Now, 1000, "mic", "xasr", ["Avalonia"], "live", "raw", "final", 100, [new SentenceRecord("raw", "final", "Cleaned", null, null)], "");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }
}
