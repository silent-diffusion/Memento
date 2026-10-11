namespace Memento.Documents.Agenda.Ocr;

/// <summary>
/// Runs Tesseract out of process (<see cref="WorkerTesseractRunner"/>: Memento.Worker's <c>ocr</c> job), so a native
/// failure in Tesseract or Leptonica can never take the app, or a recording, down. Tests substitute their own.
/// </summary>
public interface ITesseractRunner
{
    /// <summary>Starts a recognizer with the language data loaded; it reads images until it is disposed.</summary>
    /// <param name="languageDataDirectory">The folder that holds <c>&lt;language&gt;.traineddata</c>.</param>
    /// <param name="language">Tesseract's language code (<c>eng</c>).</param>
    /// <exception cref="Core.Workers.WorkerUnavailableException">The worker could not be started.</exception>
    Task<ITesseractSession> OpenAsync(string languageDataDirectory, string language, CancellationToken cancellationToken);
}
