using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using ElBruno.S1Mini.Normalization;
using ElBruno.Whisper;
using PodcastToSrt;

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
try
{
    return await RunAsync(PodcastOptions.Parse(args), cancellation.Token);
}
catch (Exception error) when (error is ArgumentException or IOException or Win32Exception or OperationCanceledException)
{
    Console.Error.WriteLine(error.Message);
    Console.Error.WriteLine(PodcastOptions.Usage);
    return 1;
}

static async Task<int> RunAsync(PodcastOptions options, CancellationToken ct)
{
    if (!File.Exists(options.Input)) throw new FileNotFoundException("Audio input does not exist.", options.Input);
    var format = options.WebVtt ? SubtitleFormat.WebVtt : SubtitleFormat.Srt;
    var output = Path.GetFullPath(options.Output);
    Directory.CreateDirectory(Path.GetDirectoryName(output)
        ?? throw new ArgumentException("Output must name a subtitle file, not a filesystem root."));
    var rawPath = Path.ChangeExtension(output, ".raw" + Path.GetExtension(output));
    var reportPath = Path.ChangeExtension(output, ".json");
    var pcmPath = Path.Combine(Path.GetTempPath(), $"podcast-{Guid.NewGuid():N}.f32");
    var timer = Stopwatch.StartNew();

    try
    {
        await DecodeAsync(options, pcmPath, ct);
        using var pcm = File.OpenRead(pcmPath);
        if (pcm.Length == 0 || pcm.Length % sizeof(float) != 0)
            throw new InvalidDataException("FFmpeg produced empty or invalid float PCM.");
        var totalSamples = pcm.Length / sizeof(float);
        Console.WriteLine($"Model={options.Model.Id}; language={options.Language ?? "auto (unconfirmed)"}; " +
            $"outputLanguage={(options.Translate ? "en" : options.Language ?? "unknown")}; " +
            $"translate={options.Translate}; cleanup={options.Cleanup}; applyCleanup={options.ShouldClean}");
        Console.WriteLine($"Audio={totalSamples / (double)AudioWindows.SampleRate:F3}s; non-overlapping windows <=25s.");
        if (options.Cleanup == CleanupMode.Experimental)
            Console.Error.WriteLine("WARNING: s1-mini supports English only. Non-English cleanup is experimental.");
        if (options.Language is null)
            Console.Error.WriteLine("Whisper 0.9.0 does not expose reliable detected-language metadata. Auto cleanup stays off.");

        using var whisper = await WhisperClient.CreateAsync(new WhisperOptions
        {
            Model = options.Model,
            Language = options.Language,
            Translate = options.Translate,
            EnableTimestamps = true,
            CacheDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ElBruno", "Whisper", "models"),
        }, cancellationToken: ct);

        using var normalizer = options.ShouldClean ? await TranscriptNormalizer.CreateAsync(cancellationToken: ct) : null;
        var rawCues = new List<SubtitleCue>();
        var cleanedCues = new List<SubtitleCue>();
        var windows = new List<object>();
        long cursor = 0;
        var buffer = new float[AudioWindows.MaxSamples];
        while (cursor < totalSamples)
        {
            ct.ThrowIfCancellationRequested();
            pcm.Position = cursor * sizeof(float);
            var count = (int)Math.Min(buffer.Length, totalSamples - cursor);
            pcm.ReadExactly(MemoryMarshal.AsBytes(buffer.AsSpan(0, count)));
            var consumed = AudioWindows.FindBoundary(buffer, count, totalSamples - cursor);
            var offset = TimeSpan.FromSeconds(options.StartSeconds + cursor / (double)AudioWindows.SampleRate);
            var windowDuration = TimeSpan.FromSeconds(consumed / (double)AudioWindows.SampleRate);
            var result = await whisper.TranscribeAsync(buffer.AsMemory(0, consumed), AudioWindows.SampleRate, ct);
            var segments = result.Segments ?? throw new InvalidDataException("Whisper did not return timestamped segments.");
            var lastEnd = TimeSpan.Zero;
            foreach (var segment in segments)
            {
                if (segment.Start < lastEnd)
                    throw new InvalidDataException("Whisper returned overlapping or nonchronological segments.");
                var text = WhisperTextDecoder.Decode(segment.Text).Trim();
                var cue = AudioWindows.Cue(segment.Start, segment.End, text, offset, windowDuration, format);
                lastEnd = segment.End;
                rawCues.Add(cue);
                var cleaned = normalizer is null ? text : await normalizer.NormalizeAsync(text, cancellationToken: ct);
                cleaned = string.Join(" ", cleaned.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
                if (cleaned.Length > 0 || !options.DropEmpty) cleanedCues.Add(cue with { Text = cleaned });
            }
            var coarse = segments.Count == 1 && segments[0].Start == TimeSpan.Zero &&
                (segments[0].End - windowDuration).Duration() < TimeSpan.FromMilliseconds(40);
            windows.Add(new
            {
                offsetSeconds = offset.TotalSeconds,
                durationSeconds = windowDuration.TotalSeconds,
                reportedLanguage = result.DetectedLanguage,
                timing = coarse ? "full-window (may be backend fallback)" : "timestamp spans (not independently aligned)",
                rawText = WhisperTextDecoder.Decode(result.Text),
                segmentCount = segments.Count,
            });
            cursor += consumed;
            Console.WriteLine($"Processed {cursor / (double)AudioWindows.SampleRate:F1}/{totalSamples / (double)AudioWindows.SampleRate:F1}s, {rawCues.Count} cues.");
            await SaveAsync();
        }
        Console.WriteLine($"Wrote {output}, {rawPath} and {reportPath} in {timer.Elapsed}.");
        return 0;

        async Task SaveAsync()
        {
            await File.WriteAllTextAsync(rawPath, SubtitleParser.Write(rawCues, format), ct);
            await File.WriteAllTextAsync(output, SubtitleParser.Write(cleanedCues, format), ct);
            await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new
            {
                input = Path.GetFileName(options.Input),
                model = options.Model.Id,
                language = options.Language ?? "auto-unconfirmed",
                outputLanguage = options.Translate ? "en" : options.Language ?? "unknown",
                options.Translate,
                cleanup = options.Cleanup.ToString(),
                appliedCleanup = options.ShouldClean,
                sourceOffsetSeconds = options.StartSeconds,
                audioSeconds = totalSamples / (double)AudioWindows.SampleRate,
                processedSeconds = cursor / (double)AudioWindows.SampleRate,
                complete = cursor == totalSamples,
                elapsedSeconds = timer.Elapsed.TotalSeconds,
                whisperPackageVersion = "0.9.0",
                whisperAssemblyVersion = typeof(WhisperClient).Assembly.GetName().Version?.ToString(),
                byteLevelDecodingAdapter = true,
                framework = RuntimeInformation.FrameworkDescription,
                operatingSystem = RuntimeInformation.OSDescription,
                processorCount = Environment.ProcessorCount,
                windows,
            }, new JsonSerializerOptions { WriteIndented = true }), ct);
        }
    }
    finally
    {
        if (File.Exists(pcmPath)) File.Delete(pcmPath);
    }
}

