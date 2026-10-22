# Podcast audio to multilingual subtitles

Local audio -> multilingual Whisper -> optional text cleanup -> SRT or WebVTT.
Run the commands below from the repository root. Install .NET 8+ and FFmpeg on PATH.
The first run downloads the selected Whisper model. Only English cleanup or the explicit
experimental mode downloads/loads s1-mini.

## Real podcast assets

[`assets/ntn459`](assets/ntn459/README.md) contains both complete owner-supplied recordings:

- `Recording.m4a`: voice only, about 23.5 minutes.
- `ntn459.mp3`: edited episode, about 24 minutes, including music.

The originals are unchanged. Their hashes and redistribution information are documented
with the assets. The two edits have different timelines.

## Spanish-preserving transcription

```powershell
dotnet run --project src\samples\PodcastToSrt -- `
  src\samples\PodcastToSrt\assets\ntn459\Recording.m4a `
  artifacts\podcast\voice-es.srt --model whisper-base --language es

dotnet run --project src\samples\PodcastToSrt -- `
  src\samples\PodcastToSrt\assets\ntn459\ntn459.mp3 `
  artifacts\podcast\edited-es.vtt --model whisper-small --language es --vtt
```

Neither command sends Spanish through s1-mini. Its upstream v1 model supports English
only. The old sample used `whisper-tiny.en`, which is also English-only. Models such as
`whisper-base` and `whisper-small` are multilingual; `.en` variants are not.

`--cleanup auto` is the default. It enables s1-mini only for explicitly English
transcription or explicit English translation. Use `--cleanup off` to disable it.
For mixed-language audio, use `--cleanup off`, even if English is the primary language.
The sample does not perform per-segment language identification.

## Language auto mode

```powershell
dotnet run --project src\samples\PodcastToSrt -- `
  src\samples\PodcastToSrt\assets\ntn459\Recording.m4a `
  artifacts\podcast\auto.srt --model whisper-base --language auto --duration-seconds 25
```

Auto leaves the language unset in Whisper. **ElBruno.Whisper 0.9.0 does not expose a
reliable detected-language result:** its `DetectedLanguage` echoes the request. Reports
label auto as unconfirmed, and auto cleanup remains off. For known Spanish audio,
prefer `--language es`. In the NTN 459 excerpt, auto mode produced a repeated
`Commission` loop instead of a usable transcript. This is a failure demonstration,
not a recommended production path. Translation requires an explicit source language
with this backend.

## Spanish audio to English subtitles

```powershell
dotnet run --project src\samples\PodcastToSrt -- `
  src\samples\PodcastToSrt\assets\ntn459\Recording.m4a `
  artifacts\podcast\translated-en.srt --model whisper-base --language es --translate
```

Whisper performs translation to English. s1-mini then normalizes that English text.
This intentionally changes the subtitle language; it is not Spanish transcription.
The `.raw.srt` sibling preserves the pre-cleanup English translation for comparison.

## Experimental Spanish cleanup

```powershell
dotnet run --project src\samples\PodcastToSrt -- `
  src\samples\PodcastToSrt\assets\ntn459\Recording.m4a `
  artifacts\podcast\experimental-es.srt --model whisper-base --language es `
  --cleanup experimental --start-seconds 60 --duration-seconds 25
```

This explicitly opts into an unsupported-language experiment. Compare against `.raw.srt`;
check meaning, accents, names, numbers, deletions and accidental translation. Do not treat
an output file as evidence that Spanish normalization is supported.

## Decoding, full-length coverage and timing

The pinned Whisper 0.9.0 file API expects WAV/raw PCM and preprocesses at most 30 seconds
per inference call. The sample therefore:

1. Uses FFmpeg to decode MP3/M4A/WAV into temporary 16 kHz mono float PCM.
2. Processes the entire PCM timeline in non-overlapping windows of at most 25 seconds.
   It chooses a low-energy boundary within the last two seconds when possible.
3. Adds each window's absolute offset back to every cue. It does not remove silence or
   concatenate speech. Non-overlapping sample ranges need no overlap deduplication.
4. Writes subtitles, a `.raw.srt`/`.raw.vtt` sibling, and a JSON report after each window.
   Only a report with `complete: true` covers the entire requested audio.

`--start-seconds` and `--duration-seconds` enable reproducible excerpts. Excerpt subtitle
times remain relative to the original recording, not rebased to zero.

Some models/backend paths return one full-window fallback segment instead of real
timestamp spans. The JSON report marks this as **possibly fallback**. Such 23-25 second
cues are coarse timing, not word alignment or polished broadcast subtitles. Even native
timestamp spans need listening review. No forced alignment, diarization, reading-speed
optimization or music-source separation is performed.

The pinned dependency also omits the GPT-2 byte-to-Unicode decoding step. The small,
tested `WhisperTextDecoder` adapter completes that exact mapping, including Spanish
accents, before serialization or cleanup. It is specific to **ElBruno.Whisper 0.9.0**,
not a generic mojibake detector. Remove/re-evaluate it when upgrading that package.

## Outputs and errors

```text
PodcastToSrt <audio-file> [output-file] [--model MODEL_ID]
  [--language es|en|auto] [--translate] [--cleanup auto|off|experimental]
  [--vtt] [--drop-empty] [--start-seconds N] [--duration-seconds N]
```

All `KnownWhisperModels` IDs are accepted. Defaults preserve the prior English path
(`whisper-tiny.en`, English cleanup); selecting Spanish/auto without a model chooses
multilingual Base. Default output goes under `artifacts\podcast`, never beside the assets.
Unknown flags, invalid model/language combinations, decoding failures and invalid timing
fail explicitly. Ctrl+C cancels the current run; any existing partial report remains
marked incomplete. Original audio is never changed.

## Reproduce the evaluation

```powershell
.\src\samples\PodcastToSrt\Evaluate-Podcast.ps1
.\src\samples\PodcastToSrt\Evaluate-Podcast.ps1 -FullEpisodes
.\src\samples\PodcastToSrt\Evaluate-Podcast.ps1 -FullEpisodes -ValidateOnly
```

This opt-in evaluation compares Base/Small on the voice excerpt and edited opening,
then auto mode, English translation/cleanup, experimental Spanish cleanup and late speech.
`-FullEpisodes` also processes both complete originals with Base/Spanish and cleanup off.
It does not run in CI. Outputs are in ignored `artifacts\multilingual`.
`-ValidateOnly` audits existing reports and subtitle bounds without running models again.

Offline tests cover routing, Unicode decoding and absolute timing without downloading
models or reading audio. See [evaluation-results.md](evaluation-results.md) for measured
observations and limits. A completed inference run does not mean its transcription is
correct: automatic-language failure, music hallucinations and coarse timing need separate review.

For cross-machine continuation, see the [handoff note](../../../docs/multilingual-handoff.md).
The separate [Foundry Local evaluation](../FoundryLocalSpeech/README.md) probes an
alternative speech backend without changing this sample. Its initial timestamp and
multilingual Parakeet gates are blocked; it is not a replacement subtitle pipeline.
The [saved baseline](baseline/README.md) preserves the existing results without requiring
new inference.
