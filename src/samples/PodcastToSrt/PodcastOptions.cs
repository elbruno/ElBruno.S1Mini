using System.Globalization;
using ElBruno.Whisper;

namespace PodcastToSrt;

public enum CleanupMode { Auto, Off, Experimental }

public sealed record PodcastOptions(
    string Input,
    string Output,
    WhisperModelDefinition Model,
    string? Language,
    bool Translate,
    CleanupMode Cleanup,
    bool WebVtt,
    bool DropEmpty,
    double StartSeconds,
    double? DurationSeconds)
{
    public const string Usage =
        "PodcastToSrt <audio-file> [output-file] [--model whisper-base] " +
        "[--language es|en|auto] [--translate] [--cleanup auto|off|experimental] " +
        "[--vtt] [--drop-empty] [--start-seconds N] [--duration-seconds N]";

    public static PodcastOptions Parse(string[] args)
    {
        var paths = new List<string>();
        string? modelId = null;
        var language = "en";
        var translate = false;
        var cleanup = CleanupMode.Auto;
        var vtt = false;
        var dropEmpty = false;
        double start = 0;
        double? duration = null;

        for (var i = 0; i < args.Length; i++)
        {
            string Value()
            {
                if (++i >= args.Length || args[i].StartsWith("--", StringComparison.Ordinal))
                    throw new ArgumentException($"Missing value for {args[i - 1]}.");
                return args[i];
            }

            switch (args[i])
            {
                case "--model": modelId = Value(); break;
                case "--language": language = Value().ToLowerInvariant(); break;
                case "--translate": translate = true; break;
                case "--cleanup":
                    cleanup = Value() switch
                    {
                        "auto" => CleanupMode.Auto,
                        "off" => CleanupMode.Off,
                        "experimental" => CleanupMode.Experimental,
                        var value => throw new ArgumentException($"Unknown cleanup mode '{value}'."),
                    };
                    break;
                case "--vtt": vtt = true; break;
                case "--drop-empty": dropEmpty = true; break;
                case "--start-seconds": start = Number(Value()); break;
                case "--duration-seconds": duration = Number(Value()); break;
                default:
                    if (args[i].StartsWith("--", StringComparison.Ordinal))
                        throw new ArgumentException($"Unknown option '{args[i]}'.");
                    paths.Add(args[i]);
                    break;
            }
        }

        if (paths.Count is < 1 or > 2)
            throw new ArgumentException(Usage);
        if (language != "auto" && !CultureInfo.GetCultures(CultureTypes.NeutralCultures)
                .Any(c => c.TwoLetterISOLanguageName == language))
            throw new ArgumentException($"Invalid language code '{language}'. Use a Whisper language code or auto.");
        if (start < 0 || duration <= 0)
            throw new ArgumentException("Start must be nonnegative and duration must be positive.");

        var model = KnownWhisperModels.FindById(modelId ?? (language == "en" ? "whisper-tiny.en" : "whisper-base"))
            ?? throw new ArgumentException($"Unknown model '{modelId}'. Use a KnownWhisperModels model ID.");
        if (model.IsEnglishOnly && (language != "en" || translate))
            throw new ArgumentException("Use a multilingual model for non-English, auto-language or translation.");
        if (translate && language == "auto")
            throw new ArgumentException("Whisper 0.9.0 translation requires an explicit source language.");
        var output = paths.Count == 2 ? paths[1]
            : Path.Combine("artifacts", "podcast", Path.GetFileNameWithoutExtension(paths[0]) + (vtt ? ".vtt" : ".srt"));
        if (string.Equals(Path.GetFullPath(output), Path.GetFullPath(paths[0]), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Output must not overwrite the input audio.");

        return new(paths[0], output, model, language == "auto" ? null : language,
            translate, cleanup, vtt, dropEmpty, start, duration);
    }

    public bool ShouldClean => Cleanup == CleanupMode.Experimental ||
        (Cleanup == CleanupMode.Auto && (Translate || Language == "en"));

    private static double Number(string text)
    {
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value))
            throw new ArgumentException($"Invalid number '{text}'.");
        return value;
    }
}
