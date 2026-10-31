using LiveLingo.Core.Transcription;

namespace LiveLingo.Core.Tests.Transcription;

public class WavReaderTests
{
    private static byte[] Wav(int rate, short channels, short bits, ushort format, byte[] data,
        bool extraChunk = false, byte[]? formatExtension = null)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write("RIFF"u8);
        w.Write(0);
        w.Write("WAVE"u8);
        if (extraChunk)
        {
            w.Write("LIST"u8);
            w.Write(3);
            w.Write(new byte[] { 1, 2, 3, 0 }); // odd-sized chunks are padded to an even length
        }
        w.Write("fmt "u8);
        w.Write(16 + (formatExtension?.Length ?? 0));
        w.Write(format);
        w.Write(channels);
        w.Write(rate);
        w.Write(rate * channels * bits / 8);
        w.Write((short)(channels * bits / 8));
        w.Write(bits);
        if (formatExtension is not null)
            w.Write(formatExtension);
        w.Write("data"u8);
        w.Write(data.Length);
        w.Write(data);
        return ms.ToArray();
    }

    private static byte[] Pcm16(params short[] samples)
    {
        var bytes = new byte[samples.Length * 2];
        Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    [Fact]
    public void Pcm16At16kHz_IsScaledToFloats()
    {
        var samples = WavReader.Read(new MemoryStream(Wav(16000, 1, 16, 1, Pcm16(0, 16384, -32768))));

        Assert.Equal(3, samples.Length);
        Assert.Equal(0f, samples[0]);
        Assert.Equal(0.5f, samples[1], 3);
        Assert.Equal(-1f, samples[2], 3);
    }

    [Fact]
    public void Stereo_IsMixedToMono()
    {
        var samples = WavReader.Read(new MemoryStream(Wav(16000, 2, 16, 1, Pcm16(16384, -16384, 8192, 8192))));

        Assert.Equal(2, samples.Length);
        Assert.Equal(0f, samples[0], 3);
        Assert.Equal(0.25f, samples[1], 3);
    }

    [Fact]
    public void HigherRates_AreResampledTo16kHz_ByAveraging()
    {
        var wav = Wav(48000, 1, 16, 1, Pcm16(0, 3000, 6000, 9000, 12000, 15000));

        var samples = WavReader.Read(new MemoryStream(wav));

        Assert.Equal(2, samples.Length);
        Assert.Equal(3000f / 32768f, samples[0], 3);
        Assert.Equal(12000f / 32768f, samples[1], 3);
    }

    [Fact]
    public void LowerRates_AreResampledTo16kHz_ByRepeatingTheNearestSample()
    {
        var samples = WavReader.Read(new MemoryStream(Wav(8000, 1, 16, 1, Pcm16(16384, -16384))));

        Assert.Equal(4, samples.Length);
        Assert.Equal([0.5f, 0.5f, -0.5f, -0.5f], samples);
    }

    [Fact]
    public void Float32AndPcm24AndPcm8_AreSupported()
    {
        var floats = new byte[8];
        BitConverter.GetBytes(0.25f).CopyTo(floats, 0);
        BitConverter.GetBytes(-0.5f).CopyTo(floats, 4);
        Assert.Equal([0.25f, -0.5f], WavReader.Read(new MemoryStream(Wav(16000, 1, 32, 3, floats))));

        var pcm24 = new byte[] { 0x00, 0x00, 0x40, 0x00, 0x00, 0xC0 }; // 0.5 and -0.5
        var read24 = WavReader.Read(new MemoryStream(Wav(16000, 1, 24, 1, pcm24)));
        Assert.Equal(0.5f, read24[0], 3);
        Assert.Equal(-0.5f, read24[1], 3);

        var read8 = WavReader.Read(new MemoryStream(Wav(16000, 1, 8, 1, [128, 255])));
        Assert.Equal(0f, read8[0], 3);
        Assert.Equal(127f / 128f, read8[1], 3);
    }

    [Fact]
    public void ChunksBeforeFormat_AreSkipped_IncludingPaddedOddOnes()
    {
        var samples = WavReader.Read(new MemoryStream(Wav(16000, 1, 16, 1, Pcm16(16384), extraChunk: true)));

        Assert.Equal([0.5f], samples);
    }

    [Fact]
    public void ExtensibleFormat_UsesTheSubFormat()
    {
        // cbSize, valid bits, channel mask, then a sub-format GUID that starts with the real format tag (3 = float)
        byte[] extension = [22, 0, 32, 0, 0, 0, 0, 0, 3, 0, 0, 0, 0, 0, 0x10, 0, 0x80, 0, 0, 0xAA, 0, 0x38, 0x9B, 0x71];

        var wav = Wav(16000, 1, 32, 0xFFFE, BitConverter.GetBytes(0.25f), formatExtension: extension);

        Assert.Equal([0.25f], WavReader.Read(new MemoryStream(wav)));
    }

    [Fact]
    public void NotAWavFile_IsRejectedWithAClearError()
    {
        var error = Assert.Throws<UnsupportedAudioException>(() => WavReader.Read(new MemoryStream("ID3 not a wav file"u8.ToArray())));

        Assert.Contains("WAV", error.Message);
    }

    [Fact]
    public void CompressedWav_IsRejected()
    {
        Assert.Throws<UnsupportedAudioException>(() =>
            WavReader.Read(new MemoryStream(Wav(16000, 1, 4, 17, [0, 0]))));
    }

    [Fact]
    public void AMissingDataChunk_IsRejected()
    {
        var bytes = Wav(16000, 1, 16, 1, Pcm16(1)).Take(36).ToArray();

        Assert.Throws<UnsupportedAudioException>(() => WavReader.Read(new MemoryStream(bytes)));
    }
}
