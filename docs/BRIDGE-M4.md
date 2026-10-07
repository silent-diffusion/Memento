# M4 — AI, documents, templates, styles (contract; to be folded into BRIDGE.md when M4 lands)

Additive to BRIDGE.md. The document model, module catalog, templates, styles and exporters live in `src/Memento.Documents` (M4b); providers and the payload composer in `src/Memento.AI` (M4a).

## Shared types (M4)

```ts
type ModuleId = 'title' | 'summary' | 'executiveSummary' | 'participants' | 'agenda' | 'topic' | 'discussion' | 'decisions' | 'actionItems' | 'owner' | 'deadline' | 'openQuestions' | 'quote' | 'highlight' | 'chapter' | 'timeline' | 'followUpEmail' | 'nextMeeting' | 'meetingPurpose' | 'notes' | 'fullTranscript' | 'customText' | 'customAi';
type ContentShape = 'paragraph' | 'list' | 'table' | 'chips' | 'labelValue' | 'quote' | 'timeline' | 'transcript' | 'text';
interface ModuleInfo { id: ModuleId; name: string; group: 'structure' | 'detail' | 'custom'; shape: ContentShape; generated: boolean; defaultLength: 'short' | 'medium' | 'long'; groundingRule: string | null; description: string }
type TextSize = 'smaller' | 'normal' | 'larger';
interface ModuleSettings { id: string; module: ModuleId; instructions: string; length: 'short' | 'medium' | 'long'; textSize: TextSize; linkToTranscript: boolean; customTitle: string | null; customText: string | null }
interface TemplateRow { modules: ModuleSettings[] }                              // 1–3
interface InputSelection { transcript: boolean; details: boolean; participants: boolean; agenda: boolean; highlights: boolean; attachments: boolean; previousDocuments: boolean }   // audio/video never exist here
interface Template { id: string; name: string; builtIn: boolean; recordingTypes: RecordingType[]; rows: TemplateRow[]; inputs: InputSelection; providerId: ProviderId | null; styleId: string; output: { alsoExportDocx: boolean; alsoExportMarkdown: boolean }; modifiedAt: string }
type ProviderId = 'anthropic' | 'openai' | 'local';
interface ProviderInfo { id: ProviderId; name: string; vendor: string; kind: 'cloud' | 'local'; ready: boolean; reason: string | null /* "No key saved", "Model not installed", "External AI is off" */; modelLabel: string | null }
interface StyleSettings { headingFace: 'sans' | 'serif'; bodyFace: 'sans' | 'serif'; baseSize: 'small' | 'normal' | 'large'; headingCase: 'normal' | 'smallCaps'; numberedHeadings: boolean; headingColor: 'navy' | 'ink' | 'forest' | 'burgundy'; tableHeaderFill: boolean; ruleUnderTitle: boolean; linesBetweenSections: boolean; spacing: 'tight' | 'normal' | 'airy'; paper: 'letter' | 'a4'; pageNumbers: boolean; runningHeader: boolean }
interface Style { id: string; name: string; builtIn: boolean; settings: StyleSettings; usedByTemplates: number; modifiedAt: string }
interface DocumentSummary { id: string; name: string; kind: 'generated' | 'written'; templateName: string | null; styleId: string; providerId: ProviderId | null; generatedAt: string | null; version: number; versions: number; modifiedAt: string; sizeBytes: number }
interface GenerationRecord { templateId: string; templateName: string; styleId: string; providerId: ProviderId; modelLabel: string; startedAt: string; durationMs: number; inputs: InputSelection; payloadHash: string; payloadKept: boolean; chunks: number; modules: { moduleId: string; claims: number; verified: number; dropped: number; notDiscussed: boolean }[] }
interface DocumentContent { schemaVersion: 1; id: string; title: string; meta: string; rows: { blocks: Block[] }[]; record: GenerationRecord | null }   // Block per src/Memento.Documents/Model; the UI renders through `documents.renderHtml`
interface GenerationProgress { jobId: string; recordingId: string; documentId: string | null; stage: 'composing' | 'generating' | 'verifying' | 'rendering' | 'done' | 'failed' | 'cancelled'; moduleId: string | null; percent: number; message: string | null }
```

## Methods (M4)

