namespace Memento.Core.Bridge.Contracts;

/// <summary>A download in progress.</summary>
public sealed record ModelInstalling(int Percent, long BytesDone);
