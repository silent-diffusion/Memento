import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join, relative, resolve } from 'node:path';
import { describe, expect, it } from 'vitest';

// Token lint (CLAUDE.md: "Use the token names from design/tokens.css verbatim. Never hand-tune a shadow or colour").
// Every stylesheet under ui/src except tokens.css is read rule by rule and fails on:
//   - a raw colour (#hex, rgb(), rgba(), hsl(), hsla()): colours come from the tokens (DESIGN.md §2.1);
//   - a box-shadow that is not `none`, a token (`var(--neo-…)`) or a 2 px-style ring in a token colour
//     (`0 0 0 2px var(--text)`): shadows are applied by role (§2.5);
//   - a border-radius off the scale of §2.3: 12, 14, 16, 20, 24, 28, 999 px, 50 % (dots, knobs), 0 and 1–3 px for
//     the hairline tracks, bars and meters of §5.5, §5.10 and §5.11.
// The few justified exceptions are listed in ALLOWED below, each with the reason. Add to it only with a reason a
// reviewer would accept (and the DESIGN.md section that says so); prefer a token or the shared recipe instead.
// TypeScript is checked for raw colours too (an inline style or an SVG fill), outside the browser-preview host.

const SRC = resolve(__dirname, '..');
const REPO = resolve(SRC, '../..');

interface Allowed {
  file: string;
  /** Matches the rule's selector text. */
  selector: RegExp;
  /** Which checks it is exempt from. */
  kinds: readonly ('colour' | 'shadow' | 'radius')[];
  why: string;
}

const VIDEO_SCHEMATIC = /\.(rec-thumb|rec-preview|rec-shot|rec-corner|rec-frame|rec-tile-thumb|vid|video-strip)/;

const ALLOWED: readonly Allowed[] = [
  { file: 'components/paper/paper.css', selector: /./, kinds: ['colour', 'shadow', 'radius'], why: 'The document paper is white with the style’s own ink in both themes (§2.5, §5.18); it is the document, not UI.' },
  { file: 'components/paper/paper-host.css', selector: /\.bar\.d/, kinds: ['colour', 'radius'], why: 'The paper skeleton’s darker bar, the paper’s own colour (§5.18).' },
  { file: 'screens/record/video.css', selector: VIDEO_SCHEMATIC, kinds: ['colour', 'shadow', 'radius'], why: 'The video schematic and the camera inset are pictures of video, dark in both themes; colours, inset shadow and small radii are the renders’ own (§18.1, §18.2).' },
  { file: 'screens/review/reviewVideo.css', selector: /./, kinds: ['colour', 'shadow', 'radius'], why: 'The Review video well and its overlay controls sit on video, dark in both themes (§18.3, renders/ReviewVideo.dc.html).' },
  { file: 'styles/library.css', selector: /\.video-strip/, kinds: ['colour', 'radius'], why: 'The grid card’s video schematic (§18.7), as the Recording session’s.' },
  { file: 'styles/surfaces.css', selector: /\.scrim/, kinds: ['colour'], why: 'The dialog scrim colours are given in §5.19 and have no token.' },
  { file: 'styles/base.css', selector: /^\.app(\.dark)?$/, kinds: ['colour'], why: 'Token definitions: the measured contrast corrections of text-3 and on-accent (PRODUCT-SPEC Accessibility), to be folded into tokens.css.' },
  { file: 'screens/styleeditor/styleeditor.css', selector: /\.style-ink-dot/, kinds: ['colour'], why: 'The document style’s fixed Ink colour shown for reference (§13), a document colour.' },
  { file: 'styles/review.css', selector: /\.scrubber-knob/, kinds: ['shadow'], why: 'The scrubber knob is the toggle knob of §5.7, whose shadow §5.7 specifies.' },
  { file: 'styles/review.css', selector: /\.(history-open|segm-speaker|people-who-toggle)/, kinds: ['radius'], why: 'Text-like buttons: the radius only shapes the focus ring around the words.' },
  { file: 'screens/docview/docview.css', selector: /\.doc-ver-open/, kinds: ['radius'], why: 'A text-like button: the radius only shapes the focus ring.' },
  { file: 'screens/review/filter.css', selector: /\.person-filter/, kinds: ['radius'], why: 'A text-like button (a speaker’s name): the radius only shapes the focus ring.' },
  { file: 'screens/review/review20.css', selector: /\.sug-accept-all/, kinds: ['radius'], why: 'A text link: the radius only shapes the focus ring.' },
  { file: 'components/editing.css', selector: /\.(segm-text--editable|segm-note-edit)/, kinds: ['radius'], why: 'The in-place transcript field hugs the words it replaces (§9), with no control shape of its own.' },
  { file: 'styles/details-sheet.css', selector: /\.drag-handle/, kinds: ['radius'], why: 'A 22 px drag handle, below the smallest control size of §2.3.' },
  { file: 'screens/builder/builder.css', selector: /\.handle/, kinds: ['radius'], why: 'The module card’s six-dot drag handle (§5.14), below the smallest control size.' },
  { file: 'styles/folders.css', selector: /\.folder-disclosure/, kinds: ['radius'], why: 'The 22 px disclosure chevron of the folder tree (§6.1).' },
];

