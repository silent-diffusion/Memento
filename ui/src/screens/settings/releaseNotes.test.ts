import { describe, expect, it } from 'vitest';
import { parseReleaseNotes, plainNotesText } from './releaseNotes';

describe('Release notes of an available update (2.1.0)', () => {
  it('keeps headings, bullet lists and paragraphs and strips inline marks to their words', () => {
    const blocks = parseReleaseNotes(
      [
        '## What’s new in 2.1.0',
        '',
        '- **Check now** only checks; `Update now` downloads.',
        '* See [the guide](https://example.org/guide) for _details_.',
        '  continued on the next line',
        '',
        'Your recordings are __not__ affected.',
        'Second line of the same paragraph.',
        '',
        '1. First',
        '2) Second',
        '---',
        '> Quoted line',
      ].join('\n'),
    );

    expect(blocks).toEqual([
      { kind: 'heading', text: 'What’s new in 2.1.0' },
      { kind: 'list', items: ['Check now only checks; Update now downloads.', 'See the guide for details. continued on the next line'] },
      { kind: 'paragraph', text: 'Your recordings are not affected. Second line of the same paragraph.' },
      { kind: 'list', items: ['First', 'Second'] },
      { kind: 'paragraph', text: 'Quoted line' },
    ]);
  });

  it('never keeps HTML or link targets, so nothing from the feed is injected or opened', () => {
    const blocks = parseReleaseNotes('<script>alert(1)</script>Fixed <b>export</b> ![logo](https://example.org/x.png)\n- <img src=x onerror=alert(1)>[site](javascript:alert(1))');
    const text = JSON.stringify(blocks);

    expect(blocks).toEqual([
      { kind: 'paragraph', text: 'alert(1)Fixed export logo' },
      { kind: 'list', items: ['site'] },
    ]);
    expect(text).not.toContain('<');
    expect(text).not.toContain('example.org');
    expect(text).not.toContain('javascript:');
  });

  it('reads nothing from missing or blank notes and keeps snake_case words', () => {
    expect(parseReleaseNotes(null)).toEqual([]);
    expect(parseReleaseNotes(undefined)).toEqual([]);
    expect(parseReleaseNotes('\n\n  \n')).toEqual([]);
    expect(plainNotesText('the update_feed switch, *quiet* mic and 2 * 3')).toBe('the update_feed switch, quiet mic and 2 * 3');
  });
});
