namespace Memento.Core.Bridge.Contracts;

/// <summary>One file an export would write, with its (estimated) size.</summary>
/// <param name="Component">The dialog row it belongs to: a key of <see cref="ExportSelection"/>.</param>
/// <param name="Name">The file name as it will be written (attachments under <c>Attachments/</c>).</param>
public sealed record ExportEstimateItem(string Component, string Name, long Bytes);
