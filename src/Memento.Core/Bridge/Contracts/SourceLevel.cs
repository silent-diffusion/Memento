namespace Memento.Core.Bridge.Contracts;

/// <summary>One meter reading: RMS and peak, both 0..1 of full scale.</summary>
public sealed record SourceLevel(string SourceId, double Rms, double Peak);
