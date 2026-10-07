<#
.SYNOPSIS
  Answers a Windows open-file or choose-folder dialog the way a person would: types the path into the
  dialog's name box and presses its Open / Select Folder button. For the end-to-end checks, which drive
  Memento's own pickers ("Import audio or video", "Choose an agenda", ...).

.DESCRIPTION
  The common item dialog is a Win32 dialog (#32770): its name box is the Edit inside the control with
  id 1148 ("File name:") or 1152 ("Folder:"), and its default button has id 1. They are reached with
  window messages (WM_SETTEXT, BM_CLICK), which work whatever UI Automation sees of them.

.PARAMETER Title
  The dialog's window title, e.g. "Import audio or video".

.PARAMETER Path
  The full path to type. An empty string presses Cancel instead.

.EXAMPLE
  powershell -File tools/e2e/answer-dialog.ps1 -Title "Choose an agenda" -Path "C:\fixtures\agenda.docx"
#>
param(
  [Parameter(Mandatory = $true)] [string] $Title,
  [string] $Path = '',
  [int] $TimeoutSeconds = 30
)

$ErrorActionPreference = 'Stop'

Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

public static class MementoDialog
{
    private delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc proc, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, EnumProc proc, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder text, int max);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int GetDlgCtrlID(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr GetParent(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SendMessage(IntPtr hwnd, uint msg, IntPtr wParam, string lParam);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    private const uint WM_SETTEXT = 0x000C;
    private const uint BM_CLICK = 0x00F5;

    private static string Text(IntPtr hwnd) { var b = new StringBuilder(512); GetWindowText(hwnd, b, b.Capacity); return b.ToString(); }
    private static string Class(IntPtr hwnd) { var b = new StringBuilder(256); GetClassName(hwnd, b, b.Capacity); return b.ToString(); }

    public static IntPtr Find(string title)
    {
        var found = IntPtr.Zero;
        EnumWindows((h, _) =>
        {
            if (IsWindowVisible(h) && Class(h) == "#32770" && Text(h) == title) { found = h; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    public static bool IsOpen(IntPtr dialog) { return IsWindow(dialog) && IsWindowVisible(dialog); }

    private static List<IntPtr> Children(IntPtr dialog)
    {
        var all = new List<IntPtr>();
        EnumChildWindows(dialog, (h, _) => { all.Add(h); return true; }, IntPtr.Zero);
        return all;
    }

    /// <summary>The Edit of the name box: itself id 1148/1152, or inside a control with that id.</summary>
    public static IntPtr NameBox(IntPtr dialog)
    {
        foreach (var h in Children(dialog))
        {
            if (Class(h) != "Edit") continue;
            for (var p = h; p != IntPtr.Zero && p != dialog; p = GetParent(p))
            {
                var id = GetDlgCtrlID(p);
                if (id == 1148 || id == 1152) return h;
            }
        }
        return IntPtr.Zero;
    }

    public static IntPtr Button(IntPtr dialog, int id)
    {
        foreach (var h in Children(dialog))
        {
            if (Class(h) == "Button" && GetDlgCtrlID(h) == id) return h;
        }
        return IntPtr.Zero;
    }

    public static void SetText(IntPtr hwnd, string text) { SendMessage(hwnd, WM_SETTEXT, IntPtr.Zero, text); }

    // Posted, not sent: the dialog may answer the click with a message box of its own.
    public static void Click(IntPtr hwnd) { PostMessage(hwnd, BM_CLICK, IntPtr.Zero, IntPtr.Zero); }
}
'@

$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
$dialog = [IntPtr]::Zero
while ($dialog -eq [IntPtr]::Zero -and (Get-Date) -lt $deadline) {
  $dialog = [MementoDialog]::Find($Title)
  if ($dialog -eq [IntPtr]::Zero) { Start-Sleep -Milliseconds 200 }
}
if ($dialog -eq [IntPtr]::Zero) {
  Write-Error "No dialog titled '$Title' appeared within $TimeoutSeconds s."
  exit 2
}

if ($Path -eq '') {
  [MementoDialog]::Click([MementoDialog]::Button($dialog, 2))
  exit 0
}

# The dialog is found before its controls exist; wait for the name box.
$box = [IntPtr]::Zero
while ($box -eq [IntPtr]::Zero -and (Get-Date) -lt $deadline) {
  $box = [MementoDialog]::NameBox($dialog)
  if ($box -eq [IntPtr]::Zero) { Start-Sleep -Milliseconds 200 }
}
if ($box -eq [IntPtr]::Zero) {
  Write-Error "The dialog '$Title' has no name box."
  exit 3
}

Start-Sleep -Milliseconds 300
[MementoDialog]::SetText($box, $Path)
Start-Sleep -Milliseconds 300
[MementoDialog]::Click([MementoDialog]::Button($dialog, 1))

# A choose-folder dialog may first move into the typed folder; press Select Folder again while it stays open.
for ($i = 0; $i -lt 10; $i++) {
  Start-Sleep -Milliseconds 500
  if (-not [MementoDialog]::IsOpen($dialog)) { exit 0 }
  $ok = [MementoDialog]::Button($dialog, 1)
  if ($ok -ne [IntPtr]::Zero) { [MementoDialog]::Click($ok) }
}
if ([MementoDialog]::IsOpen($dialog)) {
  Write-Error "The dialog '$Title' did not accept '$Path'."
  exit 4
}
exit 0
