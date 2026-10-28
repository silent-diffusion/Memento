// The 2.9.0 Ask end-to-end check, driven through the real UI of a published app over the DevTools protocol (clicks,
// typing and key presses; the bridge is never called). The AI is the fake OpenAI-compatible server on this PC
// (fake-openai-server.mjs, 127.0.0.1 on a free port) configured as Your own server: nothing leaves the PC and no real
// model, Ollama, LM Studio or AI service is used. Each step saves a screenshot; the run ends with a JSON summary.
//
//   node tools/e2e/m15-ask.mjs [--exe <Memento.exe>] [--data <dir>] [--out <dir>] [--port <n>] [--delay-ms <n>]
//                              [--timeout-min <n>]
//
// --exe defaults to artifacts/publish-ask/win-x64/Memento.exe (publish the branch there first). The app runs with
// LOCALAPPDATA pointed at --data (default artifacts/e2e-data-m15), the simulated audio engine and the update feed off.
// On any failure, Ctrl+C or after --timeout-min (default 20) the run kills the Memento it started (by its PID only) and
// stops the fake server, then exits 1.
//
// When --data has no transcribed recording, a synthetic WAV (a quiet tone, never played) is imported with automatic
// transcription off, Memento is closed and a synthetic 300-line transcript (invented speakers and words) is written
// beside it; a later run reuses it and deletes its earlier Ask conversation first.
//
//  1. Settings › AI and privacy: the fake server's address, Test connection, qwen3:8b, a 131,072-token context.
//  2. Review › Ask: the server is the provider and the note says nothing leaves the PC; a question streams its answer
//     (the text grows while it is written), which cites two lines as chips (and one line it was not given, shown as
//     unverified) and says it was answered from the whole recording.
//  3. A citation chip opens Review at its line (?t= in the address); Back returns to the conversation.
//  4. Stop mid-stream keeps a stopped answer.
//  5. A 2,048-token context: the next answer is given sections ("Answered from N of M sections …") and lists them.
//  6. Memento is restarted: the conversation is still there.
//  7. Clear conversation (after its confirmation) empties it and deletes ask/conversation.json.
//  8. What the fake server received: streamed chat requests for qwen3:8b with the question and the transcript, the
//     whole recording first and sections for the small context, no key, no audio or video file names.

import { spawn } from 'node:child_process';
import { existsSync, mkdirSync, readdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { App, repoRoot } from './app.mjs';
import { sleep } from './cdp.mjs';
import { startFakeOpenAiServer } from './fake-openai-server.mjs';

const args = process.argv.slice(2);
const option = (name, fallback) => (args.includes(name) ? args[args.indexOf(name) + 1] : fallback);
const exe = resolve(option('--exe', join(repoRoot, 'artifacts', 'publish-ask', 'win-x64', 'Memento.exe')));
const dataRoot = resolve(option('--data', join(repoRoot, 'artifacts', 'e2e-data-m15')));
const out = resolve(option('--out', join(repoRoot, 'artifacts', 'e2e', 'm15')));
const delayMs = Number(option('--delay-ms', '60'));
const runMinutes = Number(option('--timeout-min', '20'));
const MODEL = 'qwen3:8b';
const summary = { steps: [], checks: {}, passed: 0, failed: 0 };

let shotIndex = 0;
let page;
const app = new App({ exe, dataRoot, port: Number(option('--port', '9795')), args: ['--simulate-audio', '--update-feed=off'] });
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
  if (!condition) {
    summary.failed += 1;
    throw new Error(`Check failed: ${message}`);
  }
  summary.passed += 1;
  summary.checks[message] = true;
}

const readJson = (file) => JSON.parse(readFileSync(file, 'utf8'));
const projectIds = () => (existsSync(library()) ? readdirSync(library()).sort() : []);
const projectFolder = (id) => join(library(), id);
const manifestOf = (id) => readJson(join(projectFolder(id), 'project.json'));
const conversationFile = (id) => join(projectFolder(id), 'ask', 'conversation.json');
const settingsText = () => (existsSync(join(app.memento, 'settings.json')) ? readFileSync(join(app.memento, 'settings.json'), 'utf8') : '');
const squeeze = (expression) => page.eval(`(${expression}).replace(/\\s+/g, ' ').trim()`);

async function savedInSettings(pattern, what) {
  const deadline = Date.now() + 30_000;
  while (!pattern.test(settingsText()) && Date.now() < deadline) await sleep(250);
  check(pattern.test(settingsText()), `${what} is saved in settings.json`);
}

