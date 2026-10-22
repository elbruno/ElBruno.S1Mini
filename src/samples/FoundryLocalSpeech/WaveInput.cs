namespace FoundryLocalSpeech;

public sealed record WaveData(long Offset, long Length)
{
    public double DurationSeconds => Length / 32000d;
}

public static class WaveInput
{
    public static double Duration(string path)
    {
        using var stream = File.OpenRead(path);
        return Inspect(stream).DurationSeconds;
    }

    public static double Duration(Stream stream) => Inspect(stream).DurationSeconds;

    public static WaveData Inspect(Stream stream)
    {
        stream.Position = 0;
        using var reader = new BinaryReader(stream, System.Text.Encoding.ASCII, leaveOpen: true);
        if (stream.Length < 12 || new string(reader.ReadChars(4)) != "RIFF")
            throw new InvalidDataException("Expected a RIFF WAV file. Decode with FFmpeg first.");
        var length = reader.ReadUInt32();
        if (length + 8L != stream.Length || new string(reader.ReadChars(4)) != "WAVE")
            throw new InvalidDataException("Invalid or truncated WAV container.");
        var formatFound = false;
        long dataBytes = 0;
        long dataOffset = 0;
        while (stream.Position < stream.Length)
        {
            if (stream.Length - stream.Position < 8) throw new InvalidDataException("Truncated WAV chunk.");
            var id = new string(reader.ReadChars(4));
            var size = reader.ReadUInt32();
            var next = stream.Position + size + (size % 2);
            if (next > stream.Length) throw new InvalidDataException("WAV chunk exceeds the file.");
            if (id == "fmt ")
            {
                if (formatFound || size < 16 ||
                    reader.ReadUInt16() != 1 || reader.ReadUInt16() != 1 ||
                    reader.ReadUInt32() != 16000 || reader.ReadUInt32() != 32000 ||
                    reader.ReadUInt16() != 2 || reader.ReadUInt16() != 16)
                    throw new InvalidDataException("Expected one PCM 16 kHz mono signed 16-bit WAV format chunk.");
                formatFound = true;
            }
            else if (id == "data")
            {
                if (dataBytes != 0 || size == 0 || size % 2 != 0)
                    throw new InvalidDataException("Expected one nonempty, sample-aligned WAV data chunk.");
                dataBytes = size;
                dataOffset = stream.Position;
            }
            stream.Position = next;
        }
        if (!formatFound || dataBytes == 0) throw new InvalidDataException("WAV format or audio data is missing.");
        return new(dataOffset, dataBytes);
    }
}
