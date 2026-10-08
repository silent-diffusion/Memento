// Shared check for the Audio and Video rows of the AI checklists (Builder › Inputs and output and
// Settings › AI and privacy): greyed, locked, "never sent", and not a control.
import { expect } from 'vitest';

/** The text of the elements an aria-labelledby / aria-describedby list points at. */
export function textOfIds(ids: string | null): string {
  return (ids ?? '')
    .split(/\s+/)
    .filter((id) => id !== '')
    .map((id) => document.getElementById(id)?.textContent.trim() ?? '')
    .join(' ');
}

export function expectNeverSentRows(rows: readonly Element[]): void {
  expect(rows.map((row) => row.getAttribute('data-never-sent'))).toEqual(['audio', 'video']);
  for (const row of rows) {
    const name = row.getAttribute('data-never-sent') === 'audio' ? 'Audio' : 'Video';
    expect(row.classList.contains('never-sent')).toBe(true);
    // No checkbox, nothing focusable, not a label for anything.
    expect(row.tagName).toBe('DIV');
    expect(row.querySelector('input, button, [tabindex]')).toBeNull();
    expect(row.hasAttribute('tabindex')).toBe(false);
    expect(row.querySelector('svg')).not.toBeNull();
    // Read as a disabled item named "Audio never sent", with the reason as its description.
    expect(row.getAttribute('aria-disabled')).toBe('true');
    expect(textOfIds(row.getAttribute('aria-labelledby'))).toBe(`${name} never sent`);
    expect(textOfIds(row.getAttribute('aria-describedby'))).toBe(`${name} stays on this PC and is never sent to any AI service, so it cannot be ticked.`);
  }
}
