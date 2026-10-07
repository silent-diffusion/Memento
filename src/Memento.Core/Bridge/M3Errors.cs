namespace Memento.Core.Bridge;

/// <summary>Shared wording for M3 method errors (DESIGN.md §17: name the thing, say what is safe, offer the fix).</summary>
internal static class M3Errors
{
    public static BridgeException Invalid(string message, string? detail = null) => new(BridgeErrorCodes.InvalidParams, message, detail);

    /// <summary>A path the UI sent must be a full path to an existing file.</summary>
    public static string RequireFile(string path, string what)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
        {
            throw Invalid($"The {what} needs a full file path such as C:\\Folder\\file.docx. Nothing was changed.");
        }

        if (!File.Exists(path))
        {
            throw Invalid($"\"{Path.GetFileName(path)}\" could not be found; it may have been moved or deleted. Nothing was changed. Choose the file again.", Path.GetFileName(path));
        }

        return path;
    }

    /// <summary>Plain words for an I/O failure: a full drive, a refusal, or the system's own message.</summary>
    public static string Reason(Exception exception) => exception switch
    {
        _ when Audio.DiskErrors.IsDiskFull(exception) => "the drive is full",
        UnauthorizedAccessException => "Windows denied access",
        IOException io when io.HResult is unchecked((int)0x80070020) or unchecked((int)0x80070021) => "another program is using the file",
        _ => exception.Message.TrimEnd('.'),
    };
}
