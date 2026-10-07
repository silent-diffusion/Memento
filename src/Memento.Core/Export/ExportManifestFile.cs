namespace Memento.Core.Export;

/// <summary>One file in an export's <c>manifest.json</c>.</summary>
/// <param name="Name">Relative to the manifest, forward slashes.</param>
public sealed record ExportManifestFile(string Name, long Bytes, string Sha256);
