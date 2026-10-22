using System.Text.Json;
using ElBruno.S1Mini.Normalization;

namespace FoundryLocalSpeech;

public sealed record SpeechSpan(double Start, double End, string Text);

public sealed record TimingEvidence(string Status, string Reason, IReadOnlyList<SpeechSpan> Spans)
{
    public bool CanWriteSubtitles => Status == "native-segments-unverified";
}

public static class SpeechEvidence
{
    public static TimingEvidence Inspect(JsonElement response, double audioSeconds)
    {
        if (!double.IsFinite(audioSeconds) || audioSeconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(audioSeconds));
        if (response.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Expected a transcription response object.");

        if (!response.TryGetProperty("segments", out var segments) ||
            segments.ValueKind == JsonValueKind.Null ||
            (segments.ValueKind == JsonValueKind.Array && segments.GetArrayLength() == 0))
            return new("missing-timestamps", "The response did not supply segment timestamps. No timing was invented.", []);
        if (segments.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("The segments field must be an array.");

        var spans = new List<SpeechSpan>();
        double lastEnd = 0;
        foreach (var segment in segments.EnumerateArray())
        {
            if (segment.ValueKind == JsonValueKind.Object &&
                ((segment.TryGetProperty("start", out var optionalStart) && optionalStart.ValueKind == JsonValueKind.Null) ||
                 (segment.TryGetProperty("end", out var optionalEnd) && optionalEnd.ValueKind == JsonValueKind.Null)))
                return new("missing-timestamps", "The SDK returned segments without acoustic start/end values.", []);
            if (segment.ValueKind != JsonValueKind.Object ||
                !segment.TryGetProperty("start", out var startValue) || startValue.ValueKind != JsonValueKind.Number ||
                !segment.TryGetProperty("end", out var endValue) || endValue.ValueKind != JsonValueKind.Number ||
                !segment.TryGetProperty("text", out var textValue) || textValue.ValueKind != JsonValueKind.String ||
                !startValue.TryGetDouble(out var start) || !endValue.TryGetDouble(out var end))
                throw new InvalidDataException("Each segment must have numeric start/end and string text.");
            var text = textValue.GetString()!;
            if (!double.IsFinite(start) || !double.IsFinite(end) || start < lastEnd ||
                end <= start || end > audioSeconds + 0.001)
                throw new InvalidDataException("Segment timestamps are invalid, overlapping or outside the decoded audio.");
            spans.Add(new(start, end, text));
            lastEnd = end;
        }

        if (spans.Count == 1 && spans[0].Start == 0 && Math.Abs(spans[0].End - audioSeconds) < 0.04)
            return new("possibly-coarse", "One full-file segment is not evidence of acoustic alignment.", spans);
        return new("native-segments-unverified", "Numeric segments were returned. Listening review is still required.", spans);
    }

    public static string WriteSubtitles(TimingEvidence timing, double offsetSeconds, SubtitleFormat format)
    {
        if (!double.IsFinite(offsetSeconds) || offsetSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(offsetSeconds));
        if (!timing.CanWriteSubtitles)
            throw new InvalidOperationException($"Subtitle gate failed: {timing.Status}. {timing.Reason}");
        return SubtitleParser.Write(timing.Spans.Select(span => SubtitleCue.FromTimes(
            TimeSpan.FromSeconds(offsetSeconds + span.Start),
            TimeSpan.FromSeconds(offsetSeconds + span.End),
            string.Join(" ", span.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)), format)).ToList(), format);
    }
}