const RADII = new Set(['0', '0px', '1px', '2px', '3px', '12px', '14px', '16px', '20px', '24px', '28px', '999px', '50%', 'inherit']);
const COLOUR = /#[0-9a-fA-F]{3,8}\b|\b(rgba?|hsla?)\(/;
const TOKEN_SHADOW = /^(none|inherit|(var\(--[a-z0-9-]+\)|0 0 0 [0-9.]+px var\(--[a-z0-9-]+\))(\s*,\s*(var\(--[a-z0-9-]+\)|0 0 0 [0-9.]+px var\(--[a-z0-9-]+\)))*)(\s*!important)?$/;

function files(dir: string, ext: RegExp): string[] {
  return readdirSync(dir).flatMap((name) => {
    const path = join(dir, name);
    return statSync(path).isDirectory() ? files(path, ext) : ext.test(name) ? [path] : [];
  });
}

interface Finding {
  file: string;
  selector: string;
  kind: 'colour' | 'shadow' | 'radius';
  declaration: string;
}

function lintCss(path: string): Finding[] {
  return lintText(relative(SRC, path).replace(/\\/g, '/'), readFileSync(path, 'utf8'));
}

function lintText(file: string, source: string): Finding[] {
  // Comments out: they quote the renders' colours.
  const css = source.replace(/\/\*[\s\S]*?\*\//g, '');
  const findings: Finding[] = [];
  for (const match of css.matchAll(/([^{}]+)\{([^{}]*)\}/g)) {
    const selector = (match[1] ?? '').trim().replace(/\s+/g, ' ');
    if (selector.startsWith('@') || /^(from|to|\d+%)/.test(selector)) {
      continue;
    }
    for (const raw of (match[2] ?? '').split(';')) {
      const declaration = raw.trim();
      const colon = declaration.indexOf(':');
      if (colon < 0) {
        continue;
      }
      const property = declaration.slice(0, colon).trim();
      const value = declaration.slice(colon + 1).trim();
      const kinds: Finding['kind'][] = [];
      if (property === 'box-shadow') {
        if (!TOKEN_SHADOW.test(value)) {
          kinds.push('shadow');
        }
      } else if (COLOUR.test(value)) {
        kinds.push('colour');
      }
      if (property === 'border-radius' && !value.replace(/\s*!important$/, '').split(/\s+/).every((v) => RADII.has(v))) {
        kinds.push('radius');
      }
      for (const kind of kinds) {
        const allowed = ALLOWED.some((a) => a.file === file && a.selector.test(selector) && a.kinds.includes(kind));
        if (!allowed) {
          findings.push({ file, selector, kind, declaration });
        }
      }
    }
  }
  return findings;
}

describe('token lint (DESIGN.md §2, CLAUDE.md code style)', () => {
  it('keeps ui/src/styles/tokens.css a verbatim copy of design/tokens.css', () => {
    const normal = (p: string): string => readFileSync(p, 'utf8').replace(/\r\n/g, '\n');
    expect(normal(join(SRC, 'styles', 'tokens.css'))).toBe(normal(join(REPO, 'design', 'tokens.css')));
  });

  it('uses no raw colour, hand-tuned shadow or off-scale radius outside tokens.css', () => {
    const sheets = files(SRC, /\.css$/).filter((p) => !p.endsWith(join('styles', 'tokens.css')) && !p.endsWith('fonts.css'));
    expect(sheets.length).toBeGreaterThan(20);
    const findings = sheets.flatMap(lintCss);
    expect(findings.map((f) => `${f.file} ${f.selector} { ${f.declaration} } (${f.kind})`)).toEqual([]);
  });

  it('uses no raw colour in the components and screens (inline styles, SVG fills)', () => {
    const sources = files(SRC, /\.tsx?$/).filter((p) => !/\.test\.tsx?$/.test(p) && !/[\\/]bridge[\\/]mock/.test(p) && !/[\\/]testing[\\/]/.test(p));
    const documentColours = 'screens/styleeditor/StyleEditorScreen.tsx'; // The style swatches are document colours (§13).
    const offenders = sources
      .filter((p) => relative(SRC, p).replace(/\\/g, '/') !== documentColours)
      .filter((p) => /["'`](#[0-9a-fA-F]{3,8}|(rgba?|hsla?)\()/.test(readFileSync(p, 'utf8')))
      .map((p) => relative(SRC, p));
    expect(offenders).toEqual([]);
  });

  it('gives a bare `btn ghost` and `btn primary` the full §5.1 recipe, at no weight', () => {
    const base = readFileSync(join(SRC, 'styles', 'base.css'), 'utf8');
    const rule = (selector: string): string => {
      const at = base.indexOf(`${selector} {`);
      expect(at, selector).toBeGreaterThan(-1);
      return base.slice(at, base.indexOf('}', at));
    };
    const ghost = rule('.btn:where(.ghost)');
    expect(ghost).toContain('background: var(--surface)');
    expect(ghost).toContain('box-shadow: var(--neo-sm)');
    expect(ghost).toContain('height: 36px');
    const primary = rule('.btn:where(.primary)');
    expect(primary).toContain('background: var(--accent-grad)');
    expect(primary).toContain('color: var(--on-accent)');
  });

  it('marks on the accent fill take the label colour, never a fixed white', () => {
    const sheets = ['styles/record.css', 'styles/shell.css'].map((f) => readFileSync(join(SRC, f), 'utf8').replace(/\r\n/g, '\n')).join('\n');
    for (const selector of ['.rec-dot', '.rec-start-dot']) {
      // The rule of that selector alone (at the start of a line), not `.rec-resume .rec-start-dot`.
      const at = sheets.indexOf(`\n${selector} {`);
      expect(sheets.slice(at, sheets.indexOf('}', at)), selector).toContain('background: var(--on-accent)');
    }
    expect(readFileSync(join(SRC, 'components', 'icons.tsx'), 'utf8')).not.toMatch(/fill="#/);
  });

  it('flags what it is meant to flag', () => {
    const kinds = (css: string): string[] => lintText('styles/probe.css', css).map((f) => f.kind);
    expect(kinds('.a { color: #fff; }')).toEqual(['colour']);
    expect(kinds('.a { background: rgba(0, 0, 0, 0.5); }')).toEqual(['colour']);
    expect(kinds('.a { box-shadow: 0 4px 8px rgba(0, 0, 0, 0.4); }')).toEqual(['shadow']);
    expect(kinds('.a { box-shadow: var(--neo-sm), 0 0 0 2px var(--text); }')).toEqual([]);
    expect(kinds('.a { border-radius: 9px; }')).toEqual(['radius']);
    expect(kinds('.a { border-radius: 14px; background: var(--surface); }')).toEqual([]);
    expect(kinds('/* #2A2824 in a comment */ .a { color: var(--text); }')).toEqual([]);
  });
});
