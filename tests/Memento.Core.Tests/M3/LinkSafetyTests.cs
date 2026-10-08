using System.Diagnostics;
using Memento.Core.Bridge;
using Memento.Core.Projects;

namespace Memento.Core.Tests.M3;

/// <summary>Security audit 2026-10-07, SA-12: junctions inside or around the library are never followed.</summary>
public sealed class LinkSafetyTests : IDisposable
{
    private readonly M3Host _m3 = new(settingsLibrary: true);

    public void Dispose() => _m3.Dispose();

    [Fact]
    public void RealPathResolvesAJunctionAlongThePath()
    {
        var target = _m3.Directory.File("Real");
        Directory.CreateDirectory(Path.Combine(target, "inner"));
        var link = _m3.Directory.File("Link");
        if (!TryCreateJunction(link, target))
        {
            return;
        }

        Assert.Equal(Path.Combine(target, "inner", "new"), LinkSafeFiles.RealPath(Path.Combine(link, "inner", "new")), ignoreCase: true);
    }

    [Fact]
    public async Task DeletingAProjectNeverTouchesFilesBehindAJunctionInIt()
    {
        var id = await _m3.RecordAsync();
        var outside = _m3.Directory.File("Outside");
        Directory.CreateDirectory(outside);
        var precious = Path.Combine(outside, "precious.txt");
        File.WriteAllText(precious, "keep");
        File.SetAttributes(precious, FileAttributes.ReadOnly);
        if (!TryCreateJunction(Path.Combine(_m3.Host.Store.GetProjectFolder(id), "planted"), outside))
        {
            return;
        }

        await _m3.ResultAsync("project.delete", new { recordingId = id });

        Assert.True(File.Exists(precious));
        Assert.True((File.GetAttributes(precious) & FileAttributes.ReadOnly) != 0);
        File.SetAttributes(precious, FileAttributes.Normal);
    }

    [Fact]
    public async Task TheLibraryCannotMoveIntoItselfThroughAJunction()
    {
        await _m3.RecordAsync();
        var root = _m3.Host.Settings.Current.EffectiveLibraryPath;
        var inside = Path.Combine(root, "empty-inside");
        Directory.CreateDirectory(inside);
        var link = _m3.Directory.File("LooksElsewhere");
        if (!TryCreateJunction(link, inside))
        {
            return;
        }

        var error = await _m3.ErrorAsync("library.move", new { newPath = link });

        Assert.Equal(DomainErrorCodes.LibraryMoveRefused, error.GetProperty("code").GetString());
        Assert.Equal(root, _m3.Host.Settings.Current.EffectiveLibraryPath);
    }

    internal static bool TryCreateJunction(string link, string target)
    {
        var start = new ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in new[] { "/c", "mklink", "/J", link, target })
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start);
        if (process is null)
        {
            return false;
        }

        process.WaitForExit(10_000);
        return process.ExitCode == 0 && Directory.Exists(link);
    }
}
