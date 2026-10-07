# Memento — conventions for builders

Read in this order before changing anything: `docs/ARCHITECTURE.md` (how it is built), `docs/PRODUCT-SPEC.md` (what it must do), `design/DESIGN.md` + `design/tokens.css` (how it looks), `docs/ROADMAP.md` (what is in scope now).

## Hard rules

- **No personal data in the repo.** No real names, email addresses, user-specific paths (`C:\Users\<name>`), API keys, tokens, or recordings. Test fixtures are synthetic. Check `git diff` for these before every commit.
- **Nothing leaves the PC** without an explicit user action, and audio/video never leave at all. External AI is off by default.
- **Recording is sacred.** No code path may block or slow the capture threads. Finalized tracks are read-only. User data is deleted only through the designed Delete flow.
- **Atomic writes, versioned schemas.** Write `.tmp` then move. Every JSON file has `schemaVersion`.
- **Specific errors.** Name the thing, the time or amount, what is safe, then the fix (DESIGN.md §17). Never "Something went wrong".
- **Tests ship with code.** Every bridge method, parser, exporter, stage and validator has unit tests. CI must stay green.
- **Engines are pluggable and chosen in Settings.** New engines register with the model manager catalog; they add no UI outside Settings.

## Code style

- C#: .NET 8, nullable enabled, warnings as errors, `async`/`await` with `CancellationToken` on every I/O method, `ILogger<T>` for logging, records for DTOs, `System.Text.Json` with source generation for all bridge and file schemas. File-scoped namespaces. One type per file.
- TypeScript: strict mode, Preact function components, no `any`, no default exports, CSS via `tokens.css` class names and a small set of component stylesheets. No UI libraries beyond Preact and its signals.
- Use the token names from `design/tokens.css` verbatim. Never hand-tune a shadow or colour; apply the shadow tokens by role (DESIGN.md §2.5).
- Keep host and UI contracts in sync: `src/Memento.Core/Bridge/Contracts/*.cs` ↔ `ui/src/bridge/types.ts`.
- Dependencies: prefer managed, permissively licensed (MIT/Apache/BSD/OFL) packages. Record every new dependency and its license in `docs/THIRD-PARTY.md`. Anything that ships in the installer must be permissive; build-time-only tools may be weak-copyleft (MPL) if nothing from them is bundled. No GPL/AGPL anywhere. Microsoft runtime redistributables (the VC++ runtime DLLs shipped app-locally beside the worker) are accepted under Microsoft's redistribution terms and listed in THIRD-PARTY.md. No network calls at build time other than package restore.

## Repo layout and commands

See `docs/ARCHITECTURE.md` §2. From the repo root:

```
dotnet build Memento.sln
dotnet test Memento.sln
cd ui && npm ci && npm run build && npm test
```

The host serves `ui/dist` through the WebView2 virtual host; run `npm run build` before `dotnet run --project src/Memento.App`.

## Git

- Branch from `main`, small focused commits, imperative subject lines ("Add process loopback capture"), body explains why.
- Do not commit `ui/dist`, `bin/`, `obj/`, `node_modules/`, models, logs or local libraries (see `.gitignore`).
- Releases are tags `vX.Y.Z` on `main`; CI builds the installer.
