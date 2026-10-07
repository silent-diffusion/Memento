namespace Memento.Core.Bridge.Contracts;

/// <summary>One audio row of the Export dialog (BRIDGE.md M3 <c>ExportSelection</c>).</summary>
public sealed record ExportAudioChoice
{
    public bool On { get; init; }

    /// <summary><c>flac</c>, <c>wav</c> or <c>mp3</c>.</summary>
    public string Format { get; init; } = "flac";

    /// <summary>MP3 only (96–320); <c>null</c> uses 192 kbps.</summary>
    public int? BitrateKbps { get; init; }
}
