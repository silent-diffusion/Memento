using System.Text.Json;
using System.Text.Json.Serialization;
using Memento.Core.Transcripts;

namespace Memento.Core.Voices;

/// <summary>
/// A voice Memento learned because the user named a speaker in Review with "Remember speakers by voice" on
/// (<c>voices/known.json</c>). Its signature is the running mean of its last <see cref="MaxSamples"/> confirmations, so
/// one odd recording cannot pull it far and an old one stops counting.
/// </summary>
public sealed record KnownVoice
{
    /// <summary>The signature is the mean of at most this many confirmations, the newest.</summary>
    public const int MaxSamples = 10;

    /// <summary>At most this many recordings are kept in <see cref="DeclinedIn"/> and <see cref="Recordings"/>.</summary>
    public const int MaxRecordings = 500;

    /// <summary><c>v</c> and ten hex digits.</summary>
    public required string Id { get; init; }

    public required string Name { get; init; }

    /// <summary>The catalog id of the voice model; voices of another model are never compared.</summary>
    public required string EmbeddingModelId { get; init; }

    /// <summary>Settings › Known voices › "Suggest this voice"; off keeps the voice but suggests it nowhere.</summary>
    public bool Suggest { get; init; } = true;

    /// <summary>The newest confirmations, oldest first, at most <see cref="MaxSamples"/>.</summary>
    public IReadOnlyList<KnownVoiceSample> Samples { get; init; } = [];

    /// <summary>Every recording the name was confirmed in ("Confirmed in n recordings").</summary>
    public IReadOnlyList<string> Recordings { get; init; } = [];

    /// <summary>Recordings where the user said "Not {name}": the voice is not suggested there again.</summary>
    public IReadOnlyList<string> DeclinedIn { get; init; } = [];

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset LastConfirmedAt { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    /// <summary>The signature: the mean direction of the samples, each counted once; <c>null</c> without a usable sample.</summary>
    public VoicePrint? Signature()
    {
        double[]? sum = null;
        var seconds = 0.0;
        foreach (var sample in Samples.TakeLast(MaxSamples))
        {
            if (VoicePrint.From(sample.Embedding, sample.Seconds) is not { } print)
            {
                continue;
            }

            if (sum is null)
            {
                sum = new double[print.Direction.Length];
            }
            else if (sum.Length != print.Direction.Length)
            {
                continue;
            }

            for (var i = 0; i < sum.Length; i++)
            {
                sum[i] += print.Direction[i];
            }

            seconds += print.Seconds;
        }

        if (sum is null)
        {
            return null;
        }

        var length = Math.Sqrt(sum.Sum(x => x * x));
        return length <= 0 ? null : new VoicePrint(sum.Select(x => x / length).ToArray(), seconds);
    }
}
