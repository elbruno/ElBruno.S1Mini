# Image prompts for `26-08-20-intro.md`

## How these were generated

All three images were generated with the [`t2i`](https://github.com/elbruno) CLI on 2026-08-20 using the prompts below verbatim.

```bash
t2i "<prompt>" --provider foundry-gpt-image-2
```

Notes worth knowing before you re-run these:

- **Provider matters.** `foundry-gpt-image-2` (model `gpt-image-2`) is what produced these. The other providers failed on this machine: `foundry-flux2` returned `DeploymentNotFound: flux.2-flex does not exist`, and `foundry-mai2` rejected the request with `Model not supported with Responses API`.
- **`--out` is ignored** by this provider. It auto-names files from a prompt slug plus a timestamp, e.g. `create-a-modern-dark-friendly-technical-hero-illustration-fo-20260820-143643.png`. Rename after generation.
- **`--width` / `--height` are ignored.** Output is always **1024×1024**, so the "16:9 aspect ratio" instruction in each prompt is a stylistic hint at best — the provider does not honor it.
- **Post-processing:** only `pipeline.png` was cropped, to 1024×576, taking the region `y = 214..790` to remove empty background above and below the icon row. `hero.png` and `before-after.png` are unmodified 1024×1024 originals; both compose edge-to-edge and a 16:9 crop would clip content.

Consistent visual direction for all three images: modern technical illustration for a .NET developer audience, dark-friendly palette, clean geometric shapes, subtle cyan/purple highlights, no real brand logos, no copyrighted characters, no long rendered text. If text must appear, keep it to short generic labels only.

## `26-10-21-srt-support.md` images

Generated 2026-10-20 with `t2i "<prompt>" --provider foundry-gpt-image-2`, then renamed. Both are unmodified 1024x1024 originals. Shared style: modern dark-friendly technical illustration, dark navy background, subtle grid, cyan and violet accents, no real logos.

- `26-10-21-srt-hero.png`: a subtitle file with timeline timestamp blocks, messy text bubbles flowing through a compact local AI module and coming out as clean text, with timestamps aligned and untouched.
- `26-10-21-srt-flow.png`: four icons in a row (subtitle file, parser into cue cards, AI chip cleaning each card's text, writer reassembling the file), with clock icons passing straight through unchanged below.

## `hero.png`

**Purpose:** Introduce the idea of local transcript cleanup: rough speech-to-text enters a local .NET/ONNX processing box and comes out as clean written text.

**Placement in post:** Immediately after YAML front matter, before the H1.

**Alt text:** `Hero illustration for local transcript cleanup in .NET`

**Generation prompt:**

> Create a modern dark-friendly technical hero illustration for a developer blog post about local transcript cleanup in .NET. Show an abstract laptop or workstation with a microphone waveform flowing into a compact local AI processing module, then transforming into clean document lines. Use a dark navy background, subtle grid texture, cyan and violet accent lines, and minimal geometric shapes. Include visual hints of privacy/local execution such as a small shield or closed loop around the computer, but do not use real logos. Avoid long readable text; use only tiny abstract code/document marks. Clean, professional, high contrast, 16:9 aspect ratio.

## `pipeline.png`

**Purpose:** Explain the live microphone pipeline: microphone -> neural VAD -> Whisper speech-to-text -> s1-mini cleanup.

**Placement in post:** In the “The live microphone sample” section, after the command-line examples and screenshot placeholder.

**Alt text:** `Pipeline diagram for microphone to Silero VAD to Whisper to s1-mini`

**Generation prompt:**

> Create a clean dark-mode pipeline diagram illustration for a technical .NET AI blog. Show four connected stages from left to right: microphone capture, neural speech detection, speech-to-text transcription, transcript cleanup. Use simple icon-like abstract shapes: microphone, small neural network node cluster, document with waveform, polished document with checkmark. Connect stages with glowing cyan arrows. Use short generic labels only if needed, no real brand logos, no long strings of text. Style should match the hero image: dark navy background, subtle grid, cyan and purple highlights, modern developer documentation aesthetic, 16:9 aspect ratio.

## `before-after.png`

**Purpose:** Visualize the main before/after behavior without depending on text-to-image rendering long transcript strings.

**Placement in post:** In the “What the model actually cleaned today” section, after the verified output table.

**Alt text:** `Before and after transcript cleanup examples`

**Generation prompt:**

> Create a dark-friendly before-and-after technical illustration for transcript normalization. Show two side-by-side panels: the left panel is messy raw transcript represented by uneven grey lines, repeated marks, filler bubbles, and jagged punctuation; the right panel is clean written text represented by aligned bright lines, tidy punctuation symbols, and a subtle checkmark. Use abstract marks instead of long readable text because generated text may be inaccurate. Keep the style modern and minimal for a .NET developer audience, with a navy background, cyan and violet accents, subtle grid, and crisp vector-like shapes. No logos, no copyrighted characters, 16:9 aspect ratio.
