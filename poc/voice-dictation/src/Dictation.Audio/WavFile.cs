using System.Text;

namespace Dictation.Audio;

/// <summary>Reader for 16 kHz mono 16-bit PCM WAV files, the only format the pipeline uses.</summary>
public static class WavFile
{
    public const int HeaderBytes = 44;

    public static float[] ReadMono16k(string path)
    {
        using var reader = new BinaryReader(File.OpenRead(path));
        if (new string(reader.ReadChars(4)) != "RIFF") throw new InvalidDataException("Not a RIFF file: " + path);
        reader.ReadInt32();
        if (new string(reader.ReadChars(4)) != "WAVE") throw new InvalidDataException("Not a WAVE file: " + path);

        int channels = 0, rate = 0, bits = 0;
        while (reader.BaseStream.Position < reader.BaseStream.Length)
        {
            var id = new string(reader.ReadChars(4));
            var size = reader.ReadInt32();
            if (id == "fmt ")
            {
                reader.ReadInt16();
                channels = reader.ReadInt16();
                rate = reader.ReadInt32();
                reader.ReadInt32();
                reader.ReadInt16();
                bits = reader.ReadInt16();
                reader.BaseStream.Seek(size - 16, SeekOrigin.Current);
            }
            else if (id == "data")
            {
                if (channels != 1 || rate != 16000 || bits != 16)
                    throw new InvalidDataException($"Expected 16 kHz mono 16-bit, got {rate} Hz {channels} ch {bits} bit: {path}");
                var samples = new float[size / 2];
                for (var i = 0; i < samples.Length; i++) samples[i] = reader.ReadInt16() / 32768f;
                return samples;
            }
            else
            {
                reader.BaseStream.Seek(size, SeekOrigin.Current);
            }
        }
        throw new InvalidDataException("No data chunk: " + path);
    }
}

/// <summary>
/// Appends samples to a WAV file and rewrites the header after every append,
/// so a recording that ends in a crash or a kill is still a valid file up to the last frame.
/// </summary>
public sealed class WavWriter : IDisposable
{
    private readonly FileStream _file;
    private readonly byte[] _scratch = new byte[2];
    private int _dataBytes;

    public WavWriter(string path)
    {
        _file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        WriteHeader();
    }

    public long Samples => _dataBytes / 2;

    public void Append(ReadOnlySpan<float> samples)
    {
        var bytes = new byte[samples.Length * 2];
        for (var i = 0; i < samples.Length; i++)
        {
            var value = (short)Math.Clamp((int)MathF.Round(samples[i] * 32767f), short.MinValue, short.MaxValue);
            BitConverter.TryWriteBytes(_scratch, value);
            bytes[2 * i] = _scratch[0];
            bytes[2 * i + 1] = _scratch[1];
        }
        _file.Seek(0, SeekOrigin.End);
        _file.Write(bytes);
        _dataBytes += bytes.Length;
        WriteHeader();
    }

    private void WriteHeader()
    {
        _file.Seek(0, SeekOrigin.Begin);
        using var writer = new BinaryWriter(_file, Encoding.ASCII, leaveOpen: true);
        writer.Write("RIFF"u8);
        writer.Write(36 + _dataBytes);
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(16000);
        writer.Write(16000 * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(_dataBytes);
        writer.Flush();
        _file.Flush();
    }

    public void Dispose() => _file.Dispose();
}
