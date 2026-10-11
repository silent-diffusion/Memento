using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using Memento.Video.Interop;

namespace Memento.Video.Sources;

/// <summary>
/// The DirectShow video input devices (<c>CLSID_VideoInputDeviceCategory</c> through the system device enumerator):
/// every camera Windows knows, plus the virtual and software cameras that register only a DirectShow filter (OBS
/// Virtual Camera, NDI, Snap Camera …). Listing reads the registry and the device list only; no filter is loaded, so
/// nothing starts. ENGINE-NOTES.md §R.
/// </summary>
internal static class DirectShowDevices
{
    /// <summary>The devices with their id keys (<see cref="CameraListMerger.DirectShowKey"/>); empty when DirectShow is missing.</summary>
    public static IReadOnlyList<CameraDevice> List()
    {
        var result = new List<CameraDevice>();
        try
        {
            ComApartment.InMta(() =>
            {
                Walk((key, name, path, _) =>
                {
                    result.Add(new CameraDevice(name, key, path));
                    return false;
                });
                return 0;
            });
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException)
        {
            // No DirectShow (a stripped-down Windows) or a broken registration: Media Foundation's list stands alone.
        }

        return result;
    }

    /// <summary>The device's capture filter, or null when no device has <paramref name="key"/> now. Call from an MTA thread.</summary>
    public static DirectShow.IBaseFilter? Bind(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        DirectShow.IBaseFilter? filter = null;
        Walk((candidate, _, _, moniker) =>
        {
            if (!string.Equals(candidate, key, StringComparison.Ordinal))
            {
                return false;
            }

            var iid = DirectShow.IidBaseFilter;
            moniker.BindToObject(null!, null, ref iid, out var bound);
            filter = (DirectShow.IBaseFilter)bound;
            return true;
        });
        return filter;
    }

    /// <summary>Calls <paramref name="visit"/> for each device until it returns true.</summary>
    private static void Walk(Func<string, string, string?, IMoniker, bool> visit)
    {
        var enumerator = DirectShow.Create<DirectShow.ICreateDevEnum>(DirectShow.ClsidSystemDeviceEnum, typeof(DirectShow.ICreateDevEnum).GUID);
        IEnumMoniker? monikers = null;
        try
        {
            var hr = enumerator.CreateClassEnumerator(DirectShow.ClsidVideoInputDeviceCategory, out monikers, 0);
            if (hr != DirectShow.SOk || monikers is null)
            {
                return;
            }

            var seen = new Dictionary<string, int>(StringComparer.Ordinal);
            var one = new IMoniker[1];
            while (monikers.Next(1, one, IntPtr.Zero) == DirectShow.SOk)
            {
                var moniker = one[0];
                try
                {
                    var (name, path, clsid) = Read(moniker);
                    if (name is null)
                    {
                        continue;
                    }

                    if (visit(CameraListMerger.DirectShowKey(name, path, clsid, seen), name, path, moniker))
                    {
                        return;
                    }
                }
                finally
                {
                    DirectShow.Release(moniker);
                }
            }
        }
        finally
        {
            DirectShow.Release(monikers);
            DirectShow.Release(enumerator);
        }
    }

    private static (string? Name, string? Path, string? Clsid) Read(IMoniker moniker)
    {
        var iid = DirectShow.IidPropertyBag;
        moniker.BindToStorage(null!, null, ref iid, out var storage);
        var bag = (DirectShow.IPropertyBag)storage;
        try
        {
            return (Property(bag, "FriendlyName"), Property(bag, "DevicePath"), Property(bag, "CLSID"));
        }
        finally
        {
            // A device moniker is often its own property bag: then the bag is the moniker's RCW, released by the caller.
            if (!ReferenceEquals(storage, moniker))
            {
                DirectShow.Release(bag);
            }
        }
    }

    private static string? Property(DirectShow.IPropertyBag bag, string name)
    {
        object? value = null;
        return bag.Read(name, ref value, IntPtr.Zero) == DirectShow.SOk && value is string text && !string.IsNullOrWhiteSpace(text) ? text.Trim() : null;
    }
}
