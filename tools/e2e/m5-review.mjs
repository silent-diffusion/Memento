// Review editing end to end, through the real UI of the published app over the DevTools protocol (mouse clicks, typing
// and key presses; the bridge is only read): the speaker menu's search adds a new speaker, a line is corrected in place
// by clicking its words, a highlight is named in the outline, and Undo (the header button, Ctrl+Z and Ctrl+Y) takes
// each back and does it again; then (after 1.2.0) the transcript is filtered by a speaker from the People list, the lines
// shown are copied to the Windows clipboard (read back with PowerShell; the run overwrites the clipboard), and the
// transcript is exported as Markdown and text without timestamps and speakers. Every step is checked against the
// project's files on disk (or the clipboard, or the exported files) and saves a screenshot.
//
//   node tools/e2e/m5-review.mjs --audio <wav> --models <dir> [--data <dir>] [--out <dir>] [--port 9783]
//
// --audio is imported with "Import audio or video" (Windows' own picker, answered by answer-dialog.ps1). A synthetic
// two-voice WAV is made by `powershell -File tools/e2e/speech.ps1 -Path artifacts/e2e-fixtures/two-voices.wav`.
// --models is a models folder (whisper and sherpa-onnx are copied; the run downloads nothing). The app runs with
// LOCALAPPDATA pointed at --data (default artifacts/e2e-data-m5), so the real library is never touched.

