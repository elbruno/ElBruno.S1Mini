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

## Next session: plan first

The user's next requested step is a plan, not immediate implementation:

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
