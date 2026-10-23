// The 2.5 "Your own server" end-to-end check, driven through the real UI of the published app over the DevTools
// protocol (clicks, typing and key presses; the bridge is never called). The server is a fake OpenAI-compatible one on
// this PC (fake-openai-server.mjs, 127.0.0.1 on a free port): nothing leaves the PC, no real Ollama or LM Studio is
// used and no AI service is called. Each step saves a screenshot; the run ends with a JSON summary.
//
//   node tools/e2e/m14-server.mjs [--data <dir>] [--out <dir>] [--port <n>] [--simulate] [--delay-ms <n>]
//                                 [--timeout-min <n>]
//
// --delay-ms (default 150) is the fake server's pause between streamed chunks, so a pass is seen writing; a pass that
// finishes before the sheet sees it counts too. On any failure, Ctrl+C or after --timeout-min (default 45) the run
// kills the Memento it started (by its PID) and stops the fake server, then exits 1.
//
// The app runs with LOCALAPPDATA pointed at --data (default artifacts/e2e-data-m14), so the real library, settings and
// logs are never touched, with the simulated audio engine (--simulate is the default and the only mode: nothing is
// recorded or played) and the update feed off. When --data has no transcribed recording, a synthetic WAV written by
// this script (a quiet tone, never played) is imported through "Import audio or video" with automatic transcription
// off, Memento is closed, and a synthetic transcript (two invented speakers, invented words) is written beside it, as a
// finished transcription would; a later run reuses it (its earlier documents are removed first).
//
//  1. Settings › AI and privacy with Allow external AI services off: the address of the fake server is typed and saved
//     with Enter (settings.json holds it before any test), the line under the card says "On this PC · nothing leaves
//     it", Test connection names the server and lists its three models (one GET /v1/models reached the fake server),
//     and qwen3:8b is chosen from the list (saved in settings.json).
//  2. Review › Create document: the Builder's Your own server card names the model and address on this PC; it is
//     chosen and Meeting minutes is generated. No send confirmation appears (Ask before every send is on, but nothing
//     leaves the PC); Live output names the server and shows a pass it answered (streaming, or already finished when
//     the server is fast): the exact request (System / User) and the reply, streamed rather than whole; the viewer opens.
//  3. How this was made names "Your own server · qwen3:8b at 127.0.0.1:<port> (this PC)" and that everything stayed
//     on this PC; History records the generation with the server.
//  4. Every request the fake server got: POST /v1/chat/completions with stream: true, model qwen3:8b, a non-empty
//     system and user message, the transcript's words, no key, no audio or video file names; at least one asked for
//     a JSON schema.
//  5. The fake server then refuses json_schema with 400 (--reject-json-schema), and a second Meeting minutes is
//     generated: the host falls back (json_object or the prompt alone), the document is written, and the requests
//     after the refusals carry no json_schema.
//
// The host half of 2.5 lands separately from the UI; run this on a published build that has both.

