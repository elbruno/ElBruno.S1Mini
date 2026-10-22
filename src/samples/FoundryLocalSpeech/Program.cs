using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using ElBruno.S1Mini.Normalization;
using FoundryLocalSpeech;
using Microsoft.AI.Foundry.Local;
using Microsoft.Extensions.Logging;

const string usage = """
FoundryLocalSpeech catalog <new-output-directory>
FoundryLocalSpeech transcribe|stream|pcm-stream <exact-model-variant-id> <wav> <new-output-directory> <en|es|auto> <source-offset-seconds>
Exit codes: 0=catalog saved or numeric segments returned (alignment unverified), 1=error, 2=subtitle timing gate blocked.
""";

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
var json = new JsonSerializerOptions { WriteIndented = true };
string? output = null;
var ownsOutput = false;
PcmProgress? pcmProgress = null;
try
{
    if (args.Length != 2 && args.Length != 6) throw new ArgumentException(usage);
    var command = args[0];
    if (command != "catalog" && command != "transcribe" && command != "stream" && command != "pcm-stream")
        throw new ArgumentException(usage);
    if ((command == "catalog") != (args.Length == 2)) throw new ArgumentException(usage);
    output = Path.GetFullPath(args[command == "catalog" ? 1 : 3]);
    if (Directory.Exists(output) || File.Exists(output))
        throw new ArgumentException("Use a new output directory so earlier evidence cannot be overwritten.");
    Directory.CreateDirectory(output);
    ownsOutput = true;
    var ct = cancellation.Token;
    using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
    await FoundryLocalManager.CreateAsync(new Configuration
    {
        AppName = "ElBrunoS1MiniSpeechEvaluation",
        LogLevel = Microsoft.AI.Foundry.Local.LogLevel.Warning,
    }, loggerFactory.CreateLogger("FoundryLocal"), ct);
    using var manager = FoundryLocalManager.Instance;
    var catalog = await manager.GetCatalogAsync(ct);
    var models = await catalog.ListModelsAsync(ct);
    await SaveAsync("catalog.json", models.Select(model => new
    {
        model.Alias,
        variants = model.Variants.Select(variant => variant.Info),
    }));
    await SaveAsync("api.json", new
    {
        sdkPackage = "Microsoft.AI.Foundry.Local",
        sdkPackageVersion = "2.2.1",
        sdkAssemblyVersion = typeof(FoundryLocalManager).Assembly.GetName().Version?.ToString(),
        audioMethods = typeof(Session).GetMethods(BindingFlags.Public | BindingFlags.Instance |
            BindingFlags.DeclaredOnly).Select(method => method.ToString()),
        responseProperties = typeof(SpeechResultItem).GetProperties().Select(property => property.Name),
        segmentProperties = typeof(SpeechSegmentItem).GetProperties().Select(property => property.Name),
        framework = RuntimeInformation.FrameworkDescription,
        os = RuntimeInformation.OSDescription,
        architecture = RuntimeInformation.ProcessArchitecture.ToString(),
        processorCount = Environment.ProcessorCount,
    });
    if (command == "catalog") return 0;

    var input = Path.GetFullPath(args[2]);
    if (!File.Exists(input)) throw new FileNotFoundException("Audio input not found.", input);
    if (!new[] { "en", "es", "auto" }.Contains(args[4]))
        throw new ArgumentException("Language must be en, es or auto. No detected-language or translation claims are made.");
    if (args[4] == "auto" && command != "pcm-stream")
        Console.Error.WriteLine("WARNING: Omitting language is diagnostic only. The tested Whisper path defaults to English, not language detection.");
    var offset = double.Parse(args[5], CultureInfo.InvariantCulture);
    if (!double.IsFinite(offset) || offset < 0)
        throw new ArgumentException("Offset must be nonnegative and finite.");
    await using var inputStream = File.OpenRead(input);
    var wave = WaveInput.Inspect(inputStream);
    var duration = wave.DurationSeconds;
    inputStream.Position = 0;
    var inputHash = Convert.ToHexString(await SHA256.HashDataAsync(inputStream, ct));
    await SaveAsync("input.json", new
    {
        file = Path.GetFileName(input),
        sha256 = inputHash,
        sourceOffsetSeconds = offset,
        decodedDurationSeconds = duration,
        requestedLanguage = args[4],
        command,
        inputMode = command == "pcm-stream" ? "incremental-pcm" : "file",
    });
    var model = await catalog.GetModelVariantAsync(args[1], ct)
        ?? throw new ArgumentException($"Exact variant not found: {args[1]}. Inspect catalog.json.");
    await SaveAsync("model.json", model.Info);
    var wasCached = await model.IsCachedAsync(ct);
    var initialization = Stopwatch.StartNew();
    await model.DownloadAsync(ct: ct);
    await model.LoadAsync(ct);
    initialization.Stop();
    try
    {
        using var session = new AudioSession(model);
        var options = new RequestOptions();
        if (command != "pcm-stream") options.AdditionalOptions["temperature"] = "0";
        var spanishOnlyPcm = command == "pcm-stream" && model.Alias == "nemotron-speech-streaming-es-0.6b";
        if (spanishOnlyPcm && args[4] == "en")
            throw new ArgumentException("The Spanish-only Nemotron model cannot satisfy an English language request.");
        if (!spanishOnlyPcm && (args[4] != "auto" || command == "pcm-stream"))
            options.AdditionalOptions["language"] = args[4];
        if (spanishOnlyPcm)
            Console.WriteLine("Spanish-only model: language conditioning is not sent; the encoder has no lang_id input.");
        session.SetOptions(options);
        using var inputQueue = command == "pcm-stream" ? new ItemQueue() : null;
        using var request = new Request();
        if (inputQueue is not null)
        {
            request.AddItem(AudioItem.CreateFormatDescriptor("pcm", 16000, 1));
            request.AddItem(inputQueue, takeOwnership: false);
        }
        else request.AddItem(new AudioItem(input));
        var timer = Stopwatch.StartNew();
        JsonElement nativeResponse;
        FinishReason finishReason;
        if (command == "transcribe")
        {
            using var response = await session.ProcessRequestAsync(request, ct);
            nativeResponse = CaptureResponse(response);
            finishReason = response.FinishReason;
        }
        else if (inputQueue is null)
        {
            session.SetStreaming(true);
            var chunks = new List<JsonElement>();
            await using var stream = session.ProcessStreamingRequestAsync(request, ct);
            await foreach (var item in stream.WithCancellation(ct))
            {
                using (item)
                {
                    if (item is not SpeechSegmentItem segment)
                        throw new InvalidDataException($"Unexpected streaming item: {item.GetType().Name}");
                    chunks.Add(CaptureSegment(segment));
                    await SaveAsync("chunks.json", chunks);
                }
            }
            using var response = await stream.FinalResponse;
            nativeResponse = CaptureResponse(response);
            finishReason = response.FinishReason;
        }
        else
        {
            session.SetStreaming(true);
            pcmProgress = new PcmProgress();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(120));
            // A hung native call must not outlive its buffers or trigger an unsafe model unload.
            using var watchdog = new Timer(_ =>
            {
                Console.Error.WriteLine("PCM inference did not stop within its watchdog limit; terminating this probe process.");
                try
                {
                    File.WriteAllText(Path.Combine(output, "failure.json"), JsonSerializer.Serialize(new
                    {
                        complete = false,
                        error = "PCM inference exceeded the 150-second watchdog. Probe process terminated without native disposal.",
                        pcmProgress,
                    }, json));
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    Console.Error.WriteLine(error);
                }
                finally
                {
                    Environment.Exit(1);
                }
            }, null, TimeSpan.FromSeconds(150), Timeout.InfiniteTimeSpan);
            await using var stream = session.ProcessStreamingRequestAsync(request, timeout.Token);
            var chunks = new List<JsonElement>();
            await PcmStreaming.RunAsync(inputStream, wave, pcmProgress, async (bytes, token) =>
            {
                while (inputQueue.Count >= PcmStreaming.MaxQueuedChunks)
                    await Task.Delay(10, token);
                token.ThrowIfCancellationRequested();
                using var item = BytesItem.CreateOwned(bytes);
                inputQueue.Push(item);
            }, inputQueue.MarkFinished, async token =>
            {
                using var registration = token.Register(timeout.Cancel);
                await foreach (var item in stream.WithCancellation(token))
                {
                    using (item)
                    {
                        if (item is not SpeechSegmentItem segment)
                            throw new InvalidDataException($"Unexpected PCM streaming item: {item.GetType().Name}");
                        chunks.Add(CaptureSegment(segment));
                        await SaveAsync("chunks.json", chunks);
                    }
                }
            }, timeout.Token);
            using var response = await stream.FinalResponse;
            nativeResponse = CaptureResponse(response);
            finishReason = response.FinishReason;
            if (pcmProgress.SubmittedSamples != wave.Length / 2)
                throw new InvalidDataException("Not all decoded samples were submitted.");
            if (inputQueue.Count != 0)
                throw new InvalidDataException("Inference stopped with unconsumed PCM chunks.");
        }
        timer.Stop();
        await SaveAsync("response.json", nativeResponse);
        if (finishReason != FinishReason.Stop)
            throw new InvalidDataException($"Inference did not finish normally: {finishReason}. Inspect response.json.");
        var timing = SpeechEvidence.Inspect(nativeResponse, duration);
        if (timing.CanWriteSubtitles)
        {
            await File.WriteAllTextAsync(Path.Combine(output, "raw.srt"),
                SpeechEvidence.WriteSubtitles(timing, offset, SubtitleFormat.Srt), ct);
            await File.WriteAllTextAsync(Path.Combine(output, "raw.vtt"),
                SpeechEvidence.WriteSubtitles(timing, offset, SubtitleFormat.WebVtt), ct);
        }
        await SaveAsync("report.json", new
        {
            complete = true,
            scope = "one supplied audio file, not verified full-episode coverage",
            model = model.Id,
            requestedLanguage = args[4],
            detectedLanguageVerified = false,
            sourceOffsetSeconds = offset,
            decodedDurationSeconds = duration,
            inputSha256 = inputHash,
            command,
            inputMode = command == "pcm-stream" ? "incremental-pcm" : "file",
            requestOptions = options.AdditionalOptions,
            pcmProgress,
            expectedSamples = wave.Length / 2,
            pcmChunkBytes = command == "pcm-stream" ? PcmStreaming.ChunkBytes : (int?)null,
            remainingQueuedChunks = inputQueue?.Count,
            bufferedReplay = command == "pcm-stream",
            finishReason = finishReason.ToString(),
            sdkPackage = "Microsoft.AI.Foundry.Local",
            sdkPackageVersion = "2.2.1",
            wasCached,
            initializationSeconds = initialization.Elapsed.TotalSeconds,
            inferenceSeconds = timer.Elapsed.TotalSeconds,
            realTimeFactor = timer.Elapsed.TotalSeconds / duration,
            peakWorkingSetBytes = Process.GetCurrentProcess().PeakWorkingSet64,
            timing,
            cleanupApplied = false,
            accuracyScored = false,
            acousticAlignmentVerified = false,
        });
        Console.WriteLine($"{model.Id}: {timing.Status}. {timing.Reason}");
        return timing.CanWriteSubtitles ? 0 : 2;
    }
    finally
    {
        await model.UnloadAsync(CancellationToken.None);
    }

    Task SaveAsync<T>(string name, T value) =>
        File.WriteAllTextAsync(Path.Combine(output, name), JsonSerializer.Serialize(value, json), ct);

    static JsonElement CaptureResponse(Response response)
    {
        var results = response.OfType<SpeechResultItem>().ToList();
        if (results.Count != 1) throw new InvalidDataException("Expected exactly one final speech result.");
        var result = results[0];
        return JsonSerializer.SerializeToElement(new
        {
            text = result.Text,
            language = result.Language,
            durationMs = result.DurationMs,
            finishReason = response.FinishReason.ToString(),
            segments = result.Segments.Select(CaptureSegment).ToArray(),
        });
    }

    static JsonElement CaptureSegment(SpeechSegmentItem segment) =>
        JsonSerializer.SerializeToElement(new
        {
            text = segment.Text,
            start = segment.StartTimeMs / 1000d,
            end = segment.EndTimeMs / 1000d,
            segment.StartTimeMs,
            segment.EndTimeMs,
            kind = segment.Kind.ToString(),
            segment.Language,
            segment.UtteranceStart,
            segment.Words,
        });
}
catch (Exception error) when (error is ArgumentException or IOException or InvalidOperationException or
    FormatException or OverflowException or FoundryLocalException or OperationCanceledException or JsonException)
{
    Console.Error.WriteLine(error);
    if (ownsOutput && output is not null)
        await File.WriteAllTextAsync(Path.Combine(output, "failure.json"), JsonSerializer.Serialize(new
        {
            complete = false,
            errorType = error.GetType().Name,
            error = error.Message,
            pcmProgress,
        }, json));
    return 1;
}
