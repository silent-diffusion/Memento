// The M1 end-to-end check, driven through the real UI of the published app (mouse clicks, typing and key presses
// over the DevTools protocol; the bridge is never called directly). Each step saves a screenshot and the run ends
// with a JSON summary of what it measured on disk and in the logs.
//
//   node tools/e2e/m1-flow.mjs [--simulate] [--data <dir>] [--out <dir>] [--port <n>] [--models <dir>]
//
// The app runs with LOCALAPPDATA pointed at --data (default artifacts/e2e-data[-sim]), so the real library is never
// touched. With real audio the microphone is recorded: delete --data afterwards (it holds room audio). --models copies
// installed models (a folder m2-flow.mjs --keep-models wrote) in first, so the recordings are transcribed too.

import { createHash } from 'node:crypto';
import { cpSync, existsSync, readdirSync, readFileSync, rmSync } from 'node:fs';
import { join } from 'node:path';
import { App, repoRoot } from './app.mjs';
import { sleep } from './cdp.mjs';
import { playTone } from './tone.mjs';

const args = process.argv.slice(2);
const flag = (name) => args.includes(name);
const option = (name, fallback) => (args.includes(name) ? args[args.indexOf(name) + 1] : fallback);
const simulate = flag('--simulate');
const dataRoot = option('--data', join(repoRoot, 'artifacts', simulate ? 'e2e-data-sim' : 'e2e-data'));
const out = option('--out', join(repoRoot, 'artifacts', 'e2e', simulate ? 'simulated' : 'real'));
const port = Number(option('--port', '9333'));
const models = option('--models', null);
const appSource = simulate ? 'Simulated meeting app' : 'Windows PowerShell';
const summary = { mode: simulate ? 'simulated' : 'real', steps: [] };

let shotIndex = 0;
let page;
const app = new App({ dataRoot, port, args: simulate ? ['--simulate-audio'] : [] });
const library = () => join(app.memento, 'Library', 'projects');

function log(step, detail = '') {
  const line = `[${new Date().toLocaleTimeString()}] ${step}${detail ? ' · ' + detail : ''}`;
  console.log(line);
  summary.steps.push(line);
}

async function shot(name) {
  await sleep(500); // Let screen transitions finish.
  shotIndex += 1;
  const file = join(out, `${String(shotIndex).padStart(2, '0')}-${name}.png`);
  await page.screenshot(file);
  return file;
}

function check(condition, message) {
  if (!condition) throw new Error(`Check failed: ${message}`);
}

async function hasText(text, timeoutMs = 20_000) {
  return page.waitFor(`document.body.innerText.includes(${JSON.stringify(text)})`, `"${text}"`, timeoutMs);
}

/**
 * Review's transcript area once the recording is stored. Since M2 the transcript stage runs after storing: without a
 * model it waits for one ("Transcription needs the … model"); with --models it transcribes (or skips silent tracks).
 */
async function transcriptArea(timeoutMs = 180_000) {
  return page.waitFor(
    `(() => { const t = document.querySelector('.transcript')?.innerText ?? ''; return t.includes('Transcription needs the') || t.includes('Not transcribed yet') || t.includes('Click a line to play it') || t.includes('no part of the transcript') || (!!document.querySelector('.transcript') && !document.querySelector('.tx-progress') && /Mark as reviewed|Reviewed/.test(t)); })()`,
    'the transcript area to settle',
    timeoutMs,
  );
}

/** Recorded time shown on the Record screen's big timer, in seconds. */
async function elapsedSeconds() {
  const text = await page.text('[aria-label="Recording"]');
  const match = /(\d\d):(\d\d):(\d\d)/.exec(text);
  return match ? Number(match[1]) * 3600 + Number(match[2]) * 60 + Number(match[3]) : 0;
}

async function waitElapsed(seconds) {
  await page.waitFor(`(() => { const m = /(\\d\\d):(\\d\\d):(\\d\\d)/.exec(document.querySelector('[aria-label="Recording"]')?.innerText ?? ''); return m && (+m[1]*3600 + +m[2]*60 + +m[3]) >= ${seconds}; })()`, `${seconds} s recorded`, (seconds + 60) * 1000);
}

