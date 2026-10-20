using Microsoft.Extensions.AI;

namespace ElBruno.S1Mini.Normalization;

/// <summary>
/// Cleans raw speech-to-text transcripts into well-formed written text using
/// <c>superwhisper/s1-mini</c>, a single-task ASR transcript normalizer —
/// <b>not a general-purpose chat model</b>.
/// <para>
/// The model removes filler words, resolves self-corrections to what the speaker
/// ultimately landed on, applies punctuation/capitalization, and renders spoken
/// numbers/dates/times/currency/emails in written form. It does not answer
/// questions or hold a conversation.
/// </para>
/// <para>
/// Input is recommended to stay under ~1,000 tokens; longer transcripts should be
/// split into chunks (e.g. by sentence or utterance boundary) and normalized
/// individually — see <see cref="NormalizeChunkedAsync"/>.
/// </para>
/// </summary>
public sealed class TranscriptNormalizer : IDisposable
{
    /// <summary>
    /// The verbatim system prompt required by the s1-mini model card.
    /// </summary>
    public const string DefaultSystemPrompt =
        "You are a text normalizer for speech-to-text transcripts. The input begins with a control line " +
        "specifying the styling, structure, and context settings; clean the transcript to match those " +
        "settings and output only the cleaned text.";

    private readonly IChatClient _chatClient;
    private readonly bool _ownsChatClient;
    private bool _disposed;

    /// <summary>
    /// Wraps an existing <see cref="IChatClient"/> (e.g. a <see cref="S1MiniClient"/>
    /// or any other provider). The caller retains ownership of
    /// <paramref name="chatClient"/> and is responsible for disposing it; disposing
    /// this <see cref="TranscriptNormalizer"/> will not dispose it. This constructor
    /// is the primary seam for unit testing with a fake/mock client.
    /// </summary>
    public TranscriptNormalizer(IChatClient chatClient)
        : this(chatClient, ownsChatClient: false)
    {
    }

    internal TranscriptNormalizer(IChatClient chatClient, bool ownsChatClient)
    {
        _chatClient = chatClient ?? throw new ArgumentNullException(nameof(chatClient));
        _ownsChatClient = ownsChatClient;
    }

    /// <summary>
    /// Creates a <see cref="TranscriptNormalizer"/> backed by a freshly created
    /// <see cref="S1MiniClient"/>. The model is downloaded automatically on first
    /// use (unless <see cref="S1MiniOptions.EnsureModelDownloaded"/> is disabled or
    /// <see cref="S1MiniOptions.ModelPath"/> is set). The returned instance owns
    /// the underlying chat client and disposes it when disposed.
    /// </summary>
    public static async Task<TranscriptNormalizer> CreateAsync(
        S1MiniOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var client = await S1MiniClient.CreateAsync(options, cancellationToken).ConfigureAwait(false);
        return new TranscriptNormalizer(client, ownsChatClient: true);
    }

    /// <summary>
    /// Cleans a raw speech-to-text transcript into well-formed written text.
    /// </summary>
    /// <param name="transcript">
    /// The raw transcript to clean. Recommended to stay under ~1,000 tokens; for
    /// longer input, chunk the transcript yourself (or use
    /// <see cref="NormalizeChunkedAsync"/>) and normalize each piece separately.
    /// </param>
    /// <param name="options">
    /// Styling/structure/context and generation settings. Defaults to
    /// semi-formal/prose/general with greedy decoding, matching the model card.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The cleaned transcript text, trimmed. If <paramref name="transcript"/> is
    /// empty/whitespace, or the input is pure filler/noise, an empty string is
    /// returned (matching the model card's documented behavior) without an
    /// unnecessary model call for empty/whitespace input.
    /// </returns>
    public async Task<string> NormalizeAsync(
        string transcript,
        TranscriptNormalizerOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (string.IsNullOrWhiteSpace(transcript))
        {
            return string.Empty;
        }

        options ??= new TranscriptNormalizerOptions();

        var systemPrompt = options.SystemPrompt ?? DefaultSystemPrompt;
        var userPrompt = BuildPrompt(transcript, options);

        var chatOptions = new ChatOptions
        {
            // Greedy decoding per the model card (do_sample=False). Temperature=0
            // is the library-wide "greedy" sentinel — safe to send because the
            // ORT-GenAI runtime layer omits the native "temperature" search option
            // whenever Temperature <= 0 (see OnnxGenAIRuntime.ApplyParameters).
            Temperature = 0f,
            MaxOutputTokens = options.MaxTokens,
        };

        // Empty generations are correct here: for pure-filler input, s1-mini
        // emits EOS immediately, and the runtime returns string.Empty without
        // touching any zero-length decode path.
        var response = await _chatClient.GetResponseAsync(
            [
                new ChatMessage(ChatRole.System, systemPrompt),
                new ChatMessage(ChatRole.User, userPrompt),
            ],
            chatOptions,
            cancellationToken).ConfigureAwait(false);

        return (response.Text ?? string.Empty).Trim();
    }

