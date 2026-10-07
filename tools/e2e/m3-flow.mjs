// The M3 end-to-end check, driven through the real UI of the published app over the DevTools protocol (mouse clicks,
// typing and key presses; the bridge is never called). Windows' own file and folder pickers are answered through UI
// Automation (answer-dialog.ps1), as a person would type a path into them. Each step saves a screenshot; the run ends
// with a JSON summary.
//
//   node tools/e2e/m3-flow.mjs --mp3 <audio file> --play <speech file> [--models <dir>] [--seconds 60]
//                              [--data <dir>] [--out <dir>] [--port <n>]
//
// --mp3 is the file imported with "Import audio or video" (a public-domain recording; the run used LibriVox's "After
// Twenty Years", https://archive.org/download/short_story_048_1104_librivox/shortstory048_aftertwentyyears_kam2_64kb.mp3,
// Public Domain Mark 1.0). --models copies installed models (a folder m2-flow.mjs --keep-models wrote) in first.
// The app runs with LOCALAPPDATA pointed at --data (default artifacts/e2e-data-m3), so the real library is never
// touched. The last step records the microphone: delete --data afterwards (it holds room audio).

import { execFileSync, spawn } from 'node:child_process';
import { cpSync, existsSync, mkdirSync, readdirSync, readFileSync, rmSync, statSync } from 'node:fs';
import { createHash } from 'node:crypto';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import { App, repoRoot } from './app.mjs';
import { sleep } from './cdp.mjs';

const args = process.argv.slice(2);
const option = (name, fallback) => (args.includes(name) ? args[args.indexOf(name) + 1] : fallback);
const dataRoot = resolve(option('--data', join(repoRoot, 'artifacts', 'e2e-data-m3')));
const out = resolve(option('--out', join(repoRoot, 'artifacts', 'e2e', 'm3')));
const mp3 = option('--mp3', null) && resolve(option('--mp3', null));
const play = option('--play', null) && resolve(option('--play', null));
const models = option('--models', null) && resolve(option('--models', null));
const seconds = Number(option('--seconds', '60'));
const fixtures = join(repoRoot, 'tests', 'Memento.Documents.Tests', 'fixtures');
const agendaFile = join(fixtures, 'messy-mixed.docx');
const secondAttachment = join(fixtures, 'board-table.docx');
// A fake key in the shape of one: Settings stores it with DPAPI; nothing is ever sent (external AI stays off).
const FAKE_KEY = 'fake-e2e-key-0123456789';
const summary = { steps: [], timings: {}, workerModules: [] };

let shotIndex = 0;
let page;
const app = new App({ dataRoot, port: Number(option('--port', '9555')) });
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
const manifestOf = (id) => readJson(join(projectFolder(id), 'project.json'));
const transcriptOf = (id) => (existsSync(join(projectFolder(id), 'transcript.json')) ? readJson(join(projectFolder(id), 'transcript.json')) : null);
const historyOf = (id) => readFileSync(join(projectFolder(id), 'history.jsonl'), 'utf8').trim().split('\n').map((l) => JSON.parse(l));
const sha256 = (file) => createHash('sha256').update(readFileSync(file)).digest('hex');

function logLines(pattern) {
  const logs = join(app.memento, 'logs');
  if (!existsSync(logs)) return [];
  return readdirSync(logs).flatMap((f) => readFileSync(join(logs, f), 'utf8').split('\n')).filter((l) => pattern.test(l));
}

/** Answers Windows' file or folder picker titled `title` with `path` (Cancel when path is ''). Resolves when done. */
function answerDialog(title, path) {
  return new Promise((done, fail) => {
    const child = spawn('powershell.exe', ['-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', join(repoRoot, 'tools', 'e2e', 'answer-dialog.ps1'), '-Title', title, '-Path', path], { stdio: ['ignore', 'ignore', 'pipe'], windowsHide: true });
    let errors = '';
    child.stderr.on('data', (d) => (errors += d));
    child.once('exit', (code) => (code === 0 ? done() : fail(new Error(`answer-dialog "${title}" exited ${code}: ${errors.trim()}`))));
  });
}

