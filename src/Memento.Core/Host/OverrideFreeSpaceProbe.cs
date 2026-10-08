using System.Globalization;

namespace Memento.Core.Host;

/// <summary>
/// The test seam behind the hidden <c>--free-space-override</c> switch: every drive reports a fixed number of free
/// bytes, or the number written in a text file, read again on every call so a test can lower it while Memento runs
/// (the low-space banner, the transcription pause and the clean stop at the floor). When the file is missing or does
/// not hold a number, the real probe answers.
/// </summary>
public sealed class OverrideFreeSpaceProbe : IFreeSpaceProbe
{
    private readonly long? _fixedBytes;
    private readonly string? _file;
    private readonly IFreeSpaceProbe _fallback;

    /// <param name="spec">A whole number of bytes, or the path of a file that holds one.</param>
    /// <param name="fallback">Answers when the file gives no number.</param>
    public OverrideFreeSpaceProbe(string spec, IFreeSpaceProbe fallback)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(spec);
        ArgumentNullException.ThrowIfNull(fallback);
        _fallback = fallback;
        if (TryParse(spec, out var bytes))
        {
            _fixedBytes = bytes;
        }
        else
        {
            _file = Path.GetFullPath(spec);
        }
    }

    public long? GetFreeBytes(string path)
    {
        if (_fixedBytes is { } bytes)
        {
            return bytes;
        }

        try
        {
            if (TryParse(File.ReadAllText(_file!), out var fromFile))
            {
                return fromFile;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Missing or being rewritten: the real drive answers this time.
        }

        return _fallback.GetFreeBytes(path);
    }

    private static bool TryParse(string text, out long bytes) =>
        long.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out bytes);
}
