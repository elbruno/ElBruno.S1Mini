namespace ElBruno.S1Mini.Normalization;

/// <summary>Supported subtitle file formats.</summary>
public enum SubtitleFormat
{
    /// <summary>SubRip (.srt).</summary>
    Srt,

    /// <summary>WebVTT (.vtt).</summary>
    WebVtt,
}

/// <summary>
/// A single subtitle cue. <see cref="Timing"/> is kept verbatim (for example
/// <c>00:00:01,000 --> 00:00:03,500</c>, including any WebVTT cue settings) so
/// timestamps are never altered.
/// </summary>
/// <param name="Identifier">Optional cue identifier (SRT index or WebVTT cue id).</param>
/// <param name="Timing">The raw timing line.</param>
/// <param name="Text">The cue text (may contain line breaks and markup).</param>
public sealed record SubtitleCue(string? Identifier, string Timing, string Text);
