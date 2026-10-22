# Saved multilingual baseline

Snapshot of the ten evaluation scenarios described in
[`evaluation-results.md`](../evaluation-results.md), generated on 2026-10-22.

Each basename includes:

- `.json`: measured settings, decoded duration, processed coverage and window hypotheses.
- `.srt` or `.vtt`: final output.
- `.raw.srt` or `.raw.vtt`: output before optional s1-mini cleanup.

These are model hypotheses, **not reference transcripts or approved subtitles**.
The automatic-language output intentionally preserves its observed repetition failure.
The full-episode outputs use coarse window-level timing.

They are checked in as a bounded handoff snapshot so another machine can inspect the
same results without downloading models or regenerating inference. New runs still
write to ignored `artifacts\multilingual`; do not overwrite this baseline accidentally.
Reports retain the original environment metadata and are not results for the new machine.
