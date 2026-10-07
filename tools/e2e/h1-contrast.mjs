// H1 contrast check: the WCAG 2.2 contrast ratio of every text colour token on every background token in both themes,
// from ui/src/styles/tokens.css (a verbatim copy of design/tokens.css) and as shipped, with the corrections in
// ui/src/styles/base.css applied; plus the label on the accent and danger fills (both gradient ends and the middle,
// where the label sits). Normal text needs 4.5:1 (AA); large text and non-text parts (the focus ring, dots) 3:1.
//
//   node tools/e2e/h1-contrast.mjs [--json <out.json>]   (exit code 1 when a shipped pair fails)
import { readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { repoRoot } from './app.mjs';

const styles = join(repoRoot, 'ui', 'src', 'styles');
const tokensCss = readFileSync(join(styles, 'tokens.css'), 'utf8');
const baseCss = readFileSync(join(styles, 'base.css'), 'utf8').replace(/\/\*[\s\S]*?\*\//g, '');

function block(css, selector) {
  const tokens = {};
  const pattern = new RegExp(`(^|\\n|\\})\\s*${selector.replace(/\./g, '\\.')}\\s*\\{([^}]*)\\}`, 'g');
  for (const match of css.matchAll(pattern)) {
    for (const [, name, value] of match[2].matchAll(/--([\w-]+)\s*:\s*([^;]+)/g)) tokens[name] = value.trim();
  }
  return tokens;
}

const rgb = (value) => {
  const hex = /^#([0-9a-f]{6})$/i.exec(value);
  if (!hex) throw new Error(`not a plain colour: ${value}`);
  return [0, 2, 4].map((i) => parseInt(hex[1].slice(i, i + 2), 16));
};
const channel = (c) => {
  const s = c / 255;
  return s <= 0.04045 ? s / 12.92 : ((s + 0.055) / 1.055) ** 2.4;
};
const luminance = (value) => {
  const [r, g, b] = rgb(value);
  return 0.2126 * channel(r) + 0.7152 * channel(g) + 0.0722 * channel(b);
};
const ratio = (a, b) => {
  const [l1, l2] = [luminance(a), luminance(b)].sort((x, y) => y - x);
  return (l1 + 0.05) / (l2 + 0.05);
};
const middle = (a, b) => '#' + rgb(a).map((v, i) => Math.round((v + rgb(b)[i]) / 2).toString(16).padStart(2, '0')).join('');

const TEXT = ['text', 'text-2', 'text-3', 'accent-text', 'accent', 'ok', 'danger', 'focus'];
const BACKGROUNDS = ['bg', 'surface-2', 'accent-soft', 'ok-soft', 'danger-soft'];
const NEEDS = { accent: 3, focus: 3 }; // accent: dots, icons and large text only; focus: the ring (non-text)

function rowsFor(variant, theme, tokens) {
  const rows = [];
  for (const fg of TEXT) {
    for (const bg of BACKGROUNDS) {
      const r = ratio(tokens[fg], tokens[bg]);
      const need = NEEDS[fg] ?? 4.5;
      rows.push({ variant, theme, fg, bg, fgValue: tokens[fg], bgValue: tokens[bg], ratio: Math.round(r * 100) / 100, need, pass: r >= need });
    }
  }
  const label = tokens['on-accent'] ?? '#FFFFFF';
  for (const grad of ['accent-grad', 'danger-grad']) {
    const [from, to] = tokens[grad].match(/#[0-9a-f]{6}/gi);
    for (const [where, stop] of [['start', from], ['middle', middle(from, to)], ['end', to]]) {
      const r = ratio(label, stop);
      // The label sits in the middle of the fill; the ends are reported for reference.
      rows.push({ variant, theme, fg: `label ${label}`, bg: `${grad} ${where} ${stop}`, fgValue: label, bgValue: stop, ratio: Math.round(r * 100) / 100, need: 4.5, pass: where !== 'middle' || r >= 4.5, reference: where !== 'middle' });
    }
  }
  return rows;
}

const light = block(tokensCss, '.app');
const dark = { ...light, ...block(tokensCss, '.app.dark') };
const shippedLight = { ...light, ...block(baseCss, '.app') };
const shippedDark = { ...dark, ...block(baseCss, '.app'), ...block(baseCss, '.app.dark') };
const rows = [
  ...rowsFor('design tokens', 'light', light),
  ...rowsFor('design tokens', 'dark', dark),
  ...rowsFor('as shipped', 'light', shippedLight),
  ...rowsFor('as shipped', 'dark', shippedDark),
];

for (const r of rows) {
  const verdict = r.reference ? (r.ratio >= 4.5 ? 'ok  ' : 'ref ') : r.pass ? 'pass' : 'FAIL';
  console.log(`${verdict} ${r.variant.padEnd(13)} ${r.theme.padEnd(5)} ${r.fg.padEnd(15)} on ${r.bg.padEnd(28)} ${r.ratio.toFixed(2).padStart(5)}:1 (needs ${r.need})`);
}
const failing = (variant) => rows.filter((r) => r.variant === variant && !r.pass);
console.log(`\ndesign tokens: ${failing('design tokens').length} pairs below their threshold; as shipped: ${failing('as shipped').length}`);
const out = process.argv.includes('--json') ? process.argv[process.argv.indexOf('--json') + 1] : null;
if (out) writeFileSync(out, JSON.stringify({ rows, failingDesign: failing('design tokens'), failingShipped: failing('as shipped') }, null, 2));
process.exitCode = failing('as shipped').length > 0 ? 1 : 0;
