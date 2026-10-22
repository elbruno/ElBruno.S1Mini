# Foundry Local speech evaluation, 2026-10-22

**Decision: do not replace step 1 of the subtitle pipeline yet.** Spanish text
transcription works on the tested Foundry Local Whisper Base path and, in the
follow-up below, both Nemotron PCM paths. None supplies acoustic timestamps on
the pinned SDK. Multilingual Parakeet v3 was not in the catalog.
The existing Whisper sample and blog pipeline remain unchanged.

The [bounded evidence snapshot](baseline/2026-10-22.json) preserves catalog/API
metadata, fixture hashes, raw transcript text, representative native response items,
timing verdicts and the Parakeet failure. Complete local outputs are under ignored
`artifacts\foundry-speech`. No model weights or generated audio are added to Git.

## Environment

- Windows x64, OS version 10.0.26300; AMD EPYC 74F3, 36 logical processors exposed.
- .NET SDK 11.0.100-rc.2.26504.105; application runtime .NET 8.0.32.
- FFmpeg 9.0.2; the stale existing installation was restored because its executable
  was missing. The runner accepts an explicit FFmpeg path.
- Foundry CLI 0.11.0 for independent catalog discovery only. Its initially stopped
  daemon was started by discovery and stopped after the probes; inference itself
  used the in-process SDK.
- Initial tutorial route: `Microsoft.AI.Foundry.Local.WinML` 1.2.4.
- Final harness: `Microsoft.AI.Foundry.Local` 2.2.1 and its typed `AudioSession`.
  The published package's source revision is
  `1f0d8ffd3d54443e12230da051e0c63e75645356`.
- Selected speech variants use `CPUExecutionProvider`. The v2 runtime warned that
  NVML initialization failed and CUDA detection was unavailable. No GPU/NPU result
  or hardware-acceleration claim is made.

This is a different host from the existing NTN baseline's documented i9 machine.
Neither the host nor preprocessing/tool versions establish a controlled performance
comparison with those earlier runs.

## Gate 1: actual model availability

Both the CLI catalog and the SDK catalog exposed:

| Model | Exact selected CPU variant | Approximate catalog size |
|---|---|---:|
| Parakeet v2 | `parakeet-tdt-0.6b-v2-generic-cpu:1` | 692 MB |
| Whisper Base | `openai-whisper-base-generic-cpu:3` | 205 MB |
| Whisper Small | `openai-whisper-small-generic-cpu:4` | 438 MB |
| Whisper Medium | `openai-whisper-medium-generic-cpu:4` | 937 MB |
| Whisper Large v3 Turbo | `openai-whisper-large-v3-turbo-generic-cpu:4` | 1,284 MB |

Whisper Tiny and three Nemotron variants, including a Spanish variant, were also
listed. Initially only Base and the Parakeet v2 file path were exercised; the
Nemotron follow-up below is separate. Presence in
the catalog is not evidence that a particular SDK request path can execute it.

