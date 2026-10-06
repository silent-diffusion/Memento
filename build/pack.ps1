<#
.SYNOPSIS
  Builds the UI, publishes Memento for win-x64 and packs a Velopack release into build/out.

.DESCRIPTION
  1. ui: npm ci + npm run build (skip with -SkipUiBuild when ui/dist is already fresh, as in CI).
  2. dotnet publish of src/Memento.App with the win-x64 profile (self-contained folder, ReadyToRun).
  3. vpk pack (the vpk tool is restored from dotnet-tools.json) with the version from Directory.Build.props.
     The installer bootstraps the WebView2 Runtime when it is missing (--framework webview2).

  Output in build/out: MementoApp-win-Setup.exe (per-user installer), MementoApp-win-Portable.zip,
  the full .nupkg and the releases.win.json / RELEASES update feed.

.EXAMPLE
  pwsh build/pack.ps1
#>
[CmdletBinding()]
param(
  # ui/dist is already built (CI builds and tests the UI in earlier steps).
  [switch] $SkipUiBuild,
  # Keep build/out as it is: release.yml downloads the previous release there first so vpk can build a delta.
  [switch] $KeepOutput
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$uiDir = Join-Path $repoRoot 'ui'
$appProject = Join-Path $repoRoot 'src/Memento.App/Memento.App.csproj'
$publishDir = Join-Path $repoRoot 'artifacts/publish/win-x64'
$outputDir = Join-Path $PSScriptRoot 'out'
$icon = Join-Path $PSScriptRoot 'icons/memento.ico'
$releaseNotes = Join-Path $PSScriptRoot 'RELEASE-NOTES.md'

# Velopack installs to %LOCALAPPDATA%\<packId> and replaces or deletes that whole folder on install,
# update and uninstall. Memento keeps user data (Library, settings, logs, models) in
# %LOCALAPPDATA%\Memento (ARCHITECTURE.md section 4), so the pack id must never be "Memento":
# uninstalling would delete every recording. The id is permanent once a release is published.
$packId = 'MementoApp'

function Invoke-Native {
  param([string] $FilePath, [string[]] $Arguments, [string] $WorkingDirectory = $repoRoot)
  Write-Host "> $FilePath $($Arguments -join ' ')" -ForegroundColor DarkGray
  Push-Location $WorkingDirectory
  try {
    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
      throw "'$FilePath $($Arguments -join ' ')' failed with exit code $LASTEXITCODE."
    }
  }
  finally {
    Pop-Location
  }
}

function Get-ProductVersion {
  [xml] $props = Get-Content (Join-Path $repoRoot 'Directory.Build.props') -Raw
  $node = $props.SelectSingleNode('/Project/PropertyGroup/Version')
  if ($null -eq $node -or [string]::IsNullOrWhiteSpace($node.InnerText)) {
    throw 'Directory.Build.props has no <Version> property.'
  }
  return $node.InnerText.Trim()
}

# The section of RELEASE-NOTES.md for this version becomes the release notes in the package
# (and the GitHub release body when release.yml uploads it).
function Write-VersionNotes([string] $version, [string] $destination) {
  $lines = Get-Content $releaseNotes
  $start = -1
  for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match "^##\s+$([regex]::Escape($version))(\s|$)") { $start = $i; break }
  }
  if ($start -lt 0) {
    throw "build/RELEASE-NOTES.md has no '## $version' section. Add one before packing."
  }
  $end = $lines.Count
  for ($i = $start + 1; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match '^##\s') { $end = $i; break }
  }
  $section = $lines[($start + 1)..($end - 1)] -join [Environment]::NewLine
  Set-Content -Path $destination -Value $section.Trim() -Encoding utf8
}

$version = Get-ProductVersion
Write-Host "Packing Memento $version" -ForegroundColor Cyan

if (-not $SkipUiBuild) {
  Invoke-Native 'npm' @('ci') $uiDir
  Invoke-Native 'npm' @('run', 'build') $uiDir
}
if (-not (Test-Path (Join-Path $uiDir 'dist/index.html'))) {
  throw 'ui/dist is missing. Run npm ci and npm run build in ui, or drop -SkipUiBuild.'
}

if (Test-Path $publishDir) { Remove-Item -Recurse -Force $publishDir }
Invoke-Native 'dotnet' @(
  'publish', $appProject,
  '-c', 'Release',
  '-r', 'win-x64',
  '--self-contained',
  '-p:PublishProfile=win-x64',
  '-o', $publishDir)

if ((Test-Path $outputDir) -and -not $KeepOutput) { Remove-Item -Recurse -Force $outputDir }
New-Item -ItemType Directory -Force -Path $outputDir | Out-Null
$notesFile = Join-Path $repoRoot "artifacts/release-notes-$version.md"
Write-VersionNotes $version $notesFile

Invoke-Native 'dotnet' @('tool', 'restore')
Invoke-Native 'dotnet' @(
  'vpk', 'pack',
  '--packId', $packId,
  '--packVersion', $version,
  '--packDir', $publishDir,
  '--runtime', 'win-x64',
  '--mainExe', 'Memento.exe',
  '--packTitle', 'Memento',
  '--packAuthors', 'silent-diffusion',
  '--icon', $icon,
  '--releaseNotes', $notesFile,
  '--framework', 'webview2',
  '--outputDir', $outputDir)

$setup = Get-ChildItem $outputDir -Filter '*Setup.exe' | Select-Object -First 1
if (-not $setup) {
  throw "vpk pack finished but no Setup.exe is in $outputDir."
}
Write-Host ("Installer: {0} ({1:N1} MB)" -f $setup.FullName, ($setup.Length / 1MB)) -ForegroundColor Green
