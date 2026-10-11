namespace Memento.Core.Bridge.Contracts;

/// <summary>Result of <c>video.thumbnail</c>: one still as a <c>data:image/jpeg;base64,…</c> URL, never written to disk.</summary>
public sealed record VideoThumbnailResult(string DataUrl, int Width, int Height);