    /// <summary>
    /// Convenience overload for transcripts longer than the model's recommended
    /// ~1,000-token input. Splits the transcript into sentence-boundary chunks,
    /// normalizes each chunk independently (each call is stateless and does not
    /// see prior chunks' cleaned output), and joins the results with a single
    /// space. This is a best-effort convenience: for transcripts with context that
    /// spans sentence boundaries (e.g. a self-correction split across chunks),
    /// prefer chunking manually at a natural pause in the audio instead.
    /// </summary>
    public async Task<string> NormalizeChunkedAsync(
        string transcript,
        TranscriptNormalizerOptions? options = null,
        int maxCharsPerChunk = 3500,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (string.IsNullOrWhiteSpace(transcript))
        {
            return string.Empty;
        }

        var chunks = SplitIntoChunks(transcript, maxCharsPerChunk);
        var cleaned = new List<string>(chunks.Count);

        foreach (var chunk in chunks)
        {
            var result = await NormalizeAsync(chunk, options, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(result))
            {
                cleaned.Add(result);
            }
        }

        return string.Join(" ", cleaned);
    }

    /// <summary>
    /// Normalizes the text of every cue in an SRT or WebVTT file while leaving all
    /// timestamps untouched. Each cue is normalized independently (the model is
    /// stateless), markup tags are stripped, and multi-line cues are joined into one line.
    /// </summary>
    /// <param name="content">The subtitle file content.</param>
    /// <param name="format">The format; auto-detected from the content when <see langword="null"/>.</param>
    /// <param name="options">Normalization options.</param>
    /// <param name="dropEmptyCues">Remove cues whose text normalizes to empty (pure filler). Defaults to keeping them with empty text.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The normalized subtitle file content in the same format.</returns>
    public async Task<string> NormalizeSubtitlesAsync(
        string content,
        SubtitleFormat? format = null,
        TranscriptNormalizerOptions? options = null,
        bool dropEmptyCues = false,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(content);

        var fmt = format ?? SubtitleParser.DetectFormat(content);
        var cues = SubtitleParser.Parse(content, fmt);
        var result = new List<SubtitleCue>(cues.Count);

        foreach (var cue in cues)
        {
            var plain = System.Text.RegularExpressions.Regex.Replace(cue.Text, "<[^>]+>", string.Empty);
            plain = System.Text.RegularExpressions.Regex.Replace(plain, @"\s+", " ").Trim();

            var cleaned = await NormalizeAsync(plain, options, cancellationToken).ConfigureAwait(false);
            cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\s+", " ").Trim();

            if (cleaned.Length == 0 && dropEmptyCues)
            {
                continue;
            }

            result.Add(cue with { Text = cleaned });
        }

        return SubtitleParser.Write(result, fmt);
    }

    /// <summary>
    /// Splits a long transcript into chunks at sentence boundaries, keeping each
    /// chunk at or under <paramref name="maxCharsPerChunk"/> characters when possible.
    /// Internal so it can be unit-tested without a model.
    /// </summary>
    internal static List<string> SplitIntoChunks(string transcript, int maxCharsPerChunk)
    {
        var sentences = transcript.Split(['.', '!', '?'], StringSplitOptions.RemoveEmptyEntries);
        var chunks = new List<string>();
        var current = new System.Text.StringBuilder();

        foreach (var rawSentence in sentences)
        {
            var sentence = rawSentence.Trim();
            if (sentence.Length == 0)
            {
                continue;
            }

            if (current.Length > 0 && current.Length + sentence.Length + 1 > maxCharsPerChunk)
            {
                chunks.Add(current.ToString().Trim());
                current.Clear();
            }

            current.Append(sentence).Append(". ");
        }

        if (current.Length > 0)
        {
            chunks.Add(current.ToString().Trim());
        }

        return chunks.Count > 0 ? chunks : [transcript];
    }

    /// <summary>
    /// Builds the s1-mini user-message prompt: the <c>[Styling: ...] [Structure: ...] [Context: ...]</c>
    /// control line followed by the raw transcript. Internal so it can be unit-tested without a model.
    /// </summary>
    internal static string BuildPrompt(string transcript, TranscriptNormalizerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var controlLine =
            $"[Styling: {options.Styling.ToWireValue()}] " +
            $"[Structure: {options.Structure.ToWireValue()}] " +
            $"[Context: {options.Context.ToWireValue()}]";

        return $"{controlLine}\n{transcript}";
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_ownsChatClient)
        {
            _chatClient.Dispose();
        }
    }
}
