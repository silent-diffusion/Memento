namespace Memento.Documents.Agenda;

/// <summary>The limits every agenda import enforces.</summary>
public static class AgendaLimits
{
    /// <summary>25 MB: larger files are refused before parsing.</summary>
    public const long MaxFileBytes = 25L * 1024 * 1024;

    /// <summary>Images wider or taller than this are refused before decoding.</summary>
    public const int MaxImageSide = 10_000;

    /// <summary>The most items an agenda holds (matches the project store's list limit); the rest go into a warning.</summary>
    public const int MaxItems = 200;

    /// <summary>Items longer than this are marked uncertain (they may be several items run together).</summary>
    public const int LongItemLength = 200;

    /// <summary>Items shorter than this are marked uncertain (they may be stray marks).</summary>
    public const int ShortItemLength = 3;

    /// <summary>Text recognition upscales images until the median word is at least this tall.</summary>
    public const int MinOcrWordHeight = 24;
}