function answerDialog(title, path) {
  return new Promise((done, fail) => {
    const child = spawn('powershell.exe', ['-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', join(repoRoot, 'tools', 'e2e', 'answer-dialog.ps1'), '-Title', title, '-Path', path], { stdio: ['ignore', 'ignore', 'pipe'], windowsHide: true });
    let errors = '';
    child.stderr.on('data', (d) => (errors += d));
    child.once('exit', (code) => (code === 0 ? done() : fail(new Error(`answer-dialog "${title}" exited ${code}: ${errors.trim()}`))));
  });
}

async function clickAt(point) {
  await page.send('Input.dispatchMouseEvent', { type: 'mouseMoved', x: point.x, y: point.y });
  await page.send('Input.dispatchMouseEvent', { type: 'mousePressed', x: point.x, y: point.y, button: 'left', clickCount: 1 });
  await page.send('Input.dispatchMouseEvent', { type: 'mouseReleased', x: point.x, y: point.y, button: 'left', clickCount: 1 });
}

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

async function openAsk() {
  await openReview();
  await page.click({ name: 'Ask', within: '.spoke-actions' });
  await page.waitFor(`!!document.querySelector('#ask-question')`, 'the Ask page', 20_000);
}

const answerCount = `document.querySelectorAll('.ask-answer:not(.ask-answer--live)').length`;
const lastAnswer = `[...document.querySelectorAll('.ask-answer:not(.ask-answer--live)')].at(-1)`;

/** Types a question and presses Enter; resolves once the answer is written (or `onLive` returns). */
async function ask(question, { onLive } = {}) {
  const before = await page.eval(answerCount);
  await page.click({ selector: '#ask-question' });
  await page.type(question);
  await page.key('Enter');
  if (onLive) return onLive(before);
  await page.waitFor(`${answerCount} > ${before}`, `the answer to "${question}"`, 120_000);
  return before;
}

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

/** 300 invented lines: the river hall exchange first, then distinct filler about a backlog. */
function syntheticLines() {
  const opening = [
    ['spk1', 'Good morning. Today we choose the venue for the spring workshop.'],
    ['spk2', 'The hall by the river holds sixty people and is free in April.'],
    ['spk1', 'Then we book the river hall for the second week of April.'],
    ['spk2', 'I will send the booking form to the venue by Friday.'],
  ];
  const topics = ['printer queue', 'parking permits', 'newsletter layout', 'office plants', 'badge readers', 'coffee machine', 'shared calendar', 'locker keys'];
  const filler = Array.from({ length: 296 }, (_, i) => [i % 2 === 0 ? 'spk1' : 'spk2', `Item ${i + 1} of the backlog is about the ${topics[i % topics.length]}, and we will look at it again next month.`]);
  return [...opening, ...filler];
}

