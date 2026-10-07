namespace Memento.Core.Bridge.Contracts;

/// <summary>Result of a method that starts a background job; progress arrives as events carrying this id.</summary>
public sealed record JobIdResult(string JobId);
