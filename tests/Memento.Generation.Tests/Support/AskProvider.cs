using System.Globalization;
using System.Text.RegularExpressions;
using Memento.AI;

namespace Memento.Generation.Tests.Support;

/// <summary>
/// A scripted provider for Ask (2.9.0): answers by citing the first transcript line it was given and one line that was
/// not given (which must be dropped), streamed in pieces; it can hold after the first piece until released or cancelled,
/// or fail with a provider error. Keeps every request.
/// </summary>
internal sealed partial class AskProvider(AiProviderKind kind = AiProviderKind.Local, int context = 16384) : IAiProvider
{
    public List<AiRequest> Requests { get; } = [];

    /// <summary>Thrown instead of answering.</summary>
    public AiException? Failure { get; set; }

    /// <summary>When set, the answer waits after its first piece until this completes (or the request is cancelled).</summary>
    public TaskCompletionSource? Hold { get; set; }

    /// <summary>Completes once the first piece has been reported.</summary>
    public TaskCompletionSource FirstPiece { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public string Id => kind switch { AiProviderKind.Local => "local", AiProviderKind.Server => "server", _ => "anthropic" };

    public string DisplayName => kind switch { AiProviderKind.Local => "Local model", AiProviderKind.Server => "Your own server", _ => "Claude" };

    public AiProviderKind Kind => kind;

    public string Model => "scripted";

    public AiCapabilities Capabilities { get; } = new(context, context / 2, true, false, true, false);

    public int CountTokens(string text) => EstimatingTokenCounter.Generic.Count(text);

    public Task<AiReadiness> CheckAsync(CancellationToken cancellationToken) => Task.FromResult(AiReadiness.Ready());

    public async Task<AiResponse> GenerateAsync(AiRequest request, IProgress<AiProgress>? progress, CancellationToken cancellationToken)
    {
        lock (Requests)
        {
            Requests.Add(request);
        }

        if (Failure is { } failure)
        {
            throw failure;
        }

        var message = request.Messages[^1].Content;
        var first = FirstLine().Match(message) is { Success: true } m ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : 1;
        string[] pieces = ["The team ", $"agreed on it [{first}]", " and also [99999]."];
        progress?.Report(new AiProgress(AiProgressStage.Generating, pieces[0], 1));
        FirstPiece.TrySetResult();
        if (Hold is { } hold)
        {
            await hold.Task.WaitAsync(cancellationToken);
        }

        foreach (var piece in pieces.Skip(1))
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new AiProgress(AiProgressStage.Generating, piece, 2));
            await Task.Delay(100, cancellationToken);
        }

        var text = string.Concat(pieces);
        return new AiResponse(Id, Model, text, null, AiStopReason.Completed, "end_turn", new AiUsage(10, 5), new AiTimings(TimeSpan.Zero, null, TimeSpan.FromMilliseconds(5)), "hash");
    }

    [GeneratedRegex(@"^\[(\d+)\] ", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex FirstLine();
}
