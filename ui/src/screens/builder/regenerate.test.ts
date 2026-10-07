import { describe, expect, it } from 'vitest';
import { builtInTemplates } from '../../bridge/mockTemplates';
import type { DocumentContent, Template } from '../../bridge/types';
import { layoutOfDocument } from './regenerate';

function minutes(): Template {
  const found = builtInTemplates().find((t) => t.id === 'meeting-minutes');
  if (found === undefined) throw new Error('The mock has no Meeting minutes template.');
  return found;
}

const module = (id: string, kind: string, textSize: 'smaller' | 'normal' | 'larger' = 'normal') => ({ id, module: kind, title: kind, textSize, linkToTranscript: true, blocks: [] });

describe('regenerating starts from the document’s layout', () => {
  it('keeps the rows, text sizes and links the document has, with the template’s instructions', () => {
    const template = minutes();
    const document: Pick<DocumentContent, 'rows'> = {
      rows: [{ modules: [module('m01', 'executiveSummary')] }, { modules: [module('m04', 'agenda'), module('m05', 'discussion', 'larger')] }],
    };

    const rows = layoutOfDocument(template, document);

    expect(rows.map((r) => r.modules.map((m) => m.id))).toEqual([['m01'], ['m04', 'm05']]);
    expect(rows[1]?.modules[1]?.textSize).toBe('larger');
    expect(rows[1]?.modules[1]?.instructions).toBe(template.rows.flatMap((r) => r.modules).find((m) => m.id === 'm05')?.instructions);
  });

  it('falls back to the template when the document has no modules', () => {
    const template = minutes();

    expect(layoutOfDocument(template, { rows: [] })).toBe(template.rows);
  });
});
