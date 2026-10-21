using ElBruno.S1Mini.Normalization;
using ElBruno.S1Mini.Tests.TestDoubles;

namespace ElBruno.S1Mini.Tests.Normalization;

public class SubtitleNormalizationTests
{
    private const string Srt =
        "1\r\n00:00:01,000 --> 00:00:03,500\r\num so <i>hello</i>\r\nthere everyone\r\n\r\n" +
        "2\r\n00:00:04,000 --> 00:00:05,000\r\num uh\r\n\r\n" +
        "3\r\n00:00:06,000 --> 00:00:08,000\r\nwelcome back\r\n";

    [Theory]
    [InlineData(SubtitleFormat.Srt, "00:00:01,234 --> 00:00:05,678")]
    [InlineData(SubtitleFormat.WebVtt, "00:00:01.234 --> 00:00:05.678")]
    public void FromTimes_FormatsWhisperTimes(SubtitleFormat format, string expectedTiming)
    {
        var cue = SubtitleCue.FromTimes(
            TimeSpan.FromMilliseconds(1234),
            TimeSpan.FromMilliseconds(5678),
            "Hello.",
            format);

        Assert.Equal(expectedTiming, cue.Timing);
        Assert.Null(cue.Identifier);
        Assert.Equal("Hello.", cue.Text);
    }

    [Fact]
    public void FromTimes_Srt_DoesNotWrapAtTwentyFourHours()
    {
        var cue = SubtitleCue.FromTimes(
            TimeSpan.FromHours(27),
            TimeSpan.FromHours(27).Add(TimeSpan.FromSeconds(1)),
            "Long recording.",
            SubtitleFormat.Srt);

        Assert.Equal("27:00:00,000 --> 27:00:01,000", cue.Timing);
    }

    [Fact]
    public void DetectFormat_Works()
    {
        Assert.Equal(SubtitleFormat.WebVtt, SubtitleParser.DetectFormat("\uFEFFWEBVTT\n\n"));
        Assert.Equal(SubtitleFormat.Srt, SubtitleParser.DetectFormat(Srt));
    }

    [Fact]
    public void Parse_Srt_HandlesCrLfAndMultiline()
    {
        var cues = SubtitleParser.Parse(Srt, SubtitleFormat.Srt);

        Assert.Equal(3, cues.Count);
        Assert.Equal("00:00:01,000 --> 00:00:03,500", cues[0].Timing);
        Assert.Equal("um so <i>hello</i>\nthere everyone", cues[0].Text);
    }

    [Fact]
    public async Task NormalizeSubtitles_Srt_KeepsTimestampsAndEmptyCues()
    {
        var fake = new FakeChatClient();
        fake.EnqueueResponse("So hello there everyone.");
        fake.EnqueueResponse("");
        fake.EnqueueResponse("Welcome back.");
        using var normalizer = new TranscriptNormalizer(fake);

        var output = await normalizer.NormalizeSubtitlesAsync(Srt);

        Assert.Equal(
            "1\n00:00:01,000 --> 00:00:03,500\nSo hello there everyone.\n\n" +
            "2\n00:00:04,000 --> 00:00:05,000\n\n" +
            "3\n00:00:06,000 --> 00:00:08,000\nWelcome back.\n\n",
            output);
        Assert.Equal(3, fake.CallCount);
    }

    [Fact]
    public async Task NormalizeSubtitles_DropEmptyCues_RenumbersSrt()
    {
        var fake = new FakeChatClient();
        fake.EnqueueResponse("Hello.");
        fake.EnqueueResponse("");
        fake.EnqueueResponse("Welcome back.");
        using var normalizer = new TranscriptNormalizer(fake);

        var output = await normalizer.NormalizeSubtitlesAsync(Srt, dropEmptyCues: true);

        Assert.Contains("2\n00:00:06,000 --> 00:00:08,000\nWelcome back.", output);
        Assert.DoesNotContain("00:00:04,000", output);
    }

    [Fact]
    public async Task NormalizeSubtitles_WebVtt_PreservesIdsAndSettings_SkipsNotes()
    {
        const string vtt =
            "WEBVTT\n\nNOTE a comment\n\nintro\n00:00:01.000 --> 00:00:02.000 align:start\n<v Bob>um hi\n";
        var fake = new FakeChatClient();
        fake.EnqueueResponse("Hi.");
        using var normalizer = new TranscriptNormalizer(fake);

        var output = await normalizer.NormalizeSubtitlesAsync(vtt);

        Assert.Equal("WEBVTT\n\nintro\n00:00:01.000 --> 00:00:02.000 align:start\nHi.\n\n", output);
        Assert.Equal(1, fake.CallCount);
        Assert.DoesNotContain("<v", fake.LastMessages![1].Text);
    }
}
