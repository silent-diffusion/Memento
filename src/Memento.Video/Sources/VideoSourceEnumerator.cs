using System.Diagnostics;
using System.Globalization;
using Memento.Video.Gpu;
using Memento.Video.Interop;
using SharpGen.Runtime;
using Vortice.MediaFoundation;

namespace Memento.Video.Sources;

/// <summary>
/// Lists what can be captured, for the Recording screen's VIDEO section and the picker (DESIGN.md §18.1, §18.2).
/// Cheap and side-effect free: nothing is opened, so no camera light comes on and no capture border appears.
/// </summary>
public static class VideoSourceEnumerator
{
    /// <summary>Connected displays, primary first ("Display 1"), with physical size and refresh rate.</summary>
    public static IReadOnlyList<VideoSourceInfo> ListDisplays()
    {
        var entries = new List<DisplayNaming.DisplayEntry>();
        foreach (var monitor in User32.Monitors())
        {
            var mode = User32.CurrentMode(monitor.DeviceName);
            entries.Add(mode is { } m
                ? new DisplayNaming.DisplayEntry(monitor.DeviceName, m.Left, m.Top, m.Width, m.Height, monitor.IsPrimary, m.RefreshRate)
                : new DisplayNaming.DisplayEntry(monitor.DeviceName, monitor.Bounds.Left, monitor.Bounds.Top, monitor.Bounds.Width, monitor.Bounds.Height, monitor.IsPrimary, 0));
        }

        return DisplayNaming.Name(entries);
    }

    /// <summary>
    /// Top-level windows as Windows' own picker lists them. Windows of <paramref name="ownProcessId"/> (Memento) are
    /// listed but excluded; windows that hide themselves from capture are marked protected.
    /// </summary>
    public static IReadOnlyList<VideoSourceInfo> ListWindows(int ownProcessId)
    {
        var processNames = new Dictionary<int, string>();
        var result = new List<VideoSourceInfo>();
        foreach (var window in User32.CapturableWindows())
        {
            if (!processNames.TryGetValue(window.ProcessId, out var processName))
            {
                processName = ProcessName(window.ProcessId);
                processNames[window.ProcessId] = processName;
            }

            var excluded = window.ProcessId == ownProcessId;
            string detail;
            if (excluded)
            {
                detail = "This app is excluded from capture";
            }
            else if (window.DisplayAffinity != User32.WdaNone)
            {
                detail = processName + " · protected, can't be captured";
            }
            else if (window.IsMinimized)
            {
                detail = processName + " · minimised";
            }
            else
            {
                detail = string.Create(CultureInfo.InvariantCulture, $"{processName} · {window.Bounds.Width} × {window.Bounds.Height}");
            }

            result.Add(new VideoSourceInfo(
                VideoSourceId.Window(window.Handle),
                window.Title,
                detail,
                window.Bounds.Width,
                window.Bounds.Height,
                0,
                false,
                excluded || window.DisplayAffinity != User32.WdaNone,
                processName,
                window.DisplayAffinity != User32.WdaNone));
        }

        return result;
    }

    /// <summary>Video capture devices Media Foundation sees (webcams, capture cards), by name and symbolic link.</summary>
    public static IReadOnlyList<VideoSourceInfo> ListCameras()
    {
        MediaFoundationRuntime.EnsureStarted();
        var result = new List<VideoSourceInfo>();
        try
        {
            using var attributes = MediaFactory.MFCreateAttributes(1);
            attributes.Set(CaptureDeviceAttributeKeys.SourceType, CaptureDeviceAttributeKeys.SourceTypeVidcap);
            using var devices = MediaFactory.MFEnumDeviceSources(attributes);
            foreach (var device in devices)
            {
                using (device)
                {
                    var name = device.GetString(CaptureDeviceAttributeKeys.FriendlyName);
                    var link = device.GetString(CaptureDeviceAttributeKeys.SourceTypeVidcapSymbolicLink);
                    result.Add(new VideoSourceInfo(VideoSourceId.Camera(link), name, "Camera", 0, 0, 0, false, false));
                }
            }
        }
        catch (SharpGenException)
        {
            // No camera stack (Windows N without the Media Feature Pack) or no devices: an empty list.
        }

        return result;
    }

    private static string ProcessName(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            try
            {
                var description = process.MainModule?.FileVersionInfo.FileDescription;
                if (!string.IsNullOrWhiteSpace(description))
                {
                    return description.Trim();
                }
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // Elevated or protected process: its module can't be read; the process name will do.
            }

            return process.ProcessName;
        }
        catch (ArgumentException)
        {
            return "Unknown app";
        }
        catch (InvalidOperationException)
        {
            return "Unknown app";
        }
    }
}
