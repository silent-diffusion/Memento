<#
.SYNOPSIS
  Writes CycloneDX software bills of materials for the shipped app into build/sbom.

.DESCRIPTION
  memento-dotnet.cdx.json: every NuGet package the app and the worker ship, from the solution with
  the test projects excluded (CycloneDX .NET tool, restored from dotnet-tools.json).
  memento-ui.cdx.json: the UI's runtime npm packages (production dependencies only; the dev tools
  build the bundle but are not shipped), from ui/package-lock.json (@cyclonedx/cyclonedx-npm, pinned
  below and run through npx).

  release.yml runs this script and attaches both files to the GitHub release.

.EXAMPLE
  pwsh build/sbom.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$outputDir = Join-Path $PSScriptRoot 'sbom'
$cyclonedxNpm = '@cyclonedx/cyclonedx-npm@6.0.1'

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

[xml] $props = Get-Content (Join-Path $repoRoot 'Directory.Build.props') -Raw
$version = $props.SelectSingleNode('/Project/PropertyGroup/Version').InnerText.Trim()

New-Item -ItemType Directory -Force -Path $outputDir | Out-Null

Invoke-Native 'dotnet' @('tool', 'restore')
Invoke-Native 'dotnet' @(
  'CycloneDX', 'Memento.sln',
  '--exclude-test-projects',
  '--output', $outputDir,
  '--filename', 'memento-dotnet.cdx.json',
  '--output-format', 'Json',
  '--set-name', 'Memento',
  '--set-version', $version,
  '--no-serial-number')

Invoke-Native 'npx' @(
  '--yes', $cyclonedxNpm,
  '--omit', 'dev',
  '--package-lock-only',
  '--output-reproducible',
  '--output-format', 'JSON',
  '--output-file', (Join-Path $outputDir 'memento-ui.cdx.json')) (Join-Path $repoRoot 'ui')

Get-ChildItem $outputDir -Filter '*.cdx.json' | ForEach-Object { Write-Host "SBOM: $($_.FullName) ($($_.Length) bytes)" }
