# Memento — Security

Memento records meetings, transcribes them and writes documents from them, all on the user's PC. Recordings can be privileged (legal, medical, HR, board meetings), so this document says what Memento protects, against whom, what it promises, what it does not try to defend against, and how to report a problem. The latest audit and its findings are in [`audits/`](audits/) (first audit: [`SECURITY-AUDIT-2026-10-07.md`](audits/SECURITY-AUDIT-2026-10-07.md)). The audit is repeated before every major version (ROADMAP.md, "Security audit").

## 1. What is protected (assets)

| Asset | Where it lives | Why it matters |
|---|---|---|
| Recordings (tracks, mix) | `<library>\projects\<id>\tracks\`, `mix.*` | The most sensitive data; must never leave the PC. |
| Transcripts, annotations, agendas, attachments | the same project folder | Full text of what was said, names of people. |
| Known voices (2.0, opt-in) | `<library>\voices\known.json` | Voice signatures (192 numbers from the voice model per confirmation, at most 10 per name, never audio) under the names the user confirmed, and which recordings confirmed or declined them. A signature can tell whether a later recording contains that person, so it is personal data: it is written only while Settings › Speakers › "Remember speakers by voice" is on (off by default) and only when the user names a speaker in Review, never leaves the PC, is not exported, moves with the library, and is deleted by Forget / Forget all in Settings (and a recording's confirmations with the recording). Plain JSON like the transcripts, not encrypted at rest. |
| Documents and generation records | `<library>\projects\<id>\documents\` | Summaries, decisions, action items; the record may keep the exact payload sent to an AI provider. |
| AI provider keys | `%LOCALAPPDATA%\Memento\secrets.bin` (DPAPI, current user) | Billing and account access at the provider. |
| Settings, logs, crash reports | `%LOCALAPPDATA%\Memento\` | Must never contain content or keys. |
| The installed app and its models | `%LOCALAPPDATA%\MementoApp\`, `%LOCALAPPDATA%\Memento\models\` | Code and model files the app loads and runs. |

## 2. Who might attack (attackers considered)

1. **A hostile file the user imports**: an agenda (DOCX, XLSX, PDF, image, CSV, Markdown, text), a media file, an attachment, or a whole project folder copied into the library. It may try to crash or hang the app (zip bombs, decompression bombs, deep nesting, ReDoS), escape a folder (path traversal, junctions, reserved names), run code (macros, scripts, shortcuts), or reach the network (UNC paths that leak the user's NTLM hash).
2. **Hostile text aimed at the AI prompts**: transcript speech, agenda items, participant names or document edits that try to change the model's instructions, forge citations, smuggle HTML or links into a document, or extract a key.
3. **A compromised model host or update server**: a model file or update package that was replaced, a redirect to another host, a truncated or spliced download.
4. **Script running inside the interface**: if anything ever managed to run script in the WebView2 page, it could call any bridge method. The bridge therefore never trusts the page with file paths, URLs or ids beyond what each method needs.
5. **Another local user on the same PC**: no access to the user's profile, but possibly write access to a library moved to a shared drive.
6. **A malicious build dependency**: an npm or NuGet package that runs during the build or in CI.

## 3. What Memento promises

- **Nothing leaves the PC without an explicit action.** With AI off, Memento makes no network connection except: a model download the user starts in Settings, opening a link the user clicks, and the update check (Settings › General › "Install updates automatically", on by default; "Check now" when off). Verified by the network tests and the OS-level check described in the audit.
- **Audio and video never leave the PC**, not even to an AI provider. The payload composer refuses media.
- **External AI is off by default.** When the user turns it on, only the inputs ticked for that document are sent, only to the provider chosen, and the user can preview exactly what will be sent.
- **Keys are encrypted** with Windows DPAPI (current-user scope) and are never written to settings, logs, exports, crash reports, project folders or bridge responses (the interface only learns that a key is saved).
- **Logs contain no content**: no transcript, document or agenda text, no participant names, no keys. Logs contain file paths, ids, counts, codes and timings. File paths can contain a recording's title when the user exports it (the export folder is named after it).
- **Downloads are verified**: every model file is checked against the SHA-256 in the catalog built into the app before it is used; a partial file is never loaded.
- **User data is only deleted through the designed flows** (Delete recording, remove attachment, remove model, forget a known voice, the storage options the user chose, library move after verification), never outside the folders they own.

## 4. How it is built to keep those promises

- **WebView2 page**: served from the app's own files at `https://app.memento/` with a strict Content-Security-Policy (`default-src 'none'`, scripts and styles only from the app, no inline script, no remote content of any kind). Navigation, frames, new windows and downloads are refused; host objects, browser permissions, dev tools (in Release), autofill, SmartScreen and crash-dump upload are off. Messages are accepted only from the app's origin, at most 1 MiB and 32 levels deep. Document HTML is rebuilt element by element from an allow-list before it is shown.
- **Bridge**: every method validates its parameters; ids are checked against strict patterns before they become paths; the page cannot name a file on disk (files come from the host's own picker or a drop the host recorded); `app.openExternal` opens only `https:` links and Windows settings pages.
- **Project folders are untrusted input**: every file name in `project.json` must stay inside its folder; junctions and symbolic links are never followed when walking, deleting or moving the library.
- **Parsers** run with size, depth, entry-count, pixel and time limits, and turn every failure into a specific, documented error.
- **AI**: untrusted text is placed in neutralised sections and the prompts say that section content is data, never instructions; every claim must cite a real transcript line, quotes must be the transcript's words, owners must be real participants, and a verifier and a deterministic validator gate everything. Model output is only ever text in the document model; it never reaches a file path, a shell, a URL or raw HTML.
- **Native code** (whisper.cpp, llama.cpp, sherpa-onnx) runs in a separate worker process in a Windows job object, so a crash cannot take the recording down.
- **Supply chain**: pinned package versions, `package-lock.json`, NuGet restore from nuget.org only, Dependabot, a CycloneDX SBOM and SHA-256 sums attached to every release, release publishing in a separate job with the only write token.

## 5. Out of scope

- **A compromised Windows installation, an administrator or malware running as the user.** Anything running as the user can read the user's files and decrypt DPAPI data; Memento cannot defend against it. DPAPI protects keys from other users and from copies of the file taken off the PC.
- **A tampered Memento binary.** The model catalog and the update logic are part of the app; if the app itself is replaced, nothing it checks can be trusted.
- **Physical access to an unlocked PC.**
- **The AI provider's own handling of data** the user chose to send (see the provider's terms).
- **The WebView2 runtime's own background traffic** to Microsoft (runtime updates and configuration; see the audit, finding SA-16). It carries no recording, transcript or document data, because the page never loads remote content.

## 6. Unsigned installers

Until code signing is set up (ROADMAP.md, M5), the installer and updates are **not Authenticode-signed**. Windows SmartScreen will warn on first run, and a user cannot tell a genuine `Setup.exe` from a modified one by its signature. Updates are only as trustworthy as the project's GitHub releases: Velopack checks each update package against the SHA-256 in the release feed, but the feed comes from the same release. To check a download by hand, compare its SHA-256 with `SHA256SUMS.txt` on the release page (`Get-FileHash MementoApp-win-Setup.exe`) and download only from the project's GitHub releases.

## 7. Reporting a vulnerability

Please report security problems privately through **GitHub Security Advisories**: on the repository, open *Security* › *Advisories* › *Report a vulnerability*. Do not open a public issue for a vulnerability and do not include real recordings or personal data in a report; a synthetic file that reproduces the problem is ideal. There is no security e-mail address.

We aim to acknowledge a report within a week, agree on a fix and a disclosure date with the reporter, and credit the reporter in the release notes unless they prefer otherwise.
