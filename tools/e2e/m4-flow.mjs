// The M4 end-to-end check, driven through the real UI of the published app over the DevTools protocol (mouse clicks,
// drags, typing and key presses; the bridge is never called). Windows' own save pickers are answered through UI
// Automation (answer-dialog.ps1). Each step saves a screenshot; the run ends with a JSON summary.
//
//   node tools/e2e/m4-flow.mjs --play <speech file> --models <dir> [--seconds 180] [--data <dir>] [--out <dir>]
//                              [--port <n>] [--from-review] [--simulate]
//
// --play is a public-domain recording of two readers that plays through the default output while Memento records the
// microphone and system audio. --models is a folder with whisper/, sherpa-onnx/ and llama/ (Qwen3.5 4B, Ministral 3 3B or
// both; with one of them the run checks it is selected without choosing, 1.1.0): its files are
// hard-linked into the private data root (copied when a link is not possible). The app runs with LOCALAPPDATA pointed
// at --data (default artifacts/e2e-data-m4), so the real library is never touched; the microphone is recorded, so
// delete --data afterwards. Claude is pointed at a fake server on this PC (MEMENTO_TEST_ANTHROPIC_URL) that refuses the
// fake key; nothing leaves the PC. --from-review reuses the recording of an earlier run in --data. --simulate (when
// another app is recording the microphone) records from the simulated engine instead, without playing anything, and
// then imports --play through "Import audio or video", so every later step reads real speech.

import { execFileSync, spawn } from 'node:child_process';
import { copyFileSync, existsSync, linkSync, mkdirSync, readdirSync, readFileSync, rmSync, statSync } from 'node:fs';
import { createServer } from 'node:http';
import { join, resolve } from 'node:path';
import { App, repoRoot } from './app.mjs';
import { sleep } from './cdp.mjs';

const args = process.argv.slice(2);
const option = (name, fallback) => (args.includes(name) ? args[args.indexOf(name) + 1] : fallback);
const flag = (name) => args.includes(name);
const dataRoot = resolve(option('--data', join(repoRoot, 'artifacts', 'e2e-data-m4')));
const out = resolve(option('--out', join(repoRoot, 'artifacts', 'e2e', 'm4')));
const play = option('--play', null) && resolve(option('--play', null));
const models = option('--models', null) && resolve(option('--models', null));
const seconds = Number(option('--seconds', '180'));
const exports = join(dataRoot, 'exports');
// A fake key in the shape of one (8+ characters): Settings stores it with DPAPI; it only ever reaches the fake server.
const FAKE_KEY = 'fake-e2e-key-0123456789';
const TITLE = 'M4 check two readers';
const summary = { steps: [], timings: {}, checks: {} };

let shotIndex = 0;
let page;
const simulate = flag('--simulate');
const app = new App({ dataRoot, port: Number(option('--port', '9777')), args: simulate ? ['--simulate-audio'] : [] });
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

const hasText = (text, timeoutMs = 20_000) => page.waitFor(`document.body.innerText.includes(${JSON.stringify(text)})`, `"${text}"`, timeoutMs);
const readJson = (file) => JSON.parse(readFileSync(file, 'utf8'));
const projectIds = () => (existsSync(library()) ? readdirSync(library()).sort() : []);
const projectFolder = (id) => join(library(), id);
const manifestOf = (id) => readJson(join(projectFolder(id), 'project.json'));
const transcriptOf = (id) => readJson(join(projectFolder(id), 'transcript.json'));
const historyOf = (id) => readFileSync(join(projectFolder(id), 'history.jsonl'), 'utf8').trim().split('\n').map((l) => JSON.parse(l));
const documentFiles = (id) => (existsSync(join(projectFolder(id), 'documents')) ? readdirSync(join(projectFolder(id), 'documents')).filter((f) => f.endsWith('.json')) : []);

/** Hard-links (or copies) every model file into the data root's models folder. */
function installModels(from) {
  const to = join(app.memento, 'models');
  for (const kind of readdirSync(from)) {
    mkdirSync(join(to, kind), { recursive: true });
    for (const file of readdirSync(join(from, kind))) {
      const target = join(to, kind, file);
      if (existsSync(target)) continue;
      try {
        linkSync(join(from, kind, file), target);
      } catch {
        copyFileSync(join(from, kind, file), target);
      }
    }
  }
}

/** --from-review: the documents an earlier run wrote (and their kept versions) go, so this run's document is the only one. */
function forgetEarlierDocuments() {
  for (const id of projectIds()) {
    rmSync(join(projectFolder(id), 'documents'), { recursive: true, force: true });
    const versions = join(projectFolder(id), 'versions');
    if (!existsSync(versions)) continue;
    for (const file of readdirSync(versions).filter((f) => f.startsWith('document.'))) rmSync(join(versions, file), { force: true });
  }
  // The templates and styles an earlier run saved, so this run's copies get the same names.
  rmSync(join(app.memento, 'templates'), { recursive: true, force: true });
  rmSync(join(app.memento, 'styles'), { recursive: true, force: true });
}

/** Answers Windows' file or folder picker titled `title` with `path`. Resolves when done. */
function answerDialog(title, path) {
  return new Promise((done, fail) => {
    const child = spawn('powershell.exe', ['-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', join(repoRoot, 'tools', 'e2e', 'answer-dialog.ps1'), '-Title', title, '-Path', path], { stdio: ['ignore', 'ignore', 'pipe'], windowsHide: true });
    let errors = '';
    child.stderr.on('data', (d) => (errors += d));
    child.once('exit', (code) => (code === 0 ? done() : fail(new Error(`answer-dialog "${title}" exited ${code}: ${errors.trim()}`))));
  });
}

