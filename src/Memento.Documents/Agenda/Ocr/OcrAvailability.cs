namespace Memento.Documents.Agenda.Ocr;

/// <summary>Whether an OCR engine can run now.</summary>
/// <param name="IsAvailable">The engine can recognize text in the requested language.</param>
/// <param name="Reason">When not available: a user-facing sentence naming what is missing and where to get it.</param>
/// <param name="Languages">The BCP-47 languages the engine can read now.</param>
public sealed record OcrAvailability(bool IsAvailable, string? Reason, IReadOnlyList<string> Languages)
{
    public static OcrAvailability Available(IReadOnlyList<string> languages) => new(true, null, languages);

    public static OcrAvailability NotInstalled(string reason) => new(false, reason, []);
}
