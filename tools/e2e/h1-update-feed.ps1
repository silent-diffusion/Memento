# Builds a local update feed: the 0.5.0 release from build/out plus a 0.5.1 built from the same source with
# -p:Version=0.5.1 (nothing in the repository changes). Run from the repository root.
$ErrorActionPreference = 'Stop'
$env:PATH = "C:\Program Files\nodejs;C:\Program Files\dotnet;$env:PATH"
$h1 = Split-Path -Parent $MyInvocation.MyCommand.Path
$feed = Join-Path $h1 'feed'
$publish = Join-Path $h1 'publish-051'
if (Test-Path $feed) { Remove-Item -Recurse -Force $feed }
if (Test-Path $publish) { Remove-Item -Recurse -Force $publish }
New-Item -ItemType Directory -Force $feed | Out-Null
Copy-Item build\out\* $feed
& dotnet publish src/Memento.App/Memento.App.csproj -c Release -r win-x64 --self-contained -p:PublishProfile=win-x64 -p:Version=0.5.1 -o $publish | Select-Object -Last 1
$notes = Join-Path $h1 'notes-051.md'
Set-Content -Path $notes -Value 'Test release 0.5.1 for the H1 update check (local feed only).' -Encoding utf8
& dotnet vpk pack --packId MementoApp --packVersion 0.5.1 --packDir $publish --runtime win-x64 --mainExe Memento.exe --packTitle Memento --packAuthors silent-diffusion --icon build\icons\memento.ico --releaseNotes $notes --framework webview2 --outputDir $feed | Select-Object -Last 3
Get-ChildItem $feed | Select-Object Name, Length
