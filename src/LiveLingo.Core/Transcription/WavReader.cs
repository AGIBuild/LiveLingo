using System.Buffers.Binary;
using LiveLingo.Core.Dictation;

namespace LiveLingo.Core.Transcription;

/// <summary>The audio file is not one this release can read.</summary>
public sealed class UnsupportedAudioException(string message) : Exception(message);

/// <summary>Reads an uncompressed WAV file as 16 kHz mono floats, the format the speech models take.</summary>
public static class WavReader
{
    private const ushort Pcm = 1;
    private const ushort IeeeFloat = 3;
    private const ushort Extensible = 0xFFFE;

    public static float[] ReadFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Read(stream);
    }

    public static float[] Read(Stream stream)
    {
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var bytes = buffer.GetBuffer().AsSpan(0, (int)buffer.Length);

        if (bytes.Length < 12 || !bytes[..4].SequenceEqual("RIFF"u8) || !bytes.Slice(8, 4).SequenceEqual("WAVE"u8))
            throw new UnsupportedAudioException("Only uncompressed WAV audio files are supported.");

        Format? format = null;
        var position = 12;
        while (position + 8 <= bytes.Length)
        {
            var id = bytes.Slice(position, 4);
            var size = (long)BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(position + 4, 4));
            var body = position + 8;
            if (id.SequenceEqual("fmt "u8))
            {
                format = ParseFormat(bytes.Slice(body, (int)Math.Min(size, bytes.Length - body)));
            }
            else if (id.SequenceEqual("data"u8))
            {
                if (format is null)
                    throw new UnsupportedAudioException("The WAV file has no format description.");
                // Streamed files may declare a size larger than what is there.
                var length = (int)Math.Min(size, bytes.Length - body);
                return Resample(Decode(bytes.Slice(body, length), format), format);
            }

            position = (int)Math.Min(int.MaxValue, body + size + (size & 1));
        }

        throw new UnsupportedAudioException("The WAV file has no audio data.");
    }

    private sealed record Format(ushort Tag, int Channels, int Rate, int Bits);

    private static Format ParseFormat(ReadOnlySpan<byte> chunk)
    {
        if (chunk.Length < 16)
            throw new UnsupportedAudioException("The WAV format description is damaged.");

        var tag = BinaryPrimitives.ReadUInt16LittleEndian(chunk);
        var channels = BinaryPrimitives.ReadUInt16LittleEndian(chunk[2..]);
        var rate = (int)BinaryPrimitives.ReadUInt32LittleEndian(chunk[4..]);
        var bits = BinaryPrimitives.ReadUInt16LittleEndian(chunk[14..]);
        if (tag == Extensible && chunk.Length >= 26)
            tag = BinaryPrimitives.ReadUInt16LittleEndian(chunk[24..]);

        var supported = tag switch
        {
            Pcm => bits is 8 or 16 or 24 or 32,
            IeeeFloat => bits == 32,
            _ => false
        };
        if (!supported || channels == 0 || rate <= 0)
            throw new UnsupportedAudioException("Only uncompressed WAV audio (PCM or float) is supported.");

        return new Format(tag, channels, rate, bits);
    }

    /// <summary>Decodes to floats and mixes all channels into one.</summary>
    private static float[] Decode(ReadOnlySpan<byte> data, Format format)
    {
        var bytesPerSample = format.Bits / 8;
        var frames = data.Length / (bytesPerSample * format.Channels);
        var mono = new float[frames];
        for (var frame = 0; frame < frames; frame++)
        {
            var sum = 0f;
            for (var channel = 0; channel < format.Channels; channel++)
                sum += Sample(data[((frame * format.Channels + channel) * bytesPerSample)..], format);
            mono[frame] = sum / format.Channels;
        }
        return mono;
    }

    private static float Sample(ReadOnlySpan<byte> data, Format format) => format.Bits switch
    {
        8 => (data[0] - 128) / 128f,
        16 => BinaryPrimitives.ReadInt16LittleEndian(data) / 32768f,
        24 => ((data[0] << 8 | data[1] << 16 | data[2] << 24) >> 8) / 8388608f,
        _ when format.Tag == IeeeFloat => BinaryPrimitives.ReadSingleLittleEndian(data),
        _ => BinaryPrimitives.ReadInt32LittleEndian(data) / 2147483648f
    };

    /// <summary>Averages when going down and repeats the nearest sample when going up.</summary>
    private static float[] Resample(float[] input, Format format)
    {
        if (format.Rate == AudioFormat.SampleRate)
            return input;

        var ratio = (double)format.Rate / AudioFormat.SampleRate;
        var output = new float[(int)(input.Length / ratio)];
        for (var i = 0; i < output.Length; i++)
        {
            var start = (int)(i * ratio);
            if (ratio <= 1)
            {
                output[i] = input[start];
                continue;
            }

            var end = Math.Clamp((int)((i + 1) * ratio), start + 1, input.Length);
            var sum = 0f;
            for (var j = start; j < end; j++)
                sum += input[j];
            output[i] = sum / (end - start);
        }
        return output;
    }
}