function startPlayback(file) {
  if (!file) return null;
  const child = spawn('powershell.exe', ['-NoProfile', '-NonInteractive', '-Command', `(New-Object Media.SoundPlayer '${file}').PlaySync()`], { stdio: 'ignore', windowsHide: true });
  return { stop: () => spawn('taskkill', ['/F', '/T', '/PID', String(child.pid)], { stdio: 'ignore' }) };
}

async function waitElapsed(target) {
  await page.waitFor(`(() => { const m = /(\\d\\d):(\\d\\d):(\\d\\d)/.exec(document.querySelector('[aria-label="Recording"]')?.innerText ?? ''); return m && (+m[1]*3600 + +m[2]*60 + +m[3]) >= ${target}; })()`, `${target} s recorded`, (target + 90) * 1000);
}

async function selectAll() {
  await page.eval(`(() => { const el = document.activeElement; if (el && typeof el.select === 'function') el.select(); return true; })()`);
}

/** Waits until every stage of the recording is done (or failed), up to `minutes`. */
async function waitProcessed(id, minutes) {
  const deadline = Date.now() + minutes * 60_000;
  while (Date.now() < deadline) {
    const stages = manifestOf(id).stages;
    if (stages.length > 1 && !stages.some((s) => s.state === 'active' || s.state === 'queued')) return stages;
    await sleep(1000);
  }
  throw new Error(`${id} was still processing after ${minutes} min`);
}

/** Back to the Library from any screen (spoke screens have a back button, Settings and Review a Library button). */
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

/** The title of the recording the documents are made from (the imported one with --simulate). */
let reviewTitle = TITLE;

async function openReview() {
  await home();
  const row = await page.eval(`(() => { const t = [...document.querySelectorAll('.lib-item .row-title')].find((e) => e.innerText.trim() === ${JSON.stringify(reviewTitle)}); if (!t) return null; t.scrollIntoView({ block: 'center' }); const r = t.getBoundingClientRect(); return { x: r.left + r.width / 2, y: r.top + r.height / 2 }; })()`);
  if (row === null) throw new Error(`No Library row titled "${reviewTitle}"`);
  await clickAt(row);
  await page.waitFor(`!!document.querySelector('.review-panes') && document.querySelectorAll('.segm').length > 3`, 'Review with the transcript', 30_000);
}

/** The centre of an element (scrolled into view). */
const centreOf = (selector) =>
  page.eval(`(() => { const el = document.querySelector(${JSON.stringify(selector)}); if (!el) return null; el.scrollIntoView({ block: 'center' }); const r = el.getBoundingClientRect(); return { x: r.left + r.width / 2, y: r.top + r.height / 2, left: r.left, right: r.right, top: r.top, bottom: r.bottom }; })()`);

/**
 * An HTML5 drag from one point to another, as a mouse would do it: the press and the first moves go through Input,
 * WebView2 hands the drag to DevTools (Input.setInterceptDrags), and the drop is dispatched with its data.
 */
let dragData = null;
async function drag(fromSelector, toSelector) {
  const from = await centreOf(fromSelector);
  if (from === null) throw new Error(`Nothing to drag at ${fromSelector}`);
  await page.send('Input.setInterceptDrags', { enabled: true });
  if (dragData === null) {
    dragData = { waiter: null };
    page.on((message) => {
      if (message.method === 'Input.dragIntercepted') dragData.waiter?.(message.params.data);
    });
  }
  const intercepted = new Promise((done) => (dragData.waiter = done));
  await page.send('Input.dispatchMouseEvent', { type: 'mouseMoved', x: from.x, y: from.y });
  await page.send('Input.dispatchMouseEvent', { type: 'mousePressed', x: from.x, y: from.y, button: 'left', clickCount: 1 });
  for (let i = 1; i <= 6; i++) {
    await page.send('Input.dispatchMouseEvent', { type: 'mouseMoved', x: from.x + i * 4, y: from.y + i * 4, button: 'left', buttons: 1 });
  }
  const data = await Promise.race([intercepted, sleep(3000).then(() => null)]);
  if (data === null) throw new Error('The drag did not start');
  // Drop zones appear once a drag is under way: find the target now.
  await sleep(300);
  const to = await centreOf(toSelector);
  if (to === null) throw new Error(`No drop target at ${toSelector}`);
  await page.send('Input.dispatchDragEvent', { type: 'dragEnter', x: to.x, y: to.y, data });
  for (let i = 0; i < 3; i++) {
    await page.send('Input.dispatchDragEvent', { type: 'dragOver', x: to.x, y: to.y, data });
    await sleep(80);
  }
  await page.send('Input.dispatchDragEvent', { type: 'drop', x: to.x, y: to.y, data });
  await page.send('Input.dispatchMouseEvent', { type: 'mouseReleased', x: to.x, y: to.y, button: 'left', clickCount: 1 });
  await page.send('Input.setInterceptDrags', { enabled: false });
}

/** A Messages API stand-in on this PC that refuses every key, as Anthropic does an unknown one. */
function startFakeAnthropic() {
  const requests = [];
  const server = createServer((request, response) => {
    let body = '';
    request.on('data', (d) => (body += d));
    request.on('end', () => {
      requests.push({ method: request.method, url: request.url, key: request.headers['x-api-key'] === FAKE_KEY, bytes: body.length });
      response.writeHead(401, { 'content-type': 'application/json', 'request-id': 'req_fake_e2e' });
      response.end(JSON.stringify({ type: 'error', error: { type: 'authentication_error', message: 'invalid x-api-key' } }));
    });
  });
  return new Promise((done) => server.listen(0, '127.0.0.1', () => done({ server, requests, url: `http://127.0.0.1:${server.address().port}/` })));
}

