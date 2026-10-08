# Releasing Memento

A release is a tag `vX.Y.Z` on `main`. `.github/workflows/release.yml` builds, tests and packs it with a read-only token, then a separate job that runs no project code publishes the GitHub release: `MementoApp-win-Setup.exe`, `MementoApp-win-Portable.zip`, the full (and, after the first release, delta) `.nupkg`, the Velopack feed `releases.win.json`, the two CycloneDX SBOMs and `SHA256SUMS.txt`. Versions before 0.5.0 and versions with a suffix (`1.1.0-rc.1`) are published as pre-releases; every other version is a full release, and the app's update check follows the same rule (`UpdatePolicy`).

Never reuse or move a published tag: installed copies update from the feed of the latest full release, and Velopack's package id (`MementoApp`) is permanent.

## Checklist

### 1. Version

- [ ] `<Version>` in `Directory.Build.props` is the new version (SemVer; a suffix makes a pre-release).
- [ ] Nothing else carries the version: the app, the worker, the installer and the About section read it from the build.

### 2. Release notes

- [ ] `build/RELEASE-NOTES.md` has a `## X.Y.Z` section at the top, in plain language for the people who use Memento: what is new, what was fixed, what to know (unsigned installer, the update check, the WebView2 runtime's own connections), and the known limitations. Subsections use `###`, because `## ` starts the next version.
- [ ] A version that was never released on its own is either folded into this section or headed `## A.B.C (not released on its own; ships in X.Y.Z)`, which `build/pack.ps1` appends to these notes.
- [ ] `README.md` (status line, What it does), `docs/USER-GUIDE.md` ("This guide covers version …", known limitations) and `docs/ROADMAP.md` (milestone status, what was and was not executed) match the release.

### 3. CI green

- [ ] On the commit to tag, locally or in CI:
  - `dotnet build Memento.sln -c Release` with zero warnings;
  - `dotnet test Memento.sln -c Release --filter "Category!=Hardware"`;
  - `cd ui && npm ci && npm run lint && npm test && npm run build`.
- [ ] The CI workflow is green on `main` for that commit.
- [ ] `build/pack.ps1` succeeds locally and `build/out` holds `MementoApp-win-Setup.exe`.
- [ ] `git grep -nE "<your Windows user name>|Users[\\]|@.*[.]com|sk-[a]nt-" -- . ':!design' ':!docs/PRODUCT-SPEC.md'` shows no personal data, user path or key (CLAUDE.md, "No personal data in the repo"); review what it lists. (The brackets keep this line from matching itself.)

### 4. Tag

- [ ] `git tag -a vX.Y.Z -m "Memento X.Y.Z"` on the `main` commit, then `git push origin vX.Y.Z`. The workflow refuses a tag that does not match `<Version>`.
- [ ] Watch both jobs of the Release workflow (Build, test and pack; Publish the GitHub release) to the end.

### 5. Verify the published assets

- [ ] The release exists under the tag, titled "Memento X.Y.Z", marked **Latest** (not pre-release) for a full release, and its text is the `## X.Y.Z` section of the notes.
- [ ] Assets: `MementoApp-win-Setup.exe`, `MementoApp-win-Portable.zip`, `MementoApp-X.Y.Z-full.nupkg` (and a `-delta.nupkg` from the second release on), `releases.win.json`, `memento-dotnet.cdx.json`, `memento-ui.cdx.json`, `SHA256SUMS.txt`.
- [ ] Download Setup and check it against the list: `Get-FileHash MementoApp-win-Setup.exe -Algorithm SHA256` equals its line in `SHA256SUMS.txt`.
- [ ] `releases.win.json` names version X.Y.Z, and the sizes and SHA-256 of its packages match the assets.

### 6. Install from the release

On a clean Windows 11 account or machine (no earlier Memento, no `%LOCALAPPDATA%\Memento`):

- [ ] Run the downloaded Setup. SmartScreen warns while the installer is unsigned ("More info" → "Run anyway"); the WebView2 Runtime is installed first if it is missing. Memento installs to `%LOCALAPPDATA%\MementoApp` without administrator rights and starts.
- [ ] The first-run Library shows; Settings › General › About shows X.Y.Z; `%LOCALAPPDATA%\Memento\logs` says X.Y.Z.
- [ ] A short recording with the microphone and "Everything this PC plays" stops, saves and opens in Review; a model installs from Settings › Transcription and the recording is transcribed; an export opens in Explorer.
- [ ] Settings › General › Updates › **Check now** answers "Memento X.Y.Z is the newest version."
- [ ] Uninstall from Windows Settings › Apps: the program folder is removed, `%LOCALAPPDATA%\Memento` (recordings, settings, models) is kept.

### 7. Update from the previous version

On a machine with the previous full release installed and a recording in its library:

- [ ] Start it and wait (or **Check now**): the footer shows "Downloading Memento X.Y.Z", then a toast offers **Restart to update**. No check or download starts while recording or processing.
- [ ] **Restart to update**: Memento closes normally, the update installs and Memento comes back as X.Y.Z with the same library, settings, models and keys.
- [ ] Before the first public release this was checked against a local feed (`tools/e2e/h1-update-feed.ps1`, `tools/e2e/h1-update.mjs`); from the second release on it is checked against the real feed.

## If something is wrong

- A failed build or test job publishes nothing: fix on `main`, delete the unpublished tag (`git push origin :vX.Y.Z`) and tag again.
- A published release with a defect is never replaced: publish X.Y.(Z+1). Installed copies update to it.
