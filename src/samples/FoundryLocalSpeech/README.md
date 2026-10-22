# Foundry Local speech feasibility evaluation

An opt-in Windows C# experiment for replacing the speech-to-text stage of
[`PodcastToSrt`](../PodcastToSrt/README.md). It does not change that sample, the
normalizer package, or the blog's recommended pipeline.

**Current result: blocked for timestamped multilingual subtitles.** On 2026-10-22,
the catalog exposed English-only Parakeet v2, not multilingual v3. Foundry Local
Whisper Base transcribed Spanish, but neither the tutorial SDK nor the newer typed
SDK supplied acoustic timestamps. A follow-up tested **Spanish Nemotron and
multilingual Nemotron 3.5 with incremental PCM input: both transcribe Spanish**,
including longer and closing passages, but neither supplies acoustic timestamps
on the pinned SDK. See [measured results](evaluation-results.md), the initial
[saved evidence](baseline/2026-10-22.json), and the
[Nemotron evidence](baseline/2026-10-22-nemotron.json).

## Prerequisites and isolation

- Windows x64, a .NET SDK capable of building .NET 8 projects, and .NET 8 runtime.
- FFmpeg on PATH, or an explicit executable path passed with `-Ffmpeg`.
- `Microsoft.AI.Foundry.Local` **2.2.1**, pinned in this sample only. This newer unified
  package includes Windows runtime support. It supersedes the tutorial's
  `Microsoft.AI.Foundry.Local.WinML` 1.2.4 route tested during the initial probe.
- Network access for package restoration, catalog retrieval and initial model downloads.
  Inference uses the in-process local SDK, not an Azure speech endpoint. Offline
  startup, telemetry behavior and coexistence with s1-mini inference are not verified.

No Foundry CLI or Azure credentials are required. The harness does not start a web
server or download all hardware execution providers. The recorded candidates are
explicit CPU variants. Other architectures/providers have not been evaluated.
It never changes original audio or runs inference in automated tests.

## Discover actual models and API capabilities

Run from the repository root, using a new output directory for every invocation:

```powershell
dotnet run --project src\samples\FoundryLocalSpeech -c Release -- `
  catalog artifacts\foundry-speech\catalog
```

`catalog.json` records available variants, registry revisions, licenses, model sizes
and execution providers. `api.json` records package/assembly versions, API properties
and environment details. Catalog availability can change; do not assume an alias
means a particular model revision or language capability.

In the measured catalog:

| Candidate | Exact CPU variant | Eligibility |
|---|---|---|
| Whisper Base | `openai-whisper-base-generic-cpu:3` | Spanish text probe completed; subtitle timing blocked |
| Spanish Nemotron | `nemotron-speech-streaming-es-0.6b-ft-generic-cpu:1` | Spanish PCM transcription completed; subtitle timing blocked |
| Nemotron 3.5 | `nemotron-3.5-asr-streaming-0.6b-generic-cpu:3` | Spanish PCM transcription completed; subtitle timing blocked |
| Parakeet v2 | `parakeet-tdt-0.6b-v2-generic-cpu:1` | English only; attempted file inference failed |
| Parakeet v3 | Not present | Multilingual evaluation blocked |

Additional Whisper variants and English-only Nemotron were discovered, not exercised.
The catalog's license label is not a substitute for upstream weight terms:
Parakeet's entry reports MIT but links NVIDIA's CC BY 4.0 terms. Resolve that
discrepancy before redistributing weights; this sample distributes no model weights.

## Reproduce the Spanish excerpt probes

```powershell
.\src\samples\FoundryLocalSpeech\Evaluate-FoundrySpeech.ps1 `
  -AudioFile src\samples\PodcastToSrt\assets\ntn459\Recording.m4a `
  -ModelId openai-whisper-base-generic-cpu:3 `
  -Language es -StartSeconds 60 -DurationSeconds 25 `
  -OutputDirectory artifacts\foundry-speech\voice-base-es

.\src\samples\FoundryLocalSpeech\Evaluate-FoundrySpeech.ps1 `
  -AudioFile src\samples\PodcastToSrt\assets\ntn459\ntn459.mp3 `
  -ModelId openai-whisper-base-generic-cpu:3 `
  -Language es -StartSeconds 0 -DurationSeconds 25 `
  -OutputDirectory artifacts\foundry-speech\edited-base-es
