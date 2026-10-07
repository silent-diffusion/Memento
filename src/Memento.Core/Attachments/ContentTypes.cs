namespace Memento.Core.Attachments;

/// <summary>MIME types for common attachment and agenda files, from the extension.</summary>
public static class ContentTypes
{
    private static readonly Dictionary<string, string> ByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        [".doc"] = "application/msword",
        [".xls"] = "application/vnd.ms-excel",
        [".txt"] = "text/plain",
        [".md"] = "text/markdown",
        [".markdown"] = "text/markdown",
        [".csv"] = "text/csv",
        [".tsv"] = "text/tab-separated-values",
        [".json"] = "application/json",
        [".html"] = "text/html",
        [".htm"] = "text/html",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".bmp"] = "image/bmp",
        [".tif"] = "image/tiff",
        [".tiff"] = "image/tiff",
        [".heic"] = "image/heic",
        [".webp"] = "image/webp",
        [".svg"] = "image/svg+xml",
        [".zip"] = "application/zip",
        [".mp3"] = "audio/mpeg",
        [".wav"] = "audio/wav",
        [".flac"] = "audio/flac",
        [".m4a"] = "audio/mp4",
        [".mp4"] = "video/mp4",
    };

    /// <summary>The MIME type for <paramref name="fileName"/>'s extension, or <c>null</c> when unknown.</summary>
    public static string? For(string fileName) =>
        ByExtension.TryGetValue(Path.GetExtension(fileName ?? string.Empty), out var type) ? type : null;
}
