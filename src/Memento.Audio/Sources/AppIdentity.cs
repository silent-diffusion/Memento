using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Memento.Audio.Sources;

/// <summary>
/// Name and icon of a process that owns an audio session. Name order (ENGINE-NOTES.md §C): the session's
/// DisplayName (usually empty), then the executable's FileDescription, then the process name. The executable
/// comes from <c>Process.MainModule</c>, or <c>QueryFullProcessImageName</c> when modules cannot be enumerated.
/// </summary>
internal static class AppIdentity
{
    public const int IconSize = 32;

    /// <summary>Picks the display name; pure so it can be tested.</summary>
    public static string ResolveName(string? sessionDisplayName, string? fileDescription, string? processName, int processId)
    {
        if (!string.IsNullOrWhiteSpace(sessionDisplayName))
        {
            return sessionDisplayName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(fileDescription))
        {
            return fileDescription.Trim();
        }

        return !string.IsNullOrWhiteSpace(processName) ? processName : $"Process {processId}";
    }

    /// <summary>Session display names can be indirect resource strings ("@%SystemRoot%\…,-202").</summary>
    public static string? CleanDisplayName(string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return null;
        }

        return displayName.StartsWith('@') ? ProcessInfoNative.LoadIndirectString(displayName) : displayName;
    }

    public static string? ExecutablePath(Process process)
    {
        try
        {
            var path = process.MainModule?.FileName;
            if (!string.IsNullOrEmpty(path))
            {
                return path;
            }
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
        {
            // "Unable to enumerate the process modules": elevated, protected or exited.
        }

        try
        {
            return ProcessInfoNative.QueryImagePath(process.Id);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    public static string? FileDescription(string? executablePath)
    {
        if (string.IsNullOrEmpty(executablePath))
        {
            return null;
        }

        try
        {
            return FileVersionInfo.GetVersionInfo(executablePath).FileDescription;
        }
        catch (Exception ex) when (ex is FileNotFoundException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>32×32 PNG of the icon associated with <paramref name="executablePath"/>, or null.</summary>
    public static byte[]? IconPng(string? executablePath)
    {
        if (string.IsNullOrEmpty(executablePath) || !File.Exists(executablePath))
        {
            return null;
        }

        try
        {
            using var icon = Icon.ExtractAssociatedIcon(executablePath);
            if (icon is null)
            {
                return null;
            }

            using var bitmap = icon.ToBitmap();
            using var sized = bitmap.Width == IconSize && bitmap.Height == IconSize ? null : new Bitmap(bitmap, IconSize, IconSize);
            using var png = new MemoryStream();
            (sized ?? bitmap).Save(png, ImageFormat.Png);
            return png.ToArray();
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or ExternalException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
