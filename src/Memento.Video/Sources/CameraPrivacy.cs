using Microsoft.Win32;

namespace Memento.Video.Sources;

/// <summary>
/// Windows' camera privacy switches (Settings › Privacy &amp; security › Camera) as the capability access manager
/// stores them. Memento is an unpackaged desktop app, so three switches apply: camera access for the PC
/// (<c>HKLM ...\ConsentStore\webcam</c>), for this user (<c>HKCU</c>, same key), and "Let desktop apps access your
/// camera" (<c>HKCU ...\webcam\NonPackaged</c>). Reading them lets Memento say which switch is off before the camera
/// fails with a bare "access denied".
/// </summary>
public static class CameraPrivacy
{
    internal const string ConsentStore = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\webcam";

    public static CameraAccess Read() => Read(
        Value(Registry.LocalMachine, ConsentStore),
        Value(Registry.CurrentUser, ConsentStore),
        Value(Registry.CurrentUser, ConsentStore + @"\NonPackaged"));

    /// <summary>Combines the three switches ("Allow", "Deny" or missing). Pure, for tests.</summary>
    public static CameraAccess Read(string? machine, string? user, string? desktopApps)
    {
        if (IsDeny(machine))
        {
            return CameraAccess.DeniedForPc;
        }

        if (IsDeny(user))
        {
            return CameraAccess.DeniedForUser;
        }

        if (IsDeny(desktopApps))
        {
            return CameraAccess.DeniedForDesktopApps;
        }

        return machine is null && user is null && desktopApps is null ? CameraAccess.Unknown : CameraAccess.Allowed;
    }

    /// <summary>Where the switch that is off lives, for the §18.6 permission card.</summary>
    public static string Where(CameraAccess access) => access switch
    {
        CameraAccess.DeniedForPc => "Privacy & security › Camera › Camera access",
        CameraAccess.DeniedForUser => "Privacy & security › Camera › Let apps access your camera",
        CameraAccess.DeniedForDesktopApps => "Privacy & security › Camera › Let desktop apps access your camera",
        _ => "Privacy & security › Camera",
    };

    /// <summary>The Settings page to open (<c>ms-settings:privacy-webcam</c>).</summary>
    public const string SettingsUri = "ms-settings:privacy-webcam";

    private static bool IsDeny(string? value) => string.Equals(value, "Deny", StringComparison.OrdinalIgnoreCase);

    private static string? Value(RegistryKey root, string path)
    {
        try
        {
            using var key = root.OpenSubKey(path);
            return key?.GetValue("Value") as string;
        }
        catch (System.Security.SecurityException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