| Method | Params | Result | Notes |
|---|---|---|---|
| `modules.list` | `{}` | `{ modules: ModuleInfo[] }` | The catalog; the palette reads it. |
| `templates.list` / `templates.get` | `{}` / `{ templateId }` | `{ templates: Template[] }` / `Template` | Built-ins first. |
| `templates.save` | `{ template: Template }` | `Template` | New id when `id` is empty. Built-ins cannot be saved over; saving one creates a copy. |
| `templates.duplicate` / `templates.delete` / `templates.resetBuiltIn` | `{ templateId }` | `Template` / `{}` / `Template` | Delete refused for built-ins (`templates.builtIn`). |
| `styles.list` / `styles.get` / `styles.save` / `styles.duplicate` / `styles.delete` / `styles.resetBuiltIn` | likewise | likewise | |
| `styles.sampleHtml` | `{ settings: StyleSettings }` | `{ html }` | The Style editor's live sample page (fixed sample minutes). |
| `providers.list` | `{}` | `{ providers: ProviderInfo[], externalAiEnabled: boolean }` | Readiness from keys, model install state, VRAM. |
| `generation.preview` | `{ recordingId, template: Template }` | `{ payloadText: string, bytes: number, chunks: number, inputsUsed: InputSelection, warnings: string[] }` | "Preview exactly what will be sent" (for Local: "stays on this PC"). Nothing is sent. |
| `generation.previewHtml` | `{ recordingId, template: Template, styleId }` | `{ html }` | The Builder's live preview paper with skeletons, from the catalog shapes. |
| `generation.start` | `{ recordingId, template: Template, documentId?: string /* regenerate into */ }` | `{ jobId }` | Refused with `ai.disabled`, `ai.providerNotReady` (detail), `generation.noTranscript`, `generation.busy`. Honours "ask before every send" by returning `{ jobId, confirmationRequired: true, summary }` first; the UI calls `generation.confirm { jobId, approved }`. |
| `generation.confirm` / `generation.cancel` | `{ jobId, approved }` / `{ jobId }` | `{}` | |
| `documents.list` | `{ recordingId }` | `{ documents: DocumentSummary[] }` | |
| `documents.get` | `{ recordingId, documentId }` | `{ document: DocumentContent, summary: DocumentSummary }` | |
| `documents.renderHtml` | `{ recordingId, documentId, mode: 'view' \| 'print' }` | `{ html }` | The viewer paper and the print HTML. |
| `documents.create` | `{ recordingId, name, styleId }` | `DocumentSummary` | A hand-written document with one empty text block. |
| `documents.saveEdit` | `{ recordingId, documentId, html }` | `{ document: DocumentContent, version }` | The viewer's light edits (host parses HTML back into blocks; refuses unknown markup with `documents.unsupportedEdit`). Debounced by the UI. |
| `documents.rename` / `documents.duplicate` / `documents.delete` | `{ recordingId, documentId, name? }` | | Delete asks nothing on the host; the UI confirms. |
| `documents.makeTemplate` | `{ recordingId, documentId, name }` | `Template` | From the document's generation record. |
| `documents.versions` / `documents.restoreVersion` | `{ recordingId, documentId }` / `{ recordingId, documentId, versionId }` | `{ versions: { id, at, reason: 'generated' \| 'edited' \| 'restored' \| 'regenerated', changes: number }[] }` / `{ document }` | When history is on. |
| `documents.export` | `{ recordingId, documentId, format: 'docx' \| 'pdf' \| 'markdown', path?: string }` | `{ path, bytes, sha256 }` | Single-document export (the Export dialog's Documents row uses `export.run` with `documents` filled). PDF is produced by the host through WebView2 print. |

## Events (M4)

| Event | Payload |
|---|---|
| `generation.progress` | `GenerationProgress` |
| `documents.changed` | `{ recordingId, documentId, reason: 'generated' \| 'edited' \| 'created' \| 'deleted' \| 'restored' }` |
| `templates.changed` / `styles.changed` | `{}` |

## Settings (M4)

`ai.enabled`, `ai.askBeforeSend`, `ai.keepRecord`, `ai.share` already exist (M3). Add `ai.defaultProviderId: ProviderId | null`, `ai.localModelId: string` (catalog id, default by hardware), `documents.defaultTemplateId`, `documents.defaultStyleId`.

## Error codes (M4)

`ai.disabled`, `ai.providerNotReady`, `ai.noKey`, `ai.invalidKey`, `ai.rateLimited`, `ai.network`, `ai.providerError`, `ai.contentTooLong`, `ai.modelNotInstalled`, `ai.notEnoughVram`, `ai.workerCrashed`, `generation.noTranscript`, `generation.busy`, `generation.notFound`, `templates.notFound`, `templates.builtIn`, `styles.notFound`, `styles.builtIn`, `styles.inUse`, `documents.notFound`, `documents.unsupportedEdit`, `documents.versionNotFound`.

## Design references

Builder: DESIGN §10 and `Builder.dc.html` (palette groups, structure rows, drag and drop, module card with instructions/length/**text size**/link toggle, preview with skeletons, inputs and output tab, provider cards, style segmented control, the notice "Nothing leaves this PC until you press Generate"). Viewer: §12 and `DocView.dc.html`. Style editor: §13 and `StyleEditor.dc.html`. Error states: §17 (AI provider failed card). The "ask before every send" confirmation is a dialog (§5.19) showing the provider, the inputs, the size and the chunk count.
