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

// One ghost recipe (DESIGN.md §2.3, §5.1; 2.4.0, review R5). `btn ghost` in base.css is the whole button, and its
// sizes are the modifiers `ghost-row` (28 px), `ghost-main` (32 px) and `ghost-stage` (48 px) beside the 36 px default.
// Two checks keep per-screen ghost classes from coming back:
//   - a stylesheet rule outside base.css that re-declares the ghost: its fill (`background: var(--surface)` or
//     `transparent`) and its label colour (`color: var(--text-2)`) on a control (a fixed height, `border: none` or a
//     shadow of its own, `none` included). Cards share the fill and colour but have no height and keep their shadow.
//   - a class written next to `btn ghost` in a component that sets anything of the recipe or its size (height,
//     padding, radius, fill, border, shadow, colour, type). Such a class may only lay the button out (flex, margin,
//     width, alignment).
// The exceptions are controls that are not the ghost, or whose size another section of DESIGN.md fixes.

interface GhostAllowed {
  file: string;
  selector: RegExp;
  why: string;
}

const GHOST_RULE_ALLOWED: readonly GhostAllowed[] = [
  { file: 'styles/spoke.css', selector: /^\.back-btn$/, why: 'The back control is shadowless at rest and raised on hover (§3, the renders); one component, SpokeHeader.' },
  { file: 'screens/docview/docview.css', selector: /^\.tb-stamp$/, why: 'Insert timestamp sits inside the viewer toolbar card, shadowless like the card’s icon buttons (§5.1 Icon, §12).' },
  { file: 'screens/builder/builder.css', selector: /^\.btn\.add-module$/, why: 'An “add” affordance: a dashed line-strong outline flat on the ground (§2.3, §10), not a raised ghost.' },
  { file: 'styles/details-sheet.css', selector: /^button\.btn\.add-row$/, why: 'The agenda’s + Add an item: a dashed “add” outline (§2.3, §14), not a raised ghost.' },
  { file: 'styles/details-sheet.css', selector: /^\.link-btn$/, why: 'A text link styled as a button, with no raised shape.' },
  { file: 'screens/review/review20.css', selector: /^\.btn\.link\.sug-accept-all$/, why: 'A text link (Accept all), with no raised shape.' },
];

const GHOST_CLASS_ALLOWED: Readonly<Record<string, string>> = {
  'back-btn': 'The back control’s shape and rest state are §3’s (shadowless, 0 12 0 8 padding around the chevron).',
  'tb-stamp': 'Insert timestamp lies flat in the viewer toolbar card like its icon buttons (§5.1, §12).',
  'add-module': 'The Builder’s dashed “add” outline (§2.3, §10).',
  'rec-mark': '§8’s stage controls beside the 72 px Stop keep 56 px: Mark highlight is a 56 px pill with a `text` label.',
  'rec-round': '§8’s 56 px round Pause/Resume beside the 72 px Stop, with a `text` glyph.',
  'chip-select': 'A select drawn as the 32 px filter chip of §5.2 (radius 999).',
  'select-btn': 'A select shows its value in `text` (§5.3), not the ghost label colour; its size is the ghost size passed to SelectMenu.',
};

const GHOST_SIZE_CLASSES = new Set(['btn', 'ghost', 'ghost-row', 'ghost-main', 'ghost-stage', 'on']);
const RECIPE_PROPERTY = /^(height|min-height|padding(-[a-z]+)?|border(-[a-z]+)*|background(-color)?|box-shadow|color|font(-size|-weight)?)$/;

interface Declarations {
  selector: string;
  values: Map<string, string>;
}

