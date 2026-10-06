namespace Memento.Core.Bridge.Contracts;

/// <summary>A chapter marker. <paramref name="Origin"/> is <c>user</c>, <c>local</c> or <c>ai</c>.</summary>
public sealed record Chapter(string Id, long AtMs, string Title, string Origin);
