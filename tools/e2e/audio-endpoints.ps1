<#
.SYNOPSIS
  Test helper for H1 device events: lists audio endpoints, sets the default output, and disables or enables an
  endpoint, the way Windows' Sound settings do (no administrator rights needed). Always restore what you change.

.EXAMPLE
  ./audio-endpoints.ps1 list
  ./audio-endpoints.ps1 default '{0.0.0.00000000}.{1ddc5dd4-...}'
  ./audio-endpoints.ps1 disable '{0.0.1.00000000}.{d072640d-...}'
  ./audio-endpoints.ps1 enable  '{0.0.1.00000000}.{d072640d-...}'
#>
param(
  [Parameter(Mandatory = $true, Position = 0)][ValidateSet('list', 'default', 'disable', 'enable')] [string] $Command,
  [Parameter(Position = 1)] [string] $Id
)

$ErrorActionPreference = 'Stop'
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

public static class AudioEndpoints
{
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] private class MMDeviceEnumeratorCo { }
    [ComImport, Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")] private class PolicyConfigCo { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        int EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection devices);
        int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection
    {
        int GetCount(out int count);
        int Item(int index, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
        int OpenPropertyStore(int access, out IPropertyStore properties);
        int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        int GetState(out int state);
    }

    [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        int GetCount(out int count);
        int GetAt(int index, out PropertyKey key);
        int GetValue(ref PropertyKey key, out PropVariant value);
    }

    [StructLayout(LayoutKind.Sequential)] private struct PropertyKey { public Guid fmtid; public int pid; }
    [StructLayout(LayoutKind.Explicit)] private struct PropVariant { [FieldOffset(0)] public short vt; [FieldOffset(8)] public IntPtr pointer; }

    // IPolicyConfig (Windows 7 and later), used by the Sound control panel; only the two methods needed are typed.
    [ComImport, Guid("f8679f50-850a-41cf-9c72-430f290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        int GetMixFormat(); int GetDeviceFormat(); int ResetDeviceFormat(); int SetDeviceFormat();
        int GetProcessingPeriod(); int SetProcessingPeriod(); int GetShareMode(); int SetShareMode();
        int GetPropertyValue(); int SetPropertyValue();
        int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, int role);
        int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string id, int visible);
    }

    public static List<string> List()
    {
        var result = new List<string>();
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorCo();
        string defaultRender = DefaultId(enumerator, 0), defaultCapture = DefaultId(enumerator, 1);
        foreach (var flow in new[] { 0, 1 })
        {
            IMMDeviceCollection devices;
            Check(enumerator.EnumAudioEndpoints(flow, 0x1 | 0x2 | 0x4 | 0x8, out devices));
            int count; devices.GetCount(out count);
            for (var i = 0; i < count; i++)
            {
                IMMDevice device; devices.Item(i, out device);
                string id; device.GetId(out id);
                int state; device.GetState(out state);
                var isDefault = id == defaultRender || id == defaultCapture;
                result.Add(string.Format("{0}\t{1}\t{2}\t{3}\t{4}", flow == 0 ? "render" : "capture", StateName(state), isDefault ? "default" : "", id, Name(device)));
            }
        }
        return result;
    }

    public static void SetDefault(string id)
    {
        var policy = (IPolicyConfig)new PolicyConfigCo();
        for (var role = 0; role < 3; role++) Check(policy.SetDefaultEndpoint(id, role));
    }

    public static void SetEnabled(string id, bool enabled)
    {
        var policy = (IPolicyConfig)new PolicyConfigCo();
        Check(policy.SetEndpointVisibility(id, enabled ? 1 : 0));
    }

    private static string DefaultId(IMMDeviceEnumerator enumerator, int flow)
    {
        IMMDevice device;
        if (enumerator.GetDefaultAudioEndpoint(flow, 0, out device) != 0 || device == null) return null;
        string id; device.GetId(out id);
        return id;
    }

    private static string Name(IMMDevice device)
    {
        IPropertyStore store;
        if (device.OpenPropertyStore(0, out store) != 0) return "";
        var key = new PropertyKey { fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), pid = 14 };
        PropVariant value;
        if (store.GetValue(ref key, out value) != 0 || value.vt != 31) return "";
        return Marshal.PtrToStringUni(value.pointer);
    }

    private static string StateName(int state)
    {
        switch (state) { case 1: return "active"; case 2: return "disabled"; case 4: return "notpresent"; case 8: return "unplugged"; default: return state.ToString(); }
    }

    private static void Check(int hr) { if (hr < 0) Marshal.ThrowExceptionForHR(hr); }
}
'@

switch ($Command) {
  'list' { [AudioEndpoints]::List() }
  'default' { [AudioEndpoints]::SetDefault($Id); "default render endpoint: $Id" }
  'disable' { [AudioEndpoints]::SetEnabled($Id, $false); "disabled: $Id" }
  'enable' { [AudioEndpoints]::SetEnabled($Id, $true); "enabled: $Id" }
}