NVIDIA identifies [Parakeet v2](https://huggingface.co/nvidia/parakeet-tdt-0.6b-v2)
as English-only. [Parakeet v3](https://huggingface.co/nvidia/parakeet-tdt-0.6b-v3)
supports 25 languages, including Spanish, and advertises timestamps in its supported
runtimes. **No v3 entry appeared in the discovered catalog.** v2 is not an acceptable
substitute for the proposed multilingual model.

The Parakeet catalog metadata labels the model MIT, while its license-description
link leads to NVIDIA's CC BY 4.0 model terms. Weight redistribution needs a separate
license review; a catalog label alone does not resolve the discrepancy.

## Gate 2: timestamp-bearing output

Whisper Base was requested with explicit Spanish and temperature zero on the same
60-85 second voice excerpt used within the existing baseline's longer passage.
The edited recording was evaluated independently at 0-25 seconds. The two edits
were not treated as aligned recordings.

| SDK / path | Source excerpt | Response items | Items with start/end | Result |
|---|---|---:|---:|---|
| WinML 1.2.4, one-shot | Voice, 60-85 s | Segments null | 0 | Text only |
| WinML 1.2.4, streaming | Voice, 60-85 s | 113 text chunks | 0 | Text only |
| Unified 2.2.1, one-shot | Voice, 60-85 s | 113 speech segments | 0 | Token fragments, not timed utterances |
| Unified 2.2.1, streaming final result | Voice, 60-85 s | 113 speech segments | 0 | Token fragments, not timed utterances |
| Unified 2.2.1, one-shot | Edited, 0-25 s | 52 speech segments | 0 | Token fragments, not timed utterances |

The v2 segments had `Kind=None`, null start/end/language and empty word lists.
Final language and duration were also null. The final finish reason was `Stop`.
The harness returned exit code 2 and did not write SRT/WebVTT.

Representative native fields, copied while the result owner was alive:

```json
{
  "text": " con",
  "start": null,
  "end": null,
  "StartTimeMs": null,
  "EndTimeMs": null,
  "kind": "None",
  "Language": null,
  "UtteranceStart": false,
  "Words": []
}
```

The voice transcript included `gpt4 y cloudzone de antropi` and `openen a ir`.
These are unverified model hypotheses, not a reference transcript or evidence of
better name recognition. Spanish accents survived without the old dependency's
byte-decoding workaround. That observation is not an accuracy score.

### Why another timestamp option is not enough on this implementation

The package revision's
[Whisper generator](https://github.com/microsoft/Foundry-Local/blob/1f0d8ffd3d54443e12230da051e0c63e75645356/sdk_v2/cpp/src/inferencing/generative/audio/onnx_audio_generator.cc)
constructs the transcription prompt with **`<|notimestamps|>`**. It also defaults
to **English when language is omitted**, rather than doing automatic detection.

The same revision's
[speech-output design](https://github.com/microsoft/Foundry-Local/blob/1f0d8ffd3d54443e12230da051e0c63e75645356/sdk_v2/cpp/docs/SpeechOutputTypes.md)
explicitly documents one segment per decoded token and says timing, word details,
language and total duration are intentionally unset by the current producer.
Typed timestamp properties therefore represent contract capacity, not implemented
alignment. Merely converting the result to a verbose JSON shape cannot supply the
missing acoustic information.

This explains the measured result on these versions. It does not claim all future
Foundry Local models/releases or all separate Parakeet runtimes lack timestamps.

## Parakeet v2 diagnostic

To test model execution separately from Spanish eligibility, a synthetic English
clip was generated with Windows SAPI and decoded to the same PCM format. The
script and audio hashes are in the evidence snapshot. This was a capability
probe, not a human-speech accuracy fixture.

Variant `parakeet-tdt-0.6b-v2-generic-cpu:1` downloaded and loaded, but file
transcription through the unified 2.2.1 `AudioSession` failed:

```text
onnx_audio_generator.cc:140 fl::OnnxAudioGenerator::Create model does not support audio processing
```

The harness preserved `failure.json` and the wrapper recorded exit code 1.
No Parakeet transcript, timing or accuracy result is claimed. The failure applies
to this tested file-inference path, not proof that every live/streaming path or
runtime rejects the model. Resolving that would not make v2 multilingual.

## Runtime observations, not a speed benchmark

The saved v2 observations were 0.627 seconds for voice one-shot, 1.545 seconds for
voice streaming and 0.467 seconds for edited one-shot, each processing a 25-second
WAV. They exclude model download/load and include response capture overhead.
Streaming repeatedly saves accumulated chunks, so its overhead is especially
different. Peak process working sets were approximately 450, 450 and 445 MiB.

These are individual observations, not a three-run median, a cross-backend ranking
or a performance acceptance result. The existing baseline used a different host,
longer excerpts and a different timing scope.

## What is complete and what remains blocked

Implemented and exercised: isolated pinned SDK sample, actual catalog/API discovery,
hashed reproducible excerpt preparation, strict PCM duration validation, one-shot
and streaming response capture, explicit failure/blocked statuses, and timing-gated
serialization through the existing subtitle writer. Offline tests cover absent and
invalid times, coarse fallback spans, offsets, Unicode, WAV metadata/padding and
truncated/malformed inputs. Existing subtitle and podcast routing tests still pass.

**Not implemented or performed after the failed prerequisite gates:** a human-reference
WER/CER scorer, listening-reviewed reference creation, broader language/code-switching
fixtures, full-episode Foundry processing, subtitle readability/alignment scoring,
English cleanup integration, native-runtime coexistence measurements, offline
inference verification or controlled three-run warm benchmarks. No 15% WER improvement,
500 ms alignment target or full-file coverage claim is made.

Text accuracy can be evaluated separately. Resume the subtitle-specific
[evaluation gates](README.md#remaining-gates-before-changing-the-blog) only after
identifying a supported local path with both the required languages and real
acoustic timing. A separate Parakeet v3 runtime, forced aligner or upstream SDK
change would be an explicit architecture/dependency change, not a silent fallback.

## Nemotron follow-up: real incremental PCM input

**Both tested Nemotron variants work for Spanish text. Neither supplies subtitle
timestamps through this SDK.** A failed file-based path or Whisper's output does
not establish that the catalog's other speech models cannot transcribe.
The [separate evidence snapshot](baseline/2026-10-22-nemotron.json) preserves
the matrix, raw transcripts, representative native items, options and progress.

| Candidate | Exact CPU variant | Catalog size | Verified language eligibility |
|---|---|---:|---|
| Spanish Nemotron | `nemotron-speech-streaming-es-0.6b-ft-generic-cpu:1` | 729 MB | Microsoft's asset description identifies a Spanish-specialized fine-tune |
| Nemotron 3.5 | `nemotron-3.5-asr-streaming-0.6b-generic-cpu:3` | 756 MB | NVIDIA lists 40 language-locales, including Spanish es-US and es-ES |

The Spanish eligibility source is Microsoft's
[asset description](https://github.com/Azure/azureml-assets/blob/main/assets/models/foundrylocal/nemotron-speech-streaming-es-0.6b-ft-generic-cpu/description.md),
not an inference from the English base model's name. The multilingual source is
the [NVIDIA model card](https://huggingface.co/nvidia/nemotron-3.5-asr-streaming-0.6b).
Only Spanish audio was exercised here; other languages and code-switching remain
untested. English-only Nemotron was outside the approved follow-up scope.

### Input path and actual request settings

The pinned
[AudioSession implementation](https://github.com/microsoft/Foundry-Local/blob/1f0d8ffd3d54443e12230da051e0c63e75645356/sdk_v2/cpp/src/inferencing/generative/audio/audio_session.cc)
selects incremental audio processing for a PCM format descriptor plus an
`ItemQueue`. This differs from the single-file request used by the original probe.
The new `pcm-stream` mode feeds only validated PCM16 LE mono 16 kHz audio bytes
concurrently with output consumption. Chunk ownership transfers to the native
queue, which remains alive until inference and final-response copying finish.
Backpressure, cancellation, EOF, final aggregate capture and the final short input
chunk are handled explicitly. See [reproduction instructions](README.md#reproduce-nemotron-incremental-pcm-input).

Initial Spanish-only runs sent `language=es` and succeeded, but the runtime warned
that its encoder has no `lang_id` input. The final harness omits that inapplicable
option for this alias. Both short probes were repeated successfully without it;
the longer and closing Spanish-only probes also omitted it. Their original
warning-bearing runs are retained separately in the evidence. Nemotron 3.5 uses
`language=es` without that warning. No temperature or translation is requested.

### Completed real-audio matrix

Each cell below is successful inference (`Stop`), not subtitle success. Voice
and edited recordings have different timelines. The 75-second decode contained
1,200,002 samples (75.000125 seconds); the closing request stopped at source EOF
with 462,637 samples (28.9148125 seconds), not a fabricated 60-second span.

| Source / excerpt | Submitted samples / chunks | Spanish Nemotron token segments | Nemotron 3.5 token segments | Timed segments, either model |
|---|---:|---:|---:|---:|
| Voice, 60-85 s | 400,000 / 196 | 152 | 211 | 0 |
| Edited, 0-25 s | 400,000 / 196 | 76 | 116 | 0 |
| Voice, 60-135 s requested | 1,200,002 / 586 | 430 | 576 | 0 |
| Voice, 1380 s to EOF | 462,637 / 226 | 144 | 237 | 0 |

Every run submitted all decoded samples and signaled EOF. Runs after adding the
queue-drain assertion also recorded zero queued chunks at completion, including
both longer and closing runs. Both models' closing transcripts end with
`feliz año nuevo`. This demonstrates ingestion/finalization and late speech output,
not that every spoken word was correctly transcribed or that full-episode coverage
passed. There was no full-episode inference run or human-reviewed reference.

Spanish Nemotron's longer transcript continues beyond the first 25 seconds to
`se podía ser inteligencia artificial`. Nemotron 3.5 also continues to that
discussion, with recognition errors and interspersed literal `<es-US>` markers.
Both edited-opening transcripts begin `Buenas, buenas, bienvenidos...`.
Names and technical terms remain imperfect in the raw hypotheses. No WER/CER
score or accuracy ranking is justified by these outputs.

All inspected token segments still have null acoustic start/end fields, empty
word lists and no detected language metadata. The final transcript is taken from
the aggregate response, not concatenated with streaming hypotheses. The timing
gate returns `missing-timestamps`, exit code 2, and no SRT/WebVTT is written.
Stream arrival times, PCM chunk durations and language markers are not substitutes
for acoustic word/utterance alignment.

### Runtime and licensing caveats

For the 75-second passage, individual observed inference times were 42.082 seconds
for Spanish Nemotron and 13.799 seconds for Nemotron 3.5, with peak process working
sets about 1002 and 1039 MiB respectively. These include capture overhead, exclude
download/load and are not controlled repeat-run benchmarks or accuracy comparisons.
PCM is replayed as fast as the bounded queue permits, not at microphone cadence.
Initial downloads succeeded; no offline-startup claim is made.

The catalog labels both Nemotron entries MIT. Microsoft's Spanish asset description
also says MIT, but the catalog's linked Foundry Nemotron license-description path
returned 404 during this evaluation. NVIDIA's Nemotron 3.5 card instead declares
`OpenMDW-1.1` for the upstream model. Confirm the applicable derivative/weight terms
before redistribution rather than treating the catalog label as sufficient.
No model weights are committed or redistributed here.

### Decision and independent alignment need

Nemotron is a viable **text-transcription candidate** for further Spanish quality
evaluation. It is not yet a drop-in replacement for step 1, which must deliver
real segment timestamps. That requires a separately evaluated alignment stage or
an upstream path with populated timing. No aligner was added or tested, and no
synthetic timing is emitted. The blog table, existing Whisper path and library API
remain unchanged. s1-mini was not loaded; Spanish/mixed/unknown text must continue
to bypass its English-only cleanup by default.
