using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Memento.Audio;

/// <summary>
/// Identifies a source the way the bridge does: <c>mic:&lt;endpointId&gt;</c>, <c>system:&lt;endpointId&gt;</c>
/// or <c>app:&lt;pid&gt;</c>. Endpoint ids are the MMDevice ids (stable across sessions); pids are not.
/// </summary>
public readonly record struct AudioSourceId
{
    private const string MicPrefix = "mic:";
    private const string SystemPrefix = "system:";
    private const string AppPrefix = "app:";

    private AudioSourceId(AudioSourceKind kind, string? endpointId, int processId)
    {
        Kind = kind;
        EndpointId = endpointId;
        ProcessId = processId;
    }

    public AudioSourceKind Kind { get; }

    /// <summary>MMDevice endpoint id for microphone and system sources; null for applications.</summary>
    public string? EndpointId { get; }

    /// <summary>Target process id for application sources; 0 otherwise.</summary>
    public int ProcessId { get; }

    public static AudioSourceId Microphone(string endpointId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpointId);
        return new AudioSourceId(AudioSourceKind.Microphone, endpointId, 0);
    }

    public static AudioSourceId System(string endpointId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpointId);
        return new AudioSourceId(AudioSourceKind.System, endpointId, 0);
    }

    public static AudioSourceId Application(int processId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(processId);
        return new AudioSourceId(AudioSourceKind.Application, null, processId);
    }

    public static AudioSourceId Parse(string value) =>
        TryParse(value, out var id) ? id : throw new FormatException($"'{value}' is not an audio source id (expected mic:<endpoint>, system:<endpoint> or app:<pid>).");

    public static bool TryParse([NotNullWhen(true)] string? value, out AudioSourceId id)
    {
        id = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (value.StartsWith(MicPrefix, StringComparison.Ordinal) && value.Length > MicPrefix.Length)
        {
            id = Microphone(value[MicPrefix.Length..]);
            return true;
        }

        if (value.StartsWith(SystemPrefix, StringComparison.Ordinal) && value.Length > SystemPrefix.Length)
        {
            id = System(value[SystemPrefix.Length..]);
            return true;
        }

        if (value.StartsWith(AppPrefix, StringComparison.Ordinal)
            && int.TryParse(value.AsSpan(AppPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var pid)
            && pid > 0)
        {
            id = Application(pid);
            return true;
        }

        return false;
    }

    public override string ToString() => Kind switch
    {
        AudioSourceKind.Microphone => MicPrefix + EndpointId,
        AudioSourceKind.System => SystemPrefix + EndpointId,
        AudioSourceKind.Application => AppPrefix + ProcessId.ToString(CultureInfo.InvariantCulture),
        _ => "unknown",
    };
}
