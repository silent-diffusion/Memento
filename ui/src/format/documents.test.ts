import { describe, expect, it } from 'vitest';
import { documentWord } from './documents';

describe('the document word', () => {
  it('is the last word of the template name', () => {
    expect(documentWord('Meeting minutes')).toBe('minutes');
    expect(documentWord('Interview notes')).toBe('notes');
  });

  it('skips a copy marker and numbers', () => {
    expect(documentWord('Meeting minutes (copy)')).toBe('minutes');
    expect(documentWord('Interview notes (copy 2)')).toBe('notes');
    expect(documentWord('Weekly sync 2')).toBe('sync');
  });

  it('falls back to "document" when no word is left', () => {
    expect(documentWord('')).toBe('document');
    expect(documentWord('(copy)')).toBe('document');
    expect(documentWord('2026')).toBe('document');
  });
});