function rules(source: string): Declarations[] {
  const css = source.replace(/\/\*[\s\S]*?\*\//g, '');
  const out: Declarations[] = [];
  for (const match of css.matchAll(/([^{}]+)\{([^{}]*)\}/g)) {
    const selector = (match[1] ?? '').trim().replace(/\s+/g, ' ');
    if (selector.startsWith('@') || /^(from|to|\d+%)/.test(selector)) {
      continue;
    }
    const values = new Map<string, string>();
    for (const raw of (match[2] ?? '').split(';')) {
      const colon = raw.indexOf(':');
      if (colon >= 0) {
        values.set(raw.slice(0, colon).trim(), raw.slice(colon + 1).trim().replace(/\s*!important$/, ''));
      }
    }
    out.push({ selector, values });
  }
  return out;
}

/** The rules of a stylesheet that re-declare the ghost recipe on a `.btn` button's class (the first check above). */
function lintGhostRules(file: string, source: string, buttonClasses: ReadonlySet<string>): string[] {
  return ghostRecipeRules(source, buttonClasses)
    .filter((selector) => !GHOST_RULE_ALLOWED.some((a) => a.file === file && a.selector.test(selector)))
    .map((selector) => `${file} ${selector}`);
}

/** The selectors of the rules that look like a ghost recipe, allowed or not. */
function ghostRecipeRules(source: string, buttonClasses: ReadonlySet<string>): string[] {
  return rules(source)
    .filter(({ selector }) => selectorList(selector).some((part) => [...buttonClasses].some((name) => hasClass(subject(part), name))))
    .filter(({ values }) => {
      const fill = /^(var\(--surface\)|transparent)$/.test(values.get('background') ?? '');
      const label = values.get('color') === 'var(--text-2)';
      const control = values.has('height') || /^(none|0)$/.test(values.get('border') ?? '') || values.has('box-shadow');
      return fill && label && control;
    })
    .map(({ selector }) => selector);
}

function hasClass(compound: string, name: string): boolean {
  return new RegExp(`\\.${name}(?![\\w-])`).test(compound);
}

/** Splits a selector list at its top-level commas (not those inside :where(…) or :not(…)). */
function selectorList(selector: string): string[] {
  const parts: string[] = [];
  let depth = 0;
  let start = 0;
  for (let i = 0; i < selector.length; i += 1) {
    const c = selector[i];
    depth += c === '(' ? 1 : c === ')' ? -1 : 0;
    if (c === ',' && depth === 0) {
      parts.push(selector.slice(start, i).trim());
      start = i + 1;
    }
  }
  parts.push(selector.slice(start).trim());
  return parts;
}

/** The compound selector a rule applies to: the last one after any combinator. */
function subject(selector: string): string {
  return selector.split(/\s*[\s>+~]\s*/).filter((s) => s !== '').at(-1) ?? '';
}

/** The classes written beside `btn ghost` (or, with `anyButton`, beside `btn`) in the components' string literals. */
function ghostCoClasses(sources: readonly string[], anyButton = false): Set<string> {
  const found = new Set<string>();
  // `btn` as a whole class name (not icon-btn, link-btn…).
  const pattern = anyButton ? /(["'`])([^"'`]*(?<![\w-])btn(?![\w-])[^"'`]*)\1/g : /(["'`])([^"'`]*(?<![\w-])btn ghost(?![\w-])[^"'`]*)\1/g;
  for (const source of sources) {
    for (const literal of source.matchAll(pattern)) {
      for (const name of (literal[2] ?? '').split(/\s+/)) {
        if (/^[a-z][a-z0-9-]*$/.test(name) && !GHOST_SIZE_CLASSES.has(name)) {
          found.add(name);
        }
      }
    }
  }
  return found;
}

/** Rules that size or paint a class written beside `btn ghost` (the second check above). */
function lintGhostClasses(classes: ReadonlySet<string>, sheets: readonly { file: string; source: string }[]): string[] {
  const findings: string[] = [];
  for (const { file, source } of sheets) {
    for (const { selector, values } of rules(source)) {
      for (const part of selectorList(selector)) {
        const target = subject(part);
        for (const name of classes) {
          if (name in GHOST_CLASS_ALLOWED || !hasClass(target, name)) {
            continue;
          }
          const recipe = [...values.keys()].filter((p) => RECIPE_PROPERTY.test(p));
          if (recipe.length > 0) {
            findings.push(`${file} ${part} { ${recipe.join(', ')} } on \`btn ghost ${name}\``);
          }
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

  it('carries the measured contrast corrections in tokens.css and defines no token anywhere else (2.4.0, R6)', () => {
    const tokens = readFileSync(join(REPO, 'design', 'tokens.css'), 'utf8');
    const block = (selector: string): string => {
      const at = tokens.indexOf(`\n${selector}{`);
      expect(at, selector).toBeGreaterThan(-1);
      return tokens.slice(at, tokens.indexOf('}', at));
    };
    // DESIGN.md §2.1: text-3 reaches 4.5:1 on every ground; labels on the accent and danger fills use on-accent.
    expect(block('.app')).toContain('--text-3:#68645E;');
    expect(block('.app')).toContain('--on-accent:#FFFFFF;');
    expect(block('.app.dark')).toContain('--text-3:#96928B;');
    expect(block('.app.dark')).toContain('--on-accent:#1F1E1B;');
    const sheets = files(SRC, /\.css$/).filter((p) => !p.endsWith(join('styles', 'tokens.css')));
    const definitions = sheets.filter((p) => /(^|[{;\s])--(text-3|on-accent)\s*:/.test(readFileSync(p, 'utf8').replace(/\/\*[\s\S]*?\*\//g, '')));
    expect(definitions.map((p) => relative(SRC, p))).toEqual([]);
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

  it('sizes the ghost by role with three modifiers: 28 px row, 32 px card main action, 48 px stage (§2.3)', () => {
    const base = readFileSync(join(SRC, 'styles', 'base.css'), 'utf8').replace(/\r\n/g, '\n');
    const size = (modifier: string): Map<string, string> => {
      const rule = rules(base).find((r) => r.selector === `.btn:where(.ghost.${modifier})`);
      expect(rule, modifier).toBeDefined();
      return rule?.values ?? new Map<string, string>();
    };
    expect(size('ghost-row').get('height')).toBe('28px');
    expect(size('ghost-row').get('border-radius')).toBe('12px');
    expect(size('ghost-main').get('height')).toBe('32px');
    expect(size('ghost-main').get('border-radius')).toBe('14px');
    expect(size('ghost-stage').get('height')).toBe('48px');
    expect(size('ghost-stage').get('border-radius')).toBe('20px');
    // A modifier only resizes: the fill, shadow and label colour stay the recipe's.
    for (const modifier of ['ghost-row', 'ghost-main', 'ghost-stage']) {
      expect([...size(modifier).keys()].filter((p) => /^(background|box-shadow|color|border)$/.test(p)), modifier).toEqual([]);
    }
  });

  it('has no per-screen ghost recipe outside base.css', () => {
    const sheets = files(SRC, /\.css$/)
      .filter((p) => !p.endsWith(join('styles', 'tokens.css')) && !p.endsWith(join('styles', 'base.css')))
      .map((p) => ({ file: relative(SRC, p).replace(/\\/g, '/'), source: readFileSync(p, 'utf8') }));
    const components = files(SRC, /\.tsx$/).filter((p) => !p.endsWith('.test.tsx')).map((p) => readFileSync(p, 'utf8'));
    const buttonClasses = ghostCoClasses(components, true);
    expect(buttonClasses.has('add-row')).toBe(true);
    expect(sheets.flatMap(({ file, source }) => lintGhostRules(file, source, buttonClasses))).toEqual([]);
    // Every exception still matches a rule; drop it from GHOST_RULE_ALLOWED when its rule goes.
    const unused = GHOST_RULE_ALLOWED.filter((a) => !sheets.some(({ file, source }) => file === a.file && ghostRecipeRules(source, buttonClasses).some((s) => a.selector.test(s))));
    expect(unused.map((a) => `${a.file} ${String(a.selector)}`)).toEqual([]);

    const classes = ghostCoClasses(components);
    // The scan sees the classes the components write (a sanity check that it reads them at all).
    expect(classes.has('select-btn')).toBe(true);
    expect(classes.has('undo-btn')).toBe(true);
    expect(lintGhostClasses(classes, sheets)).toEqual([]);
    // Every exception is still in use; drop it from GHOST_CLASS_ALLOWED when its class goes.
    expect(Object.keys(GHOST_CLASS_ALLOWED).filter((name) => !classes.has(name))).toEqual([]);
  });

  it('flags a new per-screen ghost class', () => {
    const probe = (css: string): string[] => lintGhostRules('styles/probe.css', css, new Set(['foo-btn', 'foo-card']));
    expect(probe('.foo-btn { height: 30px; background: var(--surface); box-shadow: var(--neo-sm); color: var(--text-2); }')).toEqual(['styles/probe.css .foo-btn']);
    expect(probe('.foo-btn { border: none; background: transparent; color: var(--text-2); }')).toEqual(['styles/probe.css .foo-btn']);
    // A card shares the fill and colour but is not a control.
    expect(probe('.foo-card { padding: 16px; background: var(--surface); color: var(--text-2); }')).toEqual([]);
    expect(probe('.foo-btn { flex: none; margin-left: auto; }')).toEqual([]);

    const sheet = [{ file: 'styles/probe.css', source: '.foo-btn { height: 26px; } .row .foo-btn, .x { padding: 0 8px; } .foo-wrap .bar { height: 9px; } .foo-gap { flex: none; }' }];
    const classes = ghostCoClasses(['<button class="btn ghost foo-btn" />', "cls = 'btn ghost foo-gap ghost-row';"]);
    expect([...classes]).toEqual(['foo-btn', 'foo-gap']);
    expect(lintGhostClasses(classes, sheet)).toEqual([
      'styles/probe.css .foo-btn { height } on `btn ghost foo-btn`',
      'styles/probe.css .row .foo-btn { padding } on `btn ghost foo-btn`',
    ]);
  });

  it('marks on the accent fill take the label colour, never a fixed white', () => {
    const sheets = ['styles/record.css', 'styles/shell.css'].map((f) => readFileSync(join(SRC, f), 'utf8').replace(/\r\n/g, '\n')).join('\n');
    for (const selector of ['.rec-dot', '.rec-start-dot']) {
      // The rule of that selector alone (at the start of a line), not `.ghost .rec-start-dot` (Resume's accent dot).
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