async function newRecording(title) {
  await page.click({ name: 'New recording' });
  await hasText('AUDIO SOURCES');
  await page.click({ role: 'textbox', name: 'Untitled meeting', exact: false });
  await page.key('a', ['ctrl']);
  await page.type(title);
  await page.key('Enter');
}

const projectIds = () => (existsSync(library()) ? readdirSync(library()).sort() : []);
const readJson = (file) => JSON.parse(readFileSync(file, 'utf8'));
const sha256 = (file) => createHash('sha256').update(readFileSync(file)).digest('hex');

function describeProject(id) {
  const folder = join(library(), id);
  const manifest = readJson(join(folder, 'project.json'));
  const files = Object.entries(manifest.integrity.files).map(([file, hash]) => {
    const path = join(folder, file);
    return { file, bytes: existsSync(path) ? readFileSync(path).length : null, hashMatches: existsSync(path) && sha256(path) === hash };
  });
  const history = readFileSync(join(folder, 'history.jsonl'), 'utf8').trim().split('\n').map((l) => JSON.parse(l));
  return {
    id,
    state: manifest.state,
    durationMs: manifest.durationMs,
    tracks: manifest.tracks.map((t) => ({ id: t.id, file: t.file, codec: t.codec, channels: t.channels, durationMs: t.durationMs, startOffsetMs: t.startOffsetMs, endedEarlyAtMs: t.endedEarlyAtMs, endReason: t.endReason })),
    mix: manifest.mix && { file: manifest.mix.file, codec: manifest.mix.codec, durationMs: manifest.mix.durationMs },
    pauses: manifest.pauses,
    stages: manifest.stages.map((s) => `${s.stage}:${s.state}`),
    recovery: manifest.recovery,
    files,
    leftoverWavs: readdirSync(join(folder, 'tracks')).filter((f) => f.endsWith('.wav')).concat(readdirSync(folder).filter((f) => f.endsWith('.wav'))),
    history: history.map((h) => `${h.stage}/${h.event}: ${h.summary}`),
  };
}

function logLines(pattern) {
  const logs = join(app.memento, 'logs');
  if (!existsSync(logs)) return [];
  return readdirSync(logs).flatMap((f) => readFileSync(join(logs, f), 'utf8').split('\n')).filter((l) => pattern.test(l));
}

