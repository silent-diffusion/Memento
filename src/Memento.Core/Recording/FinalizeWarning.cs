namespace Memento.Core.Recording;

/// <summary>
/// Something finalize could not do as planned but recovered from, e.g. a track kept as WAV because FLAC encoding
/// failed. Each becomes its own History line (<c>stored</c> · <c>info</c>).
/// </summary>
/// <param name="Summary">Short, e.g. "Kept mic as WAV".</param>
/// <param name="Detail">Specific: what failed, what is safe, what to do (DESIGN.md §17).</param>
public sealed record FinalizeWarning(string Summary, string Detail);
