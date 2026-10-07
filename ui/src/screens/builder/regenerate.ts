// Regenerating a document starts from the layout the document has, not from the template as saved: a module moved
// beside another, a text size or a transcript link changed in the Builder before the first generation is kept. The
// template still supplies each module's instructions and length (the document does not store them).
import type { DocumentContent, ModuleId, ModuleSettings, Template, TemplateRow } from '../../bridge/types';

export function layoutOfDocument(template: Template, document: Pick<DocumentContent, 'rows'>): TemplateRow[] {
  const byId = new Map(template.rows.flatMap((r) => r.modules).map((m) => [m.id, m]));
  const rows = document.rows
    .map((row) => ({
      modules: row.modules.map((m): ModuleSettings => {
        const base = byId.get(m.id);
        return {
          id: m.id,
          module: base?.module ?? (m.module as ModuleId),
          instructions: base?.instructions ?? '',
          length: base?.length ?? 'medium',
          textSize: m.textSize,
          linkToTranscript: m.linkToTranscript,
          customTitle: base?.customTitle ?? null,
          customText: base?.customText ?? null,
        };
      }),
    }))
    .filter((row) => row.modules.length > 0);
  return rows.length > 0 ? rows : template.rows;
}