```

Add `-Streaming` and choose another output directory to capture streamed speech
items and the final aggregate. Use `-Ffmpeg 'C:\path\to\ffmpeg.exe'` when needed.

The script decodes a bounded excerpt into signed 16-bit mono 16 kHz PCM WAV,
retaining silence and original source offsets. `preparation.json` records original
and decoded SHA-256 hashes, FFmpeg version, arguments and exit status. The C#
program validates the WAV container and derives actual duration from its sample
count, rather than assuming the requested excerpt length was available.
Decoded audio remains in ignored artifacts for reproduction.

The language option sets a requested language, not proof that a model supports it
or detected it correctly. For file input, `auto` omits the language option; it does not establish
automatic language identification. In the pinned Whisper implementation, omitting
language actually defaults to English, so `auto` is diagnostic only and emits a
warning. Prefer explicit `es` for the podcast fixtures. No translation is requested.

## Reproduce Nemotron incremental PCM input

`-Streaming` streams **output from a file request**. It does not feed audio
incrementally. Use the separate `-PcmStreaming` switch for the Nemotron path:

```powershell
.\src\samples\FoundryLocalSpeech\Evaluate-FoundrySpeech.ps1 `
  -AudioFile src\samples\PodcastToSrt\assets\ntn459\Recording.m4a `
  -ModelId nemotron-speech-streaming-es-0.6b-ft-generic-cpu:1 `
  -Language es -StartSeconds 60 -DurationSeconds 75 -PcmStreaming `
  -OutputDirectory artifacts\foundry-speech\nemotron-es-continuity

.\src\samples\FoundryLocalSpeech\Evaluate-FoundrySpeech.ps1 `
  -AudioFile src\samples\PodcastToSrt\assets\ntn459\Recording.m4a `
  -ModelId nemotron-3.5-asr-streaming-0.6b-generic-cpu:3 `
  -Language es -StartSeconds 60 -DurationSeconds 75 -PcmStreaming `
  -OutputDirectory artifacts\foundry-speech\nemotron35-continuity
```

For each model, the recorded matrix also includes voice 60-85 seconds, edited MP3
0-25 seconds, and voice from 1380 seconds to actual EOF (request 60 seconds).
Use a distinct output directory for each run. The source timelines are independent.
Actual sample counts, not requested durations, determine each excerpt's duration.

The harness sends a PCM format descriptor and a retained native input queue.
Only validated WAV `data` bytes are fed: owned 4096-byte chunks (2048 samples),
including the final short chunk, with at most eight chunks queued before a push.
The output consumer starts before the producer. The producer signals end of input
once; the harness drains output, awaits the final aggregate, and checks submitted
sample counts and an empty queue. It does not concatenate partial hypotheses.
This is bounded, buffered replay, not microphone capture or a real-time latency test.

Producer/consumer failures cancel their counterpart and native inference. A
120-second cooperative timeout and 150-second process watchdog bound PCM probes.
The hard watchdog writes a failure and exits without disposing potentially live
native buffers. These fixed limits are for bounded excerpts, not full episodes.

Spanish-only Nemotron has no `lang_id` input. Its PCM requests omit language
conditioning and reject an English request; `es` or `auto` still means a Spanish-only
model, not language detection. Nemotron 3.5 sends `language=es`; PCM `auto` sends
the literal `auto` value, but automatic detection has not been tested. No
Whisper-specific temperature is sent in PCM mode. Requested and actual options
are saved separately.

Nemotron 3.5 sometimes emits literal `<es-US>` markers inside its transcript.
Raw evidence preserves them. They are not speech or usable timing, and no text
cleanup or marker removal is silently applied. The vendor's multilingual support
does not establish tested code-switching or accuracy in languages beyond Spanish.

## Inspect a prepared WAV directly

```powershell
dotnet run --project src\samples\FoundryLocalSpeech -c Release -- `
  transcribe openai-whisper-base-generic-cpu:3 `
  artifacts\foundry-speech\voice-base-es\input.wav `
  artifacts\foundry-speech\direct es 60
```

Use `stream` instead of `transcribe` to inspect incremental output, or `pcm-stream`
to feed incremental PCM input. Requests and
native response items are disposed before unloading the model.

