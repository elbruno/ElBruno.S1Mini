using ElBruno.S1Mini.Normalization;
using PodcastToSrt;

namespace ElBruno.S1Mini.Tests.Normalization;

public class PodcastSampleTests
{
    [Theory]
    [InlineData("EspaÃ±a", "España")]
    [InlineData("Â¿QuÃ© cambiÃ³?", "¿Qué cambió?")]
    [InlineData("pelÃŃcula", "película")]
    [InlineData("Hello, .NET!\nDeepSeek", "Hello, .NET!\nDeepSeek")]
    public void ByteDecoder_HandlesPinnedWhisperOutput(string encoded, string expected)
    {
        Assert.Equal(expected, WhisperTextDecoder.Decode(encoded));
    }

    [Fact]
    public void DefaultEnglishInvocation_PreservesCleanup()
    {
        var options = PodcastOptions.Parse(["episode.wav"]);
        Assert.Equal("whisper-tiny.en", options.Model.Id);
        Assert.Equal("en", options.Language);
        Assert.True(options.ShouldClean);
        Assert.False(options.Translate);
    }

    [Theory]
    [InlineData("es")]
    [InlineData("auto")]
    public void NonEnglishOrUnknown_UsesMultilingualModelWithoutCleanup(string language)
    {
        var options = PodcastOptions.Parse(["episode.m4a", "--language", language]);
        Assert.True(options.Model.IsMultilingual);
        Assert.False(options.ShouldClean);
        Assert.False(options.Translate);
    }

    [Fact]
    public void Translation_EnablesEnglishCleanup()
    {
        var options = PodcastOptions.Parse(["episode.mp3", "--language", "es", "--translate"]);
        Assert.True(options.ShouldClean);
        Assert.True(options.Translate);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    public void ExplicitOff_DisablesCleanupEvenForTranslation(string language)
    {
        var options = PodcastOptions.Parse(["episode.mp3", "--language", language,
            "--model", "whisper-base", "--translate", "--cleanup", "off"]);
        Assert.False(options.ShouldClean);
    }

    [Fact]
    public void Experimental_AllowsNonEnglishCleanupExplicitly()
    {
        var options = PodcastOptions.Parse(["episode.mp3", "--language", "es", "--cleanup", "experimental"]);
        Assert.True(options.ShouldClean);
        Assert.Equal(CleanupMode.Experimental, options.Cleanup);
    }

    [Theory]
    [InlineData("--model", "whisper-tiny.en", "--language", "es")]
    [InlineData("--model", "whisper-tiny.en", "--language", "auto")]
    [InlineData("--translate", "--language", "auto")]
    [InlineData("--model", "not-a-model")]
    [InlineData("--language", "not-a-language")]
    [InlineData("--cleanup", "unsupported")]
    [InlineData("--duration-seconds", "0")]
    [InlineData("--duration-seconds", "NaN")]
    [InlineData("--start-seconds", "-1")]
    [InlineData("--start-seconds")]
    [InlineData("--surprise")]
    public void InvalidOptions_FailExplicitly(params string[] args)
    {
        Assert.Throws<ArgumentException>(() => PodcastOptions.Parse(["episode.mp3", .. args]));
    }

    [Fact]
    public void OutputCannotOverwriteAudio()
    {
        Assert.Throws<ArgumentException>(() => PodcastOptions.Parse(["episode.mp3", "episode.mp3"]));
    }

    [Fact]
    public void Windows_PreferQuietBoundaryAndCoverAllSamplesWithoutOverlap()
    {
        var samples = Enumerable.Repeat(0.5f, AudioWindows.MaxSamples).ToArray();
        Array.Clear(samples, 24 * AudioWindows.SampleRate, AudioWindows.SampleRate / 50);
        var boundary = AudioWindows.FindBoundary(samples, samples.Length, 2L * samples.Length);
        Assert.Equal(24 * AudioWindows.SampleRate + AudioWindows.SampleRate / 50, boundary);
        Assert.InRange(boundary, 23 * AudioWindows.SampleRate, AudioWindows.MaxSamples);
        Assert.Equal(400, AudioWindows.FindBoundary(samples, 400, 400));
    }

    [Fact]
    public void Windows_KeepLastWindowIntactAndAvoidTinyPcmRemainders()
    {
        var samples = new float[AudioWindows.MaxSamples];
        Assert.Equal(samples.Length, AudioWindows.FindBoundary(samples, samples.Length, samples.Length));
        var remaining = samples.Length + 2L;
        var boundary = AudioWindows.FindBoundary(samples, samples.Length, remaining);
        Assert.True(remaining - boundary >= 5L * AudioWindows.SampleRate);
        Assert.InRange(boundary, 18 * AudioWindows.SampleRate, AudioWindows.MaxSamples);
    }

    [Fact]
    public void Windows_ProcessAnEntirePodcastWithoutGapsOrDuplicateSamples()
    {
        var total = 1409L * AudioWindows.SampleRate;
        var buffer = new float[AudioWindows.MaxSamples];
        long cursor = 0;
        var windowCount = 0;
        while (cursor < total)
        {
            var count = (int)Math.Min(buffer.Length, total - cursor);
            var consumed = AudioWindows.FindBoundary(buffer, count, total - cursor);
            Assert.InRange(consumed, 1, AudioWindows.MaxSamples);
            cursor += consumed;
            Assert.True(cursor <= total);
            windowCount++;
        }
        Assert.Equal(total, cursor);
        Assert.True(windowCount > 50);
    }

    [Theory]
    [InlineData(SubtitleFormat.Srt, "00:01:01,000 --> 00:01:03,000")]
    [InlineData(SubtitleFormat.WebVtt, "00:01:01.000 --> 00:01:03.000")]
    public void Cue_AddsOriginalOffsetAndPreservesSpanish(SubtitleFormat format, string expected)
    {
        var cue = AudioWindows.Cue(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3),
            "¿Qué cambió? España, mañana y DeepSeek.", TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(25), format);
        Assert.Equal(expected, cue.Timing);
        var content = SubtitleParser.Write([cue], format);
        Assert.Equal(cue.Text, Assert.Single(SubtitleParser.Parse(content, format)).Text);
    }

    [Theory]
    [InlineData(-1, 4)]
    [InlineData(4, 3)]
    [InlineData(0, 26)]
    public void Cue_RejectsInvalidWindowBounds(int start, int end)
    {
        Assert.Throws<InvalidDataException>(() => AudioWindows.Cue(TimeSpan.FromSeconds(start),
            TimeSpan.FromSeconds(end), "Hola.", TimeSpan.Zero, TimeSpan.FromSeconds(25), SubtitleFormat.Srt));
    }
}
