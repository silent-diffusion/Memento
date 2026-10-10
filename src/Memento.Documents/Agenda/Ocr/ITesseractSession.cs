using Memento.Core.Workers;

namespace Memento.Documents.Agenda.Ocr;

/// <summary>A loaded Tesseract recognizer (<see cref="ITesseractRunner.OpenAsync"/>). Disposing it ends it.</summary>
public interface ITesseractSession : IAsyncDisposable
{
    /// <summary>Reads one prepared gray image.</summary>
    /// <exception cref="WorkerJobException">Tesseract reported a failure.</exception>
    /// <exception cref="WorkerCrashedException">The recognizer stopped without answering.</exception>
    Task<OcrRecognized> RecognizeAsync(OcrImage image, CancellationToken cancellationToken);
}
