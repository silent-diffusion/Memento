namespace Memento.Documents.Agenda;

/// <summary>How to parse one agenda source.</summary>
public sealed record AgendaParseOptions
{
    public static AgendaParseOptions Default { get; } = new();

    /// <summary>The file name as the user sees it, used in messages and to choose between text formats.</summary>
    public string? FileName { get; init; }

    /// <summary>The MIME type reported by the drop or picker, if any. A hint only; the content decides.</summary>
    public string? ContentType { get; init; }

    /// <summary>
    /// The kind the content was sniffed as. <see cref="AgendaImporter"/> sets it; a parser called directly falls back to
    /// <see cref="FileName"/> and <see cref="ContentType"/>.
    /// </summary>
    public AgendaSourceKind? SourceKind { get; init; }

    /// <summary>The BCP-47 language for text recognition (e.g. <c>en-US</c>); <c>null</c> uses the Windows profile languages.</summary>
    public string? OcrLanguage { get; init; }

    /// <summary>The preferred text recognition engine id (<c>windows</c> or <c>tesseract</c>); the others are fallbacks.</summary>
    public string? OcrEngineId { get; init; }

    public long MaxFileBytes { get; init; } = AgendaLimits.MaxFileBytes;

    public int MaxImageSide { get; init; } = AgendaLimits.MaxImageSide;

    public int MaxItems { get; init; } = AgendaLimits.MaxItems;

    /// <summary>How long one parse may run before it is stopped with <c>agenda.unreadable</c>.</summary>
    public TimeSpan ParseTimeout { get; init; } = AgendaLimits.ParseTimeout;
}
