namespace Memento.Core.Workers;

/// <summary>
/// Tesseract text recognition for agenda photos and scans (<see cref="WorkerJobKinds.Ocr"/>): the worker loads the
/// language data once and stays loaded, answering each <c>recognize</c> line (<see cref="OcrImage"/>) with a
/// <c>recognized</c> line (<see cref="OcrRecognized"/>), until <c>end</c> or <c>cancel</c>. It runs on the processor only.
/// </summary>
/// <param name="LanguageDataDirectory">The folder that holds <c>&lt;language&gt;.traineddata</c> (the model manager's <c>models\tesseract</c>).</param>
/// <param name="Language">Tesseract's language code, such as <c>eng</c>.</param>
public sealed record OcrJob(string LanguageDataDirectory, string Language);
