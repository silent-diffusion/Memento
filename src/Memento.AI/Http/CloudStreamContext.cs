using System.Text;

namespace Memento.AI.Http;

/// <summary>Collects streamed text for one attempt and reports it as progress, with first-token timing.</summary>
internal sealed class CloudStreamContext(IProgress<AiProgress>? progress, int attempt, TimeProvider time, long attemptStarted, ITokenCounter counter)
{
    /// <summary>
    /// The most answer text Memento keeps (4 MiB of characters): far past any output token limit, so only a broken or
    /// hostile endpoint reaches it, and it cannot make Memento hold an endless answer in memory.
    /// </summary>
    public const int MaxAnswerChars = 4 * 1024 * 1024;

    private readonly StringBuilder _text = new();
    private long? _firstTokenAt;
    private int _estimatedTokens;

    public int Attempt => attempt;

    public bool ProducedOutput => _text.Length > 0;

    public string Text => _text.ToString();

    public TimeSpan? FirstToken => _firstTokenAt is { } at ? time.GetElapsedTime(attemptStarted, at) : null;

    public TimeSpan? Generation => _firstTokenAt is { } at ? time.GetElapsedTime(at) : null;

    public void Append(string? delta)
    {
        if (string.IsNullOrEmpty(delta))
        {
            return;
        }

        if (delta.Length > MaxAnswerChars - _text.Length)
        {
            throw new CloudAnswerTooLongException();
        }

        _firstTokenAt ??= time.GetTimestamp();
        _text.Append(delta);
        _estimatedTokens += Math.Max(1, counter.Count(delta));
        progress?.Report(new AiProgress(AiProgressStage.Generating, delta, _estimatedTokens, Attempt: attempt));
    }
}
