using System.Text;

namespace PodcastToSrt;

/// <summary>
/// Completes GPT-2 byte-level decoding omitted by ElBruno.Whisper 0.9.0.
/// Only use on that package's output, never on already decoded Unicode text.
/// </summary>
public static class WhisperTextDecoder
{
    private static readonly Dictionary<char, byte> ByteMap = CreateByteMap();
    private static readonly UTF8Encoding Utf8 = new(false, true);

    public static string Decode(string text)
    {
        var bytes = new byte[text.Length];
        for (var i = 0; i < text.Length; i++)
        {
            // Whisper 0.9.0 already replaces the encoded space and newline symbols.
            bytes[i] = text[i] switch
            {
                ' ' => 32,
                '\n' => 10,
                _ when ByteMap.TryGetValue(text[i], out var value) => value,
                _ => throw new InvalidDataException($"Unexpected character U+{(int)text[i]:X4} in Whisper 0.9.0 byte-level output."),
            };
        }
        return Utf8.GetString(bytes);
    }

    private static Dictionary<char, byte> CreateByteMap()
    {
        var visible = Enumerable.Range(33, 94).Concat(Enumerable.Range(161, 12))
            .Concat(Enumerable.Range(174, 82)).ToHashSet();
        var map = new Dictionary<char, byte>();
        var extra = 256;
        for (var b = 0; b < 256; b++)
            map.Add((char)(visible.Contains(b) ? b : extra++), (byte)b);
        return map;
    }
}
