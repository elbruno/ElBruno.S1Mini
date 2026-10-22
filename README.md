# ElBruno.S1Mini

[![NuGet](https://img.shields.io/nuget/v/ElBruno.S1Mini.svg?style=flat-square&logo=nuget)](https://www.nuget.org/packages/ElBruno.S1Mini)
[![NuGet Downloads](https://img.shields.io/nuget/dt/ElBruno.S1Mini.svg?style=flat-square&logo=nuget)](https://www.nuget.org/packages/ElBruno.S1Mini)
[![CI Build](https://github.com/elbruno/ElBruno.S1Mini/actions/workflows/build.yml/badge.svg)](https://github.com/elbruno/ElBruno.S1Mini/actions/workflows/build.yml)
[![Publish to NuGet](https://github.com/elbruno/ElBruno.S1Mini/actions/workflows/publish.yml/badge.svg)](https://github.com/elbruno/ElBruno.S1Mini/actions/workflows/publish.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg?style=flat-square)](LICENSE)
[![HuggingFace](https://img.shields.io/badge/🤗_HuggingFace-ONNX_Model-orange?style=flat-square)](https://huggingface.co/elbruno/s1-mini-onnx)
[![.NET](https://img.shields.io/badge/.NET-8.0%20|%2010.0-512BD4?style=flat-square&logo=dotnet)](https://dotnet.microsoft.com/)
[![GitHub stars](https://img.shields.io/github/stars/elbruno/ElBruno.S1Mini?style=social)](https://github.com/elbruno/ElBruno.S1Mini)

## Local ASR transcript normalizer for .NET 🧹

Clean raw speech-to-text transcripts into well-formed written text in .NET. Powered by [`superwhisper/s1-mini`](https://huggingface.co/superwhisper/s1-mini) (a 0.6B Qwen3 fine-tune) running locally via ONNX Runtime GenAI. Auto-downloads the INT4 ONNX model from HuggingFace.

> ⚠️ **s1-mini is not a chat model.** It performs exactly one task — normalizing an ASR transcript. Give it anything else and you get unpredictable output.

## Packages

| Package | NuGet | Downloads | Description |
|---------|-------|-----------|-------------|
| `ElBruno.S1Mini` | [![NuGet](https://img.shields.io/nuget/v/ElBruno.S1Mini.svg?style=flat-square&logo=nuget)](https://www.nuget.org/packages/ElBruno.S1Mini) | [![NuGet Downloads](https://img.shields.io/nuget/dt/ElBruno.S1Mini.svg?style=flat-square&logo=nuget)](https://www.nuget.org/packages/ElBruno.S1Mini) | Self-contained transcript normalizer with `S1MiniClient` (`IChatClient`) and `TranscriptNormalizer`. |

## Features

- 🧹 **Filler removal + punctuation + capitalization** in one call
- 🔁 **Self-correction resolution** — `"nine am no sorry ten thirty"` becomes `10:30`
- 🔢 **Written form for spoken numbers, dates, times, currency, emails**
- 🎚️ **Control-line settings** — `Styling` (formal / semi-formal / casual) and `Context` (general / email) with empirically verified behavior
- 📦 **Auto-download** — INT4 ONNX model fetched from HuggingFace on first use
- 🧵 **`IChatClient` compatible** — `S1MiniClient` plugs into `Microsoft.Extensions.AI`
- 💉 **DI-friendly** — `AddTranscriptNormalizer()` for ASP.NET Core
- 🪁 **Chunking helper** — `NormalizeChunkedAsync` for transcripts longer than the ~1,000-token recommended input
- 🛑 **Empty-in / empty-out** — pure filler returns `string.Empty`, as the model card documents
- 🦺 **Temperature-0 safe** — the ORT-GenAI native divide-by-zero trap is guarded at the runtime layer so callers can always use greedy decoding

## Installation

```bash
dotnet add package ElBruno.S1Mini
```

## Quick Start

```csharp
using ElBruno.S1Mini.Normalization;

// Downloads elbruno/s1-mini-onnx (int4) on first run.
using var normalizer = await TranscriptNormalizer.CreateAsync();

var cleaned = await normalizer.NormalizeAsync(
    "so um i need to like send the the report by uh friday no wait make that thursday");

Console.WriteLine(cleaned);
// So I need to send the report by Thursday.
```

## What it actually changes

Three short console samples. Each is a complete program — the only difference is the input.

**1 · Collapses stutters and repeated words**

```csharp
using ElBruno.S1Mini.Normalization;

using var normalizer = await TranscriptNormalizer.CreateAsync();

Console.WriteLine(await normalizer.NormalizeAsync(
    "you don't have any any any any change at all?"));
```

```text
You don't have any change at all.
```

**2 · Resolves self-corrections to what the speaker landed on**

```csharp
using ElBruno.S1Mini.Normalization;

using var normalizer = await TranscriptNormalizer.CreateAsync();

Console.WriteLine(await normalizer.NormalizeAsync(
    "i think we should uh go with with option b i mean option c"));
```

```text
I think we should go with option C.
```

Note it keeps `option C` — the choice the speaker corrected *to*, not the one they abandoned.

**3 · Writes spoken numbers, times, and dates in written form**

```csharp
using ElBruno.S1Mini.Normalization;

using var normalizer = await TranscriptNormalizer.CreateAsync();

Console.WriteLine(await normalizer.NormalizeAsync(
    "lets meet at uh three thirty on on tuesday the the tenth"));
```

```text
Let's meet at 3:30 on Tuesday the 10th.
```

> These outputs are measured against the real INT4 model with default options, and are
> pinned by regression tests in
> [`TranscriptNormalizerModelTests`](src/tests/ElBruno.S1Mini.Tests/Normalization/TranscriptNormalizerModelTests.cs)
> so they cannot silently drift.
>
> It normalizes rather than rewrites: casual phrasing survives. `the the total was like
> twenty five dollars and uh fifty cents` becomes `The total was like $25.50.` — the
> numerals are fixed, but the colloquial "like" stays.

## Control-line options

```csharp
using ElBruno.S1Mini.Normalization;

var cleaned = await normalizer.NormalizeAsync(
    transcript,
    new TranscriptNormalizerOptions
    {
        Styling = TranscriptStyling.Formal,
        Context = TranscriptContext.Email,
    });
```

Empirically verified against the real INT4 model:

- `Styling`: `SemiFormal` (default), `Formal`, `Casual` — all three produce distinct output.
- `Structure`: `Prose` (default), `Lists` — **caveat:** `Lists` did **not** reliably produce Markdown bullets in testing; output stayed prose. Kept as a model-card value.
- `Context`: `General` (default), `Email` — distinct. `Message` and `Notes` are accepted but empirically behave **identically to `General`**; kept for API completeness.

See [docs/transcript-normalization.md](docs/transcript-normalization.md) for the full behavior table and before/after examples.

## Dependency Injection

```csharp
using ElBruno.S1Mini;

builder.Services.AddTranscriptNormalizer(options =>
{
    options.CacheDirectory = @"C:\models";
});
```

`TranscriptNormalizer` is registered as its own service type — not as `IChatClient`. s1-mini is not a chat model, and exposing it as one would mislead consumers expecting chat semantics.

## Compose with any `IChatClient`

`TranscriptNormalizer` is a control-line + prompt-builder layer over any `IChatClient`. Supply your own client (as long as it is really pointed at s1-mini):

```csharp
using ElBruno.S1Mini;
using ElBruno.S1Mini.Normalization;

using var chatClient = await S1MiniClient.CreateAsync();
using var normalizer = new TranscriptNormalizer(chatClient);
```

`S1MiniClient` also implements `IChatClient` directly, so you can plug it into any `Microsoft.Extensions.AI` pipeline — with the caveat that it only handles the exact prompt shape s1-mini was fine-tuned on.

## Chunking long transcripts

```csharp
var cleaned = await normalizer.NormalizeChunkedAsync(longTranscript, maxCharsPerChunk: 3500);
```

Each chunk is normalized statelessly at sentence boundaries; for tighter control on transcripts with context spanning boundaries, chunk manually at a natural pause instead.

## SRT and WebVTT subtitles

The model only sees text. `NormalizeSubtitlesAsync` parses the cues, cleans each cue's text, and writes the file back with the **original timestamps untouched**.

```csharp
using var normalizer = await TranscriptNormalizer.CreateAsync();
var srt = await File.ReadAllTextAsync("episode.srt");
var cleaned = await normalizer.NormalizeSubtitlesAsync(srt); // format auto-detected
await File.WriteAllTextAsync("episode.clean.srt", cleaned);
```

Notes: each cue is normalized independently (no cross-cue context), markup tags are stripped, multi-line cues become one line, and cues that are pure filler are kept empty unless you pass `dropEmptyCues: true` (SRT is renumbered).

Starting from audio instead of an existing SRT? See the [`PodcastToSrt`](src/samples/PodcastToSrt/README.md) sample. It decodes MP3/M4A with FFmpeg, processes complete recordings in bounded Whisper windows, and preserves absolute offsets in SRT/WebVTT. The repository includes both the voice-only and music-edited NTN 459 recordings for opt-in evaluation.

**Spanish and multilingual audio:** select a multilingual model such as `whisper-base` or `whisper-small` and pass `--language es`. The old `whisper-tiny.en` default is English-only. **s1-mini v1 also supports English only**, so the sample skips cleanup for Spanish/unknown-language output by default. English translation plus cleanup and explicitly experimental Spanish cleanup are separate options. Numeric timestamp preservation does not establish acoustic alignment; reports flag potentially coarse full-window timing.

## FP16 is currently broken on CPU

`elbruno/s1-mini-onnx` also has an `fp16/` subfolder, but that variant fails at inference on CPU with `onnxruntime-genai` 0.15.1 (upstream ORT GQA `repeat_kv` Reshape shape-mismatch bug). **Use INT4 (the default).** This library will not switch to FP16 automatically.

## Model license

`superwhisper/s1-mini` is Apache-2.0 with a naming clause. The converted ONNX artifacts (`elbruno/s1-mini-onnx`) are an **explicitly unofficial, unaffiliated, non-endorsed derivative**. `ElBruno.S1Mini`'s C# code is MIT; the downloaded model weights remain under the upstream Apache-2.0 license. Vendor quality claim: **94.8% token accuracy on 7,519 held-out English cases** — Superwhisper's measurement on their official [GGUF Q4_K_M build](https://huggingface.co/superwhisper/s1-mini-GGUF), **not** on the INT4 ONNX weights this library downloads, whose accuracy has not been separately measured. **English only, v1.**

Superwhisper also publishes official [GGUF builds](https://huggingface.co/superwhisper/s1-mini-GGUF) for llama.cpp, Ollama, and LM Studio. This library targets ONNX Runtime GenAI, which is what .NET can consume directly.

## Building from Source

```bash
git clone https://github.com/elbruno/ElBruno.S1Mini
cd ElBruno.S1Mini
dotnet build ElBruno.S1Mini.slnx
dotnet test ElBruno.S1Mini.slnx --framework net8.0
```

## What's New

- 🎞️ **`v0.2.1` SRT and WebVTT support** — `NormalizeSubtitlesAsync` cleans every cue's text and keeps the original timestamps; `SubtitleCue.FromTimes` builds cues from Whisper segments. New `PodcastToSrt` sample: audio → Whisper → s1-mini → `.srt`.
- 📚 **`v0.1.2`** — documentation release: the package page now shows before/after examples, corrects the upstream accuracy attribution, and refreshes the author links. No library code changes.
- 🦺 **ORT-GenAI temperature-0 crash guard** — the native `temperature=0` divide-by-zero trap is guarded at the runtime layer; greedy decoding is safe for every call.
- 🎙️ **`LiveMicTranscription` sample** — microphone → Silero VAD → Whisper → s1-mini, fully on-device, with `--save-audio` / `--wav` replay for reproducible testing.
- 🧵 **`S1MiniClient`** — self-contained `IChatClient` implementation with automatic HuggingFace download of `elbruno/s1-mini-onnx` (int4).

## Documentation

- [Getting Started](docs/getting-started.md) — installation, first steps, configuration
- [Transcript Normalization](docs/transcript-normalization.md) — full API guide, control-line reference, empirical behavior table

## Samples

| Sample | Description |
|--------|-------------|
| [FoundryLocalSpeech](src/samples/FoundryLocalSpeech/README.md) | Windows-only, opt-in Foundry Local speech feasibility probe. Captures model/API evidence and raw responses; blocks subtitle output when acoustic timestamps are unavailable. |
| [HelloS1Mini](src/samples/HelloS1Mini) | Console sample covering default normalization, `Context.Email`, `Structure.Lists`, and pure-filler input. |
| [S1MiniWebSample](src/samples/S1MiniWebSample) | Blazor Server web UI: textarea → Normalize → cleaned output with styling/structure/context selectors. |
| [LiveMicTranscription](src/samples/LiveMicTranscription) | Windows-only console sample: default microphone → [Silero VAD](https://www.nuget.org/packages/ElBruno.Realtime.SileroVad) speech detection → [ElBruno.Whisper](https://www.nuget.org/packages/ElBruno.Whisper) speech-to-text → s1-mini cleanup, live and fully local. A [Spectre.Console](https://spectreconsole.net/) UI provides arrow-key model/style pickers, per-model download progress bars, a live input meter, and side-by-side raw vs. cleaned transcript panels, then offers to delete every downloaded model on exit. Supports `--save-audio` and `--wav <file\|folder>` for reproducible testing. |

### Reproducible testing with recordings

Live microphone testing is not repeatable — every attempt is a new performance. The sample
can therefore record what it captures and replay it later through the identical pipeline:

```bash
# Capture: writes each detected utterance to ./recordings/*.wav
dotnet run --project src/samples/LiveMicTranscription -- --save-audio

# Replay: same VAD, same models, no microphone needed
dotnet run --project src/samples/LiveMicTranscription -- --wav recordings
```

`--wav` accepts a single file or a folder, and resamples/downmixes anything to 16 kHz mono,
so a recording made with any tool works. This isolates model and setting changes from
variation in the speaking itself.

> **Note on Whisper + s1-mini:** Whisper does transcribe spoken fillers (`um`, `uh`) when
> they are actually captured, and s1-mini removes them. The hard part is *capturing* them:
> fillers are low-energy sounds that sit on the noise floor, so a simple energy-threshold
> gate discards exactly the words this library exists to clean up. The sample therefore
> uses **Silero VAD** (a neural speech detector) and cuts each utterance as one contiguous
> slice from the first to the last detected speech segment, plus padding — which preserves
> the quiet onsets. Measured on the same synthesized phrase:
>
> | Segmentation | Whisper output |
> |---|---|
> | Energy threshold | `So, um, hello.` (truncated at the first pause) |
> | Silero VAD + contiguous slice | `So, um, hello. I have a, uh, question here. And I want to, um, see what I am going to do here.` |
>
> s1-mini then returns `So, hello. I have a question here. And I want to see what I am going to do here.`
> The sample defaults to Whisper Tiny, which preserves fillers best; larger models tidy the
> transcript as they decode, which can make the cleanup step look like a no-op.
>
> **Utterance grouping matters too.** A hesitation ("I think… *ummm*… we should") contains a
> pause often longer than a second. Ending the phrase there splits one sentence into
> fragments and strands the filler at a boundary, so the sample waits 1.5 s of silence
> before closing an utterance.
>
> **Known model limitation:** s1-mini recognizes every common filler spelling
> (`um`, `umm`, `ummm`, `uh`, `em`, `emm`, `eh`, `ehh`, `erm`, `hmm`, `er`, `ah`) in ordinary
> sentences, but it passes greeting phrases containing a personal name through verbatim —
> `"Hello, um, hi Kara."` keeps the `um`, while `"Hello, um, hi."` and
> `"Hello, um, this is a test."` are both cleaned. Using `Context.Email` strips the filler in
> that case.

## Testing

```bash
dotnet test ElBruno.S1Mini.slnx --framework net8.0
```

Tests use a fake `IChatClient` and a recording `IGenerationSearchOptions` seam — no model downloads, no network, no GPU required.

## 📄 License

MIT — see [LICENSE](LICENSE). The downloaded s1-mini model weights are Apache-2.0 (upstream), not MIT.

## 🙏 Acknowledgments

- [`superwhisper/s1-mini`](https://huggingface.co/superwhisper/s1-mini) — the fine-tuned ASR normalizer this library wraps. All model design and training credit belongs to the Superwhisper team.
- [`Qwen/Qwen3-0.6B`](https://huggingface.co/Qwen/Qwen3-0.6B) — the base model s1-mini is fine-tuned from.
- [ONNX Runtime GenAI](https://github.com/microsoft/onnxruntime-genai) — inference engine.
- [Hugging Face](https://huggingface.co/) — model hosting.
- [Microsoft.Extensions.AI](https://learn.microsoft.com/dotnet/ai/microsoft-extensions-ai) — the `IChatClient` contract this library implements.

## Related Projects

- [ElBruno.LocalLLMs](https://github.com/elbruno/ElBruno.LocalLLMs) — Run local LLMs in .NET
- [ElBruno.Whisper](https://github.com/elbruno/ElBruno.Whisper) — Local Whisper speech-to-text in .NET
- [ElBruno.HuggingFace](https://github.com/elbruno/ElBruno.HuggingFace) — HuggingFace model utilities for .NET

## 👋 About the Author

Hi! I'm **ElBruno** 🧡, a passionate developer and content creator exploring AI, .NET, and modern development practices.

**Made with ❤️ by [ElBruno](https://github.com/elbruno)**

If you like this project, consider following my work across platforms:

- 📻 **Podcast**: [No Tiene Nombre](https://www.notienenombre.com) — Spanish-language episodes on AI, development, and tech culture
- 💻 **Blog**: [ElBruno.com](https://elbruno.com) — Deep dives on embeddings, RAG, .NET, and local AI
- 📺 **YouTube**: [youtube.com/elbruno](https://www.youtube.com/elbruno) — Demos, tutorials, and live coding
- 🔗 **LinkedIn**: [@elbruno](https://www.linkedin.com/in/elbruno/) — Professional updates and insights
- 𝕏 **Twitter**: [@elbruno](https://www.x.com/elbruno/) — Quick tips, releases, and tech news
