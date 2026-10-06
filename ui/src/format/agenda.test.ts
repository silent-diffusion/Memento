import { describe, expect, it } from 'vitest';
import type { AgendaItem } from '../bridge/types';
import { agendaMarks, agendaSourceCaption, moveItem, newAgendaItem, splitPastedAgenda } from './agenda';

describe('pasted agenda splitting', () => {
  it('takes one item per line and strips numbering', () => {
    expect(splitPastedAgenda('1. Q2 recap\n2) Hiring plan\n(3) Launch date\n4: Budget asks\n10.Open questions')).toEqual([
      'Q2 recap',
      'Hiring plan',
      'Launch date',
      'Budget asks',
      'Open questions',
    ]);
  });

  it('strips bullets, letters, roman numerals, headings and checkboxes', () => {
    expect(
      splitPastedAgenda('- Q2 recap\n* Hiring plan\n• Launch date\n– Budget asks\na. Intro\nB) Wrap-up\niv. Review\n## Notes\n- [ ] Follow up\n[x] Done item\n-Tight bullet'),
    ).toEqual(['Q2 recap', 'Hiring plan', 'Launch date', 'Budget asks', 'Intro', 'Wrap-up', 'Review', 'Notes', 'Follow up', 'Done item', 'Tight bullet']);
  });

  it('handles nested numbering', () => {
    expect(splitPastedAgenda('1.1 Scope\n1.2. Owners\n2.10 Risks')).toEqual(['Scope', 'Owners', 'Risks']);
  });

  it('drops blank lines and lines that are only a marker', () => {
    expect(splitPastedAgenda('\n\n  Q2 recap  \n\t\n-\n3.\n\nHiring plan\n   ')).toEqual(['Q2 recap', 'Hiring plan']);
  });

  it('accepts Windows and old Mac line endings', () => {
    expect(splitPastedAgenda('1. Q2 recap\r\n2. Hiring plan\r\n\r\n3. Launch date\r4. Budget')).toEqual([
      'Q2 recap',
      'Hiring plan',
      'Launch date',
      'Budget',
    ]);
  });

  it('keeps numbers that are content', () => {
    expect(splitPastedAgenda('10:30 Coffee\n10 minute break\n-5% churn review\n2026 roadmap')).toEqual([
      '10:30 Coffee',
      '10 minute break',
      '-5% churn review',
      '2026 roadmap',
    ]);
  });

  it('marks nothing uncertain', () => {
    const items = splitPastedAgenda('1. A\n2. B').map(newAgendaItem);
    expect(items.every((i) => !i.uncertain && !i.covered && i.uncertainReason === null)).toBe(true);
    expect(new Set(items.map((i) => i.id)).size).toBe(2);
  });
});

const item = (text: string, covered = false): AgendaItem => ({ id: text, text, covered, uncertain: false, uncertainReason: null });

describe('agenda marks and order', () => {
  it('marks covered, then the first open item current, the rest upcoming', () => {
    expect(agendaMarks([item('a', true), item('b', true), item('c'), item('d'), item('e')])).toEqual([
      'covered',
      'covered',
      'current',
      'upcoming',
      'upcoming',
    ]);
    expect(agendaMarks([item('a'), item('b', true), item('c')])).toEqual(['current', 'covered', 'upcoming']);
    expect(agendaMarks([])).toEqual([]);
  });

  it('moves items', () => {
    expect(moveItem(['a', 'b', 'c', 'd'], 0, 2)).toEqual(['b', 'c', 'a', 'd']);
    expect(moveItem(['a', 'b', 'c'], 2, 0)).toEqual(['c', 'a', 'b']);
    expect(moveItem(['a', 'b', 'c'], 1, 9)).toEqual(['a', 'c', 'b']);
    expect(moveItem(['a', 'b'], 5, 0)).toEqual(['a', 'b']);
  });

  it('names where the agenda came from', () => {
    expect(agendaSourceCaption(null, true)).toBe('Pasted text · parsed on this PC');
    expect(agendaSourceCaption('agenda.docx', true)).toBe('From agenda.docx · parsed on this PC');
  });
});