/** Waits for a file that matches `pattern` in `folder`, newer than `since`. */
async function waitForFile(folder, pattern, since, timeoutMs = 60_000) {
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    if (existsSync(folder)) {
      const match = readdirSync(folder).find((f) => pattern.test(f) && statSync(join(folder, f)).mtimeMs >= since && statSync(join(folder, f)).size > 0);
      if (match) return join(folder, match);
    }
    await sleep(500);
  }
  throw new Error(`No file matching ${pattern} appeared in ${folder}`);
}

/** The text of the Builder's failure card or generation card. */
const builderStatus = () => page.eval(`(document.querySelector('.ai-failure')?.innerText ?? document.querySelector('.gen-card, [role=status]')?.innerText ?? '').replace(/\\s+/g, ' ').trim()`);

/**
 * Presses Generate in the Builder (unless something else already started it, as "Switch to …" does) and waits for the
 * document viewer, the send confirmation or the failure card.
 */
async function generate(label, { press = true } = {}) {
  const started = Date.now();
  await page.waitFor(`!document.querySelector('[role=dialog]')`, 'no dialog over the Builder', 10_000);
  const begun = `!!document.querySelector('.ai-failure, #confirm-send-title, .doc-paper article') || /GENERATING|Preparing the|Reading the|Checking/.test(document.body.innerText)`;
  for (let attempt = 0; press; attempt++) {
    // Generate stays off while the provider is not ready, e.g. while another app holds the graphics card's memory.
    await page.click({ selector: '.spoke-primary' }, { timeoutMs: 180_000 });
    try {
      await page.waitFor(begun, `${label} to start`, 10_000);
      break;
    } catch (error) {
      // A click that landed while a dialog was closing: once more.
      if (attempt >= 1) throw error;
    }
  }
  await sleep(1500);
  await shot(`${label}-generating`);
  const ended = await page.waitFor(
    `document.querySelector('.ai-failure') ? 'failed' : document.querySelector('#confirm-send-title') ? 'confirm' : document.querySelector('.doc-paper article') ? 'done' : ''`,
    `${label} to finish`,
    30 * 60_000,
  );
  return { ended, seconds: Math.round((Date.now() - started) / 1000) };
}

/** The folder the Export dialog writes into (a subfolder named after the recording when that option is on). */
function exportsFolder() {
  if (!existsSync(exports)) return exports;
  const nested = readdirSync(exports).filter((f) => statSync(join(exports, f)).isDirectory());
  return nested.length === 0 ? exports : join(exports, nested.sort().at(-1));
}

/** Points the open Export dialog's folder at the run's exports folder and remembers it (a folder chosen once is kept only then). */
async function pickExportFolder() {
  if ((await page.eval(`(document.querySelector('.export-path')?.innerText ?? '').startsWith(${JSON.stringify(exports)})`)) === true) return;
  const answered = answerDialog('Choose where to save the copies', exports);
  await page.click({ name: 'Change export folder' });
  await answered;
  await page.waitFor(`(document.querySelector('.export-path')?.innerText ?? '').startsWith(${JSON.stringify(exports)})`, 'the export folder', 20_000);
  // Remember the folder, so the next exports go there without the picker.
  if ((await page.eval(`document.querySelector('[aria-label="Remember these choices"]')?.getAttribute('aria-checked')`)) !== 'true') {
    await page.click({ role: 'switch', name: 'Remember these choices' });
  }
}

/** Exports the open document in one format through the Export dialog and returns the file written. */
async function exportDocument(format, label) {
  const since = Date.now() - 1000;
  await page.click({ name: 'Export', within: 'header' });
  await page.waitFor(`!!document.querySelector('.export-dialog')`, 'the Export dialog');
  await pickExportFolder();
  // Documents is ticked by default; its format is a single-choice list (Word, PDF, Markdown). Only the document is exported.
  for (const id of ['exp-audio', 'exp-transcript']) {
    if (await page.eval(`document.getElementById('${id}')?.checked === true`)) await page.click({ selector: `#${id}` });
  }
  if ((await page.eval(`document.querySelector('[aria-label^="Format for Documents"]')?.getAttribute('aria-label')`)) !== `Format for Documents: ${label}`) {
    await page.click({ selector: '[aria-label^="Format for Documents"]' });
    await page.click({ role: 'option', name: label });
  }
  await page.waitFor(`document.querySelector('[aria-label^="Format for Documents"]')?.getAttribute('aria-label') === 'Format for Documents: ${label}'`, `the ${label} format`);
  await sleep(600);
  await shot(`export-dialog-${format}`);
  // With "Ask where to save each time" (Settings › Export) the picker confirms the folder when Export is pressed.
  const asked = answerDialog('Choose where to save the copies', exports).then(() => true, () => false);
  await page.click({ selector: '.export-foot .btn.p' });
  const ended = await page.waitFor(`[...document.querySelectorAll('button')].some((b) => b.innerText.trim() === 'Open folder') ? 'done' : document.body.innerText.includes('was not exported') ? 'failed' : ''`, `the ${format} export`, 5 * 60_000);
  check(ended === 'done', `the ${format} export finished`);
  summary.checks[`the ${format} export asked where to save`] = await Promise.race([asked, sleep(100).then(() => false)]);
  const extension = { docx: /\.docx$/i, pdf: /\.pdf$/i, markdown: /\.md$/i }[format];
  const file = await waitForFile(exportsFolder(), extension, since);
  await page.key('Escape');
  return file;
}

