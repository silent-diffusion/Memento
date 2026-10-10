// The 2.0 end-to-end check of the live transcript and Keep only the mix, through the real UI of the published app over
// the DevTools protocol. Nothing is played through the speakers: the simulated engine records four tracks and its
// microphone loops a speech WAV made by tools/e2e/speech.ps1 (written to a file, never played).
//
//   node tools/e2e/m7-live.mjs --models <dir> [--speech <wav>] [--seconds 60] [--data <dir>] [--out <dir>] [--port 9363]
//
// --models is a folder with whisper/ (and sherpa-onnx/ for speakers); its files are hard-linked (or copied) into the
// private data root (LOCALAPPDATA = --data, default artifacts/e2e-data-m7). With ggml-small.bin (or ggml-base.bin) the
// live lines must appear while recording and be replaced by the full transcript after Stop; without either, the card
// must say what to install (the live transcript never uses Turbo). The CPU time of Memento and its workers is measured
// over the recording (docs/ENGINE-NOTES.md §O), and every track must be as long as the recording (capture not slowed).

import { copyFileSync, existsSync, linkSync, mkdirSync, readdirSync, readFileSync, writeFileSync } from 'node:fs';
import { execFileSync } from 'node:child_process';
import { join, resolve } from 'node:path';
import { App, repoRoot } from './app.mjs';
import { sleep } from './cdp.mjs';

const args = process.argv.slice(2);
const option = (name, fallback) => (args.includes(name) ? args[args.indexOf(name) + 1] : fallback);
const dataRoot = resolve(option('--data', join(repoRoot, 'artifacts', 'e2e-data-m7')));
const out = resolve(option('--out', join(repoRoot, 'artifacts', 'e2e', 'm7')));
const models = option('--models', null) && resolve(option('--models', null));
const speech = resolve(option('--speech', join(repoRoot, 'artifacts', 'e2e-fixtures', 'two-voices.wav')));
const seconds = Number(option('--seconds', '60'));
const TITLE = 'M7 live check';
const summary = { steps: [], checks: {}, cpu: {} };
mkdirSync(out, { recursive: true });

const app = new App({ dataRoot, port: Number(option('--port', '9363')), args: [`--simulate-audio=speech=${speech},mics=2`, '--update-feed=off'] });
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
  const file = join(out, `${String(shotIndex).padStart(2, '0')}-${name}.png`);
  await page.screenshot(file);
}

const readJson = (file) => JSON.parse(readFileSync(file, 'utf8'));
const projectIds = () => (existsSync(library()) ? readdirSync(library()).sort() : []);
const hasText = (text, timeoutMs = 20_000) => page.waitFor(`document.body.innerText.includes(${JSON.stringify(text)})`, `"${text}"`, timeoutMs);