| Output | Contents |
|---|---|
| `catalog.json`, `api.json`, `model.json` | Resolved model and API evidence |
| `input.json` | Decoded hash, measured duration, requested language and source offset |
| `response.json` | Final transcript, finish reason, segment text, native millisecond timings and words |
| `chunks.json` | Streamed items, in arrival order, when streaming is requested |
| `report.json` | Completed request, timing verdict, actual request options, PCM progress/queue checks, runtime observations and explicit unverified claims |
| `failure.json` | Incomplete request and surfaced error |
| `raw.srt`, `raw.vtt` | Only written when all segments have valid non-coarse timing |

SDK v2 items are copied into JSON while their native owners are alive. The snapshot
includes native millisecond values and their conversion to seconds. Streamed text
is never blindly concatenated or counted twice with the final result.

Exit codes: **0** means catalog saved or numeric segment timing available,
**1** means error, **2** means inference completed but the subtitle timing gate
failed. Exit 0 does not mean human-verified alignment or publication readiness.
Existing output directories are rejected instead of overwriting prior evidence.
If `failure.json` exists, the invocation failed even if a preceding inference
report was saved before a later cleanup error.

The timing gate rejects absent/null times, invalid or overlapping spans, out-of-bounds
times and a single full-window span that could be fallback timing. It never uses
arrival times, uniform text division or stream chunk count to manufacture subtitles.
Valid spans retain their absolute source offset through the existing subtitle writer.
The one-millisecond duration tolerance allows millisecond rounding of sample lengths.

Cleanup is deliberately **off for every probe**, including English, to isolate ASR.
No s1-mini model is loaded by this harness. The existing library remains English-only.

## Remaining gates before changing the blog

The subtitle timing prerequisite failed, so full-episode runs and the cleanup
integration were not performed. Text evaluation continued with the Nemotron matrix
despite that failure. Do not treat these Spanish transcripts as a measured accuracy
improvement. WER/CER comparison can proceed independently of subtitle alignment.

Once a candidate exposes usable timing and supports the required languages:

1. Prepare listening-reviewed verbatim references with timed boundaries, separately
   for each recording's timeline, plus English and English/Spanish code-switching clips.
2. Score raw WER/CER against those references using identical excerpts for the current
   Whisper baseline and eligible Foundry models. Review names, numbers, unintended
   translation, omissions, repetitions and music/silence hallucinations separately.
3. Process both complete original recordings with explicit coverage accounting.
   Verify late speech, boundary duplicates, silence offsets and subtitle readability.
4. Exercise English-only s1-mini cleanup, bypass Spanish/mixed/unknown languages,
   verify exact timing preservation and check native-runtime coexistence.
5. Measure cold setup separately from three warm runs and verify cached offline
   inference. Initial observations here are not controlled latency benchmarks.

Proposed targets from the evaluation plan remain: at least 15% relative Spanish WER
improvement over the best scored current baseline, no more than two percentage
points English WER regression, 95% of reviewed boundaries within 500 ms, complete
annotated speech coverage, no fabricated sentences in reviewed non-speech regions,
and exact timing preservation through cleanup. No target has been scored or passed.

Offline regression coverage:

```powershell
dotnet test src\tests\ElBruno.S1Mini.Tests -c Release `
  --filter FullyQualifiedName~FoundrySpeechTests
```

References:

- [Microsoft Learn audio tutorial](https://learn.microsoft.com/en-us/azure/foundry-local/how-to/how-to-transcribe-audio?tabs=windows&pivots=programming-language-csharp)
- [Current typed SDK inference guide](https://github.com/microsoft/Foundry-Local/blob/main/sdk_v2/INFERENCING.md)
- [NVIDIA Parakeet v2 model card](https://huggingface.co/nvidia/parakeet-tdt-0.6b-v2)
- [NVIDIA multilingual Parakeet v3 model card](https://huggingface.co/nvidia/parakeet-tdt-0.6b-v3)
- [Microsoft Spanish Nemotron asset description](https://github.com/Azure/azureml-assets/blob/main/assets/models/foundrylocal/nemotron-speech-streaming-es-0.6b-ft-generic-cpu/description.md)
- [NVIDIA Nemotron 3.5 model card](https://huggingface.co/nvidia/nemotron-3.5-asr-streaming-0.6b)
