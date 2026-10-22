using System.Runtime.CompilerServices;

namespace FoundryLocalSpeech;

public sealed class PcmProgress
{
    private volatile bool _endOfInputSignaled;

    public long SubmittedSamples { get; internal set; }
    public int SubmittedChunks { get; internal set; }
    public bool EndOfInputSignaled
    {
        get => _endOfInputSignaled;
        internal set => _endOfInputSignaled = value;
    }
}

public static class PcmStreaming
{
    public const int ChunkBytes = 4096;
    public const int MaxQueuedChunks = 8;

    public static async IAsyncEnumerable<ReadOnlyMemory<byte>> ReadChunksAsync(
        Stream source, WaveData data, int chunkBytes = ChunkBytes,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (chunkBytes <= 0 || chunkBytes % 2 != 0)
            throw new ArgumentOutOfRangeException(nameof(chunkBytes), "PCM chunks must contain whole 16-bit samples.");
        if (data.Offset < 0 || data.Length <= 0 || data.Length % 2 != 0 ||
            data.Offset > source.Length || data.Length > source.Length - data.Offset)
            throw new InvalidDataException("The PCM data range is invalid.");
        source.Position = data.Offset;
        long remaining = data.Length;
        while (remaining > 0)
        {
            ct.ThrowIfCancellationRequested();
            var bytes = new byte[(int)Math.Min(chunkBytes, remaining)];
            await source.ReadExactlyAsync(bytes, ct);
            remaining -= bytes.Length;
            yield return bytes;
        }
    }

    public static async Task RunAsync(Stream source, WaveData data, PcmProgress progress,
        Func<ReadOnlyMemory<byte>, CancellationToken, Task> push,
        Action finish, Func<CancellationToken, Task> consume, CancellationToken ct)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var consumer = GuardAsync(async () =>
        {
            await consume(stop.Token);
            if (!progress.EndOfInputSignaled)
                throw new InvalidDataException("Inference ended before the PCM producer finished.");
        });
        var producer = GuardAsync(async () =>
        {
            await foreach (var chunk in ReadChunksAsync(source, data, ct: stop.Token))
            {
                await push(chunk, stop.Token);
                progress.SubmittedSamples += chunk.Length / 2;
                progress.SubmittedChunks++;
            }
            stop.Token.ThrowIfCancellationRequested();
            // Publish completion before signaling native code, which can finish immediately.
            progress.EndOfInputSignaled = true;
            try
            {
                finish();
            }
            catch
            {
                progress.EndOfInputSignaled = false;
                throw;
            }
        });
        await Task.WhenAll(producer, consumer);

        async Task GuardAsync(Func<Task> action)
        {
            try
            {
                stop.Token.ThrowIfCancellationRequested();
                await action();
            }
            catch
            {
                stop.Cancel();
                throw;
            }
        }
    }
}
