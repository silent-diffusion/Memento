using System.Text;

namespace Memento.AI.Http;

/// <summary>Collects streamed text for one attempt and reports it as progress, with first-token timing.</summary>
internal sealed class CloudStreamContext(IProgress<AiProgress>? progress, int attempt, TimeProvider time, long attemptStarted, ITokenCounter counter)
{
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

        _firstTokenAt ??= time.GetTimestamp();
        _text.Append(delta);
        _estimatedTokens += Math.Max(1, counter.Count(delta));
        progress?.Report(new AiProgress(AiProgressStage.Generating, delta, _estimatedTokens, Attempt: attempt));
    }
}
