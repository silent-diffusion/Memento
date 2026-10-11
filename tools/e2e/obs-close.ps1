# Closes an OBS Studio process the way its own window would be closed (WM_CLOSE to its main window, which also
# works while OBS sits in the notification area), so it stops its outputs, the virtual camera included, and saves
# nothing unusual. Used by m8-camera.mjs --stop-obs-after to stop the OBS Virtual Camera mid-recording. Run it only
# against an OBS this test started itself (a portable copy with its own settings): an OBS whose settings ask to
# confirm exiting while outputs are active shows that question instead of closing.
#
#   powershell -NoProfile -File tools/e2e/obs-close.ps1 -ProcessId 1234 [-TimeoutSeconds 20]
param(
  [Parameter(Mandatory = $true)] [int] $ProcessId,
  [int] $TimeoutSeconds = 20
)

$ErrorActionPreference = 'Stop'
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public static class ObsWindows {
  private delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);
  [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc proc, IntPtr lParam);
  [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int max);
  [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
  public static List<IntPtr> MainWindows(uint processId) {
    var found = new List<IntPtr>();
    EnumWindows((hwnd, _) => {
      uint pid;
      GetWindowThreadProcessId(hwnd, out pid);
      var title = new StringBuilder(256);
      GetWindowText(hwnd, title, title.Capacity);
      if (pid == processId && title.ToString().StartsWith("OBS ", StringComparison.Ordinal)) found.Add(hwnd);
      return true;
    }, IntPtr.Zero);
    return found;
  }
}
'@

$process = Get-Process -Id $ProcessId
if ($process.ProcessName -ne 'obs64') { throw "Process $ProcessId is $($process.ProcessName), not OBS Studio." }
$null = $process.Handle  # keeps the exit code readable after the process ends
$windows = [ObsWindows]::MainWindows([uint32] $ProcessId)
if ($windows.Count -eq 0) { throw "OBS ($ProcessId) has no main window to close." }
foreach ($hwnd in $windows) { [void] [ObsWindows]::PostMessage($hwnd, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) }
if (-not $process.WaitForExit($TimeoutSeconds * 1000)) { throw "OBS ($ProcessId) did not close within $TimeoutSeconds s." }
Write-Output "OBS ($ProcessId) closed with exit code $($process.ExitCode)."
