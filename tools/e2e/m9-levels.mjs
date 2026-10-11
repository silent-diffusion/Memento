// The level check end to end (after 2.0.0-beta.1, docs/ENGINE-NOTES.md §Q), through the real UI of the published app
// over the DevTools protocol. Nothing is played through the speakers: the simulated engine records two tracks, its
// microphone looping a quiet speech WAV (-38 dBFS peak over -60 dBFS room noise) and its system audio looping a loud
// one (-12 dBFS peak) that says other things, both made by tools/e2e/levels-fixture.ps1 (files, never played).
//
//   node tools/e2e/m9-levels.mjs --models <dir> [--fixtures <dir>] [--seconds 150] [--data <dir>] [--out <dir>] [--port 9391]
//
// --models is a folder with whisper/ (and sherpa-onnx/ for speakers); its files are hard-linked (or copied) into the
// private data root (LOCALAPPDATA = --data, default artifacts/e2e-data-m9). Checks: Even out source levels is on by
// default; the Record screen shows the quiet hint under the microphone's meter (and no toast); after Stop the mix
// records the microphone's gain; both speakers' lines are in the transcript (each track's truth lines, matched by
// words); Review › More › Reprocess › Rebuild the mix as recorded replaces the mix and the player loads the new file.

import { copyFileSync, existsSync, linkSync, mkdirSync, readdirSync, readFileSync, writeFileSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { App, repoRoot } from './app.mjs';
import { sleep } from './cdp.mjs';

const args = process.argv.slice(2);
const option = (name, fallback) => (args.includes(name) ? args[args.indexOf(name) + 1] : fallback);
const dataRoot = resolve(option('--data', join(repoRoot, 'artifacts', 'e2e-data-m9')));
const out = resolve(option('--out', join(repoRoot, 'artifacts', 'e2e', 'm9')));
const models = option('--models', null) && resolve(option('--models', null));
const fixtures = resolve(option('--fixtures', join(repoRoot, 'artifacts', 'e2e-fixtures', 'levels')));
const micWav = join(fixtures, 'mic.wav');
const systemWav = join(fixtures, 'system.wav');
const seconds = Number(option('--seconds', '150'));
const TITLE = 'M9 level check';
const summary = { steps: [], checks: {} };
mkdirSync(out, { recursive: true });

const app = new App({ dataRoot, port: Number(option('--port', '9391')), args: [`--simulate-audio=speech=${micWav},system-speech=${systemWav}`, '--update-feed=off'] });
const library = () => join(app.memento, 'Library', 'projects');
const t0 = Date.now();
let page;
let shotIndex = 0;

function log(step, detail = '') {
  const line = `[${((Date.now() - t0) / 1000).toFixed(0).padStart(4)} s] ${step}${detail ? ' · ' + detail : ''}`;
  console.log(line);
  summary.steps.push(line);
}

function check(condition, message) {
  if (!condition) throw new Error(`Check failed: ${message}`);
  summary.checks[message] = true;
}

async function shot(name) {
  await sleep(600);
  shotIndex += 1;
  await page.screenshot(join(out, `${String(shotIndex).padStart(2, '0')}-${name}.png`));
}

const readJson = (file) => JSON.parse(readFileSync(file, 'utf8').replace(/^﻿/, ''));
const projectIds = () => (existsSync(library()) ? readdirSync(library()).sort() : []);
const hasText = (text, timeoutMs = 20_000) => page.waitFor(`document.body.innerText.includes(${JSON.stringify(text)})`, `"${text}"`, timeoutMs);
const switchState = (name) => page.eval(`[...document.querySelectorAll('[role=switch]')].find((s) => s.getAttribute('aria-label') === ${JSON.stringify(name)})?.getAttribute('aria-checked') ?? null`);

function installModels(from) {
  const installed = [];
  for (const kind of readdirSync(from)) {
    mkdirSync(join(app.memento, 'models', kind), { recursive: true });
    for (const file of readdirSync(join(from, kind))) {
      const target = join(app.memento, 'models', kind, file);
      if (!file.endsWith('.verified.json')) installed.push(file);
      if (existsSync(target)) continue;
      try {
        linkSync(join(from, kind, file), target);
      } catch {
        copyFileSync(join(from, kind, file), target);
      }
    }
  }
  return installed;
}

async function home() {
  for (let i = 0; i < 6; i++) {
    if (await page.eval(`/Newest first|Your library is empty/.test(document.body.innerText) && !document.querySelector('[data-spoke-back]')`)) return;
    if (await page.locate({ name: 'Library' })) await page.click({ name: 'Library' });
    else if (await page.locate('[data-spoke-back]')) await page.click({ selector: '[data-spoke-back]' });
    await sleep(700);
  }
}

/** The truth lines of a track that were recorded, and how many of them a transcript line on that track shares words with. */
function heard(truthFile, segments, recordedSeconds) {
  const words = (s) => new Set(s.toLowerCase().split(/[\s,.?!]+/).filter((w) => w.length >= 4));
  const lines = readJson(truthFile).lines.filter((l) => l.end < recordedSeconds - 2);
  const found = lines.filter((l) => {
    const said = words(l.text);
    return segments.some((s) => s.start < l.end + 3 && s.end > l.start - 3 && [...words(s.text)].filter((w) => said.has(w)).length >= Math.min(3, said.size));
  });
  return { lines: lines.length, heard: found.length, missed: lines.filter((l) => !found.includes(l)).map((l) => l.text) };
}

try {
  check(existsSync(micWav) && existsSync(systemWav), `the level fixture exists in ${fixtures}; make it with tools/e2e/levels-fixture.ps1`);
  const installed = models ? installModels(models) : [];
  summary.models = installed;
  log('models', installed.join(', ') || 'none');

  page = await app.start();
  await page.waitFor(`/Newest first|Your library is empty/.test(document.body.innerText)`, 'the Library', 60_000);

  // Settings › Recording: Even out source levels, on by default.
  await home();
  await page.click({ name: 'Settings' });
  await page.click({ name: 'Recording' });
  await hasText('Even out source levels');
  check((await switchState('Even out source levels')) === 'true', 'Even out source levels is on by default');
  await shot('settings-even-levels');

  // Record the quiet microphone and the loud system audio.
  await home();
  await page.click({ name: 'New recording' });
  await hasText('AUDIO SOURCES');
  await page.click({ role: 'textbox', name: 'Untitled meeting', exact: false });
  await page.eval(`(() => { const el = document.activeElement; if (el && el.select) el.select(); return true; })()`);
  await page.type(TITLE);
  await page.key('Enter');
  for (const name of ['Microphone', 'System audio']) {
    for (let attempt = 0; attempt < 3 && (await switchState(name)) !== 'true'; attempt++) {
      await page.click({ role: 'switch', name });
      await sleep(800);
    }
    check((await switchState(name)) === 'true', `${name} is selected`);
  }
  if ((await switchState('Simulated meeting app')) === 'true') await page.click({ role: 'switch', name: 'Simulated meeting app' });
  await page.click({ name: 'Start recording' });
  await hasText('RECORDING');
  const startedAt = Date.now();
  log('recording', `microphone looping ${micWav}, system audio looping ${systemWav}`);

  // The hint under the microphone's meter, within 30 s, and never as a toast.
  await page.waitFor(`[...document.querySelectorAll('.src-hint')].some((h) => h.innerText.startsWith('Microphone is quiet ('))`, 'the quiet hint under the microphone', 30_000);
  summary.hint = await page.eval(`[...document.querySelectorAll('.src-hint')].map((h) => h.innerText)`);
  check(summary.hint.length === 1, `one hint, under the microphone only (${summary.hint.join(' | ')})`);
  check(/^Microphone is quiet \(−\d+ dB\)\. Move closer or raise its level in Windows; the transcript may miss you\.$/.test(summary.hint[0]), `the hint reads as §17 asks (${summary.hint[0]})`);
  log('hint', summary.hint[0]);
  await shot('record-quiet-hint');
  let toasts = 0;
  while (Date.now() - startedAt < seconds * 1000) {
    toasts = Math.max(toasts, await page.eval(`[...document.querySelectorAll('[role=status], [role=alert], .toast')].filter((t) => /quiet/i.test(t.innerText) && !t.classList.contains('src-hint')).length`));
    await sleep(3000);
  }
  check(toasts === 0, 'no toast about the quiet microphone');
  await page.click({ name: 'Stop and open review' });
  const [id] = projectIds();
  const folder = join(library(), id);

  const deadline = Date.now() + 20 * 60_000;
  let manifest;
  for (;;) {
    manifest = readJson(join(folder, 'project.json'));
    const busy = manifest.stages.some((s) => s.state === 'active' || s.state === 'queued');
    if (manifest.stages.length > 1 && !busy) break;
    if (Date.now() > deadline) throw new Error('still processing after 20 minutes');
    await sleep(2000);
  }
  summary.stages = manifest.stages.map((s) => `${s.stage}:${s.state}`);
  log('processed', summary.stages.join(', '));
  const recorded = manifest.durationMs / 1000;
  check(manifest.tracks.length === 2, `two tracks were recorded (${manifest.tracks.map((t) => t.id).join(', ')})`);
  const mic = manifest.tracks.find((t) => t.sourceKind === 'microphone');
  const system = manifest.tracks.find((t) => t.sourceKind === 'system');
  summary.evenLevels = manifest.mix.evenLevels;
  const micGain = manifest.mix.evenLevels?.tracks.find((t) => t.trackId === mic.id);
  check(micGain !== undefined && micGain.gainDb >= 15, `the mix lifted the microphone (${JSON.stringify(manifest.mix.evenLevels)})`);
  check(manifest.mix.evenLevels.tracks.find((t) => t.trackId === system.id).gainDb === 0, 'the system audio was left as recorded');

  const transcript = readJson(join(folder, 'transcript.json'));
  const on = (track) => transcript.segments.filter((s) => s.track === track.id);
  summary.mic = heard(micWav + '.json', on(mic), recorded);
  summary.system = heard(systemWav + '.json', on(system), recorded);
  log('heard', `microphone ${summary.mic.heard} of ${summary.mic.lines}, system audio ${summary.system.heard} of ${summary.system.lines}`);
  check(summary.mic.lines >= 10 && summary.mic.heard >= Math.ceil(summary.mic.lines * 0.8), `the microphone's speaker is in the transcript (${summary.mic.heard} of ${summary.mic.lines} lines)`);
  check(summary.system.lines >= 10 && summary.system.heard >= Math.ceil(summary.system.lines * 0.8), `the system audio's speakers are in the transcript (${summary.system.heard} of ${summary.system.lines} lines)`);
  const micSpeakers = new Set(on(mic).map((s) => s.speaker));
  const systemSpeakers = new Set(on(system).map((s) => s.speaker));
  if (transcript.speakers.length > 0) {
    check([...micSpeakers].every((s) => !systemSpeakers.has(s)), 'the microphone and the system audio have their own speakers');
  }
  await page.waitFor(`!!document.querySelector('.review-panes')`, 'Review', 60_000);
  await shot('review');

  // Review › More › Reprocess › Rebuild the mix as recorded: the way back, then the player loads the new file.
  const before = await page.eval(`document.querySelector('audio')?.getAttribute('src') ?? ''`);
  await page.click({ name: 'More actions' });
  await page.click({ name: 'Rebuild the mix as recorded' });
  const rebuildDeadline = Date.now() + 5 * 60_000;
  for (;;) {
    manifest = readJson(join(folder, 'project.json'));
    if (manifest.stages.some((s) => s.stage === 'mix' && s.state === 'done')) break;
    if (Date.now() > rebuildDeadline) throw new Error('the mix was not rebuilt within 5 minutes');
    await sleep(1000);
  }
  check(manifest.mix.evenLevels == null, 'the rebuilt mix sums the tracks as recorded');
  const history = readFileSync(join(folder, 'history.jsonl'), 'utf8');
  check(history.includes('Rebuilt the mix as recorded'), 'History says the mix was rebuilt');
  await page.waitFor(`(document.querySelector('audio')?.getAttribute('src') ?? '') !== ${JSON.stringify(before)}`, 'the player to take the rebuilt mix', 30_000);
  const player = await page.eval(`(async () => { const a = document.querySelector('audio'); for (let i = 0; i < 40 && !(a.duration > 0); i++) await new Promise((r) => setTimeout(r, 250)); return { src: a.getAttribute('src'), duration: a.duration }; })()`);
  summary.player = player;
  check(/mix\.flac\?v=[0-9a-f]{8}$/.test(player.src) && player.duration > recorded - 2, `the player loads the rebuilt mix (${JSON.stringify(player)})`);
  await shot('review-after-rebuild');

  writeFileSync(join(out, 'summary.json'), JSON.stringify(summary, null, 2));
  console.log(JSON.stringify(summary, null, 2));
  await app.close();
} catch (error) {
  summary.error = error.message;
  console.error(error);
  try {
    await shot('failure');
  } catch {
    // The page is gone.
  }
  writeFileSync(join(out, 'summary.json'), JSON.stringify(summary, null, 2));
  await app.kill();
  process.exitCode = 1;
}
