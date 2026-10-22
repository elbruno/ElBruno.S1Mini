using ElBruno.S1Mini.Normalization;

namespace PodcastToSrt;

public static class AudioWindows
{
    public const int SampleRate = 16000;
    public const int MaxSamples = 25 * SampleRate;

    // Non-overlapping windows retain every sample and every original silence.
    public static int FindBoundary(float[] samples, int count, long remainingSamples)
    {
        if (remainingSamples <= count) return count;
        // Do not leave a subsecond tail to be padded with 30 seconds of silence by Whisper.
        var tail = remainingSamples - count;
        if (tail < 5 * SampleRate)
            count -= 5 * SampleRate - (int)tail;
        const int frame = SampleRate / 50;
        var best = count;
        var lowest = double.MaxValue;
        for (var offset = count - 2 * SampleRate; offset + frame <= count; offset += frame)
        {
            double energy = 0;
            for (var j = offset; j < offset + frame; j++)
                energy += samples[j] * samples[j];
            if (energy < lowest)
            {
                lowest = energy;
                best = offset + frame;
            }
        }
        return best;
    }

    public static SubtitleCue Cue(TimeSpan start, TimeSpan end, string text,
        TimeSpan offset, TimeSpan windowDuration, SubtitleFormat format)
    {
        if (start < TimeSpan.Zero || end <= start || end > windowDuration + TimeSpan.FromMilliseconds(40))
            throw new InvalidDataException($"Invalid Whisper segment bounds {start} --> {end} for window {windowDuration}.");
        end = end > windowDuration ? windowDuration : end;
        return SubtitleCue.FromTimes(offset + start, offset + end, text, format);
    }
}
