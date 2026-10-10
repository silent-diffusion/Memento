namespace Memento.Core.Bridge.Contracts;

/// <summary>2.0: a Follow-up email module's settings (BRIDGE.md, Follow-up email).</summary>
/// <param name="Tone"><c>neutral</c>, <c>warm</c> or <c>brief</c>: the greeting, opening and closing sentences.</param>
/// <param name="SignOff">The name under the closing (one line, at most 80 characters), or <c>null</c>.</param>
public sealed record EmailModuleSettings(string Tone, string? SignOff);
