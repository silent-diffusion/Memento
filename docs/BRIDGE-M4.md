# M4 — AI, documents, templates, styles (folded into BRIDGE.md)

The M4 contract now lives in [BRIDGE.md](BRIDGE.md), in the sections that end it: "Shared types (M4)", "Methods (M4)",
"Events (M4)", "Settings snapshot (M4 additions)", "Error codes (M4)", "Design references (M4)" and "Decisions (M4, at
the M4d integration)". That is the one source; this file only points there so older links keep working.

What changed from the first draft of this file, decided when the host (M4d) and the UI (M4c) met:

- `DocumentContent.rows` are rows of modules `{ id, module, title, textSize, linkToTranscript, blocks }`.
- Paper `html` results are the `article.paper` element only; the UI bundles `PaperCss.Stylesheet` itself.
- `generation.previewHtml` accepts `recordingId: null`.
- `GenerationStartResult.summary` is `{ providerId, providerName, modelLabel, inputsUsed, bytes, chunks }`.
- `documents.versions` entries carry `version`; `ExportEstimateItem` gains `documentId`; an empty `documentIds` means all.
- `Template` gains `documentKind`; `documents.rename` / `duplicate` return `DocumentSummary`, `delete` returns `{}`.
- Local model ids are `qwen3.5-4b-q4` and `ministral-3-3b-q4`.
- The Local provider runs whenever its model is installed; "Allow external AI services" governs cloud providers only.
- A template's "Also export" toggles write into the project's `documents/exports/` and are listed in History.
- Added by the host: `documents.exportFailed`, `ProviderInfo.code` / `detail` / `modelId`, the record's `sent`, `bytes`,
  `stayedOnPc`, `claims` and `payloadText`, and the Settings model choices per cloud provider.
