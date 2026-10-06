namespace Memento.Audio.Sources;

/// <summary>
/// One recordable source, shaped like BRIDGE.md's <c>AudioSource</c> (<see cref="Id"/>, <see cref="Kind"/>,
/// <see cref="Name"/>, <see cref="Detail"/>, <see cref="IsDefault"/>, <see cref="ProcessId"/>) plus host-only extras.
/// </summary>
/// <param name="Id"><c>mic:&lt;endpointId&gt;</c>, <c>system:&lt;endpointId&gt;</c> or <c>app:&lt;pid&gt;</c>.</param>
/// <param name="Kind">Microphone, system (endpoint loopback) or application (process loopback).</param>
/// <param name="Name">"Microphone Array (Realtek(R) Audio)", "Everything this PC plays", "Zoom".</param>
/// <param name="Detail">"USB", "Built-in", "Default output, Speakers (…)", "Only this app".</param>
/// <param name="IsDefault">The Windows default device for its direction (microphones and outputs).</param>
/// <param name="ProcessId">Application sources only.</param>
public sealed record AudioSourceInfo(string Id, AudioSourceKind Kind, string Name, string Detail, bool IsDefault, int? ProcessId)
{
    /// <summary>MMDevice endpoint id for microphone and system sources.</summary>
    public string? EndpointId { get; init; }

    /// <summary>32×32 PNG of the application's icon, when requested and available. Never sent with a path.</summary>
    public byte[]? IconPng { get; init; }

    /// <summary>Executable name without extension (application sources), used to name the track file.</summary>
    public string? ProcessName { get; init; }

    public AudioSourceId SourceId => AudioSourceId.Parse(Id);
}
