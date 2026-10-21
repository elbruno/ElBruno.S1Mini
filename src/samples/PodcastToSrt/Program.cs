using ElBruno.S1Mini.Normalization;
using ElBruno.Whisper;

// Podcast audio -> timestamped, cleaned subtitles, 100% local.
//
//   audio --> Whisper (segments + timestamps) --> s1-mini (clean each segment's text) --> .srt / .vtt
//
// Usage: PodcastToSrt <audio-file> [output-file] [--vtt] [--drop-empty]

var positional = args.Where(a => !a.StartsWith("--")).ToList();
if (positional.Count == 0)
{
    Console.WriteLine("Usage: PodcastToSrt <audio-file> [output-file] [--vtt] [--drop-empty]");
    return 1;
}

var input = positional[0];
var format = args.Contains("--vtt") ? SubtitleFormat.WebVtt : SubtitleFormat.Srt;
var output = positional.Count > 1
    ? positional[1]
    : Path.ChangeExtension(input, format == SubtitleFormat.Srt ? ".srt" : ".vtt");

var cacheDirectory = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ElBruno", "Whisper", "models");

Console.WriteLine("Loading Whisper...");
using var whisper = await WhisperClient.CreateAsync(new WhisperOptions
{
    // Tiny (English) keeps spoken fillers that s1-mini is designed to clean up.
    Model = KnownWhisperModels.All[0],
    CacheDirectory = cacheDirectory,
    EnableTimestamps = true,
});

Console.WriteLine($"Transcribing {Path.GetFileName(input)}...");
var transcription = await whisper.TranscribeAsync(input);

var segments = transcription.Segments
    ?? throw new InvalidOperationException("Whisper returned no timestamped segments for this audio.");
var rawCues = segments
    .Select(s => SubtitleCue.FromTimes(s.Start, s.End, s.Text.Trim(), format))
    .ToList();
Console.WriteLine($"Whisper produced {rawCues.Count} segments.");

Console.WriteLine("Cleaning with s1-mini...");
using var normalizer = await TranscriptNormalizer.CreateAsync();
var raw = SubtitleParser.Write(rawCues, format);
var cleaned = await normalizer.NormalizeSubtitlesAsync(raw, format, dropEmptyCues: args.Contains("--drop-empty"));

await File.WriteAllTextAsync(output, cleaned);
Console.WriteLine($"Wrote {output}");
return 0;