/** Processor time (ms) of Memento and its workers so far, by process id, from Windows. */
function cpuTimes() {
  // Only this run's processes (other Memento copies on the PC are left out): the ones started from this publish folder.
  const folder = join(app.exe, '..').replace(/'/g, "''");
  const script = `Get-Process -Name Memento,Memento.Worker -ErrorAction SilentlyContinue | Where-Object { $_.Path -like '${folder}*' } | ForEach-Object { '{0} {1} {2}' -f $_.Id, $_.ProcessName, [int64]$_.TotalProcessorTime.TotalMilliseconds }; exit 0`;
  const text = execFileSync('powershell.exe', ['-NoProfile', '-NonInteractive', '-Command', script], { encoding: 'utf8' });
  return Object.fromEntries(text.trim().split(/\r?\n/).filter(Boolean).map((l) => { const [id, name, ms] = l.split(' '); return [id, { name, ms: Number(ms) }]; }));
}

function cpuBetween(before, after, wallMs, threads) {
  const used = { Memento: 0, 'Memento.Worker': 0 };
  for (const [id, now] of Object.entries(after)) used[now.name] += now.ms - (before[id]?.ms ?? 0);
  const percent = (ms) => Number(((100 * ms) / wallMs / threads).toFixed(1));
  return { appSeconds: used.Memento / 1000, workerSeconds: used['Memento.Worker'] / 1000, appPercentOfPc: percent(used.Memento), workerPercentOfPc: percent(used['Memento.Worker']), wallSeconds: wallMs / 1000, threads };
}

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

async function switchOn(name) {
  const state = `[...document.querySelectorAll('[role=switch]')].find((s) => s.getAttribute('aria-label') === ${JSON.stringify(name)})?.getAttribute('aria-checked')`;
  // A row can still move while the section loads (the sources list); a click that missed is tried again.
  for (let attempt = 0; attempt < 3 && (await page.eval(state)) !== 'true'; attempt++) {
    await sleep(800);
    await page.click({ role: 'switch', name });
    await sleep(1500);
  }
  await page.waitFor(`${state} === 'true'`, `${name} on`, 10_000);
}

async function openSettings(section) {
  await home();
  await page.click({ name: 'Settings' });
  await page.click({ name: section });
}

try {
  const installed = models ? installModels(models) : [];
  const liveModel = installed.find((f) => /ggml-(small|base)\.bin$/.test(f)) ?? null;
  summary.models = installed;
  log('models', installed.join(', ') || 'none');
  check(existsSync(speech), `the speech fixture exists (${speech}); make it with tools/e2e/speech.ps1`);

  page = await app.start();
  await page.waitFor(`/Newest first|Your library is empty/.test(document.body.innerText)`, 'the Library', 60_000);

  // Settings › Transcription: Live transcript while recording (off by default).
  await openSettings('Transcription');
  await hasText('Live transcript while recording');
  check((await page.eval(`[...document.querySelectorAll('[role=switch]')].find((s) => s.getAttribute('aria-label') === 'Live transcript while recording')?.getAttribute('aria-checked')`)) === 'false', 'live transcript is off by default');
  await switchOn('Live transcript while recording');
  await shot('settings-live-on');

  // Settings › Recording: Keep only the mix (off by default).
  await page.click({ name: 'Recording' });
  await hasText('Keep only the mix');
  check((await page.eval(`[...document.querySelectorAll('[role=switch]')].find((s) => s.getAttribute('aria-label') === 'Keep only the mix')?.getAttribute('aria-checked')`)) === 'false', 'keep only the mix is off by default');
  await switchOn('Keep only the mix');
  await shot('settings-keep-only-mix');

  // Record four tracks.
  await home();
  await page.click({ name: 'New recording' });
  await hasText('AUDIO SOURCES');
  await page.click({ role: 'textbox', name: 'Untitled meeting', exact: false });
  await page.eval(`(() => { const el = document.activeElement; if (el && el.select) el.select(); return true; })()`);
  await page.type(TITLE);
  await page.key('Enter');
  for (let i = 0; i < 2; i++) {
    await page.click({ selector: '.src.off button[role=switch]:not([disabled])' });
    await sleep(500);
  }
  await hasText('4 audio sources selected');
  await shot('record-ready');
  const threads = Number(await page.eval('navigator.hardwareConcurrency'));
  await page.click({ name: 'Start recording' });
  await hasText('RECORDING');
  const startedAt = Date.now();
  const cpuStart = cpuTimes();
  log('recording', `4 simulated tracks, the microphone looping ${speech}`);

  if (liveModel) {
    await page.waitFor(`document.querySelectorAll('[aria-label="Live transcript"] .rec-live-line').length >= 2`, 'two live lines', (seconds + 60) * 1000);
    summary.liveLines = await page.eval(`[...document.querySelectorAll('[aria-label="Live transcript"] .rec-live-line')].map((l) => l.innerText.replace(/\\s+/g, ' ').trim())`);
    check(summary.liveLines.length >= 2, `live lines appeared while recording (${summary.liveLines.length})`);
    check(await page.eval(`!!document.querySelector('[aria-label="Live transcript"] [data-provisional="true"]')`), 'the live lines are marked provisional');
    summary.liveEngine = await page.eval(`document.querySelector('[aria-label="Live transcript"] .pill')?.innerText ?? null`);
    log('live lines', `${summary.liveLines.length} · ${summary.liveEngine}`);
  } else {
    await page.waitFor(`/needs the Small or Base transcription model/.test(document.querySelector('[aria-label="Live transcript"]')?.innerText ?? '')`, 'the card naming the model to install', 60_000);
    summary.liveCard = await page.eval(`document.querySelector('[aria-label="Live transcript"]').innerText.replace(/\\s+/g, ' ').trim()`);
    check(!summary.liveCard.includes('Turbo'), 'the live transcript does not use Turbo');
    log('live unavailable', summary.liveCard);
  }

  const left = seconds * 1000 - (Date.now() - startedAt);
  if (left > 0) await sleep(left);
  const cpuEnd = cpuTimes();
  summary.cpu = cpuBetween(cpuStart, cpuEnd, Date.now() - startedAt, threads);
  log('cpu while recording', JSON.stringify(summary.cpu));
  await shot('recording-live');
  await page.click({ name: 'Stop and open review' });
  const [id] = projectIds();
  const folder = join(library(), id);

  // After Stop: the full pass replaces the draft; Review shows the transcript; only the mix is kept.
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
  check(manifest.tracks.length === 4, `four tracks were recorded (${manifest.tracks.length})`);
  for (const track of manifest.tracks) check(Math.abs(track.durationMs - manifest.durationMs) <= 250, `track ${track.id} is as long as the recording (${track.durationMs} of ${manifest.durationMs} ms)`);
  check(manifest.mixOnly !== null && manifest.mixOnly !== undefined, 'the manifest records that only the mix was kept');
  check(manifest.tracks.every((t) => !existsSync(join(folder, t.file))), 'no separate track file is left');
  check(existsSync(join(folder, manifest.mix.file)), `the mix is kept (${manifest.mix.file})`);
  check(!readdirSync(folder).some((f) => /live/i.test(f)), 'no live draft was written to the project');
  if (existsSync(join(folder, 'transcript.json'))) {
    const transcript = readJson(join(folder, 'transcript.json'));
    summary.transcriptLines = transcript.segments.length;
    await page.waitFor(`!!document.querySelector('.review-panes')`, 'Review', 60_000);
    await sleep(2000);
    check(!(await page.eval(`!!document.querySelector('.rec-live-line')`)), 'the live draft is gone after Stop');
    log('full transcript', `${summary.transcriptLines} lines`);
  }
  await shot('review-after-stop');
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
