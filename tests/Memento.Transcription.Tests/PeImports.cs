using System.Text;

namespace Memento.Transcription.Tests;

/// <summary>Reads the DLL names a PE file imports (the import directory), without dumpbin.</summary>
internal static class PeImports
{
    /// <summary>The imported DLL names, or <c>null</c> for a managed assembly or a file that is not a PE image.</summary>
    public static IReadOnlyList<string>? Read(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length < 0x40 || bytes[0] != 'M' || bytes[1] != 'Z')
        {
            return null;
        }

        var pe = BitConverter.ToInt32(bytes, 0x3C);
        if (pe <= 0 || pe + 24 > bytes.Length || BitConverter.ToUInt32(bytes, pe) != 0x00004550)
        {
            return null;
        }

        var sections = BitConverter.ToUInt16(bytes, pe + 6);
        var optionalSize = BitConverter.ToUInt16(bytes, pe + 20);
        var optional = pe + 24;
        var magic = BitConverter.ToUInt16(bytes, optional);
        var directories = optional + (magic == 0x20B ? 112 : 96);
        var importRva = BitConverter.ToUInt32(bytes, directories + 8);
        var clrRva = BitConverter.ToUInt32(bytes, directories + (14 * 8));
        if (clrRva != 0)
        {
            return null;
        }

        var sectionTable = optional + optionalSize;
        int Offset(uint rva)
        {
            for (var i = 0; i < sections; i++)
            {
                var s = sectionTable + (i * 40);
                var virtualAddress = BitConverter.ToUInt32(bytes, s + 12);
                var virtualSize = Math.Max(BitConverter.ToUInt32(bytes, s + 8), BitConverter.ToUInt32(bytes, s + 16));
                if (rva >= virtualAddress && rva < virtualAddress + virtualSize)
                {
                    return (int)(rva - virtualAddress + BitConverter.ToUInt32(bytes, s + 20));
                }
            }

            return -1;
        }

        var names = new List<string>();
        if (importRva == 0)
        {
            return names;
        }

        for (var descriptor = Offset(importRva); descriptor >= 0 && descriptor + 20 <= bytes.Length; descriptor += 20)
        {
            var nameRva = BitConverter.ToUInt32(bytes, descriptor + 12);
            if (nameRva == 0)
            {
                break;
            }

            var at = Offset(nameRva);
            var end = Array.IndexOf(bytes, (byte)0, at);
            names.Add(Encoding.ASCII.GetString(bytes, at, end - at));
        }

        return names;
    }
}
