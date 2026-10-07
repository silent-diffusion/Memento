using System.Globalization;
using System.IO.Compression;
using DocumentFormat.OpenXml.Packaging;

namespace Memento.Documents.Agenda.OpenXml;

/// <summary>
/// Checks a Word or Excel package before the Open XML SDK loads it: a small file can unpack to gigabytes (a ZIP
/// bomb), so the number of parts, their unpacked size and their compression ratio are limited, and every part is
/// inflated through a <see cref="BoundedReadStream"/> that stops at the size its header declares.
/// </summary>
internal static class PackageGuard
{
    /// <summary>Word and Excel files hold tens of parts; thousands is not a document.</summary>
    public const int MaxEntries = 2_000;

    /// <summary>The most a package may unpack to, all parts together.</summary>
    public const long MaxUncompressedBytes = 100L * 1024 * 1024;

    /// <summary>Text compresses well, but not this well: a part that unpacks this many times larger than it is stored is a bomb.</summary>
    public const int MaxCompressionRatio = 200;

    /// <summary>Parts smaller than this are not judged by their ratio (a tiny part cannot do harm).</summary>
    public const long RatioCheckedFromBytes = 1024 * 1024;

    /// <summary>The most characters the SDK reads from one part.</summary>
    public const long MaxCharactersInPart = 20_000_000;

    /// <summary>How the SDK opens a checked package: read-only, never saved, each part capped at <see cref="MaxCharactersInPart"/>.</summary>
    public static OpenSettings OpenSettings() => new() { AutoSave = false, MaxCharactersInPart = MaxCharactersInPart };

    /// <summary>
    /// Throws <c>agenda.fileTooLarge</c> when the package breaks a limit and <c>agenda.unreadable</c> when it is not a
    /// readable ZIP. Leaves <paramref name="package"/> at its start.
    /// </summary>
    public static void Check(Stream package, AgendaParseOptions options, string what, CancellationToken cancellationToken)
    {
        ZipArchive zip;
        try
        {
            zip = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        }
        catch (InvalidDataException e)
        {
            throw AgendaErrors.Unreadable(options, what, e);
        }

        using (zip)
        {
            var entries = zip.Entries;
            if (entries.Count > MaxEntries)
            {
                throw AgendaErrors.PackageTooLarge(
                    options,
                    what,
                    string.Create(CultureInfo.InvariantCulture, $"holds {entries.Count:N0} parts (at most {MaxEntries:N0})"));
            }

            long declared = 0;
            foreach (var entry in entries)
            {
                if (entry.Length is < 0 or > MaxUncompressedBytes || (declared += entry.Length) > MaxUncompressedBytes)
                {
                    throw UnpacksTooLarge(options, what, Math.Max(declared, entry.Length));
                }

                if (entry.Length >= RatioCheckedFromBytes && entry.Length > Math.Max(1, entry.CompressedLength) * MaxCompressionRatio)
                {
                    throw AgendaErrors.PackageTooLarge(
                        options,
                        what,
                        string.Create(CultureInfo.InvariantCulture, $"has a part that unpacks {entry.Length / Math.Max(1, entry.CompressedLength):N0} times larger than it is stored (at most {MaxCompressionRatio})"));
                }
            }

            long inflated = 0;
            var buffer = new byte[81920];
            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var part = new BoundedReadStream(entry.Open(), Math.Min(entry.Length, MaxUncompressedBytes - inflated));
                try
                {
                    while (part.Read(buffer, 0, buffer.Length) > 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                    }
                }
                catch (InvalidDataException) when (part.Exceeded)
                {
                    throw AgendaErrors.PackageTooLarge(
                        options,
                        what,
                        string.Create(CultureInfo.InvariantCulture, $"has a part that unpacks to more than the {entry.Length:N0} bytes it declares"));
                }
                catch (InvalidDataException e)
                {
                    throw AgendaErrors.Unreadable(options, what, e);
                }

                inflated += part.BytesRead;
            }
        }

        package.Position = 0;
    }

    private static AgendaImportException UnpacksTooLarge(AgendaParseOptions options, string what, long declared) =>
        AgendaErrors.PackageTooLarge(
            options,
            what,
            string.Create(CultureInfo.InvariantCulture, $"unpacks to {declared / (1024.0 * 1024.0):N0} MB (at most {MaxUncompressedBytes / (1024 * 1024)} MB)"));
}
