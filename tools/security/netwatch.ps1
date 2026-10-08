<#
.SYNOPSIS
  Network check of the security audit (docs/audits/SECURITY-AUDIT-2026-10-07.md, item 5): runs Memento with a fresh
  data root and lists every TCP connection and UDP endpoint of the app and its child processes (WebView2, worker).

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools/security/netwatch.ps1 `
    -Exe src/Memento.App/bin/Release/net8.0-windows10.0.19041.0/win-x64/Memento.exe `
    -DataRoot $env:TEMP\memento-netcheck -AppArgs '--screenshot', "$env:TEMP\memento-netcheck.png", '--simulate-audio'
#>
param(
  [Parameter(Mandatory)] [string] $Exe,
  [Parameter(Mandatory)] [string] $DataRoot,
  [string[]] $AppArgs = @(),
  [int] $MaxSeconds = 120,
  [hashtable] $ExtraEnv = @{}
)
# Starts the app with LOCALAPPDATA pointed at $DataRoot and records every TCP connection and UDP endpoint owned by the
# app or any of its descendants (WebView2 processes, the worker) until it exits. Loopback is reported separately.
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force $DataRoot | Out-Null
$psi = New-Object System.Diagnostics.ProcessStartInfo $Exe
$psi.Arguments = ($AppArgs | ForEach-Object { '"' + $_ + '"' }) -join ' '
$psi.UseShellExecute = $false
$psi.EnvironmentVariables['LOCALAPPDATA'] = $DataRoot
foreach ($k in $ExtraEnv.Keys) { $psi.EnvironmentVariables[$k] = $ExtraEnv[$k] }
$root = [System.Diagnostics.Process]::Start($psi)
$start = Get-Date
$pids = @{ $root.Id = 'Memento.exe' }
$seen = @{}
$udp = @{}
$lastTree = [datetime]::MinValue
while (-not $root.HasExited -and ((Get-Date) - $start).TotalSeconds -lt $MaxSeconds) {
  if (((Get-Date) - $lastTree).TotalMilliseconds -ge 700) {
    $all = Get-CimInstance Win32_Process -Property ProcessId, ParentProcessId, Name
    do {
      $added = $false
      foreach ($p in $all) {
        if ($pids.ContainsKey([int]$p.ParentProcessId) -and -not $pids.ContainsKey([int]$p.ProcessId)) { $pids[[int]$p.ProcessId] = $p.Name; $added = $true }
      }
    } while ($added)
    $lastTree = Get-Date
  }
  foreach ($c in (Get-NetTCPConnection -ErrorAction SilentlyContinue | Where-Object { $pids.ContainsKey([int]$_.OwningProcess) })) {
    $key = "$($pids[[int]$c.OwningProcess]) -> $($c.RemoteAddress):$($c.RemotePort) [$($c.State)]"
    if (-not $seen.ContainsKey($key)) {
      $name = ''
      if ($c.RemoteAddress -notin @('0.0.0.0', '::', '127.0.0.1', '::1')) {
        $name = (Get-DnsClientCache -ErrorAction SilentlyContinue | Where-Object { "$($_.Data)" -eq "$($c.RemoteAddress)" } | Select-Object -ExpandProperty Entry -Unique) -join ','
      }
      $seen["$key $name"] = [math]::Round(((Get-Date) - $start).TotalSeconds, 1)
      $seen[$key] = -1
    }
  }
  foreach ($u in (Get-NetUDPEndpoint -ErrorAction SilentlyContinue | Where-Object { $pids.ContainsKey([int]$_.OwningProcess) })) {
    $key = "$($pids[[int]$u.OwningProcess]) udp $($u.LocalAddress):$($u.LocalPort)"
    if (-not $udp.ContainsKey($key)) { $udp[$key] = [math]::Round(((Get-Date) - $start).TotalSeconds, 1) }
  }
  Start-Sleep -Milliseconds 100
}
if (-not $root.HasExited) { $root.Kill() }
"exit code: $($root.ExitCode); seconds: $([math]::Round(((Get-Date) - $start).TotalSeconds,1)); processes watched: $($pids.Count) ($((($pids.Values | Sort-Object -Unique) -join ', ')))"
"TCP (first seen, s):"
$seen.GetEnumerator() | Where-Object { $_.Value -ge 0 } | Sort-Object Value | ForEach-Object { "  $($_.Value)  $($_.Key)" }
"UDP endpoints:"
$udp.GetEnumerator() | Sort-Object Value | ForEach-Object { "  $($_.Value)  $($_.Key)" }
