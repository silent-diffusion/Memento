namespace Memento.Core.Bridge.Contracts;

/// <summary>Result of <c>documents.export</c>: where the file went, its size and SHA-256.</summary>
public sealed record DocumentFileResult(string Path, long Bytes, string Sha256);
