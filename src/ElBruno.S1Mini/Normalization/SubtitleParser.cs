using System.Text;

namespace ElBruno.S1Mini.Normalization;

/// <summary>Parses and writes SRT and WebVTT subtitle files.</summary>
public static class SubtitleParser
{
    /// <summary>Detects the format: content starting with <c>WEBVTT</c> is WebVTT, otherwise SRT.</summary>
    public static SubtitleFormat DetectFormat(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return content.TrimStart('\uFEFF', ' ', '\t', '\r', '\n').StartsWith("WEBVTT", StringComparison.Ordinal)
            ? SubtitleFormat.WebVtt
            : SubtitleFormat.Srt;
    }

    /// <summary>Parses subtitle content into cues. WebVTT NOTE/STYLE/REGION blocks are skipped.</summary>
    public static List<SubtitleCue> Parse(string content, SubtitleFormat format)
    {
        ArgumentNullException.ThrowIfNull(content);

        var lines = content.TrimStart('\uFEFF').Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var cues = new List<SubtitleCue>();
        var block = new List<string>();

        foreach (var line in lines)
        {
            if (line.Trim().Length == 0)
            {
                AddBlock(block, cues);
                block.Clear();
            }
            else
            {
                block.Add(line);
            }
        }

        AddBlock(block, cues);
        return cues;
    }

    /// <summary>Serializes cues. SRT cues are renumbered sequentially; WebVTT cue ids are preserved.</summary>
    public static string Write(IEnumerable<SubtitleCue> cues, SubtitleFormat format)
    {
        ArgumentNullException.ThrowIfNull(cues);

        var sb = new StringBuilder();
        if (format == SubtitleFormat.WebVtt)
        {
            sb.Append("WEBVTT\n\n");
        }

        var index = 1;
        foreach (var cue in cues)
        {
            if (format == SubtitleFormat.Srt)
            {
                sb.Append(index++).Append('\n');
            }
            else if (!string.IsNullOrEmpty(cue.Identifier))
            {
                sb.Append(cue.Identifier).Append('\n');
            }

            sb.Append(cue.Timing).Append('\n');
            if (cue.Text.Length > 0)
            {
                sb.Append(cue.Text).Append('\n');
            }

            sb.Append('\n');
        }

        return sb.ToString();
    }

    private static void AddBlock(List<string> block, List<SubtitleCue> cues)
    {
        if (block.Count == 0)
        {
            return;
        }

        var timingIndex = block.FindIndex(l => l.Contains("-->", StringComparison.Ordinal));
        if (timingIndex < 0 || timingIndex > 1)
        {
            return; // header, NOTE, STYLE, REGION or malformed block
        }

        var id = timingIndex == 1 ? block[0].Trim() : null;
        var text = string.Join("\n", block.Skip(timingIndex + 1));
        cues.Add(new SubtitleCue(id, block[timingIndex].Trim(), text));
    }
}
