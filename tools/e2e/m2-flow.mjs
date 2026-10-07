// The M2 end-to-end check, driven through the real UI of the published app over the DevTools protocol (mouse clicks,
// typing and key presses; the bridge is never called). Each step saves a screenshot; the run ends with a JSON summary
// of timings, what the transcript holds and the warnings and errors in the logs.
//
//   node tools/e2e/m2-flow.mjs --play <speech file> [--seconds 180] [--fail-seconds 120] [--data <dir>] [--out <dir>]
//                              [--keep-models <dir>] [--skip-failure] [--no-busy-pause] [--from-review] [--port <n>]
//
// The app runs with LOCALAPPDATA pointed at --data (default artifacts/e2e-data-m2), so the real library is never
// touched. Models are installed through Settings (about 2.2 GB from Hugging Face and GitHub); --keep-models copies the
// installed models there at the end so a later run (or the simulated M1 run) can reuse them. The microphone is
// recorded: delete --data afterwards (it holds room audio). The speech file plays through the default output.

import { execFileSync, spawn } from 'node:child_process';
import { cpSync, existsSync, readdirSync, readFileSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { App, repoRoot } from './app.mjs';
import { sleep } from './cdp.mjs';

const args = process.argv.slice(2);
const option = (name, fallback) => (args.includes(name) ? args[args.indexOf(name) + 1] : fallback);
const flag = (name) => args.includes(name);
const dataRoot = resolve(option('--data', join(repoRoot, 'artifacts', 'e2e-data-m2')));
const out = resolve(option('--out', join(repoRoot, 'artifacts', 'e2e', 'm2')));
const play = option('--play', null);
const seconds = Number(option('--seconds', '180'));
const failSeconds = Number(option('--fail-seconds', '120'));
const keepModels = option('--keep-models', null);
const summary = { steps: [], timings: {} };

let shotIndex = 0;
let page;
// Its own DevTools port (not M1's 9333), so another DevTools client on the PC cannot take over this window.
const app = new App({ dataRoot, port: Number(option('--port', '9444')) });
const library = () => join(app.memento, 'Library', 'projects');
const t0 = Date.now();

function log(step, detail = '') {
  const line = `[${((Date.now() - t0) / 1000).toFixed(0).padStart(4)} s] ${step}${detail ? ' · ' + detail : ''}`;
  console.log(line);
  summary.steps.push(line);
}

async function shot(name) {
  await sleep(500);
  shotIndex += 1;
  const file = join(out, `${String(shotIndex).padStart(2, '0')}-${name}.png`);
  await page.screenshot(file);
  return file;
}

function check(condition, message) {
  if (!condition) throw new Error(`Check failed: ${message}`);
}

const hasText = (text, timeoutMs = 20_000) => page.waitFor(`document.body.innerText.includes(${JSON.stringify(text)})`, `"${text}"`, timeoutMs);
const readJson = (file) => JSON.parse(readFileSync(file, 'utf8'));
const projectIds = () => (existsSync(library()) ? readdirSync(library()).sort() : []);
const projectFolder = (id) => join(library(), id);
const transcriptOf = (id) => (existsSync(join(projectFolder(id), 'transcript.json')) ? readJson(join(projectFolder(id), 'transcript.json')) : null);
const manifestOf = (id) => readJson(join(projectFolder(id), 'project.json'));
const historyOf = (id) => readFileSync(join(projectFolder(id), 'history.jsonl'), 'utf8').trim().split('\n').map((l) => JSON.parse(l));

function logLines(pattern) {
  const logs = join(app.memento, 'logs');
  if (!existsSync(logs)) return [];
  return readdirSync(logs).flatMap((f) => readFileSync(join(logs, f), 'utf8').split('\n')).filter((l) => pattern.test(l));
}

function workerPids() {
  try {
    const text = execFileSync('tasklist', ['/FI', 'IMAGENAME eq Memento.Worker.exe', '/FO', 'CSV', '/NH'], { encoding: 'utf8' });
    return text.split('\n').filter((l) => l.startsWith('"Memento.Worker.exe"')).map((l) => Number(l.split('","')[1]));
  } catch {
    return [];
  }
}

function gpuUtilisation() {
  try {
    return execFileSync('nvidia-smi', ['--query-gpu=utilization.gpu,memory.used', '--format=csv,noheader'], { encoding: 'utf8' }).trim();
  } catch {
    return 'nvidia-smi unavailable';
  }
}

function cpuUtilisation() {
  try {
    const value = execFileSync('powershell.exe', ['-NoProfile', '-Command', "(Get-Counter '\\Processor(_Total)\\% Processor Time' -SampleInterval 2 -MaxSamples 1).CounterSamples.CookedValue"], { encoding: 'utf8' });
    return `${Math.round(Number(value.trim()))} %`;
  } catch {
    return 'unknown';
  }
}

function startPlayback(file) {
  if (!file) return null;
  const child = spawn('powershell.exe', ['-NoProfile', '-NonInteractive', '-Command', `(New-Object Media.SoundPlayer '${file}').PlaySync()`], { stdio: 'ignore', windowsHide: true });
  return { stop: () => spawn('taskkill', ['/F', '/T', '/PID', String(child.pid)], { stdio: 'ignore' }) };
}

/** Recorded time on the Record screen's timer, in seconds. */
async function waitElapsed(target) {
  await page.waitFor(`(() => { const m = /(\\d\\d):(\\d\\d):(\\d\\d)/.exec(document.querySelector('[aria-label="Recording"]')?.innerText ?? ''); return m && (+m[1]*3600 + +m[2]*60 + +m[3]) >= ${target}; })()`, `${target} s recorded`, (target + 90) * 1000);
}

/**
 * Selects the text of the focused field, as Ctrl+A would (a Ctrl+A sent over the DevTools protocol does not reach
 * WebView2's editing commands).
 */
async function selectAll() {
  await page.eval(`(() => { const el = document.activeElement; if (el && typeof el.select === 'function') el.select(); return true; })()`);
}

async function newRecording(title) {
  await page.click({ name: 'New recording' });
  await hasText('AUDIO SOURCES');
  await page.click({ role: 'textbox', name: 'Untitled meeting', exact: false });
  await selectAll();
  await page.type(title);
  await page.key('Enter');
}

/** Installs a model from its card (`name` is the start of the model's name) and waits until it is installed. */
async function install(name) {
  const started = Date.now();
  const installed = `!!document.querySelector('[aria-label^=${JSON.stringify(`Remove ${name}`)}]')`;
  const moving = `${installed} || [...document.querySelectorAll('[role="progressbar"]')].some((p) => (p.getAttribute('aria-label') ?? '').startsWith(${JSON.stringify(`Downloading ${name}`)}))`;
  for (let attempt = 0; ; attempt++) {
    await page.click({ name: `Install ${name}`, exact: false });
    try {
      await page.waitFor(moving, `${name} to start downloading`, 10_000);
      break;
    } catch (error) {
      // A click that landed while the cards re-rendered: once more.
      if (attempt >= 2) throw error;
    }
  }
  await page.waitFor(installed, `${name} to install`, 15 * 60_000);
  return Math.round((Date.now() - started) / 100) / 10;
}

/** Goes to the Library (from wherever the app is) and opens the recording by its title. */
async function openFromLibrary(title) {
  if (!(await page.eval(`!!document.querySelector('.lib-item, [aria-label="Processing now"]') && document.body.innerText.includes('Newest first')`))) {
    await page.click({ name: 'Library' });
    await hasText('Newest first');
  }
  await page.click({ selector: '.lib-item .row-title' });
  await page.waitFor(`document.body.innerText.includes(${JSON.stringify(title)}) && !!document.querySelector('.review-panes')`, 'Review to open', 30_000);
}

/** The text of the Library processing card's stage columns, e.g. "Stored Done Transcribing 64% · local GPU …". */
const cardText = () => page.eval(`document.querySelector('[aria-label="Processing now"] .proc-stages')?.innerText.replace(/\\s+/g, ' ') ?? ''`);

const segmentCount = () => page.eval(`document.querySelectorAll('.segm').length`);

/** Steps 1–4: first run, models, the recording and its processing. Returns the recording's id. */
async function recordAndProcess() {
  // 1. First run: the empty Library; Settings › Transcription says no model is installed.
  page = await app.start();
  await hasText('Your library is empty');
  await hasText('No transcription model installed');
  await shot('first-run-library');
  log('first run', 'empty Library, footer: no transcription model installed');
  await page.click({ name: 'Settings' });
  await page.click({ name: 'Transcription' });
  await hasText('No model installed');
  await shot('settings-no-model');
  log('Settings › Transcription', 'Engine: No model installed');

  // 2. Install the models through the UI with progress.
  await page.click({ name: 'Install Large v3 Turbo', exact: false });
  await page.waitFor(`!!document.querySelector('[data-model-id="whisper-large-v3-turbo"] [role="progressbar"]')`, 'download progress', 60_000);
  await sleep(4000);
  await shot('settings-downloading-turbo');
  const turboStart = Date.now();
  await page.waitFor(`!!document.querySelector('[aria-label="Remove Large v3 Turbo"]')`, 'Large v3 Turbo to install', 20 * 60_000);
  summary.timings.turboDownloadSeconds = Math.round((Date.now() - turboStart) / 100) / 10 + 4;
  summary.timings.smallDownloadSeconds = await install('Small');
  await hasText('Local · GPU');
  await shot('settings-transcription-installed');
  await page.click({ name: 'Speakers' });
  summary.timings.segmentationDownloadSeconds = await install('Speech segmentation');
  summary.timings.voiceDownloadSeconds = await install('Voice model (NeMo TitaNet small');
  await shot('settings-speakers-installed');
  log('models installed', JSON.stringify(summary.timings));

  // --no-busy-pause: on a PC another job keeps busy, "Pause when the PC is busy" would hold transcription back.
  if (flag('--no-busy-pause')) {
    await page.click({ name: 'Transcription' });
    await page.click({ name: 'Pause when the PC is busy' });
    await page.waitFor(`document.querySelector('[aria-label="Pause when the PC is busy"]')?.getAttribute('aria-checked') === 'false'`, 'pause when busy off');
    log('Settings', 'Pause when the PC is busy: off (another job keeps this PC busy)');
  }

  // Smaller files (AAC), so the pipeline ends with the optimize stage.
  await page.click({ name: 'Recording' });
  await page.click({ name: 'Smaller AAC' });
  await hasText('160 kbps');
  await page.click({ name: 'Library' });

  // 3. New recording: microphone and system audio while the speech file plays.
  await newRecording('M2 check two readers');
  await hasText('2 audio sources selected');
  await shot('record-ready');
  const playback = startPlayback(play);
  await sleep(700);
  await page.click({ name: 'Start recording' });
  await hasText('RECORDING');
  await sleep(5000);
  await shot('recording');
  log('recording', `microphone + system audio for ${seconds} s${play ? ', speech file playing' : ''}`);
  await waitElapsed(seconds);
  summary.gpuAtStop = gpuUtilisation();
  await page.click({ name: 'Stop and open review' });
  const stoppedAt = Date.now();
  playback?.stop();
  const [firstId] = projectIds();
  summary.cpuAtStop = cpuUtilisation();
  log('stopped', `GPU before transcription: ${summary.gpuAtStop}; processor: ${summary.cpuAtStop}`);

  // 4. The Library processing card moves through the stages.
  await page.click({ name: 'Library' });
  await page.waitFor(`!!document.querySelector('[aria-label="Processing now"]')`, 'the processing card', 60_000);
  const seen = [];
  const deadline = Date.now() + 20 * 60_000;
  let last = '';
  while (Date.now() < deadline) {
    const text = await cardText();
    // Done when the card is gone and the manifest has nothing queued or running (between two stages the card can
    // blink out for a moment).
    if (text === '' && !manifestOf(firstId).stages.some((s) => s.state === 'active' || s.state === 'queued')) break;
    for (const [stage, pattern] of [['transcript', /Transcribing \d+%/], ['speakers', /Speakers \d+%/], ['topics', /Topics (\d+%|Running|Finding topics)/], ['optimize', /Smaller files \d+%/]]) {
      if (!seen.includes(stage) && pattern.test(text)) {
        seen.push(stage);
        summary.timings[`${stage}StartedAfterStopSeconds`] = Math.round((Date.now() - stoppedAt) / 1000);
        await shot(`library-card-${stage}`);
      }
    }
    if (text !== last) {
      log('card', text);
      last = text;
    }
    await sleep(300);
  }
  summary.timings.processingSecondsAfterStop = Math.round((Date.now() - stoppedAt) / 1000);
  summary.cardStagesSeen = seen;
  check(seen.includes('transcript') && seen.includes('optimize'), `the card showed the transcript and optimize stages (${seen.join(', ')})`);
  await shot('library-processed');
  log('processing done', `${summary.timings.processingSecondsAfterStop} s after stop; card showed ${seen.join(' → ')}`);
  return firstId;
}

try {
  // --from-review: start on the data root of an earlier run (models installed, the recording processed) and go
  // straight to step 5, to work on the later steps without recording again.
  let firstId;
  if (flag('--from-review')) {
    page = await app.start();
    [firstId] = projectIds();
    log('from review', `reusing ${firstId}`);
  } else {
    firstId = await recordAndProcess();
  }

  // 5. Review: transcript with speakers and low-confidence marks.
  await openFromLibrary('M2 check two readers');
  await page.waitFor(`document.querySelectorAll('.segm').length > 3`, 'transcript lines', 30_000);
  await sleep(1000);
  await shot('review-transcript');
  const first = transcriptOf(firstId);
  summary.transcript = {
    engine: first.engine,
    segments: first.segments.length,
    words: first.segments.reduce((n, s) => n + s.words.length, 0),
    lowConfidenceWords: first.segments.reduce((n, s) => n + s.words.filter((w) => w.c < first.lowConfidenceThreshold).length, 0),
    speakers: first.speakers.map((s) => `${s.name} ${Math.round(s.talkTimeMs / 1000)} s`),
    byTrack: first.segments.reduce((m, s) => ({ ...m, [s.track]: (m[s.track] ?? 0) + 1 }), {}),
    coverageGaps: first.coverageGaps,
    uncertainSpeakerLines: first.segments.filter((s) => s.speakerConfidence !== null && s.speakerConfidence < 0.7).length,
  };
  const manifest = manifestOf(firstId);
  const audioSeconds = manifest.tracks.reduce((s, t) => s + t.durationMs / 1000, 0);
  summary.timings.transcriptRtf = Math.round((first.engine.durationMs / 1000 / audioSeconds) * 1000) / 1000;
  const lowMarks = await page.eval(`document.querySelectorAll('.segm .lowc').length`);
  const speakerNames = await page.eval(`[...new Set([...document.querySelectorAll('.segm-speaker-name')].map((e) => e.textContent))]`);
  check(first.speakers.length > 0 && speakerNames.length > 0, 'the transcript has speakers');
  log('Review', `${first.segments.length} lines, ${summary.transcript.words} words, ${lowMarks} low-confidence marks shown, speakers ${speakerNames.join(', ')}; RTF ${summary.timings.transcriptRtf} (${first.engine.device})`);

  // 6. Click a line: it seeks.
  const target = await page.eval(`(() => { const rows = [...document.querySelectorAll('.segm')]; const row = rows[Math.min(3, rows.length - 1)]; return { id: row.dataset.segmentId }; })()`);
  const targetStart = first.segments.find((s) => s.id === target.id).start;
  await page.click({ selector: `.segm[data-segment-id="${target.id}"] .segm-text` });
  await sleep(800);
  const position = await page.eval(`document.querySelector('audio').currentTime`);
  check(Math.abs(position - targetStart) < 1.5, `clicking the line seeked to ${targetStart} s (at ${position})`);
  await shot('review-line-clicked');
  log('clicked a line', `seeked to ${position.toFixed(1)} s (line starts at ${targetStart.toFixed(1)} s)`);

  // 7. Double-click a line and edit it.
  const editAt = await page.locate({ selector: `.segm[data-segment-id="${target.id}"] .segm-text` });
  await page.send('Input.dispatchMouseEvent', { type: 'mousePressed', x: editAt.x, y: editAt.y, button: 'left', clickCount: 1 });
  await page.send('Input.dispatchMouseEvent', { type: 'mouseReleased', x: editAt.x, y: editAt.y, button: 'left', clickCount: 1 });
  await page.send('Input.dispatchMouseEvent', { type: 'mousePressed', x: editAt.x, y: editAt.y, button: 'left', clickCount: 2 });
  await page.send('Input.dispatchMouseEvent', { type: 'mouseReleased', x: editAt.x, y: editAt.y, button: 'left', clickCount: 2 });
  await page.waitFor(`!!document.querySelector('.segm-editor') && document.activeElement === document.querySelector('.segm-editor')`, 'the line editor, focused');
  await selectAll();
  const editedText = 'This line was corrected by the end-to-end check.';
  await page.type(editedText);
  await shot('review-editing');
  await page.key('Enter');
  await page.waitFor(`document.querySelector('.segm[data-segment-id="${target.id}"] .segm-edited') !== null`, 'the edited marker');
  await sleep(500);
  check(transcriptOf(firstId).segments.find((s) => s.id === target.id).text === editedText, 'the edit is saved in transcript.json');
  await shot('review-edited');
  log('edited a line', 'saved, "edited" marker shown');

  // 8. Rename a speaker: every line updates.
  const oldName = speakerNames[0];
  await page.click({ name: `Rename ${oldName}` });
  await page.waitFor(`document.activeElement?.getAttribute('aria-label') === ${JSON.stringify(`New name for ${oldName}`)}`, 'the name field, focused');
  await selectAll();
  await page.type('Reader One');
  await page.key('Enter');
  await page.waitFor(`[...document.querySelectorAll('.segm-speaker-name')].some((e) => e.textContent === 'Reader One') && ![...document.querySelectorAll('.segm-speaker-name')].some((e) => e.textContent === ${JSON.stringify(oldName)})`, 'the new name on every line');
  await shot('review-speaker-renamed');
  log('renamed a speaker', `${oldName} → Reader One on every line`);

  // 9. Search for a word: matches, next and previous.
  const word = first.segments
    .flatMap((s) => s.text.split(/\s+/))
    .map((w) => w.replace(/[^\p{L}]/gu, ''))
    .filter((w) => w.length >= 6)
    .map((w) => w.toLowerCase())
    .find((w, i, all) => all.indexOf(w) !== all.lastIndexOf(w)) ?? first.segments[0].text.split(/\s+/)[0];
  await page.click({ selector: '#tx-search' });
  await page.type(word);
  await page.waitFor(`/match/.test(document.querySelector('#tx-search-count')?.textContent ?? '')`, 'search results');
  const count = await page.text('#tx-search-count');
  await page.key('Enter');
  await page.waitFor(`/^1 of/.test(document.querySelector('#tx-search-count')?.textContent ?? '')`, 'the first match');
  await page.click({ name: 'Next match (Enter)' });
  await sleep(400);
  const second = await page.text('#tx-search-count');
  await page.click({ name: 'Previous match (Shift+Enter)' });
  await sleep(400);
  const back = await page.text('#tx-search-count');
  await shot('review-search');
  log('searched', `"${word}": ${count}; next → ${second}; previous → ${back}`);
  await page.key('Escape');

  // 10. Mark as reviewed.
  await page.click({ name: 'Mark as reviewed' });
  await hasText('Reviewed');
  await shot('review-reviewed');
  check(transcriptOf(firstId).reviewed === true, 'transcript.json says reviewed');
  log('marked as reviewed');

  // 11. Details: the transcript versions list shows the edit version.
  await page.click({ role: 'tab', name: 'Details' });
  await page.waitFor(`document.body.innerText.toLowerCase().includes('transcript versions')`, 'the transcript versions');
  await page.waitFor(`document.body.innerText.includes('First transcript')`, 'the version kept by the first edit');
  await shot('details-versions');
  log('versions', 'the first edit kept the first transcript as a version');

  // 12. More › Transcribe again with whisper-small: a new version appears.
  await page.click({ name: 'More actions' });
  await page.click({ name: 'Transcribe again…', role: 'menuitem' });
  await hasText('is transcribed again on this PC');
  await page.click({ selector: '[aria-label^="Model:"]' });
  await page.click({ role: 'option', name: 'Small', exact: false });
  await shot('retranscribe-dialog');
  const againAt = Date.now();
  await page.click({ name: 'Transcribe again', within: '[role=dialog]' });
  const deadlineSmall = Date.now() + 15 * 60_000;
  while (Date.now() < deadlineSmall && transcriptOf(firstId).engine.model !== 'whisper-small') await sleep(500);
  summary.timings.smallPassSeconds = Math.round((Date.now() - againAt) / 1000);
  // The pass keeps the edited transcript it replaces as a version ("Edited by you"): two kept versions now.
  await page.waitFor(`document.querySelectorAll('.version-card:not(.cur)').length >= 2 && document.body.innerText.includes('Edited by you')`, 'the version kept by the new pass', 60_000);
  await sleep(1500);
  await shot('details-after-retranscribe');
  const small = transcriptOf(firstId);
  summary.small = { engine: small.engine, segments: small.segments.length, coverageGaps: small.coverageGaps };
  log('transcribed again with Small', `${small.segments.length} lines in ${small.engine.durationMs} ms on ${small.engine.device}`);

  // 13. Restore the earlier (edited) version.
  await page.click({ name: 'Restore the version from', exact: false });
  await page.click({ name: 'Restore', within: '[role=dialog]' });
  await page.waitFor(`document.querySelector('.segm-edited') !== null`, 'the edited transcript back', 30_000);
  const restored = transcriptOf(firstId);
  check(restored.segments.some((s) => s.text === editedText), 'the restored transcript is the edited one');
  await shot('review-restored');
  log('restored the earlier version', `the edited ${restored.engine.model} transcript is back`);

  // 14. Library search finds a transcript word, with a snippet.
  await page.click({ name: 'Library' });
  await page.click({ selector: 'input[type=search]' });
  await page.type(word);
  await page.waitFor(`!!document.querySelector('.row-snippet')`, 'a transcript snippet');
  const snippet = await page.text('.row-snippet');
  await shot('library-search-snippet');
  log('Library search', `"${word}" → ${snippet.replace(/\s+/g, ' ')}`);
  await selectAll();
  await page.key('Backspace');

  // 15. Settings › Speakers: expect 2, then Identify speakers again → 2 speakers.
  await page.click({ name: 'Settings' });
  await page.click({ name: 'Speakers' });
  await page.click({ selector: '[aria-label^="Expected speakers:"]' });
  await page.click({ role: 'option', name: '2 people' });
  await sleep(800);
  check(readJson(join(app.memento, 'settings.json')).speakers.expectedSpeakers === 2, 'settings.json expects 2 speakers');
  await shot('settings-expected-2');
  await openFromLibrary('M2 check two readers');
  await page.waitFor(`document.querySelectorAll('.segm').length > 3`, 'transcript lines', 30_000);
  const before = transcriptOf(firstId).speakers.length;
  await page.click({ name: 'More actions' });
  await page.click({ name: 'Identify speakers again', role: 'menuitem' });
  const identifyAt = Date.now();
  await page.waitFor(`document.body.innerText.includes('Identifying speakers')`, 'speakers to run', 60_000).catch(() => undefined);
  const deadlineSpeakers = Date.now() + 10 * 60_000;
  // Done when a speakers pass that started after the click has completed (one may have been running already).
  const passDone = () => historyOf(firstId).some((h) => h.stage === 'speakers' && h.event === 'completed' && Date.parse(h.at) > identifyAt);
  while (Date.now() < deadlineSpeakers && !passDone()) await sleep(500);
  await sleep(1500);
  summary.timings.speakersAgainSeconds = Math.round((Date.now() - identifyAt) / 1000);
  const regrouped = transcriptOf(firstId);
  await shot('review-two-speakers');
  summary.speakersAgain = { before, after: regrouped.speakers.map((s) => `${s.name} ${Math.round(s.talkTimeMs / 1000)} s`) };
  check(regrouped.speakers.length === 2, `Identify speakers again found 2 speakers (${regrouped.speakers.length})`);
  log('identified speakers again with 2 expected', `${before} → ${regrouped.speakers.length} speakers`);

  // 16. Failure path: kill the worker while it transcribes; Retry on CPU completes.
  if (!flag('--skip-failure')) {
    await page.click({ name: 'Library' });
    await newRecording('M2 check worker killed');
    const playback2 = startPlayback(play);
    await sleep(700);
    await page.click({ name: 'Start recording' });
    await hasText('RECORDING');
    await waitElapsed(failSeconds);
    await page.click({ name: 'Stop and open review' });
    playback2?.stop();
    const secondId = projectIds().find((id) => id !== firstId);
    let killed = null;
    const killDeadline = Date.now() + 5 * 60_000;
    while (!killed && Date.now() < killDeadline) {
      const stage = manifestOf(secondId).stages.find((s) => s.stage === 'transcript');
      const pids = workerPids();
      if (stage?.state === 'active' && pids.length > 0) {
        await sleep(1500);
        execFileSync('taskkill', ['/F', '/IM', 'Memento.Worker.exe']);
        killed = { pids, label: stage.label };
      }
      await sleep(150);
    }
    check(killed, 'the worker was caught transcribing');
    log('killed Memento.Worker.exe', JSON.stringify(killed));
    await page.waitFor(`!!document.querySelector('.tx-failed')`, 'the failed card', 60_000);
    const card = await page.text('.tx-failed');
    await shot('failed-card');
    check(/Retry on CPU/.test(card), 'the failed card offers Retry on CPU');
    log('failed card', card.replace(/\s+/g, ' '));
    const cpuAt = Date.now();
    await page.click({ name: 'Retry on CPU', within: '.tx-failed' });
    await sleep(3000);
    await shot('retry-on-cpu-running');
    const cpuDeadline = Date.now() + 30 * 60_000;
    while (Date.now() < cpuDeadline && manifestOf(secondId).stages.find((s) => s.stage === 'transcript')?.state !== 'done') await sleep(1000);
    summary.timings.cpuRetrySeconds = Math.round((Date.now() - cpuAt) / 1000);
    await page.waitFor(`document.querySelectorAll('.segm').length > 1 && !document.querySelector('.tx-failed')`, 'the CPU transcript', 120_000);
    await shot('retry-on-cpu-done');
    const cpu = transcriptOf(secondId);
    const cpuAudio = manifestOf(secondId).tracks.reduce((s, t) => s + t.durationMs / 1000, 0);
    summary.cpuRetry = { engine: cpu.engine, segments: cpu.segments.length, rtf: Math.round((cpu.engine.durationMs / 1000 / cpuAudio) * 1000) / 1000, history: historyOf(secondId).filter((h) => h.stage === 'transcript').map((h) => `${h.event}: ${h.summary}`) };
    log('Retry on CPU completed', `${cpu.segments.length} lines on ${cpu.engine.device} in ${summary.timings.cpuRetrySeconds} s`);
  }

  await page.click({ name: 'Library' });
  await sleep(1500);
  await shot('library-end');
  await app.close();
  await sleep(1000);
  summary.workersLeftAfterClose = workerPids();
  log('closed', `workers left: ${summary.workersLeftAfterClose.length}`);
  summary.history = historyOf(firstId).map((h) => `${h.stage}/${h.event}: ${h.summary}`);
  summary.stageRuns = logLines(/stage \w+ ran in \d+ ms/).map((l) => l.replace(/^.*Recording /, ''));
  summary.logProblems = logLines(/\[(WRN|ERR|FTL)\]/).map((l) => l.slice(0, 400));
  if (keepModels) {
    cpSync(join(app.memento, 'models'), keepModels, { recursive: true });
    log('models kept', keepModels);
  }
  summary.result = 'passed';
} catch (error) {
  summary.result = `failed: ${error.message}`;
  console.error(error);
  try {
    await shot('failure');
  } catch {
    // The page is gone.
  }
  summary.logProblems = logLines(/\[(WRN|ERR|FTL)\]/).map((l) => l.slice(0, 400));
  await app.kill().catch(() => undefined);
  process.exitCode = 1;
} finally {
  console.log(JSON.stringify(summary, null, 2));
}
