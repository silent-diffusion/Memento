namespace Memento.Core.Bridge.Contracts;

/// <summary>A capture source the user can toggle (DESIGN.md §8 sources card).</summary>
/// <param name="Id">Stable within a session: <c>mic:&lt;endpointId&gt;</c>, <c>system:&lt;endpointId&gt;</c>, <c>app:&lt;pid&gt;</c>.</param>
/// <param name="Kind"><c>microphone</c>, <c>system</c> or <c>application</c>.</param>
/// <param name="Name">"Shure MV7", "Everything this PC plays", "Zoom".</param>
/// <param name="Detail">"USB", "Default output", "Only this app".</param>
/// <param name="ProcessId">Application sources only.</param>
public sealed record AudioSource(string Id, string Kind, string Name, string Detail, bool IsDefault, int? ProcessId);
