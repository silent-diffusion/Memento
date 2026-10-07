using System.Text.Json;
using Memento.Core.Projects;

namespace Memento.Core.Attachments;

/// <summary>
/// The <c>attachments</c> array of <c>project.json</c>. It travels in <see cref="ProjectManifest.ExtensionData"/>, which
/// every manifest write keeps, so builds before M3 leave it untouched.
/// </summary>
public static class ManifestAttachments
{
    public const string Field = "attachments";

    public static IReadOnlyList<AttachmentRecord> Read(ProjectManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (manifest.ExtensionData is null || !manifest.ExtensionData.TryGetValue(Field, out var element) || element.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        try
        {
            return element.Deserialize(AttachmentsJsonContext.Default.ListAttachmentRecord) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>A copy of <paramref name="manifest"/> with <paramref name="attachments"/> as its index (the original is not changed).</summary>
    public static ProjectManifest With(ProjectManifest manifest, IEnumerable<AttachmentRecord> attachments)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var data = manifest.ExtensionData is null
            ? new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            : new Dictionary<string, JsonElement>(manifest.ExtensionData, StringComparer.Ordinal);
        data[Field] = JsonSerializer.SerializeToElement(attachments.ToList(), AttachmentsJsonContext.Default.ListAttachmentRecord);
        return manifest with { ExtensionData = data };
    }
}
