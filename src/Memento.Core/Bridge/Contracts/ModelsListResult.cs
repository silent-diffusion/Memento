namespace Memento.Core.Bridge.Contracts;

/// <summary>Result of <c>models.list</c>.</summary>
public sealed record ModelsListResult(IReadOnlyList<ModelInfo> Models);