static async Task DecodeAsync(PodcastOptions options, string output, CancellationToken ct)
{
    var info = new ProcessStartInfo("ffmpeg") { RedirectStandardError = true, UseShellExecute = false };
    foreach (var argument in new[] { "-hide_banner", "-loglevel", "error", "-nostdin", "-i", options.Input,
        "-ss", options.StartSeconds.ToString(CultureInfo.InvariantCulture) })
        info.ArgumentList.Add(argument);
    if (options.DurationSeconds is { } duration)
    {
        info.ArgumentList.Add("-t");
        info.ArgumentList.Add(duration.ToString(CultureInfo.InvariantCulture));
    }
    foreach (var argument in new[] { "-vn", "-ac", "1", "-ar", "16000", "-f", "f32le", output })
        info.ArgumentList.Add(argument);
    Process started;
    try
    {
        started = Process.Start(info) ?? throw new IOException("Could not start FFmpeg. Install it on PATH.");
    }
    catch (Win32Exception error)
    {
        throw new IOException("Could not start FFmpeg. Install ffmpeg on PATH before running this sample.", error);
    }
    using var process = started;
    using var registration = ct.Register(() => { if (!process.HasExited) process.Kill(entireProcessTree: true); });
    var errors = process.StandardError.ReadToEndAsync(ct);
    await process.WaitForExitAsync(ct);
    var message = await errors;
    if (process.ExitCode != 0) throw new IOException($"FFmpeg decoding failed: {message}");
}
