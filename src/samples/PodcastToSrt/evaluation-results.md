# NTN 459 multilingual evaluation

Run on 2026-10-22 with the complete fixtures in [`assets/ntn459`](assets/ntn459/README.md).
This is a reproducible inference/coverage report, **not a human-scored accuracy benchmark**.

## Environment and reproduction

- Intel Core i9-14900KF, 32 logical processors, approximately 32 GB RAM.
- Windows 11 Enterprise Insider Preview, .NET 8.0.32, FFmpeg 7.1.
- ElBruno.Whisper NuGet 0.9.0; its assembly reports 0.8.0.0.
- Models: `whisper-base` and `whisper-small`, multilingual ONNX models from
  `onnx-community`, using the package's default model files. No claim of immutable model
  revision pinning is made.
- Sample-local byte-level decoding adapter enabled, with Unicode regression coverage.
- Models already downloaded before the reported comparison run. Times include decoding,
  process/model initialization, inference and intermediate output writes; these are
  single observations, not controlled repeated latency measurements.

```powershell
.\src\samples\PodcastToSrt\Evaluate-Podcast.ps1 -FullEpisodes
.\src\samples\PodcastToSrt\Evaluate-Podcast.ps1 -FullEpisodes -ValidateOnly
```

New reports, raw subtitles and final subtitles are generated under ignored
`artifacts\multilingual`. A bounded [snapshot of these ten runs](baseline/README.md)
is committed for cross-machine comparison. Fixture hashes are recorded with the assets.

## Observed runs

| Run / report basename | Input | Source start (s) | Audio processed (s) | Model | Mode | Cues | Elapsed (s) |
|---|---|---:|---:|---|---|---:|---:|
| voice-base-es | Voice M4A | 60 | 50 | Base | Spanish, cleanup off | 3 | 15.27 |
| voice-small-es | Voice M4A | 60 | 50 | Small | Spanish, cleanup off | 3 | 14.34 |
| edited-base-es | Edited MP3 | 0 | 90 | Base | Spanish, cleanup off | 4 | 10.39 |
| edited-small-es | Edited MP3 | 0 | 90 | Small | Spanish, cleanup off | 4 | 57.15 |
| voice-base-auto | Voice M4A | 60 | 25 | Base | Auto, cleanup bypassed | 1 | 20.71 |
| voice-base-english | Voice M4A | 60 | 25 | Base | English translation plus cleanup | 1 | 13.22 |
| voice-base-experimental | Voice M4A | 60 | 25 | Base | Spanish, experimental cleanup | 1 | 14.40 |
| voice-base-late | Voice M4A | 1320 | 50 | Base | Spanish, cleanup off | 3 | 19.40 |
| voice-full-base-es | Voice M4A | 0 | 1408.917313 | Base | Spanish, cleanup off | 59 | 153.04 |
| edited-full-base-es | Edited MP3 | 0 | 1447.287000 | Base | Spanish, cleanup off, WebVTT | 61 | 139.23 |

All ten reports have `complete: true`. Validation checked contiguous window coverage,
positive bounded windows, chronological non-overlapping subtitle intervals, and subtitle
ends within the processed audio. Both full runs reached the decoded end of the recording.
Container and decoded PCM durations differ slightly, as can happen with codec
padding/rounding. Coverage checks use the decoded PCM length; they do not independently
verify every word or the alignment of the compressed recording.

**Every cue in these runs was a single full-window span, potentially the backend's
fallback.** The output does not demonstrate native speech-aligned segments. It is a
complete coarse-timing baseline, not ready-to-publish broadcast subtitle timing.

Base was used for the full-length baseline to keep the evaluation bounded. No accuracy
winner is declared. Small changed some terminology/casing but still made obvious-looking
name substitutions; model size alone did not establish correctness.

## What the text actually showed

These are **unverified model outputs**, not reference transcripts.

### Spanish with an explicit language

The Small model's edited opening began:

> Buenas, buenas, bienvenidos a otro episodio de No Tiene Nombre, feliz año nuevo,
> estamos en el 2026 y si pensabas que ibas a empezar el año de una forma tranquilita,
> repasando, festejando

Base and Small both produced readable Spanish after the byte-decoding fix. Technical
names still need review: outputs contained variants such as `DeepSick`, `DeepSync`,
`Deep Six`, `OpenEI` and `Cloud Sonet de Anthropi`. Compare these with the actual speech,
not just the episode title or another model's hypothesis.

The voice-only and edited timelines differ. Their first 60 seconds must not be treated
as aligned reference passages. The Base/Small comparisons above use identical excerpts
within each individual source file.

### Auto mode failed on the selected excerpt

With no explicit source language, the 25-second voice excerpt produced **446 repetitions
of the single word `Commission`**. The output passed file/coverage validation but failed
to produce a usable transcription. The backend did not provide confirmed language metadata.
For this package and these fixtures, use `--language es`.

### English translation and cleanup

The translated/cleaned excerpt began:

> With that GPT4 and the Anthropic Clouds. But the interesting thing is that with a
> cost fraction, according to the numbers that I shared, they spent between $5 to $6
> million to train the model...

This demonstrates the separate translation path, not verified translation quality.
Awkward phrasing and names remain; s1-mini is not a Spanish translator or an acoustic
error corrector.

### Experimental Spanish cleanup

The raw excerpt included `Según los números que yo os compartieron`; s1-mini changed
it to `Según los números que yo os compartido`. It also changed formatting such as
`entre 5 a 6 millones` to `entre $5 y $6 millones de dólares`.

This is evidence of rewriting, not evidence of supported or improved Spanish cleanup.
The default therefore remains bypassing s1-mini for Spanish. The raw sibling file
allows direct inspection of additions, deletions and number/name changes.

### Late coverage, music and remaining review

The voice-only final cue reached `00:23:28,917` and included the closing greeting.
The edited final cue reached `00:24:07.287`; it contained only `y`. This short tail is
suspicious and requires listening review before publication; it is not silently removed.
The preceding edited cue contained the additional platform/support outro, which is
absent from the voice-only closing output.

No claim is made that music-induced hallucinations have been eliminated, or that every
spoken word was recognized. A VAD or alignment follow-up should be based on listening-
verified failures and must preserve original offsets.

## Accuracy review still needed

No independently verified, human-transcribed Spanish reference was available in this
session, and the tools used here did not provide an audio-listening/transcription
capability to establish one. **No WER/CER scores or music-quality rankings are reported.**
Comparing one model's output against another as “ground truth” would be misleading.

For a scored follow-up, listen to short passages from the opening, technical discussion,
middle and closing of each recording. Match the same spoken content separately in each
edit, write and review verbatim references, then score raw Whisper text. Review cleanup
separately for semantic preservation; intentional filler removal is not ordinary ASR
error. Inspect coarse subtitle timing against the original audio before using it.
