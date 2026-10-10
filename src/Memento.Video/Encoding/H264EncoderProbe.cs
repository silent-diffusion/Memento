using Memento.Video.Gpu;
using SharpGen.Runtime;
using Vortice.MediaFoundation;

namespace Memento.Video.Encoding;

/// <summary>Lists the H.264 encoders Media Foundation offers: the hardware ones with their adapter, then the software one.</summary>
public static class H264EncoderProbe
{
    private const uint EnumHardware = 0x4;
    private const uint EnumSyncMft = 0x1;
    private const uint EnumAsyncMft = 0x2;
    private const uint EnumSortAndFilter = 0x40;

    public static IReadOnlyList<H264EncoderInfo> List()
    {
        MediaFoundationRuntime.EnsureStarted();
        var result = new List<H264EncoderInfo>();
        result.AddRange(Enumerate(EnumHardware | EnumSortAndFilter, hardware: true));
        result.AddRange(Enumerate(EnumSyncMft | EnumAsyncMft | EnumSortAndFilter, hardware: false));
        return result;
    }

    /// <summary>
    /// The hardware encoder of <paramref name="adapter"/>, or null when it has none. Media Foundation's plain
    /// enumeration does not say which adapter an encoder belongs to (no <c>MFT_ENUM_ADAPTER_LUID</c> on this list), so
    /// it is matched by vendor ("VEN_10DE" for 0x10DE); with two cards of one vendor the sink writer still picks the
    /// one on the device it is given.
    /// </summary>
    public static H264EncoderInfo? HardwareFor(GpuAdapterInfo adapter)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        var vendor = "VEN_" + adapter.VendorId.ToString("X4", System.Globalization.CultureInfo.InvariantCulture);
        return List().FirstOrDefault(e => e.IsHardware && (e.AdapterLuid == adapter.Luid || string.Equals(e.VendorId, vendor, StringComparison.OrdinalIgnoreCase)));
    }

    private static List<H264EncoderInfo> Enumerate(uint flags, bool hardware)
    {
        var list = new List<H264EncoderInfo>();
        try
        {
            using var collection = MediaFactory.MFTEnumEx(
                MediaFoundationGuids.VideoEncoderCategory,
                flags,
                new RegisterTypeInfo { GuidMajorType = MediaTypeGuids.Video, GuidSubtype = VideoFormatGuids.NV12 },
                new RegisterTypeInfo { GuidMajorType = MediaTypeGuids.Video, GuidSubtype = VideoFormatGuids.H264 });
            foreach (var activate in collection)
            {
                using (activate)
                {
                    var name = TryString(activate, TransformAttributeKeys.MftFriendlyNameAttribute) ?? "H.264 encoder";
                    long? luid = null;
                    if (hardware && activate.GetUInt64(MediaFoundationGuids.MftEnumAdapterLuid, out var value).Success)
                    {
                        luid = unchecked((long)value);
                    }

                    var vendor = hardware ? TryString(activate, TransformAttributeKeys.MftEnumHardwareVendorIdAttribute) : null;
                    list.Add(new H264EncoderInfo(name, hardware, luid, vendor));
                }
            }
        }
        catch (SharpGenException)
        {
            // No encoders of this kind (Windows N without the Media Feature Pack has no software one).
        }

        return list;
    }

    private static string? TryString(IMFAttributes attributes, Guid key)
    {
        try
        {
            return attributes.GetString(key);
        }
        catch (SharpGenException)
        {
            return null;
        }
    }
}