async function transcribedRecording() {
  let [id] = projectIds().filter((p) => existsSync(join(projectFolder(p), 'transcript.json')));
  if (id !== undefined) {
    rmSync(join(projectFolder(id), 'ask'), { recursive: true, force: true });
    return id;
  }
  [id] = projectIds();
  if (id === undefined) {
    await openSettings('Transcription');
    if ((await page.eval(`document.querySelector('[aria-label="Transcribe automatically"]')?.getAttribute('aria-checked')`)) === 'true') {
      await page.click({ name: 'Transcribe automatically' });
    }
    await home();
    const wav = fixtureWav(join(dataRoot, 'm15-fixture.wav'));
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
  const segments = syntheticLines().map(([speaker, text], i) => ({
    id: `s${String(i + 1).padStart(4, '0')}`,
    start: i * 4,
    end: i * 4 + 3.6,
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
      { id: 'spk1', name: 'Speaker 1', renamed: false, color: 1, talkTimeMs: 600_000 },
      { id: 'spk2', name: 'Speaker 2', renamed: false, color: 2, talkTimeMs: 600_000 },
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

async function setContext(tokens) {
  await openSettings('AI and privacy');
  await page.waitFor(`!!document.querySelector('#server-context')`, 'the context field', 20_000);
  await page.click({ selector: '#server-context' });
  await page.eval(`(() => { document.querySelector('#server-context').select(); return true; })()`);
  await page.type(String(tokens));
  await page.key('Enter');
  await savedInSettings(new RegExp(`"contextTokens"\\s*:\\s*${tokens}\\b`), `the context of ${tokens} tokens`);
}

const messageText = (body) => (body?.messages ?? []).map((m) => (typeof m.content === 'string' ? m.content : '')).join('\n');
const isAsk = (r) => r.method === 'POST' && r.path === '/v1/chat/completions' && (r.body?.messages ?? []).some((m) => m.role === 'system' && String(m.content).includes('You answer questions about one recording'));

let fake;
let cleaned = null;

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

async function fail(error) {
  summary.error = error?.message ?? String(error);
  summary.result = 'failed';
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
  if (!existsSync(exe)) throw new Error(`${exe} is missing: publish the branch there first`);
  rmSync(out, { recursive: true, force: true });
  mkdirSync(out, { recursive: true });
  fake = await startFakeOpenAiServer({ record: join(out, 'requests.json'), delayMs });
  log('fake server', fake.url);
  page = await app.start();

  const recordingId = await transcribedRecording();
  reviewTitle = manifestOf(recordingId).details.title;
  log('recording', `${recordingId} (${reviewTitle})`);

  // 1. Your own server at the fake, with a large context.
  await openSettings('AI and privacy');
  await page.waitFor(`!!document.querySelector('#server-address')`, 'the Your own server card', 20_000);
  if ((await page.eval(`document.querySelector('#server-address').value`)) !== fake.url) {
    await page.click({ selector: '#server-address' });
    await page.eval(`(() => { document.querySelector('#server-address').select(); return true; })()`);
    await page.type(fake.url);
    await page.key('Enter');
  }
  await savedInSettings(new RegExp(fake.url.replace(/[.:/]/g, '\\$&')), 'the fake server address');
  await page.click({ name: 'Test connection' });
  await page.waitFor(`/· 3 models$/.test(document.querySelector('.server-probe')?.innerText ?? '')`, 'the probe result', 30_000);
  await page.click({ selector: '[aria-label^="Server model: "]' });
  await page.click({ role: 'option', name: MODEL });
  await savedInSettings(new RegExp(`"${MODEL}"`), 'the model');
  await setContext(131072);
  log('settings', `${fake.url} · ${MODEL} · 131072 tokens`);

  // 2. Ask from Review; a streamed answer with citations, from the whole recording.
  await openAsk();
  const note = await page.text('.ask-composer .field-note');
  check(note.includes('runs on this PC: nothing leaves it'), `the note says nothing leaves this PC (${note})`);
  check((await page.eval(`document.querySelector('.ask-provider select').value`)) === 'server', 'Your own server answers');
  await shot('ask-empty');
  const lengths = [];
  await ask('What did they decide about the river hall?', {
    onLive: async (before) => {
      await page.waitFor(`(document.querySelector('.ask-answer--live .ask-text')?.innerText ?? '').length > 20`, 'the answer to start streaming', 60_000);
      for (let i = 0; i < 6 && (await page.eval(`!!document.querySelector('.ask-answer--live')`)); i++) {
        lengths.push(await page.eval(`(document.querySelector('.ask-answer--live .ask-text')?.innerText ?? '').length`));
        await sleep(300);
      }
      await shot('ask-streaming');
      await page.waitFor(`${answerCount} > ${before}`, 'the first answer', 120_000);
    },
  });
  check(lengths.length >= 2 && lengths.at(-1) > lengths[0], `the answer grew while it was written (${lengths.join(' → ')})`);
  const chips = await page.eval(`${lastAnswer}.querySelectorAll('button.ask-cite').length`);
  const unverified = await page.eval(`${lastAnswer}.querySelectorAll('.ask-cite--none').length`);
  check(chips >= 1, `the answer cites lines as chips (${chips})`);
  check(unverified >= 1, `a line the model was not given is shown as unverified (${unverified})`);
  const firstSummary = await squeeze(`${lastAnswer}.querySelector('.ask-sections summary')?.innerText ?? ''`);
  check(/^Answered from the whole recording \(300 lines\)$/.test(firstSummary), `the first answer was given the whole recording (${firstSummary})`);
  await shot('ask-answer');
  log('answer', `${chips} chips, ${unverified} unverified · ${firstSummary}`);

  // 3. A citation opens Review at its line; Back returns.
  const chipLabel = await page.eval(`${lastAnswer}.querySelector('button.ask-cite').innerText.trim()`);
  await page.click({ selector: '.ask-answer:not(.ask-answer--live) button.ask-cite' });
  await page.waitFor(`!!document.querySelector('.review-panes') && /#\\/review\\/.+\\?t=/.test(location.hash)`, 'Review at the cited line', 30_000);
  const hash = await page.eval('location.hash');
  await shot('review-at-citation');
  check(/\?t=\d/.test(hash), `Review opened at the cited moment (${chipLabel}, ${hash})`);
  await page.click({ selector: '[data-spoke-back]' });
  await page.waitFor(`!!document.querySelector('#ask-question') && ${answerCount} === 1`, 'Back to the conversation', 20_000);
  check(true, 'Back returns to the conversation with its answer');
  log('citation', `${chipLabel} → ${hash} → back`);

  // 4. Stop mid-stream.
  fake.options.delayMs = 400;
  await ask('Who will send the booking form?', {
    onLive: async (before) => {
      await page.waitFor(`(document.querySelector('.ask-answer--live .ask-text')?.innerText ?? '').length > 20`, 'the second answer to start', 60_000);
      await page.click({ name: 'Stop', within: '.ask-answer--live' });
      await page.waitFor(`${answerCount} > ${before}`, 'the stopped answer', 30_000);
    },
  });
  fake.options.delayMs = delayMs;
  const stopped = await squeeze(`${lastAnswer}.innerText`);
  check(/stopped before it finished/.test(stopped), 'Stop keeps a stopped answer');
  check(!/whole answer, written slowly/.test(stopped), 'the stopped answer holds only what had arrived');
  await shot('ask-stopped');
  log('stop', stopped.slice(0, 80));

  // 5. A small context: sections.
  await setContext(2048);
  await openAsk();
  await ask('Tell me about the parking permits.');
  const smallSummary = await squeeze(`${lastAnswer}.querySelector('.ask-sections summary')?.innerText ?? ''`);
  check(/^Answered from \d+ of \d+ sections of the transcript$/.test(smallSummary), `the small context answer names its sections (${smallSummary})`);
  await page.click({ selector: '.ask-answer:last-of-type .ask-sections summary' });
  const listed = await page.eval(`${lastAnswer}.querySelectorAll('.ask-section-list li').length`);
  check(listed >= 1, `the sections used are listed (${listed})`);
  await shot('ask-sections');
  log('small context', `${smallSummary}, ${listed} listed`);

  // 6. Restart: the conversation is kept.
  await app.close();
  page = await app.start();
  await openAsk();
  await page.waitFor(`${answerCount} === 3`, 'the kept conversation', 20_000);
  check((await page.eval(`document.querySelectorAll('.ask-question').length`)) === 3, 'the three questions are kept after a restart');
  check(existsSync(conversationFile(recordingId)) && readJson(conversationFile(recordingId)).schemaVersion === 1, 'ask/conversation.json holds schema version 1');
  await shot('ask-after-restart');
  log('restart', 'conversation kept');

  // 7. Clear.
  await page.click({ name: 'Clear conversation' });
  await page.click({ name: 'Clear', within: '.ask-banner' });
  await page.waitFor(`${answerCount} === 0 && !!document.querySelector('.ask-empty')`, 'the cleared conversation', 20_000);
  check(!existsSync(conversationFile(recordingId)), 'Clear deletes ask/conversation.json');
  await shot('ask-cleared');

  // 8. What the fake server received.
  const asks = fake.requests.filter(isAsk);
  check(asks.length >= 3, `the questions reached the server (${asks.length})`);
  check(asks.every((r) => r.body?.stream === true && r.body?.model === MODEL), 'every question streamed to qwen3:8b');
  check(asks.every((r) => !r.authorization), 'no key was sent');
  check(messageText(asks[0].body).includes('scope="the whole recording"') && messageText(asks[0].body).includes('river hall'), 'the first question carried the whole transcript');
  check(asks.some((r) => /<transcript section="\d+ of \d+"/.test(messageText(r.body)) && messageText(r.body).includes('parking permits')), 'the small-context question carried matching sections');
  check(asks.every((r) => !/\.(mp4|mkv|webm|flac)\b|m15-fixture\.wav/i.test(JSON.stringify(r.body))), 'no audio or video file names were sent');

  summary.result = 'passed';
  console.log(JSON.stringify(summary, null, 2));
  await app.close().catch((error) => console.error(error));
  clearTimeout(watchdog);
  await cleanup();
  process.exit(0);
} catch (error) {
  clearTimeout(watchdog);
  await fail(error);
}
