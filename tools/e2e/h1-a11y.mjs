// H1 accessibility pass through the real app (DevTools protocol):
//  1. Keyboard: on every shipped screen and dialog, Tab from the top through every stop (until focus comes back
//     round) and check that each focused element has an accessible name and a visible focus indicator (a focus
//     outline, or a box-shadow ring in the focus colour), and is on screen.
//  2. Reduced motion: with prefers-reduced-motion emulated, every control that has a press/hover transform in the
//     stylesheets is forced into :hover and :active (CSS.forcePseudoState) and must compute no transform; running
//     animations are listed.
// Light and dark themes are both walked. A 3-minute public-domain two-reader WAV is imported (library.importMedia
// { path }: the import picker is Windows' own dialog) so Review has a transcript; recordings use the simulated engine.
//
//   node tools/e2e/h1-a11y.mjs --audio <wav> --models <dir> [--data <dir>] [--out <dir>] [--port 9450]
import { rmSync, writeFileSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { Run, option, repoRoot, sleep } from './h1-lib.mjs';

const args = process.argv.slice(2);
const dataRoot = resolve(option(args, '--data', join(repoRoot, 'artifacts', 'e2e-data-h1-a11y')));
const out = resolve(option(args, '--out', join(repoRoot, 'artifacts', 'e2e', 'h1-a11y')));
const audio = resolve(option(args, '--audio', ''));
const run = new Run({ name: 'H1 accessibility', dataRoot, out, port: Number(option(args, '--port', '9450')), models: option(args, '--models', null) });
const report = { screens: [], motion: [] };

const INSPECT = `(() => {
  const el = document.activeElement;
  if (!el || el === document.body || el === document.documentElement) return null;
  const text = (s) => (s ?? '').replace(/\\s+/g, ' ').trim();
  const name = (() => {
    const by = el.getAttribute('aria-labelledby');
    if (by) return text(by.split(/\\s+/).map((id) => document.getElementById(id)?.innerText ?? '').join(' '));
    const al = el.getAttribute('aria-label');
    if (al) return text(al);
    if (el.id) { const l = document.querySelector('label[for="' + CSS.escape(el.id) + '"]'); if (l) return text(l.innerText); }
    const wrap = el.closest('label');
    if (wrap) return text(wrap.innerText);
    if (el.tagName === 'INPUT' || el.tagName === 'TEXTAREA' || el.tagName === 'SELECT') return text(el.getAttribute('title'));
    return text(el.innerText) || text(el.getAttribute('title'));
  })();
  const s = getComputedStyle(el);
  const r = el.getBoundingClientRect();
  const outline = s.outlineStyle !== 'none' && parseFloat(s.outlineWidth) >= 1 && !/rgba\\(.*, 0\\)$/.test(s.outlineColor);
  // Fields show focus as a 2 px box-shadow ring in the focus colour (tokens.css), possibly mid-transition.
  // A field inside a well shows the ring on the well (:focus-within).
  const focusColour = /rgba?\\((37, 99, 235|96, 165, 250)/;
  const ring = focusColour.test(s.boxShadow) || [el.parentElement, el.parentElement?.parentElement].some((p) => p && focusColour.test(getComputedStyle(p).boxShadow));
  const path = [];
  for (let n = el; n && n !== document.body && path.length < 4; n = n.parentElement) path.unshift(n.tagName.toLowerCase() + (n.id ? '#' + n.id : '') + (n.classList.length ? '.' + [...n.classList].slice(0, 2).join('.') : ''));
  return {
    sig: path.join('>') + '|' + name,
    tag: el.tagName.toLowerCase(),
    role: el.getAttribute('role') ?? el.type ?? '',
    name,
    placeholderOnly: !name && !!el.getAttribute('placeholder'),
    focusVisible: outline || ring,
    outline: s.outlineStyle + ' ' + s.outlineWidth + ' ' + s.outlineColor,
    onScreen: r.width > 0 && r.height > 0 && r.bottom > 0 && r.right > 0 && r.top < innerHeight && r.left < innerWidth,
    path: path.join(' > '),
  };
})()`;

/** Tabs from the top of the page (or the open dialog) until focus has gone all the way round. */
async function walk(label, { max = 160 } = {}) {
  await run.page.eval(`(() => { document.activeElement?.blur?.(); window.scrollTo(0, 0); return true; })()`);
  const seen = new Map();
  const stops = [];
  let first = null;
  for (let i = 0; i < max; i++) {
    await run.page.key('Tab');
    await sleep(200);
    const info = await run.page.eval(INSPECT);
    if (!info) continue;
    if (first === null) first = info.sig;
    else if (info.sig === first) break;
    if (seen.has(info.sig)) {
      seen.set(info.sig, seen.get(info.sig) + 1);
      if (seen.get(info.sig) > 2) break;
      continue;
    }
    seen.set(info.sig, 1);
    stops.push(info);
  }
  const problems = stops.filter((s) => !s.name || !s.focusVisible || !s.onScreen);
  report.screens.push({ screen: label, stops: stops.length, problems });
  run.check(`${label}: ${stops.length} stops, every one named with a visible focus ring`, problems.length === 0, problems.map((p) => `${p.path} [${p.name || 'NO NAME'}${p.focusVisible ? '' : ', NO RING (' + p.outline + ')'}${p.onScreen ? '' : ', OFF SCREEN'}]`).join('; '));
  return stops;
}

/**
 * Forces :hover and :active on every visible control that has a press/hover transform rule and returns the ones that
 * compute a transform, with `prefers-reduced-motion` set to `preference`.
 */
async function probeTransforms(preference) {
  const page = run.page;
  await page.send('Emulation.setEmulatedMedia', { features: [{ name: 'prefers-reduced-motion', value: preference }] });
  await page.send('DOM.enable');
  await page.send('CSS.enable');
  const targets = await page.eval(`(() => {
    const rules = [];
    const walkRules = (list, media) => { for (const r of list) {
      if (r.cssRules && r.media) walkRules(r.cssRules, r.media.mediaText);
      else if (r.selectorText && r.style && (r.style.transform && r.style.transform !== 'none') && /:(hover|active)/.test(r.selectorText) && !/reduce/.test(media ?? '')) rules.push(r.selectorText);
    } };
    for (const sheet of document.styleSheets) { try { walkRules(sheet.cssRules, ''); } catch {} }
    const found = [];
    let n = 0;
    for (const selector of rules) for (const part of selector.split(',')) {
      const base = part.replace(/:(hover|active|focus-visible|focus)/g, '').trim();
      if (!base) continue;
      let els = [];
      try { els = [...document.querySelectorAll(base)].filter((e) => { const r = e.getBoundingClientRect(); return r.width > 0 && r.height > 0; }).slice(0, 2); } catch {}
      for (const e of els) { e.setAttribute('data-h1-probe', String(n)); found.push({ id: n, rule: part.trim() }); n++; }
    }
    return found;
  })()`);
  const { root } = await page.send('DOM.getDocument', { depth: -1 });
  const moved = [];
  for (const target of targets) {
    const { nodeId } = await page.send('DOM.querySelector', { nodeId: root.nodeId, selector: `[data-h1-probe="${target.id}"]` });
    if (!nodeId) continue;
    await page.send('CSS.forcePseudoState', { nodeId, forcedPseudoClasses: ['hover', 'active'] });
    await sleep(80); // the forced state reaches style a moment later
    const transform = await page.eval(`getComputedStyle(document.querySelector('[data-h1-probe="${target.id}"]')).transform`);
    await page.send('CSS.forcePseudoState', { nodeId, forcedPseudoClasses: [] });
    if (transform && transform !== 'none' && transform !== 'matrix(1, 0, 0, 1, 0, 0)') moved.push(`${target.rule} → ${transform}`);
  }
  const animations = await page.eval(`[...document.querySelectorAll('*')].filter((e) => { const s = getComputedStyle(e); return s.animationName !== 'none' && parseFloat(s.animationDuration) > 0 && s.animationPlayState === 'running'; }).map((e) => e.className + ' (' + getComputedStyle(e).animationName + ')')`);
  await page.eval(`document.querySelectorAll('[data-h1-probe]').forEach((e) => e.removeAttribute('data-h1-probe'))`);
  await page.send('Emulation.setEmulatedMedia', { features: [{ name: 'prefers-reduced-motion', value: 'no-preference' }] });
  return { probed: targets.length, moved: [...new Set(moved)], animations };
}

/** Reduced motion: no press or hover transform may remain. Without it the same probe must find some (it works). */
async function motion(label) {
  const normal = await probeTransforms('no-preference');
  const reduced = await probeTransforms('reduce');
  report.motion.push({ screen: label, probed: reduced.probed, movingNormally: normal.moved.length, movedReduced: reduced.moved, animationsReduced: reduced.animations });
  run.check(
    `${label}: reduced motion, ${reduced.probed} press/hover transforms probed (${normal.moved.length} move without it), none moves`,
    reduced.moved.length === 0,
    reduced.moved.join('; '),
  );
  if (reduced.animations.length) run.log(`${label}: animations still running with reduced motion`, reduced.animations.join(', '));
}

async function screen(label, { motionToo = false } = {}) {
  await sleep(700);
  await run.shot(label);
  await walk(label);
  if (motionToo) await motion(label);
}

async function setTheme(theme) {
  await run.bridge('settings.set', { theme });
  await sleep(600);
}

try {
  rmSync(dataRoot, { recursive: true, force: true });
  run.copyModels();
  await run.start(['--simulate-audio', '--update-feed=off']);
  for (const theme of ['light', 'dark']) {
    await setTheme(theme);
    const t = (name) => `${theme}-${name}`;
    if (theme === 'light') {
      await run.hasText('Your library is empty');
      await screen(t('library-empty'), { motionToo: true });
      await run.bridge('library.importMedia', { path: audio, title: 'Two readers' });
      const [id] = run.projectIds();
      await run.until(() => run.manifest(id).stages.every((s) => s.state === 'done' || s.state === 'failed') && run.manifest(id).stages.length > 2, 'the import to be transcribed', 10 * 60_000, 1000);
    }
    await run.page.click({ name: 'Library' }).catch(() => null);
    await run.hasText('Two readers');
    await screen(t('library-list'), { motionToo: theme === 'light' });
    await run.page.click({ name: 'Grid view' });
    await screen(t('library-grid'));
    await run.page.click({ name: 'List view' });

    // Record: ready, then recording.
    await run.page.click({ name: 'New recording' });
    await run.hasText('AUDIO SOURCES');
    await screen(t('record-ready'), { motionToo: theme === 'light' });
    await run.page.click({ name: 'Start recording' });
    await run.hasText('RECORDING');
    await screen(t('record-recording'), { motionToo: theme === 'light' });
    await run.page.click({ name: 'Details and agenda' });
    await run.until(() => run.page.eval(`!!document.querySelector('[role="dialog"]')`), 'the details sheet');
    await screen(t('record-details-sheet'), { motionToo: theme === 'light' });
    await run.page.key('Escape');
    await sleep(500);
    await run.page.click({ name: 'Stop and open review' });
    await sleep(4000);

    // Review of the imported recording.
    await run.page.click({ name: 'Library' }).catch(() => null);
    await run.page.click({ name: 'Two readers', exact: false });
    await run.page.waitFor(`document.querySelectorAll('.segm').length > 3`, 'the transcript', 30_000);
    await screen(t('review'), { motionToo: theme === 'light' });
    await run.page.click({ name: 'Export' });
    await run.page.waitFor(`!!document.querySelector('.export-dialog')`, 'the export dialog');
    await screen(t('export-dialog'), { motionToo: theme === 'light' });
    await run.page.key('Escape');
    await sleep(500);

    // Delete dialog from the Library row (not confirmed).
    await run.page.click({ name: 'Library' });
    await run.page.click({ name: 'More actions for Two readers' });
    await run.page.click({ role: 'menuitem', name: 'Delete' });
    await run.page.waitFor(`!!document.querySelector('[role="dialog"]')`, 'the delete dialog');
    await screen(t('delete-dialog'), { motionToo: theme === 'light' });
    await run.page.click({ name: 'Cancel', within: '[role="dialog"]' });

    // Settings, every section.
    await run.page.click({ name: 'Settings' });
    for (const section of ['General', 'Recording', 'Transcription', 'Speakers', 'AI and privacy', 'Documents', 'Export', 'Storage and history']) {
      await run.page.click({ name: section });
      await screen(t(`settings-${section.toLowerCase().replace(/\s+/g, '-')}`), { motionToo: theme === 'light' && section === 'General' });
    }
    await run.page.click({ name: 'Library' }).catch(() => run.page.key('Escape'));
  }
  await run.app.close();
} catch (error) {
  run.check('the pass reached the end', false, error.stack);
  await run.shot('failure').catch(() => null);
  await run.app?.kill();
} finally {
  const summary = { ...run.summary(), report };
  writeFileSync(join(out, 'summary.json'), JSON.stringify(summary, null, 2));
  console.log(JSON.stringify({ passed: summary.passed, failed: summary.failed, failures: summary.results.filter((r) => !r.passed) }, null, 2));
}
