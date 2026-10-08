using System.Buffers.Binary;
using System.Text;
using Memento.Documents.Agenda;
using Memento.Documents.Agenda.OpenXml;
using Memento.Documents.Tests.Support;

namespace Memento.Documents.Tests.Hardening;

/// <summary>Word and Excel packages are checked before the SDK loads them: no ZIP bombs, no part that lies about its size.</summary>
public sealed class OpenXmlPackageTests
{
    public static TheoryData<string> Packages => new() { "board-table.docx", "simple-list.xlsx" };

    [Theory]
    [MemberData(nameof(Packages))]
    public async Task APackageThatUnpacksPastTheLimitIsRefused(string fixture)
    {
        // 101 MB of zeros stores in about 100 KB.
        var bomb = WithEntry(Agendas.Fixture(fixture), "docProps/padding.bin", new byte[101 * 1024 * 1024]);

        var error = await Assert.ThrowsAsync<AgendaImportException>(() => Parse(fixture, bomb));

        Assert.Equal(AgendaErrorCodes.FileTooLarge, error.Code);
        Assert.Contains("unpacks to", error.Message, StringComparison.Ordinal);
        Assert.Contains("Nothing was imported.", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Packages))]
    public async Task APartThatCompressesTooWellIsRefused(string fixture)
    {
        var bomb = WithEntry(Agendas.Fixture(fixture), "docProps/padding.bin", new byte[8 * 1024 * 1024]);

        var error = await Assert.ThrowsAsync<AgendaImportException>(() => Parse(fixture, bomb));

        Assert.Equal(AgendaErrorCodes.FileTooLarge, error.Code);
        Assert.Contains("times larger than it is stored (at most 200)", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Packages))]
    public async Task APackageWithThousandsOfPartsIsRefused(string fixture)
    {
        var entries = PackageParts.Read(Agendas.Fixture(fixture));
        entries.AddRange(Enumerable.Range(0, PackageGuard.MaxEntries).Select(i => ($"docProps/part{i}.bin", new byte[] { 1 })));
        var package = PackageParts.Write(entries);

        var direct = await Assert.ThrowsAsync<AgendaImportException>(() => Parse(fixture, package));
        var imported = await Assert.ThrowsAsync<AgendaImportException>(() => Agendas.ImportAsync(package, fixture));

        Assert.Equal(AgendaErrorCodes.FileTooLarge, direct.Code);
        Assert.Equal(AgendaErrorCodes.FileTooLarge, imported.Code);
        Assert.Contains("parts (at most 2,000)", direct.Message, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Packages))]
    public async Task APartThatLiesAboutItsSizeIsReadNoFurtherThanItDeclares(string fixture)
    {
        // Text that does not compress much, so only the lie about its size can stop it.
        var random = new Random(7);
        var text = new StringBuilder();
        while (text.Length < 2 * 1024 * 1024)
        {
            text.Append(random.Next()).Append(' ');
        }

        var package = WithEntry(Agendas.Fixture(fixture), "docProps/notes.txt", Encoding.ASCII.GetBytes(text.ToString()));
        DeclareSize(package, "docProps/notes.txt", 1000);

        // .NET stops inflating at the declared size; the bounded read is the second line if it ever does not.
        var result = await Parse(fixture, package);

        Assert.NotEmpty(result.Items);
    }

    [Fact]
    public void ABoundedReadStopsOncePastItsLimit()
    {
        using var part = new BoundedReadStream(new MemoryStream(new byte[2000]), 1000);
        var buffer = new byte[600];

        Assert.Equal(600, part.Read(buffer, 0, buffer.Length));
        Assert.Throws<InvalidDataException>(() => part.Read(buffer, 0, buffer.Length));
        Assert.True(part.Exceeded);
    }

    [Fact]
    public void TheSdkReadsAtMostTwentyMillionCharactersFromAPart()
    {
        var settings = PackageGuard.OpenSettings();

        Assert.Equal(20_000_000, settings.MaxCharactersInPart);
        Assert.False(settings.AutoSave);
    }

    [Theory]
    [MemberData(nameof(Packages))]
    public async Task OrdinaryPackagesStillParse(string fixture)
    {
        var result = await Parse(fixture, Agendas.Fixture(fixture));

        Assert.NotEmpty(result.Items);
    }

    private static Task<AgendaParseResult> Parse(string fixture, byte[] package)
    {
        var options = new AgendaParseOptions { FileName = fixture };
        return fixture.EndsWith(".docx", StringComparison.Ordinal)
            ? new DocxAgendaParser().ParseAsync(new MemoryStream(package), options, CancellationToken.None)
            : new XlsxAgendaParser().ParseAsync(new MemoryStream(package), options, CancellationToken.None);
    }

    private static byte[] WithEntry(byte[] package, string name, byte[] data)
    {
        var entries = PackageParts.Read(package);
        entries.Add((name, data));
        return PackageParts.Write(entries);
    }

    /// <summary>Rewrites the uncompressed size in the central directory record of <paramref name="name"/>.</summary>
    private static void DeclareSize(byte[] package, string name, uint size)
    {
        var nameBytes = Encoding.ASCII.GetBytes(name);
        for (var i = 0; i + 46 < package.Length; i++)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(package.AsSpan(i)) != 0x02014B50)
            {
                continue;
            }

            var nameLength = BinaryPrimitives.ReadUInt16LittleEndian(package.AsSpan(i + 28));
            if (package.AsSpan(i + 46, nameLength).SequenceEqual(nameBytes))
            {
                BinaryPrimitives.WriteUInt32LittleEndian(package.AsSpan(i + 24), size);
                return;
            }
        }

        throw new InvalidOperationException($"No central directory record for {name}.");
    }
}