/** The graphics card's memory in use (other apps' too), from nvidia-smi. */
function gpuMemory() {
  try {
    const text = execFileSync('nvidia-smi', ['--query-gpu=memory.used,utilization.gpu', '--format=csv,noheader,nounits'], { encoding: 'utf8' }).trim();
    const [used, utilisation] = text.split(',').map((v) => Number(v.trim()));
    return { usedMiB: used, text: `${used} MiB in use, ${utilisation} % busy` };
  } catch {
    return { usedMiB: 0, text: 'nvidia-smi unavailable' };
  }
}

/** Page count and page sizes (points) of a PDF, read from its page objects. */
function pdfFacts(file) {
  const text = readFileSync(file).toString('latin1');
  const pages = [...text.matchAll(/\/Type\s*\/Page(?!s)/g)].length;
  const sizes = [...text.matchAll(/\/MediaBox\s*\[\s*0\s+0\s+([\d.]+)\s+([\d.]+)\s*\]/g)].map((m) => `${Math.round(Number(m[1]))}x${Math.round(Number(m[2]))}`);
  return { pages, sizes: [...new Set(sizes)], bytes: text.length };
}

/** A real mouse click at a point. */
async function clickAt(point) {
  await page.send('Input.dispatchMouseEvent', { type: 'mouseMoved', x: point.x, y: point.y });
  await page.send('Input.dispatchMouseEvent', { type: 'mousePressed', x: point.x, y: point.y, button: 'left', clickCount: 1 });
  await page.send('Input.dispatchMouseEvent', { type: 'mouseReleased', x: point.x, y: point.y, button: 'left', clickCount: 1 });
}

