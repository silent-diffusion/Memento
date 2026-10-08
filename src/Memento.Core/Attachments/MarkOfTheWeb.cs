namespace Memento.Core.Attachments;

/// <summary>
/// Windows' Mark of the Web: the <c>Zone.Identifier</c> stream that says a file came from the internet or email. Copying a
/// file's bytes drops it, so a macro-enabled agenda from an email would open in Office as a trusted local file, without
/// Protected View. Memento keeps it with every copy of a user's file it makes (attachments, the held agenda original).
/// </summary>
public static class MarkOfTheWeb
{
    private const string Stream = ":Zone.Identifier";

    /// <summary>The largest mark copied; real ones are a few hundred bytes.</summary>
    private const int MaxBytes = 64 * 1024;

    /// <summary>Copies the mark of <paramref name="sourcePath"/>, if it has one, to <paramref name="destinationPath"/>. Never throws for I/O.</summary>
    public static async Task CopyAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken)
    {
        try
        {
            var zone = new FileInfo(sourcePath + Stream);
            if (!zone.Exists || zone.Length > MaxBytes)
            {
                return;
            }

            var bytes = await File.ReadAllBytesAsync(zone.FullName, cancellationToken);
            await File.WriteAllBytesAsync(destinationPath + Stream, bytes, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            // No stream to copy (none on the source, or a file system without alternate data streams).
        }
    }
}