import { execFileSync, spawn } from 'node:child_process';
import { cpSync, existsSync, mkdirSync, readdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { Run, option, repoRoot, sleep } from './h1-lib.mjs';

const args = process.argv.slice(2);
const dataRoot = resolve(option(args, '--data', join(repoRoot, 'artifacts', 'e2e-data-m5')));
const out = resolve(option(args, '--out', join(repoRoot, 'artifacts', 'e2e', 'm5-review')));
const audio = resolve(option(args, '--audio', join(repoRoot, 'artifacts', 'e2e-fixtures', 'two-voices.wav')));
const models = option(args, '--models', null);
const run = new Run({ name: 'M5 Review editing and Undo', dataRoot, out, port: Number(option(args, '--port', '9783')) });

/** Answers Windows' file picker titled `title` with `path`. */
function answerDialog(title, path) {
  return new Promise((done, fail) => {
    const child = spawn('powershell.exe', ['-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', join(repoRoot, 'tools', 'e2e', 'answer-dialog.ps1'), '-Title', title, '-Path', path], { stdio: ['ignore', 'ignore', 'pipe'], windowsHide: true });
    let errors = '';
    child.stderr.on('data', (d) => (errors += d));
    child.once('exit', (code) => (code === 0 ? done() : fail(new Error(`answer-dialog "${title}" exited ${code}: ${errors.trim()}`))));
  });
}

const readJson = (file) => JSON.parse(readFileSync(file, 'utf8'));
const transcriptOf = (id) => readJson(join(run.folder(id), 'transcript.json'));
const annotationsOf = (id) => (existsSync(join(run.folder(id), 'annotations.json')) ? readJson(join(run.folder(id), 'annotations.json')) : { highlights: [] });
const page = () => run.page;
const undoLabel = () => page().eval(`document.querySelector('.undo-btn')?.getAttribute('aria-label') ?? null`);
const status = () => page().eval(`document.querySelector('.undo-status')?.textContent ?? ''`);

try {
  rmSync(dataRoot, { recursive: true, force: true });
  if (models) {
    for (const engine of ['whisper', 'sherpa-onnx']) {
      if (existsSync(join(models, engine))) cpSync(join(models, engine), join(dataRoot, 'Memento', 'models', engine), { recursive: true });
    }
  }
  await run.start(['--simulate-audio', '--update-feed=off']);
  await run.hasText('Your library is empty');

  // 1. Import the speech and wait for the transcript and speakers.
  const answered = answerDialog('Import audio or video', audio);
  await page().click({ name: 'Import audio or video' });
  await answered;
  await run.until(() => run.projectIds().length === 1, 'the imported recording', 60_000);
  const [id] = run.projectIds();
  await run.until(() => {
    const stages = run.manifest(id).stages;
    return stages.length > 2 && stages.every((s) => s.state === 'done' || s.state === 'failed');
  }, 'the import to be transcribed', 15 * 60_000, 1000);
  run.check('the import was transcribed', run.manifest(id).stages.every((s) => s.state === 'done'), run.manifest(id).stages.map((s) => `${s.stage}:${s.state}`).join(', '));
  await page().click({ selector: '.lib-item .row-title' });
  await page().waitFor(`!!document.querySelector('.review-panes') && document.querySelectorAll('.segm').length > 2`, 'Review with the transcript', 60_000);
  await run.shot('review');
  const before = transcriptOf(id);
  run.log('transcript', `${before.segments.length} lines, ${before.speakers.length} speakers: ${before.speakers.map((s) => s.name).join(', ')}`);

  // 2. The speaker menu: search field focused; type a new name; Enter adds the speaker and moves the line to them.
  const first = before.segments[0];
  await page().click({ selector: '[data-index="0"] .segm-speaker' });
  await page().waitFor(`document.activeElement?.getAttribute('role') === 'combobox' && document.activeElement.placeholder === 'Find or add a speaker'`, 'the focused speaker search');
  await page().type('Avery Quinn');
  await page().waitFor(`[...document.querySelectorAll('.speaker-menu [role="option"]')].some((o) => o.textContent.includes('Add “Avery Quinn” as a new speaker'))`, 'the Add row');
  await run.shot('speaker-menu-add');
  await page().key('Enter');
  await run.until(() => transcriptOf(id).speakers.some((s) => s.name === 'Avery Quinn'), 'Avery Quinn in transcript.json');
  const avery = transcriptOf(id).speakers.find((s) => s.name === 'Avery Quinn');
  run.check('the new speaker was created and given the line', transcriptOf(id).segments[0].speaker === avery.id, `${avery.id}, colour ${avery.color}`);
  await page().waitFor(`document.querySelector('[data-index="0"] .segm-speaker-name')?.textContent === 'Avery Quinn'`, 'the line showing Avery Quinn');
  run.check('the Undo button names the step', (await undoLabel()) === 'Undo add speaker Avery Quinn', await undoLabel());
  await run.shot('speaker-added');

  // Ctrl+Z on the page: the line goes back and the speaker is removed; Ctrl+Y brings the same speaker back.
  await page().eval(`document.activeElement?.blur()`);
  await page().key('z', ['ctrl']);
  await run.until(() => !transcriptOf(id).speakers.some((s) => s.name === 'Avery Quinn'), 'Avery Quinn gone after Ctrl+Z');
  run.check('Ctrl+Z put the line back', transcriptOf(id).segments[0].speaker === first.speaker, `${transcriptOf(id).segments[0].speaker}`);
  await page().waitFor(`document.querySelector('.undo-status')?.textContent === 'Undone: add speaker Avery Quinn'`, 'the Undone status', 5000).catch(() => null);
  run.check('the status says what was undone', (await status()) === 'Undone: add speaker Avery Quinn', await status());
  await run.shot('speaker-undone');
  await page().key('y', ['ctrl']);
  await run.until(() => transcriptOf(id).speakers.some((s) => s.id === avery.id && s.name === 'Avery Quinn' && s.color === avery.color), 'Avery Quinn back after Ctrl+Y');
  run.check('Ctrl+Y redid it with the same id and colour', transcriptOf(id).segments[0].speaker === avery.id);

  // 3. Correct a line in place: click its words, the caret lands there; type; Enter saves.
  const second = transcriptOf(id).segments[1];
  await page().click({ selector: '[data-index="1"] .segm-text' });
  await page().waitFor(`document.activeElement?.classList.contains('segm-editor')`, 'the line editor');
  const caret = await page().eval(`document.activeElement.selectionStart`);
  run.check('the caret is inside the text, where it was clicked', caret > 0 && caret < second.text.length, `caret ${caret} of ${second.text.length}`);
  await page().eval(`(() => { const t = document.activeElement; t.setSelectionRange(t.value.length, t.value.length); })()`);
  await page().type(' Checked.');
  await run.shot('line-editing');
  await page().key('Enter');
  await run.until(() => transcriptOf(id).segments[1].text.endsWith('Checked.'), 'the corrected line in transcript.json');
  const corrected = transcriptOf(id).segments[1];
  run.check('the original wording is kept', corrected.edited?.original === second.text, corrected.edited?.original);
  await page().waitFor(`document.querySelector('.undo-status')?.textContent === 'Saved'`, '"Saved"', 5000).catch(() => null);
  await run.shot('line-saved');
  await page().click({ name: 'Undo edit line' });
  await run.until(() => transcriptOf(id).segments[1].text === second.text, 'the line back after Undo');
  run.check('Undo took the correction back and the line reads unedited', transcriptOf(id).segments[1].edited === null);

  // 4. Name a highlight in the outline, then undo it.
  await page().click({ name: 'Highlight' });
  await run.until(() => annotationsOf(id).highlights.length === 1, 'the highlight in annotations.json');
  await page().click({ selector: '.review-outline .outline-group:nth-child(2) .chap-name' });
  await page().waitFor(`document.activeElement?.classList.contains('inline-input')`, 'the highlight name field');
  await page().type('Rows stay at 68 px');
  await run.shot('highlight-naming');
  await page().key('Enter');
  await run.until(() => annotationsOf(id).highlights[0]?.note === 'Rows stay at 68 px', 'the named highlight in annotations.json');
  run.check('the highlight was renamed in place', true);
  await run.shot('highlight-named');
  await page().eval(`document.activeElement?.blur()`);
  await page().key('z', ['ctrl']);
  await run.until(() => annotationsOf(id).highlights[0]?.note === '', 'the name gone after Ctrl+Z');
  run.check('Ctrl+Z took the name back', true);

  // 5. The merge menu searches too (when speakers were found).
  if (transcriptOf(id).speakers.length > 1) {
    await page().click({ selector: '.review-outline .person-merge' });
    await page().waitFor(`!!document.querySelector('.popover-layer .speaker-menu')`, 'the merge menu');
    await run.shot('merge-menu');
    await page().key('Escape');
  }

  // 6. (after 1.2.0) Filter by a speaker from the People list: only their lines show, with the count line.
  await page().eval(`document.activeElement?.blur()`);
  const now = transcriptOf(id);
  const named = now.speakers.find((s) => now.segments.some((seg) => seg.speaker === s.id));
  const theirs = now.segments.filter((seg) => seg.speaker === named.id && seg.text.trim() !== '');
  await page().click({ selector: `.person[data-speaker-id="${named.id}"] .person-filter` });
  await page().waitFor(`!!document.querySelector('.tx-filter-line-text')`, 'the filter line');
  const filterLine = await page().text('.tx-filter-line-text');
  run.check('the filter line counts the speaker’s lines', filterLine === `Showing ${theirs.length} of ${now.segments.length} ${now.segments.length === 1 ? 'line' : 'lines'} · ${named.name}`, filterLine);
  const shownSpeakers = await page().eval(`[...document.querySelectorAll('.segm .segm-speaker-name')].map((n) => n.textContent)`);
  run.check('only that speaker’s lines are shown', shownSpeakers.length === theirs.length && shownSpeakers.every((n) => n === named.name), shownSpeakers.join(', '));
  run.check('the People row is pressed with the count', (await page().eval(`document.querySelector('.person[data-speaker-id="${named.id}"] .person-count')?.textContent ?? ''`)) === `${theirs.length} ${theirs.length === 1 ? 'line' : 'lines'}`);
  await run.shot('filtered-by-speaker');

  // 7. Copy the lines shown: the host writes the Windows clipboard; read it back with PowerShell.
  await page().click({ selector: '.tx-filter-line .btn', name: 'Copy' });
  await page().waitFor(`(document.querySelector('.undo-status')?.textContent ?? '').startsWith('Copied')`, 'the Copied status', 10_000);
  const copiedStatus = await status();
  run.check('the copy is confirmed quietly', copiedStatus === `Copied ${theirs.length} of ${now.segments.length} lines as text`, copiedStatus);
  const clipboard = execFileSync('powershell.exe', ['-NoProfile', '-NonInteractive', '-Command', 'Get-Clipboard -Raw'], { encoding: 'utf8' });
  const others = now.segments.filter((seg) => seg.speaker !== named.id && seg.text.trim() !== '');
  run.check('the clipboard holds that speaker’s lines', theirs.every((seg) => clipboard.includes(`${named.name}: ${seg.text.trim().replace(/\s+/g, ' ')}`)), clipboard.slice(0, 300));
  run.check('and no one else’s', others.every((seg) => !clipboard.includes(seg.text.trim().replace(/\s+/g, ' '))));
  await run.shot('copied');
  await page().key('Escape');
  await page().waitFor(`!document.querySelector('.tx-filter-line')`, 'Esc to show every line');
  run.check('Esc shows every line again', true);

  // 8. Export the transcript as Markdown and text without timestamps and speakers; the files and the manifest say so.
  const exportRoot = join(out, 'export');
  rmSync(exportRoot, { recursive: true, force: true });
  mkdirSync(exportRoot, { recursive: true });
  await page().click({ name: 'Export' });
  await page().waitFor(`!!document.querySelector('.export-dialog') && !document.querySelector('.export-summary')?.innerText.includes('Estimating')`, 'the Export dialog', 30_000);
  for (const row of ['exp-audio', 'exp-tracks', 'exp-documents', 'exp-details', 'exp-attachments']) {
    if (await page().eval(`document.querySelector('#${row}')?.checked === true`)) await page().click({ selector: `#${row}` });
  }
  if (!(await page().eval(`document.querySelector('#exp-transcript')?.checked === true`))) await page().click({ selector: '#exp-transcript' });
  await page().click({ selector: '[aria-label^="Format for Transcript:"]' });
  for (const name of ['Markdown', 'Text', 'JSON']) {
    const on = await page().eval(`[...document.querySelectorAll('[role="option"]')].find((o) => o.textContent.trim() === ${JSON.stringify(name)})?.getAttribute('aria-selected') === 'true'`);
    if ((name === 'JSON') === on) await page().eval(`[...document.querySelectorAll('[role="option"]')].find((o) => o.textContent.trim() === ${JSON.stringify(name)}).click()`);
  }
  await page().key('Escape');
  await page().click({ selector: '[role="switch"][aria-label="Timestamps"]' });
  await page().click({ selector: '[role="switch"][aria-label="Speakers"]' });
  const picked = answerDialog('Choose where to save the copies', exportRoot);
  await page().click({ name: 'Change export folder' });
  await picked;
  await page().waitFor(`(document.querySelector('.export-path')?.innerText ?? '').startsWith(${JSON.stringify(exportRoot)})`, 'the export path', 20_000);
  await run.shot('export-text-options');
  await page().click({ selector: '.export-foot .btn.p' });
  await page().waitFor(`[...document.querySelectorAll('button')].some((b) => b.innerText.trim() === 'Open folder')`, 'the export to finish', 120_000);
  const [folder] = readdirSync(exportRoot);
  const exported = join(exportRoot, folder);
  const md = readFileSync(join(exported, `${folder} - transcript.md`), 'utf8');
  const txt = readFileSync(join(exported, `${folder} - transcript.txt`), 'utf8');
  const names = now.speakers.map((s) => s.name);
  run.check('the Markdown has no timestamps or speakers', !/\[\d+:\d\d:\d\d\]/.test(md) && names.every((n) => !md.includes(n)), md.slice(0, 300));
  run.check('the text has no timestamps or speakers and every line', !/\[\d+:\d\d:\d\d\]/.test(txt) && names.every((n) => !txt.includes(n)) && now.segments.every((seg) => seg.text.trim() === '' || txt.includes(seg.text.trim().replace(/\s+/g, ' '))), txt.slice(0, 300));
  const exportManifest = JSON.parse(readFileSync(join(exported, 'manifest.json'), 'utf8'));
  run.check('the manifest records the options', JSON.stringify(exportManifest.transcriptOptions) === JSON.stringify({ timestamps: false, speakers: false, layout: 'auto' }), JSON.stringify(exportManifest.transcriptOptions));
  await run.shot('export-done');
  await run.shot('done');
  await run.app.close();
} catch (error) {
  run.check('the run reached the end', false, error.stack);
  await run.shot('failure').catch(() => null);
  await run.app?.kill();
} finally {
  await sleep(500);
  const summary = run.summary();
  writeFileSync(join(out, 'summary.json'), JSON.stringify(summary, null, 2));
  console.log(JSON.stringify({ passed: summary.passed, failed: summary.failed, failures: summary.results.filter((r) => !r.passed) }, null, 2));
}
