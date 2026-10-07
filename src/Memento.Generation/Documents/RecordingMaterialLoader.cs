using System.Text;
using Memento.AI.Payload;
using Memento.Core.Bridge;
using Memento.Core.Projects;
using Memento.Core.Transcripts;
using Memento.Documents.Export;
using Memento.Documents.Model;
using Microsoft.Extensions.Logging;

namespace Memento.Generation.Documents;

/// <summary>
/// Reads a recording's <see cref="RecordingMaterial"/>: manifest, transcript, annotations, the text of text attachments
/// (plain text, Markdown, CSV up to 1 MB; other files have no text and are listed as such in the preview), and the other
/// documents as Markdown. Reads only.
/// </summary>
public sealed partial class RecordingMaterialLoader(
    IProjectStore projects,
    TranscriptStore transcripts,
    ProjectDocumentStore documents,
    MarkdownExporter markdown,
    ILogger<RecordingMaterialLoader> logger)
{
    private const long MaxAttachmentTextBytes = 1024 * 1024;

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase) { ".txt", ".md", ".markdown", ".csv" };

    private readonly ILogger<RecordingMaterialLoader> _logger = logger;

    /// <exception cref="BridgeException"><c>project.notFound</c>.</exception>
    public async Task<RecordingMaterial> LoadAsync(string recordingId, string? excludeDocumentId, CancellationToken cancellationToken)
    {
        ProjectManifest manifest;
        try
        {
            manifest = await projects.LoadAsync(recordingId, cancellationToken);
        }
        catch (ProjectNotFoundException)
        {
            throw Bridge.M4Errors.ProjectNotFound(recordingId);
        }

        TranscriptDocument? transcript;
        try
        {
            transcript = await transcripts.LoadAsync(recordingId, cancellationToken);
        }
        catch (ProjectSchemaException ex)
        {
            LogTranscriptUnreadable(ex, recordingId);
            transcript = null;
        }

        var annotations = await projects.LoadAnnotationsAsync(recordingId, cancellationToken);
        var folder = projects.GetProjectFolder(recordingId);
        var attachments = new List<PayloadAttachment>();
        foreach (var attachment in manifest.Attachments)
        {
            attachments.Add(new PayloadAttachment(attachment.Name, attachment.ContentType, await ReadTextAsync(folder, attachment, cancellationToken)));
        }

        var others = new List<PayloadDocument>();
        foreach (var document in await documents.ListAsync(recordingId, cancellationToken))
        {
            if (!string.Equals(document.Id, excludeDocumentId, StringComparison.Ordinal))
            {
                others.Add(new PayloadDocument(document.Name ?? document.Title, markdown.Export(document)));
            }
        }

        return new RecordingMaterial(recordingId, manifest, transcript, annotations, attachments, others);
    }

    private async Task<string?> ReadTextAsync(string folder, AttachmentRecord attachment, CancellationToken cancellationToken)
    {
        var path = Path.GetFullPath(Path.Combine(folder, attachment.File.Replace('/', Path.DirectorySeparatorChar)));
        var isText = TextExtensions.Contains(Path.GetExtension(attachment.Name)) || (attachment.ContentType?.StartsWith("text/", StringComparison.OrdinalIgnoreCase) ?? false);
        if (!isText || !path.StartsWith(folder, StringComparison.OrdinalIgnoreCase) || !File.Exists(path) || new FileInfo(path).Length > MaxAttachmentTextBytes)
        {
            return null;
        }

        try
        {
            return await File.ReadAllTextAsync(path, Encoding.UTF8, cancellationToken);
        }
        catch (IOException ex)
        {
            LogTranscriptUnreadable(ex, attachment.Name);
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "A file of recording {RecordingId} could not be read for a document")]
    private partial void LogTranscriptUnreadable(Exception exception, string recordingId);
}
