namespace Memento.Core.Bridge.Contracts;

/// <summary><c>generation.confirm</c>: the answer to "ask before every send".</summary>
public sealed record GenerationConfirmParams(string JobId, bool Approved);