let fake;
try {
  rmSync(out, { recursive: true, force: true });
  mkdirSync(out, { recursive: true });
  mkdirSync(exports, { recursive: true });
  fake = await startFakeAnthropic();
  process.env.MEMENTO_TEST_ANTHROPIC_URL = fake.url;
  if (models) installModels(models);
  if (flag('--from-review')) forgetEarlierDocuments();

  // 1. Settings › AI and privacy: external AI is off; the local model is installed, and it is chosen.
  page = await app.start();
  await openSettings('AI and privacy');
  await hasText('Allow external AI services');
  check((await page.eval(`document.querySelector('[aria-label="Allow external AI services"]')?.getAttribute('aria-checked')`)) !== 'true', 'external AI is off');
  // At least one local model is installed (a copied model file is hashed in the background first).
  await page.waitFor(`!!document.querySelector('[aria-label^="Remove Qwen3.5 4B"], [aria-label^="Remove Ministral 3 3B"]')`, 'a local model installed', 60_000);
  const cardOf = (name) => page.eval(`(() => { const card = [...document.querySelectorAll('.model-card')].find((c) => c.querySelector('.model-name')?.innerText.includes(${JSON.stringify(name)})); const radio = card?.querySelector('input[type=radio]'); if (!radio) return null; radio.scrollIntoView({ block: 'center' }); const b = radio.getBoundingClientRect(); return { x: b.left + b.width / 2, y: b.top + b.height / 2, checked: radio.checked, disabled: radio.disabled, installed: !!card.querySelector('[aria-label^="Remove "]'), text: card.innerText.replace(/\\s+/g, ' ') }; })()`);
  const inUse = () => page.eval(`(document.querySelector('.model-in-use')?.innerText ?? '').replace(/\\s+/g, ' ').trim()`);
  summary.gpuAtStart = gpuMemory();
  const cards = { qwen: await cardOf('Qwen3.5 4B'), ministral: await cardOf('Ministral 3 3B') };
  summary.localModelsInstalled = Object.entries(cards).filter(([, c]) => c?.installed).map(([k]) => k);
  for (const [key, card] of Object.entries(cards)) {
    check(card !== null, `the ${key} card is listed`);
    // 1.1.0: an installed, verified model never reads "Not installed", and its radio says "Use this model".
    if (card.installed) check(!card.text.includes('Not installed') && card.text.includes('Use this model') && !card.disabled, `the installed ${key} card can be used (${card.text})`);
    else check(card.disabled && card.text.includes('Not installed'), `the ${key} card that is not installed says so`);
  }
  let wanted;
  if (summary.localModelsInstalled.length === 2) {
    // Both installed: the run chooses the other model and then the wanted one, so the choice is made in Settings. When
    // another app holds the card's memory it ends on Ministral 3 3B, which runs on the processor (--model forces one).
    wanted = option('--model', summary.gpuAtStart.usedMiB > 2000 ? 'ministral' : 'qwen') === 'ministral' ? 'Ministral 3 3B' : 'Qwen3.5 4B';
    for (const name of wanted === 'Qwen3.5 4B' ? ['Ministral 3 3B', 'Qwen3.5 4B'] : ['Qwen3.5 4B', 'Ministral 3 3B']) {
      await clickAt(await cardOf(name));
      await page.waitFor(`[...document.querySelectorAll('.model-card')].find((c) => c.querySelector('.model-name')?.innerText.includes(${JSON.stringify(name)}))?.querySelector('input[type=radio]')?.checked === true`, `${name} chosen`, 10_000);
    }
  } else {
    // Only one installed (bug report for 1.0.0: only Qwen, and Settings showed Ministral "not installed"): it is the one
    // selected without choosing anything, whatever the graphics card has free.
    wanted = summary.localModelsInstalled[0] === 'qwen' ? 'Qwen3.5 4B' : 'Ministral 3 3B';
  }
  await page.waitFor(`/Documents are written with/.test(document.querySelector('.model-in-use')?.innerText ?? '')`, 'the local model in use', 20_000);
  await sleep(800);
  await shot('settings-ai-local-chosen');
  summary.localModel = (await cardOf(wanted))?.checked ? wanted : null;
  check(summary.localModel !== null, `${wanted} is the selected local model`);
  summary.settingsLocalInUse = await inUse();
  check(summary.settingsLocalInUse.includes(wanted) || summary.localModelsInstalled.length === 2, `Settings names the model that writes (${summary.settingsLocalInUse})`);
  check(/· (graphics card|processor)/.test(summary.settingsLocalInUse), `Settings names where it runs (${summary.settingsLocalInUse})`);
  log('Settings › AI and privacy', `external AI off; installed ${summary.localModelsInstalled.join(' + ')}; ${wanted} selected; ${summary.settingsLocalInUse} (graphics card: ${summary.gpuAtStart.text})`);

  // 2. Three minutes of microphone and system audio while the two readers play; the transcript with speakers.
  let recordingId;
  if (flag('--from-review')) {
    // The recording with the most transcript (with --simulate, the imported two readers rather than the tones).
    [recordingId] = projectIds().filter((id) => existsSync(join(projectFolder(id), 'transcript.json'))).sort((x, y) => transcriptOf(y).segments.length - transcriptOf(x).segments.length);
    reviewTitle = manifestOf(recordingId).details.title;
    log('from review', `reusing ${recordingId} (${reviewTitle})`);
  } else {
    await page.click({ name: 'Library' });
    await page.click({ name: 'New recording' });
    await hasText('AUDIO SOURCES');
    await page.click({ role: 'textbox', name: 'Untitled meeting', exact: false });
    await selectAll();
    await page.type(TITLE);
    await page.key('Enter');
    await hasText('2 audio sources selected');
    await shot('record-ready');
    const playback = simulate ? null : startPlayback(play);
    await sleep(700);
    await page.click({ name: 'Start recording' });
    await hasText('RECORDING');
    await sleep(5000);
    await shot('recording');
    log('recording', simulate ? `simulated microphone + system audio for ${seconds} s` : `microphone + system audio for ${seconds} s${play ? ', two readers playing' : ''}`);
    await waitElapsed(seconds);
    await page.click({ name: 'Stop and open review' });
    playback?.stop();
    [recordingId] = projectIds();
    const stoppedAt = Date.now();
    const stages = await waitProcessed(recordingId, 30);
    summary.timings.processingSeconds = Math.round((Date.now() - stoppedAt) / 1000);
    check(stages.every((s) => s.state === 'done'), `the recording was processed (${stages.map((s) => `${s.stage}:${s.state}`).join(', ')})`);
    log('processed', `${summary.timings.processingSeconds} s after stop`);
    if (simulate) {
      // The simulated engine records tones, not speech: the document steps read the two readers, imported.
      await home();
      const answered = answerDialog('Import audio or video', play);
      await page.click({ name: 'More library actions' });
      await page.click({ name: 'Import audio or video…' });
      await answered;
      await page.waitFor(`document.querySelectorAll('.lib-item').length >= 2`, 'the imported recording in the Library', 60_000);
      recordingId = projectIds().filter((id) => id !== recordingId).at(-1);
      reviewTitle = manifestOf(recordingId).details.title;
      const importedAt = Date.now();
      const imported = await waitProcessed(recordingId, 30);
      check(imported.every((s) => s.state === 'done'), `the import was processed (${imported.map((s) => `${s.stage}:${s.state}`).join(', ')})`);
      log('imported', `${play.split(/[\/]/).pop()} as ${recordingId}, processed in ${Math.round((Date.now() - importedAt) / 1000)} s`);
    }
  }
  const transcript = transcriptOf(recordingId);
  summary.transcript = { segments: transcript.segments.length, speakers: transcript.speakers.map((s) => s.name) };
  check(transcript.segments.length > 5 && transcript.speakers.length >= 1, 'the transcript has lines and speakers');

  // 3. Review › Create document: the Builder with Meeting minutes.
  await openReview();
  await shot('review-transcript');
  await page.click({ name: 'Create document' });
  await page.waitFor(`!!document.querySelector('[data-card="m05"]') && document.querySelector('#tpl-name')?.value === 'Meeting minutes'`, 'the Builder with Meeting minutes', 30_000);
  await shot('builder-meeting-minutes');

  // Drag Discussion summary beside Agenda.
  const rowsBefore = await page.eval(`document.querySelectorAll('.structure-row').length`);
  await drag('[data-card="m05"] .handle', '[data-side="2"]');
  await page.waitFor(`!!document.querySelector('[data-card="m05"]')?.closest('.structure-row')?.querySelector('[data-card="m04"]')`, 'Discussion summary beside Agenda', 10_000);
  check((await page.eval(`document.querySelectorAll('.structure-row').length`)) === rowsBefore - 1, 'the drag left one row fewer');
  await shot('builder-dragged-beside');
  log('Builder', 'Discussion summary dragged beside Agenda');

  // A larger text size for Discussion summary.
  if (!(await page.eval(`!!document.querySelector('[aria-label="Text size of Discussion summary"]')`))) await page.click({ selector: '[data-card="m05"] .modhead' });
  await page.click({ selector: '[aria-label="Text size of Discussion summary"] .seg:nth-child(3)' });
  await page.waitFor(`document.querySelector('[aria-label="Text size of Discussion summary"] .seg.on')?.innerText.trim() === 'Larger'`, 'Larger text size');
  await shot('builder-text-size');

  // Preview exactly what will be sent: for the local model, what it reads; nothing leaves the PC.
  await page.click({ name: 'Preview exactly what will be sent' });
  await page.waitFor(`!!document.querySelector('[role=dialog] textarea')`, 'the payload preview', 30_000);
  const payload = await page.eval(`document.querySelector('[role=dialog] textarea').value`);
  check(payload.includes('nothing leaves this PC') && payload.includes('<transcript>'), 'the preview shows the local model reads the transcript and nothing leaves the PC');
  check(payload.includes('Audio and video are never sent'), 'the preview says audio and video are never sent');
  summary.previewPills = await page.eval(`[...document.querySelectorAll('[role=dialog] .pill')].map((p) => p.innerText.trim())`);
  await shot('builder-payload-preview');
  await page.click({ name: 'Done', within: '[role=dialog]' });
  // The Local provider card names the model that writes and where it runs (1.1.0), as Settings does.
  const localNote = `[...document.querySelectorAll('.providers label.provider')].find((l) => l.querySelector('.provider-name')?.innerText.trim() === 'Local model')?.querySelector('.provider-note')?.innerText.trim() ?? ''`;
  await page.waitFor(`!document.querySelector('[role=dialog]')`, 'the payload preview to close', 10_000);
  for (let attempt = 0; ; attempt++) {
    // A click that lands while the dialog is still closing does nothing: press the tab until the card shows.
    if (!(await page.eval(localNote))) await page.click({ selector: '#builder-tab-inputs' });
    try {
      summary.builderLocal = await page.waitFor(localNote, 'the Local provider card', 5_000);
      break;
    } catch (error) {
      if (attempt === 2) throw error;
    }
  }
  check(/^(Qwen3\.5 4B|Ministral 3 3B) · (graphics card|processor) · on this PC$/.test(summary.builderLocal), `the Builder names the local model and where it runs (${summary.builderLocal})`);
  await shot('builder-local-provider');

  // 4. Generate with the local model: nothing is sent, so nothing is asked; progress, then the viewer.
  const first = await generate('local');
  check(first.ended !== 'confirm', 'the local model is not asked about (nothing is sent)');
  check(first.ended === 'done', `the local model wrote the minutes (${await builderStatus()})`);
  summary.timings.localGenerationSeconds = first.seconds;
  await sleep(1500);
  await shot('viewer-generated');
  log('generated', `${first.seconds} s with the local model`);

  // 5. A timestamp chip jumps to Review at that time.
  const chip = await page.eval(`(() => { const a = document.querySelector('.doc-paper a.ts'); if (!a) return null; a.scrollIntoView({ block: 'center' }); const r = a.getBoundingClientRect(); return { x: r.left + r.width / 2, y: r.top + r.height / 2, text: a.innerText.trim(), href: a.getAttribute('href'), t: Number(a.dataset.t) }; })()`);
  check(chip !== null, 'the document has timestamp chips');
  await clickAt(chip);
  await page.waitFor(`!!document.querySelector('.review-panes')`, 'Review to open from the chip', 20_000);
  await sleep(2000);
  const position = await page.eval(`document.querySelector('audio')?.currentTime ?? -1`);
  await shot('review-from-chip');
  const [mm, ss] = chip.text.split(':').map(Number);
  const chipSeconds = Number.isFinite(chip.t) ? chip.t : mm * 60 + ss;
  check(Math.abs(position - chipSeconds) < 2, `the chip ${chip.text} opened Review at that time (player at ${position.toFixed(1)} s)`);
  log('timestamp chip', `${chip.text} → Review at ${position.toFixed(1)} s`);
  await page.click({ name: 'Documents' });
  await page.click({ selector: '.doc-card' });
  await page.waitFor(`!!document.querySelector('.doc-paper article')`, 'the viewer again', 20_000);

  // 6. Edit a paragraph; "Saved".
  // A generated paragraph (one with a timestamp chip), not the meta line or a data module's note.
  const paragraph = await page.eval(`(() => { const p = [...document.querySelectorAll('.doc-paper article section p, .doc-paper article section li')].find((e) => e.isContentEditable && e.querySelector('a.ts') && e.innerText.trim().length > 30); if (!p) return null; p.scrollIntoView({ block: 'center' }); const r = p.getBoundingClientRect(); return { x: r.left + 4, y: r.top + 8 }; })()`);
  check(paragraph !== null, 'the document has a generated paragraph to edit');
  await clickAt(paragraph);
  await sleep(300);
  await page.key('Home'); // the click lands a character or two in; type at the start of the line
  await sleep(100);
  await page.type('Checked: ');
  const docFile = () => join(projectFolder(recordingId), 'documents', documentFiles(recordingId)[0]);
  await page.waitFor(`/Edited|Saving/.test(document.querySelector('.doc-saved')?.innerText ?? '')`, 'the edit to be noticed', 10_000);
  await page.waitFor(`/Saved/.test(document.querySelector('.doc-saved')?.innerText ?? '')`, '"Saved"', 20_000);
  const deadline = Date.now() + 10_000;
  while (!readFileSync(docFile(), 'utf8').includes('Checked:') && Date.now() < deadline) await sleep(300);
  check(readFileSync(docFile(), 'utf8').includes('Checked:'), 'the edit is saved in the document file');
  await shot('viewer-edited-saved');
  log('edited', `${await page.text('.doc-saved')}; the document file has the edit`);

  // 7. How this was made lists the inputs and that audio and video were not sent; the versions.
  const how = (await page.text('.how-made')).replace(/\s+/g, ' ');
  check(/Transcript/.test(how) && /Audio and video were not (sent|used)/.test(how), `How this was made lists the inputs and that audio and video were not sent (${how})`);
  summary.howMade = how;
  await shot('viewer-how-made');

  // 8. Regenerate with a changed instruction, then restore version 1.
  await page.click({ name: 'Regenerate' });
  await page.waitFor(`!!document.querySelector('[data-card="m01"]')`, 'the Builder for regenerating', 30_000);
  await page.click({ selector: '[data-card="m01"] .modhead' });
  await page.click({ selector: '[data-card="m01"] textarea' });
  await page.eval(`(() => { document.querySelector('[data-card="m01"] textarea').select(); return true; })()`);
  await page.type('Two sentences. Name the two speakers first.');
  await shot('builder-changed-instruction');
  const second = await generate('regenerate');
  check(second.ended === 'done', `the regeneration finished (${await builderStatus()})`);
  summary.timings.regenerateSeconds = second.seconds;
  await page.waitFor(`document.querySelectorAll('[aria-label^="Restore version"]').length >= 2`, 'the earlier versions', 20_000);
  await shot('viewer-regenerated-versions');
  summary.versions = await page.eval(`[...document.querySelectorAll('[aria-label^="Restore version"]')].map((b) => b.closest('li, .version-row, div')?.innerText.replace(/\\s+/g, ' ').trim())`);
  await page.click({ name: 'Restore version 1' });
  await page.click({ name: 'Restore', within: '[role=dialog]' });
  await page.waitFor(`!document.querySelector('#restore-doc-title')`, 'the restore', 20_000);
  await sleep(1500);
  await shot('viewer-restored-v1');
  check(!(await page.text('.doc-paper')).includes('Checked: '), 'version 1 is the text as generated, before the edit');
  log('versions', `regenerated in ${second.seconds} s; version 1 restored`);

  // 9. Export Word, PDF and Markdown through the Export dialog.
  const files = {};
  for (const [format, label] of [['docx', 'Word'], ['pdf', 'PDF'], ['markdown', 'Markdown']]) {
    files[format] = await exportDocument(format, label);
    log('exported', `${format}: ${files[format]} (${statSync(files[format]).size} bytes)`);
  }
  check(readFileSync(files.docx).subarray(0, 2).toString() === 'PK', 'the Word file is a zip package');
  check(readFileSync(files.pdf).subarray(0, 5).toString() === '%PDF-', 'the PDF starts with %PDF-');
  summary.pdf = pdfFacts(files.pdf);
  check(summary.pdf.pages >= 1 && summary.pdf.sizes.every((s) => s === '612x792'), `the PDF has Letter pages (${JSON.stringify(summary.pdf)})`);
  copyFileSync(files.pdf, join(out, 'minutes.pdf'));
  check(readFileSync(files.markdown, 'utf8').startsWith('# '), 'the Markdown starts with the title');
  summary.exports = Object.fromEntries(Object.entries(files).map(([k, f]) => [k, { file: f, bytes: statSync(f).size }]));
  await shot('viewer-after-exports');

  // 10. Style editor: duplicate Corporate, change the heading colour, save, use it on the template.
  await openSettings('Documents');
  await page.click({ name: 'Manage' });
  await page.click({ name: 'Duplicate Corporate' });
  // The list re-renders as the copy lands; a click in that moment does nothing, so press Open until the editor shows.
  for (let attempt = 0; ; attempt++) {
    await sleep(500);
    await page.click({ name: 'Open Corporate (copy) in the style editor' });
    try {
      await page.waitFor(`document.querySelector('#style-name')?.value === 'Corporate (copy)'`, 'the style editor', 5_000);
      break;
    } catch (error) {
      if (attempt === 2) throw error;
    }
  }
  await shot('style-editor-copy');
  await page.click({ name: 'Forest' });
  await page.click({ name: 'Save style' });
  await sleep(1200);
  await shot('style-editor-forest-saved');
  const styleFile = join(app.memento, 'styles', 'corporate-copy.json');
  check(existsSync(styleFile) && readJson(styleFile).headingColor === 'forest', 'the copied style is saved with forest headings');
  await openReview();
  await page.click({ name: 'Create document' });
  await page.waitFor(`!!document.querySelector('[data-card="m01"]')`, 'the Builder', 30_000);
  await page.click({ name: 'Inputs and output' });
  await page.click({ name: 'Corporate (copy)' });
  const savedAt = Date.now() - 1000;
  await page.click({ name: 'Save template' });
  const templateFile = await waitForFile(join(app.memento, 'templates'), /^meeting-minutes-copy\.json$/, savedAt, 20_000);
  const savedTemplate = readJson(templateFile);
  check(savedTemplate.name === 'Meeting minutes (copy)' && savedTemplate.defaultStyleId === 'corporate-copy', `the template is saved in the templates folder (${savedTemplate.name}, ${savedTemplate.defaultStyleId})`);
  await page.waitFor(`document.querySelector('.tpl-chooser button')?.getAttribute('aria-label') === 'Template: Meeting minutes (copy)'`, 'the saved template chosen in the list', 10_000);
  await shot('builder-template-with-style');
  log('template saved', templateFile);

  // 10b. Leave the Builder, Create document again: it opens Meeting minutes, the saved template is in the header's
  // template list after the built-ins; choosing it brings back its style, and the document is written with it.
  await page.click({ selector: '[data-spoke-back]' });
  await page.waitFor(`!!document.querySelector('.review-panes')`, 'Review', 20_000);
  await page.click({ name: 'Create document' });
  await page.waitFor(`!!document.querySelector('[data-card="m01"]') && document.querySelector('#tpl-name')?.value === 'Meeting minutes'`, 'the Builder with Meeting minutes', 30_000);
  await page.click({ selector: '.tpl-chooser button[aria-haspopup=listbox]' });
  summary.builderTemplates = await page.waitFor(`(() => { const items = [...document.querySelectorAll('.tpl-chooser [role=listbox] li')].map((li) => (li.getAttribute('role') === 'presentation' ? '# ' : '') + li.textContent.trim()); return items.length > 0 ? items : null; })()`, 'the template list', 10_000);
  await shot('builder-template-list');
  const savedIndex = summary.builderTemplates.indexOf('Meeting minutes (copy)');
  check(summary.builderTemplates[0] === '# Built in' && savedIndex > summary.builderTemplates.indexOf('# Your templates') && summary.builderTemplates.indexOf('# Your templates') > 4, `the Builder lists the built-ins, then the saved template (${summary.builderTemplates.join(' | ')})`);
  await page.click({ role: 'option', name: 'Meeting minutes (copy)' });
  await page.waitFor(`document.querySelector('#tpl-name')?.value === 'Meeting minutes (copy)'`, 'the saved template in the Builder', 20_000);
  await page.waitFor(`(document.querySelector('.preview-caption')?.innerText ?? '').includes('Corporate (copy) style')`, 'the saved template\'s style', 20_000);
  await shot('builder-saved-template-chosen');
  const fourth = await generate('saved-template');
  check(fourth.ended === 'done', `the saved template wrote a document (${await builderStatus()})`);
  const madeWith = documentFiles(recordingId).map((f) => readFileSync(join(projectFolder(recordingId), 'documents', f), 'utf8'));
  check(madeWith.some((text) => /"templateName":\s*"Meeting minutes \(copy\)"/.test(text)), 'the document records the saved template');
  summary.timings.savedTemplateGenerationSeconds = fourth.seconds;
  await shot('viewer-saved-template');
  log('generated with the saved template', `${fourth.seconds} s`);

  await openSettings('Documents');
  await page.click({ name: 'Manage' });
  await hasText('Corporate (copy)');
  const manager = (await page.text('body')).replace(/\s+/g, ' ');
  check(/Corporate \(copy\) Used by 1 template/.test(manager), 'the Documents manager shows the style, used by the saved template');
  check(/Meeting minutes \(copy\) \d+ modules · Corporate \(copy\) · Yours/.test(manager), 'the Documents manager lists the saved template as yours');
  await shot('settings-documents-manager');
  await page.click({ name: 'Done' });

  // 11. External AI on and a fake key for Claude: Claude is ready, the generation fails with the §17 card from the
  // fake server, and "Switch to the local model" works.
  await openSettings('AI and privacy');
  await page.click({ name: 'Allow external AI services' });
  await page.waitFor(`document.querySelector('[aria-label="Allow external AI services"]')?.getAttribute('aria-checked') === 'true'`, 'external AI on');
  await page.click({ name: 'Add a Claude (Anthropic) key' });
  await page.waitFor(`!!document.querySelector('#ai-key-input')`, 'the key dialog');
  await page.click({ selector: '#ai-key-input' });
  await page.type(FAKE_KEY);
  await page.click({ name: 'Save key' });
  await page.waitFor(`!!document.querySelector('[aria-label="Claude (Anthropic) key stored"]')`, 'the masked key');
  await shot('settings-ai-external-on');
  await openReview();
  await page.click({ name: 'Create document' });
  await page.waitFor(`!!document.querySelector('[data-card="m01"]')`, 'the Builder', 30_000);
  await page.click({ name: 'Inputs and output' });
  await page.waitFor(`/Claude[\\s\\S]{0,80}key saved/.test(document.body.innerText)`, 'Claude ready', 20_000);
  await shot('builder-providers-claude-ready');
  // The provider is a radio in a label; with a key saved Claude may already be the choice.
  const claude = await page.eval(`(() => { const label = [...document.querySelectorAll('.providers label.provider')].find((l) => l.querySelector('.provider-name')?.innerText.trim() === 'Claude'); const radio = label?.querySelector('input[type=radio]'); if (!radio) return null; radio.scrollIntoView({ block: 'center' }); const r = radio.getBoundingClientRect(); return { x: r.left + r.width / 2, y: r.top + r.height / 2, checked: radio.checked }; })()`);
  check(claude !== null, 'Claude is offered as a provider');
  if (!claude.checked) await clickAt(claude);
  await page.waitFor(`[...document.querySelectorAll('.providers label.provider')].find((l) => l.querySelector('.provider-name')?.innerText.trim() === 'Claude')?.querySelector('input').checked === true`, 'Claude chosen', 10_000);
  await page.click({ selector: '.spoke-primary' });
  await page.waitFor(`!!document.querySelector('#confirm-send-title')`, 'the send confirmation', 30_000);
  await shot('confirm-send-claude');
  await page.click({ name: 'Send', within: '[role=dialog]' });
  const failure = await page.waitFor(`document.querySelector('.ai-failure')?.innerText.replace(/\\s+/g, ' ').trim() ?? ''`, 'the provider failure card', 5 * 60_000);
  await shot('builder-claude-failed');
  check(fake.requests.length > 0 && fake.requests.every((r) => r.key && r.url === '/v1/messages'), `the request reached only the fake server (${fake.requests.length})`);
  check(/did not accept/.test(failure) && /no document was changed/.test(failure), `the card names what happened and what is safe (${failure})`);
  summary.failureCard = failure;
  log('Claude refused the fake key', failure);
  await page.click({ name: 'Switch to the local model' });
  const third = await generate('switched-to-local', { press: false });
  check(third.ended === 'done', `"Switch to the local model" wrote the minutes (${await builderStatus()})`);
  await shot('viewer-after-switch');
  log('switched to the local model', `${third.seconds} s`);

  const history = historyOf(recordingId).filter((h) => h.stage === 'minutes');
  summary.history = history.map((h) => h.summary);
  check(history.some((h) => /Audio and video were not sent/.test(h.detail ?? '')), 'History says audio and video were not sent');

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
  console.log(JSON.stringify(summary, null, 2));
  await app.kill();
  process.exitCode = 1;
} finally {
  fake?.server.close();
}