import { spawn } from 'node:child_process';
import { existsSync, mkdirSync, readdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { App, repoRoot } from './app.mjs';
import { sleep } from './cdp.mjs';
import { startFakeOpenAiServer } from './fake-openai-server.mjs';

const args = process.argv.slice(2);
const option = (name, fallback) => (args.includes(name) ? args[args.indexOf(name) + 1] : fallback);
const dataRoot = resolve(option('--data', join(repoRoot, 'artifacts', 'e2e-data-m14')));
const out = resolve(option('--out', join(repoRoot, 'artifacts', 'e2e', 'm14')));
// Slow enough that a pass is seen streaming in Live output (about 8 s for a 300-character answer).
const delayMs = Number(option('--delay-ms', '150'));
// The whole run stops (and cleans up) after this long, so nothing is left running if a step hangs.
const runMinutes = Number(option('--timeout-min', '45'));
const MODEL = 'qwen3:8b';
const summary = { steps: [], checks: {} };

let shotIndex = 0;
let page;
const app = new App({ dataRoot, port: Number(option('--port', '9791')), args: ['--simulate-audio', '--update-feed=off'] });
const library = () => join(app.memento, 'Library', 'projects');
const t0 = Date.now();

function log(step, detail = '') {
  const line = `[${((Date.now() - t0) / 1000).toFixed(0).padStart(4)} s] ${step}${detail ? ' · ' + detail : ''}`;
  console.log(line);
  summary.steps.push(line);
}

async function shot(name) {
  await sleep(600);
  shotIndex += 1;
  const file = join(out, `${String(shotIndex).padStart(2, '0')}-${name}.png`);
  await page.screenshot(file);
  return file;
}

function check(condition, message) {
  if (!condition) throw new Error(`Check failed: ${message}`);
  summary.checks[message] = true;
}

const readJson = (file) => JSON.parse(readFileSync(file, 'utf8'));
const projectIds = () => (existsSync(library()) ? readdirSync(library()).sort() : []);
const projectFolder = (id) => join(library(), id);
const manifestOf = (id) => readJson(join(projectFolder(id), 'project.json'));
const historyOf = (id) => readFileSync(join(projectFolder(id), 'history.jsonl'), 'utf8').trim().split('\n').map((l) => JSON.parse(l));
const settingsText = () => (existsSync(join(app.memento, 'settings.json')) ? readFileSync(join(app.memento, 'settings.json'), 'utf8') : '');
const squeeze = (expression) => page.eval(`(${expression}).replace(/\\s+/g, ' ').trim()`);

/** Waits until settings.json (written when settings.set returns) holds `text`. */
async function savedInSettings(text, what) {
  const deadline = Date.now() + 30_000;
  while (!settingsText().includes(text) && Date.now() < deadline) await sleep(250);
  check(settingsText().includes(text), `${what} is saved in settings.json`);
}

/** Answers Windows' file picker titled `title` with `path` (UI Automation). Resolves when done. */
function answerDialog(title, path) {
  return new Promise((done, fail) => {
    const child = spawn('powershell.exe', ['-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', join(repoRoot, 'tools', 'e2e', 'answer-dialog.ps1'), '-Title', title, '-Path', path], { stdio: ['ignore', 'ignore', 'pipe'], windowsHide: true });
    let errors = '';
    child.stderr.on('data', (d) => (errors += d));
    child.once('exit', (code) => (code === 0 ? done() : fail(new Error(`answer-dialog "${title}" exited ${code}: ${errors.trim()}`))));
  });
}

/** A real mouse click at a point. */
async function clickAt(point) {
  await page.send('Input.dispatchMouseEvent', { type: 'mouseMoved', x: point.x, y: point.y });
  await page.send('Input.dispatchMouseEvent', { type: 'mousePressed', x: point.x, y: point.y, button: 'left', clickCount: 1 });
  await page.send('Input.dispatchMouseEvent', { type: 'mouseReleased', x: point.x, y: point.y, button: 'left', clickCount: 1 });
}

/** Back to the Library from any screen. */
async function home() {
  for (let i = 0; i < 8; i++) {
    if (await page.eval(`/Newest first|Your library is empty/.test(document.body.innerText) && !document.querySelector('[data-spoke-back]')`)) return;
    if (await page.locate({ name: 'Library' })) {
      await page.click({ name: 'Library' });
    } else if (await page.locate('[data-spoke-back]')) {
      await page.click({ selector: '[data-spoke-back]' });
    }
    await sleep(700);
  }
}

async function openSettings(section) {
  await home();
  await page.click({ name: 'Settings' });
  await page.click({ name: section });
}

let reviewTitle = null;

async function openReview() {
  await home();
  const row = await page.eval(`(() => { const t = [...document.querySelectorAll('.lib-item .row-title')].find((e) => e.innerText.trim() === ${JSON.stringify(reviewTitle)}); if (!t) return null; t.scrollIntoView({ block: 'center' }); const r = t.getBoundingClientRect(); return { x: r.left + r.width / 2, y: r.top + r.height / 2 }; })()`);
  if (row === null) throw new Error(`No Library row titled "${reviewTitle}"`);
  await clickAt(row);
  await page.waitFor(`!!document.querySelector('.review-panes') && document.querySelectorAll('.segm').length > 3`, 'Review with the transcript', 30_000);
}

/** A 20-second mono WAV of a quiet 220 Hz tone: something to import. It is written to the data root and never played. */
function fixtureWav(file) {
  const rate = 16_000;
  const frames = 20 * rate;
  const data = Buffer.alloc(frames * 2);
  for (let i = 0; i < frames; i++) data.writeInt16LE(Math.round(0.05 * 32767 * Math.sin((2 * Math.PI * 220 * i) / rate)), i * 2);
  const header = Buffer.alloc(44);
  header.write('RIFF', 0);
  header.writeUInt32LE(36 + data.length, 4);
  header.write('WAVEfmt ', 8);
  header.writeUInt32LE(16, 16);
  header.writeUInt16LE(1, 20);
  header.writeUInt16LE(1, 22);
  header.writeUInt32LE(rate, 24);
  header.writeUInt32LE(rate * 2, 28);
  header.writeUInt16LE(2, 32);
  header.writeUInt16LE(16, 34);
  header.write('data', 36);
  header.writeUInt32LE(data.length, 40);
  writeFileSync(file, Buffer.concat([header, data]));
  return file;
}

/** The synthetic transcript's lines (invented speakers and words); the requests must carry them. */
const LINES = [
  ['spk1', 'Good morning. Today we choose the venue for the spring workshop.'],
  ['spk2', 'The hall by the river holds sixty people and is free in April.'],
  ['spk1', 'Then we book the river hall for the second week of April.'],
  ['spk2', 'I will send the booking form to the venue by Friday.'],
  ['spk1', 'We still need someone to plan the catering.'],
  ['spk2', 'Let us decide the catering at the next meeting.'],
  ['spk1', 'Agreed. The next meeting is on Tuesday at ten.'],
  ['spk2', 'Thank you, that is all for today.'],
];

/**
 * A recording with a transcript: the one an earlier run left (its documents removed), else the fixture imported with
 * automatic transcription off and a synthetic transcript.json written beside it while Memento is closed.
 */
async function transcribedRecording() {
  let [id] = projectIds().filter((p) => existsSync(join(projectFolder(p), 'transcript.json')));
  if (id !== undefined) {
    rmSync(join(projectFolder(id), 'documents'), { recursive: true, force: true });
    return id;
  }
  [id] = projectIds();
  if (id === undefined) {
    await openSettings('Transcription');
    if ((await page.eval(`document.querySelector('[aria-label="Transcribe automatically"]')?.getAttribute('aria-checked')`)) === 'true') {
      await page.click({ name: 'Transcribe automatically' });
    }
    await home();
    const wav = fixtureWav(join(dataRoot, 'm14-fixture.wav'));
    const answered = answerDialog('Import audio or video', wav);
    if (await page.locate({ name: 'Import audio or video' })) {
      await page.click({ name: 'Import audio or video' });
    } else {
      await page.click({ name: 'More library actions' });
      await page.click({ name: 'Import audio or video…' });
    }
    await answered;
    await page.waitFor(`document.querySelectorAll('.lib-item').length >= 1`, 'the imported recording', 60_000);
    [id] = projectIds();
  }
  const deadline = Date.now() + 10 * 60_000;
  while (manifestOf(id).stages.find((s) => s.stage === 'stored')?.state !== 'done') {
    if (Date.now() > deadline) throw new Error(`${id} was not stored after 10 min`);
    await sleep(1000);
  }
  await app.close();
  const segments = LINES.map(([speaker, text], i) => ({
    id: `s${String(i + 1).padStart(4, '0')}`,
    start: i * 2.5,
    end: i * 2.5 + 2.2,
    track: 'imported',
    speaker,
    speakerConfidence: 0.9,
    text,
    confidence: 0.95,
    words: [],
    edited: null,
  }));
  const transcript = {
    schemaVersion: 1,
    language: 'en',
    languageDetected: false,
    engine: { name: 'e2e seed', model: 'none', device: 'none', version: '1', durationMs: 0 },
    speakers: [
      { id: 'spk1', name: 'Speaker 1', renamed: false, color: 1, talkTimeMs: 10_000 },
      { id: 'spk2', name: 'Speaker 2', renamed: false, color: 2, talkTimeMs: 10_000 },
    ],
    segments,
    reviewed: false,
    version: 1,
    lowConfidenceThreshold: 0.5,
    coverageGaps: [],
    complete: true,
  };
  writeFileSync(join(projectFolder(id), 'transcript.json'), JSON.stringify(transcript, null, 2));
  page = await app.start();
  return id;
}

/** The Builder for the recording on Inputs and output with Your own server chosen; returns its card's text. */
async function builderWithServer() {
  await openReview();
  await page.click({ name: 'Create document' });
  await page.waitFor(`!!document.querySelector('[data-card="m01"]')`, 'the Builder', 30_000);
  await page.click({ name: 'Inputs and output' });
  await page.waitFor(`!!document.querySelector('.providers label.provider[data-provider="server"]')`, 'the Your own server card', 20_000);
  const radio = await page.eval(`(() => { const r = document.querySelector('.providers label.provider[data-provider="server"] input[type=radio]'); r.scrollIntoView({ block: 'center' }); const b = r.getBoundingClientRect(); return { x: b.left + b.width / 2, y: b.top + b.height / 2, checked: r.checked, disabled: r.disabled }; })()`);
  check(!radio.disabled, 'the Your own server card can be chosen');
  if (!radio.checked) await clickAt(radio);
  await page.waitFor(`document.querySelector('.providers label.provider[data-provider="server"] input[type=radio]')?.checked === true`, 'Your own server chosen', 10_000);
  return squeeze(`document.querySelector('.providers label.provider[data-provider="server"]')?.innerText ?? ''`);
}

/** Presses Generate and waits for the viewer, the send confirmation or the failure card; `live` watches Live output first. */
async function generate(label, { live = false } = {}) {
  const started = Date.now();
  await page.click({ selector: '.spoke-primary' }, { timeoutMs: 60_000 });
  await page.waitFor(`!!document.querySelector('.ai-failure, #confirm-send-title, .doc-paper article, .gen-card')`, `${label} to start`, 20_000);
  await shot(`${label}-generating`);
  if (live) await watchLiveOutput(label);
  const ended = await page.waitFor(
    `document.querySelector('.ai-failure') ? 'failed' : document.querySelector('#confirm-send-title') ? 'confirm' : document.querySelector('.doc-paper article') ? 'done' : ''`,
    `${label} to finish`,
    10 * 60_000,
  );
  return { ended, seconds: Math.round((Date.now() - started) / 1000) };
}

/**
 * The first pass the server answers (not a step done in code) once it is visibly streaming ("Writing · N tokens") or
 * finished, as { id, state: 'streaming' | 'done', tokens, meta }; null while none is. A fast server (or a host that does
 * not count tokens while streaming) can finish a pass before it is ever seen writing, so a finished pass counts too.
 */
const modelPass = `(() => {
  for (const row of document.querySelectorAll('.live-sheet .live-pass')) {
    const meta = row.querySelector('.live-pass-meta')?.innerText.trim() ?? '';
    if (meta.startsWith('In code')) continue;
    const writing = /^Writing · ([\\d,]+) tokens?/.exec(meta);
    if (writing && Number(writing[1].replace(/,/g, '')) > 0) return { id: row.dataset.pass, state: 'streaming', tokens: Number(writing[1].replace(/,/g, '')), meta };
    if (row.querySelector('.live-dot--done')) {
      const done = /^([\\d,]+) tokens?/.exec(meta);
      return { id: row.dataset.pass, state: 'done', tokens: done ? Number(done[1].replace(/,/g, '')) : null, meta };
    }
  }
  return null;
})()`;

/**
 * Live output while the server writes: the sheet names the server on this PC, a pass the server answered shows the
 * exact request (System / User) and its reply, which streamed (no "arrive whole" note). The sheet is opened from the
 * progress card, or from the viewer when the generation finished first; both are accepted.
 */
async function watchLiveOutput(label) {
  await page.click({ name: 'Show live output' }, { timeoutMs: 60_000 });
  await page.waitFor(`!!document.querySelector('.live-sheet')`, 'the Live output sheet', 10_000);
  const seen = await page.waitFor(modelPass, 'a pass the server streams or has finished', 5 * 60_000);
  const sub = await page.text('.live-sub');
  let later = seen.state === 'done' ? 'done' : null;
  if (seen.state === 'streaming') {
    await sleep(900);
    const meta = await page.eval(`document.querySelector('.live-sheet .live-pass[data-pass="${seen.id}"] .live-pass-meta')?.innerText.trim() ?? ''`);
    const writing = /^Writing · ([\d,]+) tokens?/.exec(meta);
    later = writing ? Number(writing[1].replace(/,/g, '')) : 'done';
  }
  // Show that pass (choosing it pauses following, which is fine here) and read its request and reply.
  await page.click({ selector: `.live-sheet .live-pass[data-pass="${seen.id}"]` });
  await page.waitFor(`!!document.querySelector('.live-sheet .live-request')`, 'the pass request', 10_000);
  if (seen.state === 'done') await page.waitFor(`(document.querySelector('.live-sheet .live-reply')?.innerText ?? '').trim().length > 0`, 'the pass reply', 10_000);
  const request = await page.eval(`document.querySelector('.live-sheet .live-request')?.innerText ?? ''`);
  const reply = await page.eval(`document.querySelector('.live-sheet .live-reply')?.innerText ?? ''`);
  const whole = await page.eval(`[...document.querySelectorAll('.live-sheet .live-whole')].map((e) => e.innerText).join(' ')`);
  await shot(`${label}-live-output`);
  check(request.startsWith('System\n') && request.includes('User\n'), 'the Live output shows the exact request the server reads');
  check(reply.trim().length > 0, `the reply is in the Live output (${reply.trim().slice(0, 40)}…)`);
  check(!/arrive whole/.test(whole), 'the reply streamed (no "arrive whole" note)');
  if (seen.state === 'streaming') check(later === 'done' || later > seen.tokens, `the token count grows while the pass runs (${seen.tokens} → ${later})`);
  check(/^Your own server · qwen3:8b( at [^ ]+)? \(this PC\) · (Writing|Finished · read only)/.test(sub), `the sheet names the server on this PC (${sub})`);
  check((await page.text('.sheet-footer-note')).includes('nothing leaves it'), 'the sheet says nothing leaves this PC');
  summary.liveOutput = { sub, seen: seen.state, pass: seen.meta, tokens: seen.tokens, later, passes: await page.eval(`document.querySelectorAll('.live-sheet .live-pass').length`) };
  log('live output', `${sub} · ${seen.state === 'streaming' ? `tokens ${seen.tokens} → ${later}` : `first seen finished (${seen.meta})`}`);
  await page.key('Escape');
  await page.waitFor(`!document.querySelector('.live-sheet')`, 'the sheet to close', 10_000);
}

/** The text of every message in a chat request. */
const messageText = (body) => (body?.messages ?? []).map((m) => (typeof m.content === 'string' ? m.content : (m.content ?? []).map((p) => p.text ?? '').join(''))).join('\n');
const roleText = (body, role) => (body?.messages ?? []).filter((m) => m.role === role).map((m) => (typeof m.content === 'string' ? m.content : '')).join('\n');

let fake;
let cleaned = null;

/**
 * Stops what this run started, once: the Memento it launched (by its PID, through App.kill: taskkill /F /T /PID) when
 * it is still running, and the fake server. Each step is bounded so a hung one cannot keep the run alive.
 */
function cleanup() {
  cleaned ??= (async () => {
    const child = app.process;
    if (child && child.exitCode === null && child.signalCode === null) {
      await Promise.race([app.kill(), sleep(20_000)]);
    }
    if (fake) await Promise.race([fake.close(), sleep(5_000)]);
  })();
  return cleaned;
}

/** A failure or interruption: say why, keep the summary, clean up and exit non-zero. */
async function fail(error) {
  summary.error = error?.message ?? String(error);
  console.error(error);
  try {
    if (page) await Promise.race([shot('failure'), sleep(10_000)]);
  } catch {
    // The page is gone.
  }
  console.log(JSON.stringify(summary, null, 2));
  await cleanup();
  process.exit(1);
}

for (const signal of ['SIGINT', 'SIGTERM', 'SIGBREAK']) process.once(signal, () => void fail(new Error(`Stopped by ${signal}`)));
process.once('unhandledRejection', (reason) => void fail(reason));
const watchdog = setTimeout(() => void fail(new Error(`The run took longer than ${runMinutes} min`)), runMinutes * 60_000);

try {
  rmSync(out, { recursive: true, force: true });
  mkdirSync(out, { recursive: true });
  fake = await startFakeOpenAiServer({ record: join(out, 'requests.json'), delayMs });
  const hostPort = fake.url.replace(/^http:\/\//, '');
  log('fake server', `${fake.url} (models qwen3:8b, llama3.1:8b, gemma3:4b)`);
  page = await app.start();

  const recordingId = await transcribedRecording();
  reviewTitle = manifestOf(recordingId).details.title;
  log('recording', `${recordingId} (${reviewTitle})`);

  // 1. Settings › AI and privacy: external AI stays off; the address, Test connection and the model.
  await openSettings('AI and privacy');
  await page.waitFor(`!!document.querySelector('#server-address')`, 'the Your own server card', 20_000);
  check((await page.eval(`document.querySelector('[aria-label="Allow external AI services"]')?.getAttribute('aria-checked')`)) !== 'true', 'external AI is off');
  await page.click({ selector: '#server-address' });
  await page.eval(`(() => { const el = document.querySelector('#server-address'); el.select(); return true; })()`);
  await page.type(fake.url);
  await page.key('Enter');
  await savedInSettings(fake.url, 'the address (before any test)');
  check(fake.requests.length === 0, 'saving the address sent nothing to the server');
  await page.waitFor(`document.querySelector('.server-place')?.innerText === 'On this PC · nothing leaves it'`, 'the line under the card', 15_000);
  summary.placeLine = await page.text('.server-place');

  await page.click({ name: 'Test connection' });
  await page.waitFor(`/· 3 models$/.test(document.querySelector('.server-probe')?.innerText ?? '')`, 'the probe result', 30_000);
  summary.probeLine = await page.text('.server-probe');
  check(fake.requests.some((r) => r.method === 'GET' && r.path === '/v1/models'), 'Test connection asked /v1/models');
  check(!fake.requests.some((r) => r.method === 'POST'), 'Test connection sent no chat request');
  log('Test connection', summary.probeLine);

  await page.click({ selector: '[aria-label^="Server model: "]' });
  const listed = await page.eval(`[...document.querySelectorAll('[role="option"]')].map((o) => o.innerText.trim())`);
  check(['qwen3:8b', 'llama3.1:8b', 'gemma3:4b', 'Other model…'].every((m) => listed.includes(m)), `the model list holds the server's models and Other model… (${listed.join(', ')})`);
  await page.click({ role: 'option', name: MODEL });
  await page.waitFor(`document.querySelector('#server-model-note')?.innerText === 'Requests go to ${MODEL}.'`, 'the model chosen', 15_000);
  await savedInSettings(`"${MODEL}"`, 'the model');
  summary.contextTokens = await page.eval(`document.querySelector('#server-context')?.value`);
  await page.eval(`document.querySelector('#server-address').scrollIntoView({ block: 'start' })`);
  await shot('settings-your-own-server');
  log('Settings › AI and privacy', `${summary.placeLine}; ${MODEL}; context ${summary.contextTokens}`);

  // 2. The Builder: the card, Generate without a confirmation, Live output.
  const card = await builderWithServer();
  summary.builderCard = card;
  check(card.includes(`${MODEL} at ${hostPort} · on this PC`), `the Builder's card names the model and the server on this PC (${card})`);
  await shot('builder-server-card');
  const sentBefore = fake.requests.length;
  const first = await generate('server', { live: true });
  check(first.ended !== 'confirm', 'no send confirmation for a server on this PC');
  check(first.ended === 'done', `the server wrote the minutes (${first.ended}: ${await squeeze(`document.querySelector('.ai-failure')?.innerText ?? ''`)})`);
  summary.firstSeconds = first.seconds;
  await shot('viewer-server-minutes');

  // 3. How this was made and History.
  await page.waitFor(`!!document.querySelector('.how-made .how-facts')`, 'How this was made', 20_000);
  const how = await squeeze(`document.querySelector('.how-made')?.innerText ?? ''`);
  summary.howMade = how;
  check(new RegExp(`Your own server · ${MODEL}( at ${hostPort.replace(/\./g, '\\.')})? \\(this PC\\)`).test(how), `How this was made names the server on this PC (${how})`);
  check(/Everything stayed on this PC/.test(how), 'How this was made says everything stayed on this PC');
  await page.eval(`document.querySelector('.how-made')?.scrollIntoView({ block: 'center' })`);
  await shot('viewer-how-made');
  // The history line may be written just after the viewer opens.
  const historyBy = Date.now() + 20_000;
  while (!historyOf(recordingId).some((h) => h.stage === 'minutes' && /Your own server/.test(h.summary)) && Date.now() < historyBy) await sleep(500);
  const history = historyOf(recordingId).filter((h) => h.stage === 'minutes');
  summary.history = history.map((h) => `${h.summary} | ${h.detail ?? ''}`);
  check(history.some((h) => /Your own server/.test(h.summary)), `History records the generation with the server (${summary.history.at(-1)})`);

  // 4. What the fake server received.
  const chats = fake.requests.slice(sentBefore).filter((r) => r.method === 'POST' && r.path === '/v1/chat/completions');
  summary.requests = { count: chats.length, schemas: chats.filter((r) => r.body?.response_format?.type === 'json_schema').length, formats: [...new Set(chats.map((r) => r.body?.response_format?.type ?? 'none'))] };
  check(chats.length >= 1, `the minutes were written through the server (${chats.length} requests)`);
  check(fake.requests.slice(sentBefore).every((r) => r.path.startsWith('/v1/') || r.path.startsWith('/api/')), 'only the server API was used');
  check(chats.every((r) => r.body?.stream === true), 'every request streams (stream: true)');
  check(chats.every((r) => r.body?.model === MODEL), `every request names ${MODEL}`);
  check(chats.every((r) => roleText(r.body, 'system').trim() !== '' && roleText(r.body, 'user').trim() !== ''), 'every request has a system and a user message');
  check(chats.some((r) => messageText(r.body).includes('river hall')), 'the transcript reached the server');
  check(chats.every((r) => !r.authorization), 'no key was sent (none is saved)');
  check(chats.every((r) => !/\.(mp4|mkv|webm|mov|avi|m4v)\b|video-[\w-]+\.|m14-fixture\.wav|\.flac\b/i.test(JSON.stringify(r.body))), 'no audio or video file names were sent');
  check(summary.requests.schemas >= 1, `the host asked for structured output (${summary.requests.formats.join(', ')})`);
  log('requests', `${chats.length} chat requests · ${summary.requests.formats.join(', ')} · all streamed, ${MODEL}, no key`);

  // 5. A server without structured outputs: json_schema refused with 400, the host falls back.
  fake.options.rejectJsonSchema = true;
  const fallbackFrom = fake.requests.length;
  await builderWithServer();
  const second = await generate('server-fallback');
  const tail = fake.requests.slice(fallbackFrom).filter((r) => r.method === 'POST' && r.path === '/v1/chat/completions');
  const refused = tail.filter((r) => r.status === 400);
  const answered = tail.filter((r) => r.status === 200);
  summary.fallback = { ended: second.ended, refused: refused.length, answered: answered.length, formats: [...new Set(answered.map((r) => r.body?.response_format?.type ?? 'none'))] };
  check(refused.length >= 1 && refused.every((r) => r.body?.response_format?.type === 'json_schema'), `the server refused json_schema (${refused.length})`);
  check(answered.length >= 1 && answered.every((r) => r.body?.response_format?.type !== 'json_schema'), `the host fell back without json_schema (${summary.fallback.formats.join(', ')})`);
  check(second.ended === 'done', `the minutes were written after the fallback (${second.ended}: ${await squeeze(`document.querySelector('.ai-failure')?.innerText ?? ''`)})`);
  await shot('viewer-after-fallback');
  log('fallback', `${refused.length} refused, ${answered.length} answered as ${summary.fallback.formats.join(', ')}`);
  fake.options.rejectJsonSchema = false;

  summary.result = 'passed';
  console.log(JSON.stringify(summary, null, 2));
  // Closed the normal way; cleanup() below kills it by PID only if it did not exit.
  await app.close().catch((error) => console.error(error));
  clearTimeout(watchdog);
  await cleanup();
  process.exit(0);
} catch (error) {
  clearTimeout(watchdog);
  await fail(error);
}
