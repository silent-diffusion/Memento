using System.Text.Json;
using Memento.Documents.Model;

namespace Memento.Documents.Templates;

/// <summary>The templates that ship with Memento, read from the JSON resources in <c>Templates/BuiltIn</c>.</summary>
public static class BuiltInTemplates
{
    public const string MeetingMinutesId = "meeting-minutes";
    public const string InterviewNotesId = "interview-notes";
    public const string LectureSummaryId = "lecture-summary";
    public const string DictationCleanupId = "dictation-cleanup";

    private static readonly Lazy<IReadOnlyList<DocumentTemplate>> Loaded = new(Load);

    public static IReadOnlyList<string> Ids { get; } = [MeetingMinutesId, InterviewNotesId, LectureSummaryId, DictationCleanupId];

    /// <summary>All built-in templates in their fixed order.</summary>
    public static IReadOnlyList<DocumentTemplate> All => Loaded.Value;

    public static DocumentTemplate MeetingMinutes => Get(MeetingMinutesId);

    public static DocumentTemplate InterviewNotes => Get(InterviewNotesId);

    public static DocumentTemplate LectureSummary => Get(LectureSummaryId);

    public static DocumentTemplate DictationCleanup => Get(DictationCleanupId);

    public static DocumentTemplate Get(string id) =>
        All.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.Ordinal))
        ?? throw new KeyNotFoundException($"\"{id}\" is not a built-in template.");

    /// <summary>The built-in templates offered for a recording type (<c>meeting</c>, <c>lecture</c>…).</summary>
    public static IReadOnlyList<DocumentTemplate> ForRecordingType(string recordingType) =>
        All.Where(t => t.RecordingTypes.Count == 0 || t.RecordingTypes.Contains(recordingType, StringComparer.OrdinalIgnoreCase)).ToList();

    private static List<DocumentTemplate> Load()
    {
        var assembly = typeof(BuiltInTemplates).Assembly;
        var result = new List<DocumentTemplate>(Ids.Count);
        foreach (var id in Ids)
        {
            var name = $"Memento.Documents.Templates.{id}.json";
            using var stream = assembly.GetManifestResourceStream(name)
                ?? throw new InvalidOperationException($"The built-in template resource {name} is missing from the build.");
            var template = JsonSerializer.Deserialize(stream, TemplateJsonContext.Default.DocumentTemplate)
                ?? throw new DocumentFormatException($"The built-in template resource {name} is empty.");
            result.Add(template with { BuiltIn = true });
        }

        return result;
    }
}
