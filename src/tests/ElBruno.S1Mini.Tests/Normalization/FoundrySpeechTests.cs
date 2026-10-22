using System.Text;
using System.Text.Json;
using ElBruno.S1Mini.Normalization;
using FoundryLocalSpeech;

namespace ElBruno.S1Mini.Tests.Normalization;

public class FoundrySpeechTests
{
    [Theory]
    [InlineData("{}")]
    [InlineData("""{"segments":null}""")]
    [InlineData("""{"segments":[]}""")]
    [InlineData("""{"segments":[{"start":null,"end":null,"text":"hello"}]}""")]
    [InlineData("""{"segments":[{"start":1,"end":2,"text":"hello"},{"start":null,"end":null,"text":"there"}]}""")]
    [InlineData("""{"text":"hello","words":[{"start":1,"end":2,"word":"hello"}]}""")]
    public void MissingSegments_NeverInventsSubtitleTiming(string response)
    {
        var result = Inspect(response);
        Assert.Equal("missing-timestamps", result.Status);
        Assert.False(result.CanWriteSubtitles);
        Assert.Throws<InvalidOperationException>(() => SpeechEvidence.WriteSubtitles(result, 0, SubtitleFormat.Srt));
    }

    [Fact]
    public void FullWindowSpan_IsNotTreatedAsAcousticAlignment()
    {
        var result = Inspect("""{"segments":[{"start":0,"end":25,"text":"hello"}]}""");
        Assert.Equal("possibly-coarse", result.Status);
        Assert.False(result.CanWriteSubtitles);
        Assert.Single(result.Spans);
    }

    [Theory]
    [InlineData(SubtitleFormat.Srt, "00:01:01,250 --> 00:01:02,500")]
    [InlineData(SubtitleFormat.WebVtt, "00:01:01.250 --> 00:01:02.500")]
    public void NativeSpans_PreserveOffsetsAndUnicodeWithoutCleanup(SubtitleFormat format, string timing)
    {
        var result = Inspect("""{"segments":[{"start":1.25,"end":2.5,"text":" Espa\u00f1a\n\n.NET "},{"start":4,"end":5,"text":"fin"}]}""");
        Assert.Equal("native-segments-unverified", result.Status);
        var output = SpeechEvidence.WriteSubtitles(result, 60, format);
        var cues = SubtitleParser.Parse(output, format);
        Assert.Equal(2, cues.Count);
        Assert.Equal(timing, cues[0].Timing);
        Assert.Equal("Espa\u00f1a .NET", cues[0].Text);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("""{"segments":{}}""")]
    [InlineData("""{"segments":[{"start":-1,"end":2,"text":"bad"}]}""")]
    [InlineData("""{"segments":[{"start":2,"end":2,"text":"bad"}]}""")]
    [InlineData("""{"segments":[{"start":3,"end":2,"text":"bad"}]}""")]
    [InlineData("""{"segments":[{"start":1,"end":26,"text":"bad"}]}""")]
    [InlineData("""{"segments":[{"start":1,"end":2}]}""")]
    [InlineData("""{"segments":[{"start":1,"end":2,"text":null}]}""")]
    [InlineData("""{"segments":[{"start":"1","end":2,"text":"bad"}]}""")]
    [InlineData("""{"segments":[{"start":1,"end":3,"text":"a"},{"start":2,"end":4,"text":"b"}]}""")]
    public void InvalidSegments_FailExplicitly(string response) =>
        Assert.Throws<InvalidDataException>(() => Inspect(response));

    [Theory]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidOffsets_FailExplicitly(double offset)
    {
        var result = Inspect("""{"segments":[{"start":1,"end":2,"text":"hello"}]}""");
        Assert.Throws<ArgumentOutOfRangeException>(() => SpeechEvidence.WriteSubtitles(result, offset, SubtitleFormat.Srt));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    public void InvalidDuration_FailsExplicitly(double duration) =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SpeechEvidence.Inspect(JsonSerializer.SerializeToElement(new { }), duration));

    [Fact]
    public void WaveDuration_UsesDecodedSamplesAndSkipsPaddedMetadata()
    {
        using var wav = Wave();
        Assert.Equal(1, WaveInput.Duration(wav));
    }

    [Theory]
    [InlineData(2, 16000, 32000)]
    [InlineData(1, 44100, 32000)]
    [InlineData(1, 16000, 0)]
    [InlineData(1, 16000, 31999)]
    public void InvalidWaveFormatOrData_FailsExplicitly(ushort channels, uint rate, int bytes)
    {
        using var wav = Wave(channels, rate, bytes);
        Assert.Throws<InvalidDataException>(() => WaveInput.Duration(wav));
    }

    [Fact]
    public void TruncatedWave_FailsExplicitly()
    {
        using var wav = Wave();
        wav.SetLength(wav.Length - 2);
        Assert.Throws<InvalidDataException>(() => WaveInput.Duration(wav));
    }

