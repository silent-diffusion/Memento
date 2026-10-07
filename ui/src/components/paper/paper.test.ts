import { existsSync, readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { describe, expect, it } from 'vitest';
import viewerFixture from '../../bridge/fixtures/meeting-minutes.corporate.viewer.html?raw';
import { chipText, createChip, importPaper, markupProblems, parseChipText, serializePaper, timestampOf } from './paperDom';

const REPO = resolve(__dirname, '../../../..');

describe('the document paper (DESIGN.md §5.18, the viewer markup contract)', () => {
  it('bundles PaperCss.Stylesheet from the document engine verbatim', () => {
    const cs = resolve(REPO, 'src/Memento.Documents/Render/PaperCss.cs');
    if (!existsSync(cs)) {
      return;
    }
    const source = readFileSync(cs, 'utf8').replace(/\r\n/g, '\n');
    const start = source.indexOf('public const string Stylesheet = """') + 'public const string Stylesheet = """'.length;
    const engine = source.slice(start, source.indexOf('""";', start)).trim();
    const bundled = readFileSync(resolve(__dirname, 'paper.css'), 'utf8').replace(/\r\n/g, '\n').replace(/^\/\*[\s\S]*?\*\/\n/, '').trim();
    expect(bundled).toBe(engine);
  });

  it('keeps the preview host’s fixture copies identical to the engine’s snapshots', () => {
    for (const name of ['meeting-minutes.corporate.viewer.html', 'style-sample.corporate.html']) {
      const original = resolve(REPO, 'tests/Memento.Documents.Tests/fixtures/expected', name);
      if (existsSync(original)) {
        expect(readFileSync(resolve(__dirname, '../../bridge/fixtures', name), 'utf8'), name).toBe(readFileSync(original, 'utf8'));
      }
    }
  });

  it('imports the host’s page as the article only, dropping what the contract does not allow', () => {
    const article = importPaper(viewerFixture);
    expect(article?.matches('article.paper.paper-viewer')).toBe(true);
    expect(article?.getAttribute('data-doc')).toBe('doc-20261005-minutes');
    expect(article === null ? [] : markupProblems(article)).toEqual([]);
    expect(article?.querySelectorAll('section.paper-module')).toHaveLength(10);
    expect(article?.querySelectorAll('a.ts').length).toBeGreaterThan(5);
    const hostile = importPaper(
      '<article class="paper"><div class="paper-row" data-cols="1"><section class="paper-module" data-id="m1"><h2 class="paper-h">A</h2>' +
        '<p onclick="steal()">Text<script>alert(1)</script><img src="x"><a class="ts" href="https://example.invalid" data-t="5">0:05</a><custom-el>kept text</custom-el></p></section></div></article>',
    );
    const html = hostile?.outerHTML ?? '';
    expect(html).not.toContain('onclick');
    expect(html).not.toContain('<script');
    expect(html).not.toContain('<img');
    expect(html).not.toContain('example.invalid');
    expect(html).toContain('kept text');
    expect(importPaper('<p>No paper</p>')).toBeNull();
  });

  it('keeps only the renderer’s inline style properties (security audit SA-05)', () => {
    const article = importPaper(
      '<article class="paper" style="--paper-base:14px;--paper-head:#1F3A5F;position:fixed;inset:0;z-index:99;background-image:url(https://example.invalid/x.png)">' +
        '<div class="paper-row" data-cols="1"><section class="paper-module" data-id="m1"><h2 class="paper-h">A</h2>' +
        '<table class="paper-table"><colgroup><col style="width:40%;background:url(//example.invalid/y)"></colgroup></table>' +
        '<p style="--paper-base:url(https://example.invalid/z)">x</p><a class="ts" href="javascript:alert(1)" data-t="1">0:01</a>' +
        '<a class="ts" href="data:text/html,x" data-t="2">0:02</a></section></div></article>',
    );
    if (article === null) {
      throw new Error('no article');
    }
    expect(article.style.getPropertyValue('--paper-base')).toBe('14px');
    expect(article.style.getPropertyValue('position')).toBe('');
    expect(article.style.getPropertyValue('z-index')).toBe('');
    const html = serializePaper(article);
    expect(html).not.toContain('example.invalid');
    expect(html).not.toContain('javascript:');
    expect(html).not.toContain('data:text');
    expect(article.querySelector('col')?.style.getPropertyValue('width')).toBe('40%');
  });

  it('serialises what saveEdit receives without the viewer’s editing attributes', () => {
    const article = importPaper(viewerFixture);
    if (article === null) {
      throw new Error('no article');
    }
    article.setAttribute('contenteditable', 'true');
    article.setAttribute('role', 'textbox');
    article.setAttribute('aria-label', 'Meeting minutes');
    const saved = serializePaper(article);
    expect(saved.startsWith('<article class="paper paper-viewer caps title-rule th-fill"')).toBe(true);
    expect(saved).not.toContain('contenteditable="true"');
    expect(saved).not.toContain('role="textbox"');
    expect(saved).toContain('contenteditable="false"');
  });

  it('reads and writes timestamp chips exactly as the renderer does', () => {
    expect(chipText(1122)).toBe('18:42');
    expect(chipText(3725.25)).toBe('1:02:05');
    expect(parseChipText('18:42')).toBe(1122);
    expect(parseChipText('1:02:05')).toBe(3725);
    expect(parseChipText('75')).toBe(75);
    expect(parseChipText('18:75')).toBeNull();
    expect(parseChipText('soon')).toBeNull();
    const chip = createChip(65.5);
    expect(chip.outerHTML).toBe('<a class="ts" href="#t=65.5" data-t="65.5" contenteditable="false">1:05</a>');
    expect(timestampOf(chip)).toBe(65.5);
    expect(timestampOf(document.createElement('p'))).toBeNull();
  });
});