/** Clicks what opens a picker, then answers it. */
async function pick(target, title, path) {
  const answered = answerDialog(title, path);
  await page.click(target);
  await answered;
}

/** Which DLLs the transcription worker has loaded, sampled while it runs (for trimming its folder). */
const workerModules = new Set();
let sampling = null;
function startModuleSampling() {
  const tick = () => {
    try {
      const text = execFileSync('tasklist', ['/M', '/FI', 'IMAGENAME eq Memento.Worker.exe', '/FO', 'CSV', '/NH'], { encoding: 'utf8' });
      for (const line of text.split('\n')) {
        const cells = line.split('","');
        if (cells.length >= 3) for (const m of cells[2].replace(/"\s*$/, '').split(',')) if (m.trim()) workerModules.add(m.trim().toLowerCase());
      }
    } catch {
      // No worker running right now.
    }
  };
  sampling = setInterval(tick, 1500);
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
    if (stages.length > 0 && !stages.some((s) => s.state === 'active' || s.state === 'queued')) return stages;
    await sleep(1000);
  }
  throw new Error(`${id} was still processing after ${minutes} min`);
}

async function openSettings(section) {
  if (!(await page.locate({ name: 'Settings' }))) {
    await page.click({ name: 'Library' });
    await hasText('Newest first');
  }
  await page.click({ name: 'Settings' });
  await page.click({ name: section, role: 'tab' }).catch(() => page.click({ name: section }));
}

/** Files under `folder`, relative, with forward slashes. */
function listFiles(folder, prefix = '') {
  return readdirSync(folder).flatMap((name) => {
    const full = join(folder, name);
    return statSync(full).isDirectory() ? listFiles(full, `${prefix}${name}/`) : [`${prefix}${name}`];
  });
}

try {
  check(mp3 && existsSync(mp3), '--mp3 names an audio file to import');
  rmSync(out, { recursive: true, force: true });
  mkdirSync(out, { recursive: true });
  if (models) {
    cpSync(models, join(dataRoot, 'Memento', 'models'), { recursive: true });
    log('models copied', models);
  }
  startModuleSampling();

  // 1. First run: the empty Library offers Import audio or video.
  page = await app.start();
  await hasText('Your library is empty');
  await shot('first-run-library');
  log('first run', 'empty Library');

  // 2. Import audio or video: Windows' picker, then the new recording processing.
  const importedAt = Date.now();
  await pick({ name: 'Import audio or video' }, 'Import audio or video', mp3);
  await page.waitFor(`!!document.querySelector('.lib-item')`, 'the imported recording in the Library', 60_000);
  await shot('import-processing');
  const [importedId] = projectIds();
  log('imported', `${importedId} from ${mp3.split(/[\\/]/).pop()}`);
  const seen = new Set();
  const processingDeadline = Date.now() + 25 * 60_000;
  while (Date.now() < processingDeadline) {
    const stages = manifestOf(importedId).stages;
    for (const s of stages) {
      if (s.state === 'active' && !seen.has(s.stage)) {
        seen.add(s.stage);
        await shot(`import-stage-${s.stage}`);
        log('stage running', `${s.stage} ${s.label ?? ''}`);
      }
    }
    if (stages.length > 1 && !stages.some((s) => s.state === 'active' || s.state === 'queued')) break;
    await sleep(500);
  }
  const imported = manifestOf(importedId);
  summary.timings.importToProcessedSeconds = Math.round((Date.now() - importedAt) / 1000);
  summary.imported = { state: imported.state, durationMs: imported.durationMs, tracks: imported.tracks.map((t) => `${t.id} ${t.sourceKind} ${t.codec}`), stages: imported.stages.map((s) => `${s.stage}:${s.state}`), importedFrom: imported.importedFrom?.name };
  check(imported.state === 'ready' && imported.stages.every((s) => s.state === 'done'), `the import was stored and processed (${summary.imported.stages.join(', ')})`);
  log('processed', `${summary.timings.importToProcessedSeconds} s; ${summary.imported.stages.join(', ')}`);

  // 3. Review: the transcript of the imported audio.
  await page.click({ selector: '.lib-item .row-title' });
  await page.waitFor(`!!document.querySelector('.review-panes') && document.querySelectorAll('.segm').length > 3`, 'Review with the transcript', 60_000);
  await shot('review-imported');
  const importedTranscript = transcriptOf(importedId);
  summary.imported.segments = importedTranscript.segments.length;
  summary.imported.engine = importedTranscript.engine;
  log('Review', `${importedTranscript.segments.length} lines from ${importedTranscript.engine.model} on ${importedTranscript.engine.device}`);

  // 4. Details sheet: Paste text first (then Discard), then Choose a file with the Word agenda.
  await page.click({ role: 'tab', name: 'Details' });
  await page.click({ name: 'Edit details' });
  await page.waitFor(`!!document.querySelector('.sheet .agenda-drop')`, 'the agenda drop zone');
  await shot('details-sheet-drop-zone');
  await page.click({ name: 'Paste text', within: '.sheet' });
  await page.waitFor(`document.activeElement?.id === 'agenda-paste'`, 'the paste box, focused');
  await page.type('1. Welcome and apologies\n2. Budget for the spring fair\n   - Stalls\n   - Raffle prizes\n3. Any other business');
  await page.click({ name: 'Add items', within: '.sheet' });
  await page.waitFor(`document.querySelectorAll('.sheet .agenda-item .ii').length >= 4`, 'the pasted items');
  await shot('agenda-pasted-preview');
  const pasted = await page.eval(`[...document.querySelectorAll('.sheet .agenda-item .ii')].map((i) => i.value)`);
  log('pasted text', `${pasted.length} items: ${pasted.join(' | ')}`);
  await page.click({ name: 'Discard', within: '.sheet' });
  await page.waitFor(`!!document.querySelector('.sheet .agenda-drop')`, 'the drop zone again');

  await pick({ name: 'Choose a file', within: '.sheet' }, 'Choose an agenda', agendaFile);
  await page.waitFor(`document.querySelectorAll('.sheet .agenda-item .ii').length > 5`, 'the parsed Word agenda', 30_000);
  await shot('agenda-docx-preview');
  const parsed = await page.eval(`[...document.querySelectorAll('.sheet .agenda-item')].map((row) => ({ text: row.querySelector('.ii').value, uncertain: row.classList.contains('low'), reason: row.querySelector('.ii').title }))`);
  const uncertain = parsed.filter((p) => p.uncertain);
  summary.agenda = { parsed: parsed.length, uncertain: uncertain.map((u) => `${u.text} (${u.reason})`), notice: await page.text('.sheet .agenda-notice') };
  check(uncertain.length > 0, 'the Word agenda has an uncertain item');
  log('parsed agenda.docx', `${parsed.length} items, ${uncertain.length} uncertain: ${summary.agenda.uncertain.join('; ')}`);

  // Fix the first uncertain item: "? Any other business Date of next meeting" → "Any other business".
  const merged = parsed.findIndex((p) => p.uncertain && /merged/i.test(p.reason));
  const fixIndex = merged >= 0 ? merged : parsed.findIndex((p) => p.uncertain);
  const fixed = parsed[fixIndex].text.replace(/\s*Date of next meeting.*$/i, '').trim() || 'Any other business';
  await page.click({ selector: `.sheet .agenda-item:nth-child(${fixIndex + 1}) .ii` });
  await selectAll();
  await page.type(fixed);
  await sleep(300);
  await shot('agenda-item-fixed');
  const stillUncertain = await page.eval(`document.querySelectorAll('.sheet .agenda-item.low').length`);
  log('fixed an uncertain item', `"${parsed[fixIndex].text}" → "${fixed}"; ${stillUncertain} still dotted`);
  await page.click({ name: 'Done', within: '.sheet' });
  await page.waitFor(`!document.querySelector('.sheet')`, 'the sheet to close', 20_000);

  // 5. Review › Details: the agenda with its source caption, and the original in the attachments.
  await page.waitFor(`(document.querySelector('.detail-caption')?.innerText ?? '').includes('messy-mixed.docx')`, 'the agenda caption', 20_000);
  await page.waitFor(`[...document.querySelectorAll('[aria-label^="Open "]')].some((b) => b.getAttribute('aria-label') === 'Open messy-mixed.docx')`, 'the agenda in the attachments', 20_000);
  await shot('review-details-agenda');
  const withAgenda = manifestOf(importedId);
  summary.agenda.applied = withAgenda.details.agenda.items.length;
  summary.agenda.caption = await page.text('.detail-caption');
  summary.agenda.attachments = withAgenda.attachments.map((a) => `${a.name} (${a.kind}, ${a.sizeBytes} B)`);
  check(withAgenda.details.agenda.items.some((i) => i.text === fixed), 'the fixed item was saved');
  check(withAgenda.attachments.some((a) => a.kind === 'agenda' && a.sha256 === sha256(agendaFile)), 'the original agenda is attached, byte for byte');
  log('agenda applied', `${summary.agenda.applied} items; caption "${summary.agenda.caption}"; attachments ${summary.agenda.attachments.join(', ')}`);

  // A second attachment.
  await pick({ name: '+ Add a file' }, 'Add an attachment', secondAttachment);
  await page.waitFor(`[...document.querySelectorAll('[aria-label^="Open "]')].some((b) => b.getAttribute('aria-label') === 'Open board-table.docx')`, 'the second attachment', 20_000);
  await shot('review-two-attachments');
  log('attached', 'board-table.docx');

  // 6. Export: mixed FLAC, transcript JSON + SRT, details, attachments, in its own folder.
  const exportRoot = join(tmpdir(), `memento-e2e-export-${process.pid}`);
  rmSync(exportRoot, { recursive: true, force: true });
  mkdirSync(exportRoot, { recursive: true });
  await page.click({ name: 'Export', selector: undefined });
  await page.waitFor(`!!document.querySelector('.export-dialog') && !document.querySelector('.export-summary')?.innerText.includes('Estimating')`, 'the Export dialog with sizes', 30_000);
  await shot('export-dialog-defaults');
  const setChecked = async (id, on) => {
    const now = await page.eval(`document.querySelector('#${id}')?.checked === true`);
    if (now !== on) await page.click({ selector: `#${id}` });
  };
  await setChecked('exp-audio', true);
  await setChecked('exp-tracks', false);
  await setChecked('exp-transcript', true);
  await setChecked('exp-details', true);
  await setChecked('exp-attachments', true);
  const chooseOption = async (label, name) => {
    await page.click({ selector: `[aria-label^="${label}:"]` });
    await page.click({ role: 'option', name });
  };
  if (!(await page.eval(`(document.querySelector('[aria-label^="Format for Audio (mixed):"]')?.getAttribute('aria-label') ?? '').endsWith('FLAC')`))) {
    await chooseOption('Format for Audio (mixed)', 'FLAC');
  }
  await page.click({ selector: '[aria-label^="Format for Transcript:"]' });
  // Exactly JSON and SRT: tick those first, then untick the others (one format always stays ticked).
  for (const [format, wanted] of [['JSON', true], ['SRT', true], ['Markdown', false], ['Text', false]]) {
    const selected = await page.eval(`[...document.querySelectorAll('[role=option]')].find((o) => o.innerText.trim() === ${JSON.stringify(format)})?.getAttribute('aria-selected') === 'true'`);
    if (selected !== wanted) await page.click({ role: 'option', name: format });
  }
  check((await page.eval(`document.querySelector('[aria-label^="Format for Transcript:"]').getAttribute('aria-label')`)) === 'Format for Transcript: JSON + SRT', 'the transcript formats are JSON + SRT');
  await page.key('Escape');
  const subfolderOn = await page.eval(`document.querySelector('[aria-label="Put everything in a folder named after the recording"]')?.getAttribute('aria-checked') === 'true'`);
  if (!subfolderOn) await page.click({ name: 'Put everything in a folder named after the recording' });
  await pick({ name: 'Change export folder' }, 'Choose where to save the copies', exportRoot);
  await page.waitFor(`(document.querySelector('.export-path')?.innerText ?? '').startsWith(${JSON.stringify(exportRoot)})`, 'the export path preview', 20_000);
  await sleep(1200);
  const exportSummaryText = await page.text('.export-summary');
  await shot('export-dialog-chosen');
  log('Export dialog', `${exportSummaryText}; to ${await page.text('.export-path')}`);
  await page.click({ selector: '.export-foot .btn.p' });
  // Footer progress, then the done toast with Open folder.
  // The footer reads "Exporting … · n%" while it runs; a small export can be done before it is ever seen.
  const footerOrDone = await page.waitFor(`document.body.innerText.match(/Exporting [^\\n]* · \\d+%/)?.[0] ?? ([...document.querySelectorAll('button')].some((b) => b.innerText.trim() === 'Open folder') ? 'DONE' : '')`, 'the footer export line or the done toast', 60_000).catch(() => null);
  const footerSeen = footerOrDone === 'DONE' ? null : footerOrDone;
  if (footerSeen) await shot('export-footer-progress');
  const ended = await page.waitFor(`[...document.querySelectorAll('button')].some((b) => b.innerText.trim() === 'Open folder') ? 'done' : document.body.innerText.includes('was not exported') ? 'failed' : ''`, 'the export to end (done toast with Open folder)', 5 * 60_000);
  if (ended !== 'done') await shot('export-failed-toast');
  check(ended === 'done', `the export finished (${(await page.text('body')).match(/[^\n]*was not exported[^\n]*\n?[^\n]*/)?.[0] ?? ended})`);
  await shot('export-done-toast');
  log('exported', `footer said "${footerSeen ?? '(too quick to see)'}"`);
  await page.click({ name: 'Open folder' });
  await sleep(2500);
  // Explorer opened the folder; close that window again.
  try {
    execFileSync('powershell.exe', ['-NoProfile', '-Command', `(New-Object -ComObject Shell.Application).Windows() | Where-Object { $_.LocationURL -like '*memento-e2e-export*' } | ForEach-Object { $_.Quit() }`]);
  } catch {
    // Not fatal.
  }

  // 7. The files on disk and the manifest.
  const [exportFolder] = readdirSync(exportRoot);
  const exportDir = join(exportRoot, exportFolder);
  const written = listFiles(exportDir).sort();
  const manifest = readJson(join(exportDir, 'manifest.json'));
  const mismatched = manifest.files.filter((f) => sha256(join(exportDir, ...f.name.split('/'))) !== f.sha256 || statSync(join(exportDir, ...f.name.split('/'))).size !== f.bytes);
  summary.export = { folder: exportFolder, files: written, manifest: { ...manifest, files: manifest.files.map((f) => `${f.name} ${f.bytes} B`) }, mismatched: mismatched.map((f) => f.name), summaryText: exportSummaryText };
  const base = exportFolder;
  for (const expected of [`${base}.flac`, `${base} - transcript.json`, `${base}.srt`, `${base} - details.json`, 'Attachments/messy-mixed.docx', 'Attachments/board-table.docx', 'manifest.json']) {
    check(written.includes(expected), `the export has ${expected} (${written.join(', ')})`);
  }
  check(mismatched.length === 0 && manifest.files.length === written.length - 1, 'every manifest entry matches its file, and only the manifest is unlisted');
  check(manifest.app === 'Memento' && manifest.algorithm === 'sha256' && manifest.recordingId === importedId, 'the manifest names the app, the hash and the recording');
  log('export on disk', `${written.length} files in "${exportFolder}"; manifest ${manifest.files.length} entries, all hashes match`);

  // 8. Export failure: a folder Memento cannot write to.
  await page.click({ name: 'Export' });
  await page.waitFor(`!!document.querySelector('.export-dialog') && !document.querySelector('.export-summary')?.innerText.includes('Estimating')`, 'the Export dialog', 30_000);
  await pick({ name: 'Change export folder' }, 'Choose where to save the copies', 'C:\\Windows');
  await page.waitFor(`(document.querySelector('.export-path')?.innerText ?? '').startsWith('C:\\\\Windows')`, 'the unwritable path', 20_000);
  await page.click({ selector: '.export-foot .btn.p' });
  await page.waitFor(`!!document.querySelector('.export-failure')`, 'the inline export failure', 30_000);
  const failureText = await page.text('.export-failure');
  await shot('export-failure-unwritable');
  summary.exportFailure = failureText.replace(/\s+/g, ' ');
  log('export refused', summary.exportFailure);
  await page.click({ name: 'Cancel', within: '.export-dialog' });
  await page.waitFor(`!document.querySelector('.export-dialog')`, 'the Export dialog to close');

  // 9. Settings › AI and privacy: a fake key, masked, then Replace and Remove.
  await openSettings('AI and privacy');
  await hasText('Claude (Anthropic)');
  await shot('settings-ai');
  await page.click({ name: 'Add a ChatGPT (OpenAI) key' });
  await page.waitFor(`!!document.querySelector('#ai-key-input')`, 'the key dialog');
  await page.click({ selector: '#ai-key-input' });
  await page.type(FAKE_KEY);
  await page.click({ name: 'Save key' });
  await page.waitFor(`!!document.querySelector('[aria-label="ChatGPT (OpenAI) key stored"]')`, 'the masked key');
  const masked = await page.text('[aria-label="ChatGPT (OpenAI) key stored"]');
  check(!(await page.eval(`document.body.innerHTML.includes(${JSON.stringify(FAKE_KEY)})`)), 'the key is not in the page');
  const settingsFile = join(app.memento, 'settings.json');
  check(!existsSync(settingsFile) || !readFileSync(settingsFile, 'utf8').includes(FAKE_KEY), 'the key is not in settings.json');
  const plain = listFiles(app.memento)
    .filter((f) => !f.startsWith('models/') && !f.startsWith('webview2/'))
    .filter((f) => {
      try {
        return readFileSync(join(app.memento, ...f.split('/'))).includes(Buffer.from(FAKE_KEY));
      } catch {
        return false;
      }
    });
  check(plain.length === 0, `the key is in no file of the data folder in plain text (${plain.join(', ')})`);
  await shot('settings-ai-key-masked');
  log('key stored', `shown as "${masked}"`);
  await page.click({ name: 'Replace the ChatGPT (OpenAI) key' });
  await page.waitFor(`!!document.querySelector('#ai-key-input')`, 'the replace dialog');
  await shot('settings-ai-replace-dialog');
  await page.click({ name: 'Cancel', within: '[role=dialog]' });
  await page.click({ name: 'Remove the ChatGPT (OpenAI) key' });
  await page.waitFor(`!document.querySelector('[aria-label="ChatGPT (OpenAI) key stored"]')`, 'the key removed');
  await shot('settings-ai-key-removed');
  log('key replaced dialog shown, then removed');

  // 10. Settings › Export defaults; Storage usage and Rebuild index.
  await openSettings('Export');
  await hasText('Save copies outside Memento');
  await shot('settings-export-defaults');
  await openSettings('Storage and history');
  await hasText('Rebuild the library index');
  await shot('settings-storage-usage');
  summary.storageUsage = (await page.text('.settings-section, main')).split('\n').filter((l) => /GB|MB|recordings?/.test(l)).slice(0, 6);
  await page.click({ name: 'Rebuild' });
  await page.waitFor(`document.body.innerText.includes('The library index was rebuilt')`, 'the rebuilt toast', 30_000);
  await shot('settings-index-rebuilt');
  log('storage', `usage ${summary.storageUsage.join(' | ')}; index rebuilt`);

  // 11. Library move to a temporary folder and back.
  const originalLibrary = join(app.memento, 'Library');
  const moveTarget = join(tmpdir(), `memento-e2e-library-${process.pid}`);
  rmSync(moveTarget, { recursive: true, force: true });
  const moveTo = async (target, label) => {
    await openSettings('General');
    await pick({ name: 'Change library location' }, 'Choose where Memento keeps your library', target);
    await page.waitFor(`[...document.querySelectorAll('button')].some((b) => b.innerText.trim() === 'Move library')`, 'the move confirmation');
    await shot(`library-move-confirm-${label}`);
    const movedAt = Date.now();
    await page.click({ name: 'Move library' });
    await page.waitFor(`document.body.innerText.includes(${JSON.stringify(target)}) && !document.body.innerText.includes('Moving the library')`, 'the move to finish', 5 * 60_000);
    await sleep(1500);
    await shot(`library-moved-${label}`);
    return Math.round((Date.now() - movedAt) / 100) / 10;
  };
  mkdirSync(moveTarget, { recursive: true });
  summary.timings.moveOutSeconds = await moveTo(moveTarget, 'out');
  check(readJson(join(app.memento, 'settings.json')).libraryPath?.toLowerCase() === moveTarget.toLowerCase(), 'settings.json points at the moved library');
  check(existsSync(join(moveTarget, 'projects', importedId, 'project.json')), 'the project is in the new place');
  log('library moved out', `${moveTarget} in ${summary.timings.moveOutSeconds} s`);
  rmSync(originalLibrary, { recursive: true, force: true });
  mkdirSync(originalLibrary, { recursive: true });
  summary.timings.moveBackSeconds = await moveTo(originalLibrary, 'back');
  check(existsSync(join(originalLibrary, 'projects', importedId, 'project.json')) && !existsSync(join(moveTarget, 'projects')), 'the library is back and the temporary copy is gone');
  log('library moved back', `${summary.timings.moveBackSeconds} s`);

  // 12. Record 60 s with mic + system audio while the speech file plays, then Review › Export with the transcript.
  await page.click({ name: 'Library' });
  await page.click({ name: 'New recording' });
  await hasText('AUDIO SOURCES');
  await page.click({ role: 'textbox', name: 'Untitled meeting', exact: false });
  await selectAll();
  await page.type('M3 check recording');
  await page.key('Enter');
  await hasText('2 audio sources selected');
  const playback = startPlayback(play);
  await sleep(700);
  await page.click({ name: 'Start recording' });
  await hasText('RECORDING');
  await sleep(4000);
  await shot('recording');
  await waitElapsed(seconds);
  await page.click({ name: 'Stop and open review' });
  playback?.stop();
  const recordedId = projectIds().find((id) => id !== importedId);
  log('recorded', `${seconds} s, microphone + system audio`);
  await waitProcessed(recordedId, 20);
  await page.waitFor(`document.querySelectorAll('.segm').length > 1`, 'the recording transcript in Review', 60_000);
  await shot('review-recording-transcript');
  const recordedTranscript = transcriptOf(recordedId);
  summary.recording = { segments: recordedTranscript.segments.length, speakers: recordedTranscript.speakers.length, engine: recordedTranscript.engine };
  await page.click({ name: 'Export' });
  await page.waitFor(`!!document.querySelector('.export-dialog') && !document.querySelector('.export-summary')?.innerText.includes('Estimating')`, 'the Export dialog', 30_000);
  const transcriptRow = await page.eval(`(() => { const c = document.querySelector('#exp-transcript'); return { checked: c.checked, disabled: c.disabled }; })()`);
  check(transcriptRow.checked && !transcriptRow.disabled, 'the transcript is ticked for the recording');
  await shot('export-recording-transcript-ticked');
  await page.click({ name: 'Cancel', within: '.export-dialog' });
  await page.waitFor(`!document.querySelector('.export-dialog')`, 'the Export dialog to close');
  log('recording exported dialog', `${recordedTranscript.segments.length} lines; transcript ticked`);

  await page.click({ name: 'Library' });
  await sleep(1000);
  await shot('library-end');
  await app.close();
  clearInterval(sampling);
  summary.workerModules = [...workerModules].sort();
  summary.logProblems = logLines(/\[(WRN|ERR|FTL)\]/).map((l) => l.slice(0, 400));
  summary.screenshots = readdirSync(out).length;
  summary.result = 'passed';
  rmSync(exportRoot, { recursive: true, force: true });
  rmSync(moveTarget, { recursive: true, force: true });
} catch (error) {
  summary.result = `failed: ${error.message}`;
  console.error(error);
  try {
    await shot('failure');
  } catch {
    // The page is gone.
  }
  clearInterval(sampling);
  summary.workerModules = [...workerModules].sort();
  summary.logProblems = logLines(/\[(WRN|ERR|FTL)\]/).map((l) => l.slice(0, 400));
  await app.kill().catch(() => undefined);
  process.exitCode = 1;
} finally {
  console.log(JSON.stringify(summary, null, 2));
}
