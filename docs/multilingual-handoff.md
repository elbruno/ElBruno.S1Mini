# Multilingual evaluation handoff

## Current state

The `PodcastToSrt` sample supports explicit Spanish, multilingual Whisper models,
English translation plus optional English cleanup, and explicitly experimental Spanish
cleanup. s1-mini v1 is English-only, so Spanish bypasses it by default.

The complete owner-authorized episode assets are committed under
`src/samples/PodcastToSrt/assets/ntn459`. They include the voice-only M4A and edited MP3
with music. Attribution, redistribution confirmation and original hashes are in the
asset README.

Start with:

- [Sample usage and limitations](../src/samples/PodcastToSrt/README.md)
- [Measured results](../src/samples/PodcastToSrt/evaluation-results.md)
- [Saved baseline outputs](../src/samples/PodcastToSrt/baseline/README.md)
- [Blog draft](blogs/26-10-21-srt-support.md)

Both full recordings were processed with multilingual Whisper Base and explicit
Spanish: 59 voice-only cues and 61 edited cues. These are coarse full-window spans,
not verified speech alignment. Auto-language mode failed on the tested excerpt.
No human-reviewed reference transcripts or WER/CER scores are available.

The pinned ElBruno.Whisper 0.9.0 backend needs sample-level workarounds:
FFmpeg decoding for MP3/M4A, explicit windows to avoid its 30-second preprocessing
limit, and byte-level decoding to preserve Spanish accents. Its reported language
echoes the request rather than confirming automatic language identification.

## Resume on another machine

Clone this repository or pull `main` into an existing clean checkout.
Install the .NET 8 and 10 SDKs for the full solution, and FFmpeg on PATH for audio runs.
Models download on first inference; cached models and temporary decoded PCM are not
transferred through Git. There are no cloud credentials required for local inference.

From the repository root:

```powershell
dotnet build ElBruno.S1Mini.slnx -c Release
dotnet test src\tests\ElBruno.S1Mini.Tests -c Release

# Optional, downloads models and regenerates the full comparison:
.\src\samples\PodcastToSrt\Evaluate-Podcast.ps1 -FullEpisodes
```

The existing LiveMicTranscription sample is Windows-targeted. Check supported OS/runtime
requirements before using that sample or the full solution on another operating system.

Baseline artifacts are saved in Git; new evaluations write to ignored
`artifacts\multilingual`. Do not edit original audio or treat model hypotheses as reference
transcripts.

## Foundry Local follow-up

The requested plan was approved for implementation on 2026-10-22. The isolated
[FoundryLocalSpeech evaluation](../src/samples/FoundryLocalSpeech/README.md) now
captures SDK/catalog evidence and raw transcription responses, validates decoded WAV
duration and gates subtitle serialization on actual segment timestamps.
See its [results](../src/samples/FoundryLocalSpeech/evaluation-results.md) for the
measured blockers. The blog and existing Whisper pipeline remain unchanged.

The initial gates failed: the catalog offers English-only Parakeet v2, not multilingual
v3, and the tested Foundry Whisper Base paths returned no acoustic timing. Full-episode
Foundry runs, human-scored accuracy, cleanup/runtime coexistence and offline inference
verification remain unperformed. No model conversion or cross-repository change
was attempted.

A subsequent approved follow-up added real incremental PCM input rather than
assuming streamed output from a file request is equivalent. **Spanish Nemotron
and Nemotron 3.5 both transcribed Spanish successfully** on voice 60-85 seconds,
edited 0-25 seconds, a 75-second voice passage and voice from 1380 seconds to EOF.
Both submitted all decoded samples and finalized normally, but neither returned
acoustic segment timestamps. Nemotron 3.5 also emits literal `<es-US>` markers in
some raw transcripts. No accuracy ranking or other-language claim is established.

Use `-PcmStreaming` in the Foundry runner for these models. Spanish-only Nemotron
omits unsupported language conditioning; Nemotron 3.5 sends explicit Spanish.
The [Nemotron snapshot](../src/samples/FoundryLocalSpeech/baseline/2026-10-22-nemotron.json)
preserves exact variants, settings, fixture hashes, raw text and timing/queue
evidence. The initial snapshot is unchanged. The remaining subtitle blocker is
real acoustic alignment, not an inability to transcribe Spanish. A separate
aligner was not implemented; the blog's first step remains unchanged.

## Original comparison scope

The earlier handoff recorded the intended comparison scope:

> Plan comparisons of my ElBruno .NET speech libraries, including Parakeet and other
> available backends, for Spanish and multilingual podcast subtitles. Discover the
> actual repositories, package versions, model variants, supported languages and
> timestamp behavior before selecting candidates. If necessary, propose library
> updates to current models or ONNX conversion of newer models for local .NET use.

The plan should:

- Inventory actual ElBruno libraries and compatible models. Do not assume all Parakeet
  variants are multilingual or support Spanish.
- Compare the same owner-provided voice-only and edited recordings, keeping model/version,
  preprocessing, audio windows and settings reproducible.
- Separate ASR quality, language detection, translation, normalization and alignment.
  Include music/silence hallucinations, technical terms and complete-episode coverage.
- Establish listening-reviewed reference passages before reporting WER/CER, and match
  corresponding speech independently across the differently edited timelines.
- Evaluate native timing versus coarse/inferred timing and subtitle readability.
- Assess model licenses, ONNX export feasibility, runtime/operator support, model sizes
  and hardware requirements before proposing conversions or dependency upgrades.
- Identify which repositories would change and which public packages would require a
  release. Obtain approval before implementation, model conversion or cross-repo changes.

No new NuGet release was needed for the multilingual sample changes: the public library
API and package version remain 0.2.1.