let tone;
try {
  rmSync(dataRoot, { recursive: true, force: true });
  if (models) {
    // Installed models (copied from a folder an M2 run kept), so the recordings are transcribed too.
    cpSync(models, join(dataRoot, 'Memento', 'models'), { recursive: true });
    log('models copied', models);
  }
  if (!simulate) {
    tone = await playTone(join(repoRoot, 'artifacts', 'e2e-tone'), 600);
    log('tone playing', `PowerShell pid ${tone.pid}`);
    await sleep(2000);
  }

  // 1. First run: the empty Library.
  page = await app.start();
  await hasText('Your library is empty');
  await shot('first-run-library');
  log('first-run Library', 'empty state shown');

  // 2. New recording with microphone + system audio + one application.
  await newRecording('E2E check one');
  await page.waitFor(`document.body.innerText.includes(${JSON.stringify(appSource)})`, `the ${appSource} source`, 30_000).catch(async () => {
    await page.click({ name: 'Add a source: look again for microphones and apps' });
    await hasText(appSource, 20_000);
  });
  await page.click({ name: appSource });
  const sourcesText = await page.text('[aria-label="Sources"]');
  check(!sourcesText.includes('WebView2'), 'Memento\'s own WebView2 process is not offered as a source');
  await hasText('3 audio sources selected');
  await shot('sources-selected');
  log('sources', '3 selected: microphone, system audio, ' + appSource);

  // 3. Start.
  await page.click({ name: 'Start recording' });
  await hasText('RECORDING');
  await sleep(3000);
  await shot('recording');
  log('recording started');

  // 4. Highlight with a note.
  await waitElapsed(30);
  await page.click({ name: 'Mark highlight' });
  await page.click({ selector: 'input[placeholder="Add a note (optional)"]' });
  await page.type('Tone check marker');
  await page.key('Tab');
  await sleep(1000);
  await shot('highlight-with-note');
  log('highlight marked', `at ${await elapsedSeconds()} s with a note`);

  // 5. Pause 5 s, resume.
  await waitElapsed(45);
  await page.click({ name: 'Pause' });
  await hasText('PAUSED');
  await sleep(2500);
  await shot('paused');
  await sleep(2500);
  await page.click({ name: 'Resume' });
  await hasText('RECORDING');
  log('paused 5 s and resumed');

  // 6. Two checkpoints, then turn the application source off.
  await waitElapsed(75);
  await hasText('last checkpoint');
  await page.click({ name: appSource });
  await hasText('2 audio tracks');
  await shot('app-source-off');
  log('application source turned off', `at ${await elapsedSeconds()} s`);

  // 7. Stop: Finalizing, then Review.
  await waitElapsed(92);
  await page.click({ name: 'Stop and open review' });
  await transcriptArea();
  await sleep(1500);
  await shot('review');
  const peaksLoaded = await page.eval(`performance.getEntriesByType('resource').some((r) => r.name.endsWith('/peaks.json'))`);
  const waveBars = await page.eval(`document.querySelectorAll('main svg rect, main svg line, main svg path, .player svg *, [class*=wave] *').length`);
  check(peaksLoaded, 'Review fetched peaks.json');
  check(waveBars > 50, `the waveform has bars (${waveBars})`);
  log('Review open', `waveform from peaks.json (${waveBars} elements)`);

  // 8. Play, seek, add a chapter.
  await page.click({ name: 'Play' });
  await sleep(2500);
  const playing = await page.eval(`(() => { const a = document.querySelector('audio'); return { src: a.currentSrc, t: a.currentTime, paused: a.paused, duration: a.duration }; })()`);
  check(!playing.paused && playing.t > 1 && playing.src.endsWith('/mix.flac'), `the mix plays (${JSON.stringify(playing)})`);
  await page.clickAt('[aria-label="Playback position"]', 0.6);
  await sleep(1000);
  const seekedTo = await page.eval(`document.querySelector('audio').currentTime`);
  check(Math.abs(seekedTo - 0.6 * playing.duration) < 3, `seeking moved the playhead to 60% (${seekedTo})`);
  await page.click({ name: 'Add a chapter at', exact: false });
  await page.click({ selector: 'input[aria-label^="Title for the chapter"]' });
  await page.type('After the pause');
  await page.key('Enter');
  await page.click({ name: 'Pause' });
  await hasText('After the pause');
  await shot('review-chapter-added');
  log('played and seeked the mix', `${playing.src} · seeked to ${seekedTo.toFixed(1)} s of ${playing.duration.toFixed(1)} s · chapter added`);
  const [firstId] = projectIds();
  summary.first = describeProject(firstId);

  // 9. Library row, search.
  await page.click({ name: 'Library' });
  await hasText('1 recording');
  const row = await page.text('main');
  // Without a model the transcript waits for one; with --models it has run.
  check(row.includes('E2E check one') && /Audio only|needs a model|Transcript/.test(row), 'the row shows the recording with its transcript state');
  await shot('library-row');
  await page.click({ selector: 'input[type=search]' });
  await page.type('check one');
  await hasText('1 recording');
  await shot('library-search');
  await page.key('a', ['ctrl']);
  await page.type('zebra');
  await hasText('0 recordings');
  await page.key('a', ['ctrl']);
  await page.key('Backspace');
  log('Library', `row reads "${/Audio only|Transcript · needs a model|Transcript/.exec(row)?.[0]}"; search finds it and nothing for a non-match`);

  // 10. Settings › Recording: Smaller AAC at 160 kbps.
  await page.click({ name: 'Settings' });
  await page.click({ name: 'Recording' });
  await page.click({ name: 'Smaller AAC' });
  await hasText('160 kbps');
  await sleep(800);
  await shot('settings-aac-160');
  check(readJson(join(app.memento, 'settings.json')).recording.storage.codec === 'aac', 'settings.json says aac');
  await page.click({ name: 'Library' });
  log('Settings', 'Smaller AAC, 160 kbps');

  // 11. Second recording, 30 s, optimized to AAC.
  await newRecording('E2E check two AAC');
  await page.click({ name: 'Start recording' });
  await hasText('RECORDING');
  await waitElapsed(30);
  await page.click({ name: 'Stop and open review' });
  await transcriptArea();
  await page.waitFor(`document.querySelector('audio')?.src?.endsWith('/mix.m4a')`, 'Review to switch to the AAC mix', 90_000);
  await page.click({ name: 'History' });
  await hasText('Saved smaller files');
  await shot('second-review-optimized');
  const secondId = projectIds().find((id) => id !== firstId);
  summary.second = describeProject(secondId);
  check(summary.second.tracks.every((t) => t.file.endsWith('.m4a') && t.codec === 'aac'), 'every track is .m4a');
  check(summary.second.files.every((f) => f.hashMatches), 'every hash in the manifest matches its file');
  check(summary.second.history.some((h) => h.startsWith('optimize/completed')), 'History has the optimize stage');
  log('second recording optimized', summary.second.files.map((f) => `${f.file} ${f.bytes} B`).join(', '));
  summary.flacSizes = logLines(/FLAC encoded/).slice(-4).map((l) => l.replace(/^.*FLAC encoded /, ''));

  // 12. Delete it through the dialog.
  await page.click({ name: 'More actions' });
  await page.click({ name: 'Delete', exact: false, role: 'menuitem' });
  await hasText('This cannot be undone');
  await shot('delete-dialog');
  await page.click({ name: 'Delete', within: '[role=dialog], [role=alertdialog], dialog' });
  await hasText('1 recording');
  check(!existsSync(join(library(), secondId)), 'the deleted project folder is gone');
  log('second recording deleted', 'folder removed');

  // 13. Third recording; kill the app after a checkpoint.
  await newRecording('E2E check three crash'); // The remembered selection: the three sources of the first recording.
  await page.click({ name: 'Add a source: look again for microphones and apps' });
  await sleep(1500);
  check(!(await page.text('[aria-label="Sources"]')).includes('WebView2'), 'after Review played audio, Memento\'s WebView2 is still not offered');
  await page.click({ name: 'Start recording' });
  await hasText('RECORDING');
  await waitElapsed(40);
  await hasText('last checkpoint');
  await shot('third-before-kill');
  const killedAt = await elapsedSeconds();
  await app.kill();
  const thirdId = projectIds().find((id) => id !== firstId);
  check(existsSync(join(library(), thirdId, 'recording.state.json')), 'recording.state.json is left for recovery');
  log('app killed mid-recording', `at about ${killedAt} s`);

  // 14. Relaunch: the recovery dialog, then Open recording.
  page = await app.start();
  await hasText('Recovered an interrupted recording', 60_000);
  const dialog = await page.text('[role=dialog], [role=alertdialog], dialog');
  await shot('recovery-dialog');
  await page.click({ name: 'Open recording' });
  await hasText('E2E check three crash');
  await transcriptArea();
  await page.click({ name: 'History' });
  await hasText('Recovered after Memento closed during recording');
  await page.waitFor(`document.querySelector('audio')?.src?.endsWith('/mix.m4a')`, 'the recovered recording to be optimized', 90_000);
  await shot('recovered-review');
  summary.recoveryDialog = dialog.replace(/\s+/g, ' ').trim();
  summary.third = describeProject(thirdId);
  check(summary.third.state === 'recovered', 'the project is marked recovered');
  check(summary.third.stages.includes('optimize:done'), 'the recovered recording keeps its finished optimize stage');
  check(summary.third.files.every((f) => f.hashMatches) && summary.third.leftoverWavs.length === 0, 'recovered files match their hashes and no WAV is left');
  log('recovery', summary.recoveryDialog);

  await page.click({ name: 'Library' });
  await sleep(1000);
  await app.close();
  log('closed normally');

  summary.logProblems = logLines(/\[(WRN|ERR|FTL)\]/).map((l) => l.slice(0, 300));
  summary.drift = logLines(/Checkpoint .* drift/).map((l) => l.replace(/^.*Checkpoint /, '')).slice(-6);
  summary.checkpoints = logLines(/Checkpoint .* drift/).length;
  summary.result = 'passed';
} catch (error) {
  summary.result = `failed: ${error.message}`;
  console.error(error);
  try {
    await shot('failure');
  } catch {
    // The page is gone.
  }
  await app.kill().catch(() => undefined);
  process.exitCode = 1;
} finally {
  tone?.stop();
  console.log(JSON.stringify(summary, null, 2));
}
