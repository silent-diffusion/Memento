using System.Globalization;
using System.Text;
using System.Threading.Channels;
using Memento.Core.Workers;
using TesseractOCR;
using TesseractOCR.Enums;
using TesseractOCR.Layout;

namespace Memento.Worker;

/// <summary>
/// Tesseract text recognition for agenda photos and scans (<see cref="OcrJob"/>, ENGINE-NOTES.md §P): the language data
/// is loaded once (LSTM engine, <c>tessdata_fast</c>), then each <c>recognize</c> line, an 8-bit gray image the host
/// already decoded, evened out, scaled and straightened, is read with automatic page segmentation and answered with a
/// <c>recognized</c> line: every word with its box and confidence, and the text angle Tesseract measured. The worker
/// never decodes an image file: the host hands it raw pixels, wrapped here in an in-memory PGM for Leptonica.
/// </summary>
internal sealed class TesseractRecognizer(ProtocolWriter output)
{
    public async Task<WorkerReply> RunAsync(OcrJob job, ChannelReader<WorkerCommand> commands, CancellationToken cancellationToken)
    {
        if (job.Language.Length is 0 or > 16 || !job.Language.All(c => char.IsAsciiLetterLower(c) || c == '_'))
        {
            throw new WorkerFailure(WorkerErrorCodes.InvalidJob, $"'{job.Language}' is not a Tesseract language code");
        }

        var data = Path.Combine(job.LanguageDataDirectory, job.Language + ".traineddata");
        if (!File.Exists(data))
        {
            throw new WorkerFailure(WorkerErrorCodes.ModelLoad, $"the language data {Path.GetFileName(data)} is missing");
        }

        // Tesseract reads TESSDATA_PREFIX before the path it is given; only the model manager's folder may be used.
        Environment.SetEnvironmentVariable("TESSDATA_PREFIX", null);
        Engine engine;
        try
        {
            engine = new Engine(job.LanguageDataDirectory, job.Language, EngineMode.LstmOnly);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new WorkerFailure(WorkerErrorCodes.ModelLoad, $"Tesseract could not load {Path.GetFileName(data)} ({ex.Message.TrimEnd('.')})", ex);
        }

        using (engine)
        {
            output.Send(new WorkerReply { Type = WorkerMessageTypes.Device, Device = new WorkerDevice("cpu", "CPU", null, null, "Tesseract " + engine.Version) });
            await foreach (var command in commands.ReadAllAsync(cancellationToken))
            {
                if (command.Type == WorkerMessageTypes.End)
                {
                    break;
                }

                if (command.Type != WorkerMessageTypes.Recognize || command.Image is not { } image)
                {
                    continue;
                }

                byte[] pixels;
                try
                {
                    pixels = image.Decode();
                }
                catch (FormatException ex)
                {
                    throw new WorkerFailure(WorkerErrorCodes.InvalidJob, $"image {image.Id} is not readable: {ex.Message}");
                }

                output.Send(new WorkerReply { Type = WorkerMessageTypes.Recognized, Ocr = Recognize(engine, image, pixels, cancellationToken) });
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return new WorkerReply { Type = WorkerMessageTypes.Result };
    }

    internal static OcrRecognized Recognize(Engine engine, OcrImage image, byte[] pixels, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var pix = TesseractOCR.Pix.Image.LoadFromMemory(Pgm(image.Width, image.Height, pixels));
            var (words, angle) = Read(engine, pix, PageSegMode.Auto, cancellationToken);
            return new OcrRecognized(image.Id, words, angle);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not WorkerFailure)
        {
            throw new WorkerFailure(WorkerErrorCodes.Engine, string.Create(CultureInfo.InvariantCulture, $"Tesseract failed on image {image.Id} ({ex.Message.TrimEnd('.')})"), ex);
        }
    }

    private static (List<OcrWord> Words, double? Angle) Read(Engine engine, TesseractOCR.Pix.Image pix, PageSegMode mode, CancellationToken cancellationToken)
    {
        var words = new List<OcrWord>();
        double? angle = null;
        using var page = engine.Process(pix, mode);
        foreach (var block in page.Layout)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (angle is null && block.Properties is { } properties && float.IsFinite(properties.DeskewAngle))
            {
                // Tesseract gives the block's skew in radians, clockwise as Windows OCR measures it (3.5° came back as 0.0627).
                angle = Math.Round(properties.DeskewAngle * 180 / Math.PI, 2);
            }

            foreach (var paragraph in block.Paragraphs)
            {
                foreach (var line in paragraph.TextLines)
                {
                    foreach (var word in line.Words)
                    {
                        Add(words, word);
                    }
                }
            }
        }

        return (words, angle);
    }

    private static void Add(List<OcrWord> words, Word word)
    {
        var text = (word.Text ?? string.Empty).Trim();
        if (text.Length == 0 || word.BoundingBox is not { } box || box.Width <= 0 || box.Height <= 0)
        {
            return;
        }

        words.Add(new OcrWord(text, box.X1, box.Y1, box.Width, box.Height, Math.Round(Math.Clamp(word.Confidence / 100.0, 0, 1), 3)));
    }

    /// <summary>A binary PGM (P5) of 8-bit gray pixels: the simplest format Leptonica reads from memory.</summary>
    internal static byte[] Pgm(int width, int height, byte[] pixels)
    {
        var header = Encoding.ASCII.GetBytes(string.Create(CultureInfo.InvariantCulture, $"P5\n{width} {height}\n255\n"));
        var pgm = new byte[header.Length + pixels.Length];
        header.CopyTo(pgm, 0);
        pixels.CopyTo(pgm, header.Length);
        return pgm;
    }
}