    [Fact]
    public async Task PcmChunks_ExcludeHeadersAndMetadataAndRetainShortTail()
    {
        using var wav = Wave(bytes: 9000);
        var data = WaveInput.Inspect(wav);
        var expected = Enumerable.Range(0, 9000).Select(value => (byte)value).ToArray();
        wav.Position = data.Offset;
        wav.Write(expected);
        var chunks = new List<byte[]>();
        await foreach (var chunk in PcmStreaming.ReadChunksAsync(wav, data))
            chunks.Add(chunk.ToArray());
        Assert.Equal(new[] { 4096, 4096, 808 }, chunks.Select(chunk => chunk.Length));
        Assert.Equal(expected, chunks.SelectMany(chunk => chunk).ToArray());
        Assert.Equal(data.Offset + data.Length, wav.Position);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(-2)]
    public async Task PcmChunks_RejectInvalidChunkSizes(int chunkBytes)
    {
        using var wav = Wave();
        var data = WaveInput.Inspect(wav);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
        {
            await foreach (var chunk in PcmStreaming.ReadChunksAsync(wav, data, chunkBytes))
                Assert.Fail("No chunk should be returned.");
        });
    }

    [Fact]
    public async Task PcmLifecycle_StartsConsumerBeforeFeedingAndFinishesExactlyOnce()
    {
        using var wav = Wave(bytes: 9000);
        var data = WaveInput.Inspect(wav);
        var progress = new PcmProgress();
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var consumerStarted = false;
        var finishCount = 0;
        await PcmStreaming.RunAsync(wav, data, progress, (_, _) =>
        {
            Assert.True(consumerStarted);
            return Task.CompletedTask;
        }, () =>
        {
            finishCount++;
            finished.SetResult();
        }, async token =>
        {
            consumerStarted = true;
            await finished.Task.WaitAsync(token);
        }, CancellationToken.None);
        Assert.Equal(4500, progress.SubmittedSamples);
        Assert.Equal(3, progress.SubmittedChunks);
        Assert.True(progress.EndOfInputSignaled);
        Assert.Equal(1, finishCount);
    }

    [Fact]
    public async Task PcmLifecycle_ProducerFailureCancelsConsumer()
    {
        using var wav = Wave();
        var progress = new PcmProgress();
        var consumerStopped = false;
        await Assert.ThrowsAsync<IOException>(() => PcmStreaming.RunAsync(wav, WaveInput.Inspect(wav), progress,
            (_, _) => throw new IOException("fake push failure"),
            () => Assert.Fail("Failure must not signal successful end of input."),
            async token =>
            {
                try { await Task.Delay(Timeout.Infinite, token); }
                finally { consumerStopped = true; }
            }, CancellationToken.None));
        Assert.True(consumerStopped);
        Assert.False(progress.EndOfInputSignaled);
        Assert.Equal(0, progress.SubmittedSamples);
    }

    [Fact]
    public async Task PcmLifecycle_ConsumerFailureStopsBlockedProducer()
    {
        using var wav = Wave();
        var progress = new PcmProgress();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await Assert.ThrowsAsync<IOException>(() => PcmStreaming.RunAsync(wav, WaveInput.Inspect(wav), progress,
            async (_, token) =>
            {
                started.SetResult();
                await Task.Delay(Timeout.Infinite, token);
            }, () => Assert.Fail("Failure must not signal successful end of input."),
            async token =>
            {
                await started.Task.WaitAsync(token);
                throw new IOException("fake inference failure");
            }, CancellationToken.None));
        Assert.False(progress.EndOfInputSignaled);
    }

    [Fact]
    public async Task PcmLifecycle_CancellationStopsBothSides()
    {
        using var wav = Wave();
        using var cancellation = new CancellationTokenSource();
        var progress = new PcmProgress();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => PcmStreaming.RunAsync(
            wav, WaveInput.Inspect(wav), progress, async (_, token) =>
            {
                cancellation.Cancel();
                await Task.Delay(Timeout.Infinite, token);
            }, () => Assert.Fail("Canceled input must not signal successful completion."),
            token => Task.Delay(Timeout.Infinite, token), cancellation.Token));
        Assert.False(progress.EndOfInputSignaled);
    }

    [Fact]
    public async Task PcmLifecycle_RejectsPrematureInferenceCompletion()
    {
        using var wav = Wave();
        var progress = new PcmProgress();
        await Assert.ThrowsAsync<InvalidDataException>(() => PcmStreaming.RunAsync(
            wav, WaveInput.Inspect(wav), progress, (_, _) => Task.CompletedTask,
            () => Assert.Fail("Premature inference must cancel feeding."),
            _ => Task.CompletedTask, CancellationToken.None));
    }

    [Fact]
    public async Task PcmLifecycle_FinishFailureIsNotSuccessfulEndOfInput()
    {
        using var wav = Wave();
        var progress = new PcmProgress();
        await Assert.ThrowsAsync<IOException>(() => PcmStreaming.RunAsync(
            wav, WaveInput.Inspect(wav), progress, (_, _) => Task.CompletedTask,
            () => throw new IOException("fake queue finalization failure"),
            token => Task.Delay(Timeout.Infinite, token), CancellationToken.None));
        Assert.False(progress.EndOfInputSignaled);
        Assert.Equal(16000, progress.SubmittedSamples);
    }

    private static TimingEvidence Inspect(string response)
    {
        using var document = JsonDocument.Parse(response);
        return SpeechEvidence.Inspect(document.RootElement, 25);
    }

    private static MemoryStream Wave(ushort channels = 1, uint rate = 16000, int bytes = 32000)
    {
        var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write((uint)(46 + bytes + bytes % 2));
        writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        writer.Write(16u);
        writer.Write((ushort)1);
        writer.Write(channels);
        writer.Write(rate);
        writer.Write(32000u);
        writer.Write((ushort)2);
        writer.Write((ushort)16);
        writer.Write(Encoding.ASCII.GetBytes("LIST"));
        writer.Write(1u);
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write((uint)bytes);
        writer.Write(new byte[bytes + bytes % 2]);
        stream.Position = 0;
        return stream;
    }
}
