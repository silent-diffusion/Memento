// Review editing end to end, through the real UI of the published app over the DevTools protocol (mouse clicks, typing
// and key presses; the bridge is only read): the speaker menu's search adds a new speaker, a line is corrected in place
// by clicking its words, a highlight is named in the outline, and Undo (the header button, Ctrl+Z and Ctrl+Y) takes
// each back and does it again; then (after 1.2.0) the transcript is filtered by a speaker from the People list, the lines
// shown are copied to the Windows clipboard (read back with PowerShell; the run overwrites the clipboard), and the
// transcript is exported as Markdown and text without timestamps and speakers. Every step is checked against the
// project's files on disk (or the clipboard, or the exported files) and saves a screenshot.
//
//   node tools/e2e/m5-review.mjs --audio <wav> --models <dir> [--chapters-audio <wav>] [--part all|1.x|2.0] [--data <dir>] [--out <dir>] [--port 9783]
//
// --audio is imported with "Import audio or video" (Windows' own picker, answered by answer-dialog.ps1). A synthetic
// two-voice WAV is made by `powershell -File tools/e2e/speech.ps1 -Path artifacts/e2e-fixtures/two-voices.wav`.
// --models is a models folder (whisper and sherpa-onnx are copied; the run downloads nothing). The app runs with
// LOCALAPPDATA pointed at --data (default artifacts/e2e-data-m5), so the real library is never touched.
//
// 2.0 (--part 2.0 runs only this; all runs it after the 1.x steps): known voices, the match prompt, selecting lines and
// suggested chapters (review20 below). --chapters-audio is a seven-minute three-subject WAV:
// `powershell -File tools/e2e/speech.ps1 -Path artifacts/e2e-fixtures/three-subjects.wav -Script chapters`.

import { execFileSync, spawn } from 'node:child_process';
import { cpSync, existsSync, mkdirSync, readdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { Run, option, repoRoot, sleep } from './h1-lib.mjs';

const args = process.argv.slice(2);
const dataRoot = resolve(option(args, '--data', join(repoRoot, 'artifacts', 'e2e-data-m5')));
const out = resolve(option(args, '--out', join(repoRoot, 'artifacts', 'e2e', 'm5-review')));
const audio = resolve(option(args, '--audio', join(repoRoot, 'artifacts', 'e2e-fixtures', 'two-voices.wav')));
const models = option(args, '--models', null);
const chaptersAudio = resolve(option(args, '--chapters-audio', join(repoRoot, 'artifacts', 'e2e-fixtures', 'three-subjects.wav')));
const part = option(args, '--part', 'all');
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

/**
 * 2.0 Review (DESIGN.md §19), through the real UI: Remember speakers by voice turned on in Settings, a speaker named
 * in Review (the voice is learned into voices/known.json), the same audio imported again and the match prompt
 * accepted (the speaker is named and the voice refined), a suggested chapter accepted on a seven-minute three-subject
 * recording, and two lines selected (Ctrl+click) and given a speaker from the selection bar, with Undo.
 */
async function review20({ audio, chaptersAudio }) {
  const known = () => (existsSync(join(run.memento, 'Library', 'voices', 'known.json')) ? readJson(join(run.memento, 'Library', 'voices', 'known.json')) : null);
  const waitDone = async (count, what) => {
    await run.until(() => run.projectIds().length >= count, what, 60_000);
    const id = run.projectIds().find((p) => !seen.has(p));
    seen.add(id);
    await run.until(() => {
      const stages = run.manifest(id).stages;
      return stages.length > 2 && stages.every((s) => s.state === 'done' || s.state === 'failed');
    }, `${what} to be transcribed`, 15 * 60_000, 1000);
    run.check(`${what}: every stage done`, run.manifest(id).stages.every((s) => s.state === 'done'), run.manifest(id).stages.map((s) => `${s.stage}:${s.state}`).join(', '));
    return id;
  };
  const importFile = async (file, _count, what) => {
    const count = run.projectIds().length + 1;
    await page().click({ name: 'Library' }).catch(() => null);
    await sleep(800);
    const answered = answerDialog('Import audio or video', file);
    if (await page().locate({ name: 'Import audio or video' })) {
      await page().click({ name: 'Import audio or video' });
    } else {
      await page().click({ name: 'More library actions' });
      await page().click({ name: 'Import audio or video…' });
    }
    await answered;
    return waitDone(count, what);
  };
  const openReview = async (id) => {
    await page().eval(`location.hash = '#/review/${id}'`);
    await page().waitFor(`!!document.querySelector('.review-panes') && document.querySelectorAll('.segm').length > 1`, 'Review with the transcript', 60_000);
  };
  const clickWith = async (selector, modifiers) => {
    const at = await page().locate(selector);
    if (!at) throw new Error(`no ${selector}`);
    const mask = (modifiers.includes('ctrl') ? 2 : 0) | (modifiers.includes('shift') ? 8 : 0);
    await page().send('Input.dispatchMouseEvent', { type: 'mouseMoved', x: at.x, y: at.y });
    await page().send('Input.dispatchMouseEvent', { type: 'mousePressed', x: at.x, y: at.y, button: 'left', clickCount: 1, modifiers: mask });
    await page().send('Input.dispatchMouseEvent', { type: 'mouseReleased', x: at.x, y: at.y, button: 'left', clickCount: 1, modifiers: mask });
  };
  const seen = new Set(run.projectIds());

  // 1. Settings › Speakers: Remember speakers by voice is off; turn it on.
  await page().click({ name: 'Settings' });
  await page().click({ name: 'Speakers', role: 'tab' }).catch(() => page().click({ name: 'Speakers' }));
  await page().waitFor(`!!document.querySelector('[role="switch"][aria-label="Remember speakers by voice"]')`, 'the Remember switch');
  run.check('Remember speakers by voice starts off', (await page().eval(`document.querySelector('[role="switch"][aria-label="Remember speakers by voice"]').getAttribute('aria-checked')`)) === 'false');
  await page().click({ selector: '[role="switch"][aria-label="Remember speakers by voice"]' });
  await run.until(() => JSON.parse(readFileSync(join(run.memento, 'settings.json'), 'utf8')).speakers?.rememberVoices === true, 'rememberVoices in settings.json');
  await run.shot('20-remember-on');

  // 2. Import the two voices; name the first line's speaker in People.
  await page().click({ name: 'Library' });
  const first = await importFile(audio, 1, 'the first import');
  await openReview(first);
  const t1 = transcriptOf(first);
  const speaker = t1.speakers.find((s) => s.id === t1.segments[0].speaker) ?? t1.speakers[0];
  await page().click({ name: `Rename ${speaker.name}` });
  await page().waitFor(`document.activeElement?.classList.contains('person-input')`, 'the rename field');
  await page().eval(`document.activeElement.select()`);
  await page().type('Avery Quinn');
  await page().key('Enter');
  await run.until(() => known()?.voices?.some((v) => v.name === 'Avery Quinn'), 'Avery Quinn in voices/known.json');
  const learned = known().voices.find((v) => v.name === 'Avery Quinn');
  run.check('naming a speaker learned the voice (signature, never audio)', learned.samples.length === 1 && learned.samples[0].embedding.length > 100 && learned.recordings[0] === first, `${learned.samples[0].embedding.length} numbers, ${learned.samples[0].seconds} s of speech`);
  await run.shot('20-named');

  // 3. The same audio again: the prompt under the unnamed speaker; Use name.
  const second = await importFile(audio, 2, 'the same audio imported again');
  await openReview(second);
  await page().waitFor(`!!document.querySelector('.voice-match')`, 'the match prompt', 30_000);
  const prompt = await page().text('.voice-match');
  run.check('the prompt says who it sounds like', prompt.includes('Sounds like Avery Quinn') && prompt.includes('1 past recording'), prompt);
  await run.shot('20-match-prompt');
  await page().click({ selector: '.voice-match .btn.p' });
  await run.until(() => transcriptOf(second).speakers.some((s) => s.name === 'Avery Quinn' && s.renamed), 'Avery Quinn named in the second recording');
  run.check('Use name named the speaker', true);
  await run.until(() => known().voices.find((v) => v.name === 'Avery Quinn')?.recordings.length === 2, 'the voice refined');
  run.check('the known voice now has two confirmations', known().voices.find((v) => v.name === 'Avery Quinn').samples.length === 2);
  run.check('the step is undoable', (await undoLabel()) === 'Undo use the name Avery Quinn', await undoLabel());
  await page().waitFor(`!document.querySelector('.voice-match')`, 'the prompt gone');
  await run.shot('20-match-accepted');

  // 4. Select two lines with Ctrl+click and give them the other speaker from the bar; then Undo.
  const t2 = transcriptOf(second);
  const target = t2.speakers.find((s) => s.name !== 'Avery Quinn') ?? t2.speakers[0];
  const lines = t2.segments.slice(0, 4).filter((s) => s.speaker !== target.id).slice(0, 2);
  if (lines.length < 2) {
    run.check('two lines by Avery Quinn to move', false, 'the first lines are already the other speaker’s');
  } else {
    await clickWith(`[data-segment-id="${lines[0].id}"] .segm-side`, ['ctrl']);
    await clickWith(`[data-segment-id="${lines[1].id}"] .segm-side`, ['ctrl']);
    await page().waitFor(`(document.querySelector('.sel-count')?.textContent ?? '').startsWith('2 lines selected')`, 'two lines selected');
    await run.shot('20-two-selected');
    await page().click({ name: 'Assign speaker…' });
    await page().waitFor(`!!document.querySelector('.speaker-menu')`, 'the speaker menu');
    await page().eval(`[...document.querySelectorAll('.speaker-menu [role="option"]')].find((o) => o.textContent.trim() === ${JSON.stringify(target.name)}).click()`);
    await sleep(300);
    if (await page().eval(`!!document.querySelector('.speaker-menu--choices')`)) {
      await run.shot('20-assign-choices');
      await page().click({ selector: '.speaker-menu--choices [data-choice="lines"]' });
    }
    await run.until(() => lines.every((l) => transcriptOf(second).segments.find((s) => s.id === l.id)?.speaker === target.id), 'both lines given to the other speaker');
    run.check('the two selected lines moved in one step', (await undoLabel()) === `Undo move 2 lines to ${target.name}`, await undoLabel());
    await run.shot('20-assigned');
    await page().click({ name: `Undo move 2 lines to ${target.name}` });
    await run.until(() => lines.every((l) => transcriptOf(second).segments.find((s) => s.id === l.id)?.speaker === l.speaker), 'the lines back after Undo');
    run.check('Undo gave the lines back', true);
    await page().key('Escape');
    await page().waitFor(`!document.querySelector('.sel-bar')`, 'Esc to leave selection mode');
  }

  // 5. Suggested chapters on a seven-minute, three-subject recording: accept one.
  await page().click({ name: 'Library' });
  const third = await importFile(chaptersAudio, 3, 'the three-subject recording');
  await openReview(third);
  await page().waitFor(`document.querySelectorAll('.sug-chapter').length > 0`, 'suggested chapters', 30_000);
  const suggested = await page().eval(`[...document.querySelectorAll('.sug-chapter')].map((r) => r.querySelector('.chap-at').textContent + ' ' + r.querySelector('.sug-chapter-title').firstChild.textContent)`);
  run.log('suggested chapters', suggested.join(' | '));
  run.check('a subject change is suggested', suggested.length >= 2, suggested.join(' | '));
  await run.shot('20-suggested-chapters');
  const before = annotationsOf(third).chapters?.length ?? 0;
  await page().click({ selector: '.sug-chapter:nth-child(2) .sug-icon-btn--ok' });
  await run.until(() => (annotationsOf(third).chapters?.length ?? 0) === before + 1, 'the accepted chapter in annotations.json');
  const accepted = annotationsOf(third).chapters.at(-1);
  run.check('the accepted suggestion is an ordinary chapter (origin local)', accepted.origin === 'local', `${accepted.atMs} ms "${accepted.title}"`);
  await run.shot('20-chapter-accepted');
}


try {
  rmSync(dataRoot, { recursive: true, force: true });
  if (models) {
    for (const engine of ['whisper', 'sherpa-onnx']) {
      if (existsSync(join(models, engine))) cpSync(join(models, engine), join(dataRoot, 'Memento', 'models', engine), { recursive: true });
    }
  }
  await run.start(['--simulate-audio', '--update-feed=off']);
  await run.hasText('Your library is empty');
  if (part === '2.0') {
    await review20({ audio, chaptersAudio });
  } else {

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
  // 9. Just this line or merge (after 1.2.0). Avery Quinn has line 0; picking Avery on another line first asks how.
  const pickFor = async (index, name) => {
    await page().click({ selector: `[data-index="${index}"] .segm-speaker` });
    await page().waitFor(`document.activeElement?.getAttribute('role') === 'combobox'`, 'the speaker search');
    await page().type(name);
    await page().key('Enter');
    await page().waitFor(`!!document.querySelector('.speaker-menu--choices')`, 'the Just this line / Merge choices');
  };
  const lineIndex = transcriptOf(id).segments.findIndex((s, i) => i > 0 && s.speaker !== avery.id && s.speaker !== null);
  if (lineIndex < 0) {
    run.check('a line by another speaker to move', false, 'every line is Avery Quinn’s');
  } else {
    const other = transcriptOf(id).segments[lineIndex].speaker;
    const otherName = transcriptOf(id).speakers.find((s) => s.id === other)?.name;
    const otherLines = transcriptOf(id).segments.filter((s) => s.speaker === other).map((s) => s.id);
    await pickFor(lineIndex, 'Avery Quinn');
    const labels = await page().eval(`[...document.querySelectorAll('.speaker-menu--choices .speaker-choice-label')].map((l) => l.textContent)`);
    run.check('picking another speaker offers Just this line or Merge', labels[0] === 'Just this line' && labels[1] === `Merge ${otherName} into Avery Quinn`, labels.join(' | '));
    await run.shot('speaker-choices');
    await page().key('Enter'); // the first choice: Just this line
    await run.until(() => transcriptOf(id).segments[lineIndex].speaker === avery.id, 'the one line moved to Avery Quinn');
    run.check('Just this line moved only that line', otherLines.length === 1 || transcriptOf(id).speakers.some((s) => s.id === other), `${otherLines.length} lines of ${otherName} before`);
    await page().click({ name: `Undo move line to Avery Quinn` });
    await run.until(() => transcriptOf(id).segments[lineIndex].speaker === other, 'the line back after Undo');

    await pickFor(lineIndex, 'Avery Quinn');
    await page().click({ selector: '.speaker-menu--choices [data-choice="merge"]' });
    await run.until(() => !transcriptOf(id).speakers.some((s) => s.id === other), `${otherName} merged into Avery Quinn`);
    run.check('Merge moved every line and the old speaker went away', otherLines.every((l) => transcriptOf(id).segments.find((s) => s.id === l)?.speaker === avery.id), `${otherLines.length} lines`);
    run.check('the merge is one Undo step', (await undoLabel()) === 'Undo merge speakers', await undoLabel());
    await run.shot('speaker-merged');
    await page().click({ name: 'Undo merge speakers' });
    await run.until(() => transcriptOf(id).speakers.some((s) => s.id === other), `${otherName} back after Undo`);
    run.check('Undo put the merged speaker back with every line', otherLines.every((l) => transcriptOf(id).segments.find((s) => s.id === l)?.speaker === other));
  }

  // 10. Too many speakers: the voices found plus Avery Quinn are more than the two people in the file. Who spoke = 2,
  // then Reduce to 2 speakers (one Undo step), then Identify speakers again with 2 (regrouped from voices.json).
  const found = transcriptOf(id).speakers.length;
  run.log('speakers before Who spoke', `${found}: ${transcriptOf(id).speakers.map((s) => s.name).join(', ')}`);
  run.check('the result is over-segmented (more than the 2 voices)', found > 2, `${found} speakers`);
  await page().click({ selector: '.people-who-toggle' });
  await page().click({ name: 'How many people spoke: Auto' });
  await page().waitFor(`[...document.querySelectorAll('[role="option"]')].some((o) => o.textContent.trim() === '2 speakers')`, 'the count options');
  await page().eval(`[...document.querySelectorAll('[role="option"]')].find((o) => o.textContent.trim() === '2 speakers').click()`);
  await run.until(() => run.manifest(id).details.whoSpoke?.count === 2, 'Who spoke = 2 in project.json');
  run.check('Who spoke is saved with the recording (schema v3)', run.manifest(id).schemaVersion === 3, JSON.stringify(run.manifest(id).details.whoSpoke));
  await page().waitFor(`!!document.querySelector('.people-reduce')`, 'Reduce to 2 speakers');
  await run.shot('who-spoke-2');
  await page().click({ selector: '.people-reduce' });
  await run.until(() => transcriptOf(id).speakers.length <= 2 || transcriptOf(id).speakers.filter((s) => !s.renamed).length === 0, 'the reduce in transcript.json');
  const reduced = transcriptOf(id).speakers;
  run.check('Reduce left 2 speakers (named ones stay apart)', reduced.length === 2, reduced.map((s) => `${s.name}${s.renamed ? ' (named)' : ''}`).join(', '));
  run.check('the reduce is one Undo step', (await undoLabel()) === 'Undo reduce to 2 speakers', await undoLabel());
  await run.shot('reduced');
  await page().eval(`document.activeElement?.blur()`);
  await page().key('z', ['ctrl']);
  await run.until(() => transcriptOf(id).speakers.length === found, 'every speaker back after Ctrl+Z');
  run.check('Ctrl+Z put every merged speaker back', true, `${found} speakers`);
  await page().key('y', ['ctrl']);
  await run.until(() => transcriptOf(id).speakers.length === 2, '2 speakers again after Ctrl+Y');

  const diarized = run.history(id).filter((h) => h.stage === 'speakers' && h.event === 'started').length;
  await page().click({ selector: '.people-identify' });
  await run.until(() => run.history(id).filter((h) => h.stage === 'speakers' && h.event === 'completed').length > diarized, 'the speakers identified again', 10 * 60_000, 500);
  await run.until(() => run.manifest(id).stages.find((s) => s.stage === 'speakers')?.state === 'done', 'the speakers stage done');
  const again = transcriptOf(id).speakers;
  const start = run.history(id).filter((h) => h.stage === 'speakers' && h.event === 'started').at(-1);
  const done = run.history(id).filter((h) => h.stage === 'speakers' && h.event === 'completed').at(-1);
  run.check('Identify speakers again ends with 2 speakers', again.length === 2, again.map((s) => s.name).join(', '));
  run.check('it regrouped what was heard instead of listening again', start?.summary === 'Identifying speakers (from the voices heard before)', `${start?.summary} · ${start?.detail}`);
  run.check('History names the recording’s own count', (start?.detail ?? '').includes('2 expected (this recording)') && done?.summary === 'Found 2 speakers', done?.detail);
  run.check('voices.json is kept beside the transcript', existsSync(join(run.folder(id), 'voices.json')));
  await page().waitFor(`[...document.querySelectorAll('.person[data-speaker-id]')].length === 2`, 'two people in the People list', 10_000).catch(() => null);
  await run.shot('identified-again');
  if (part === 'all') await review20({ audio, chaptersAudio });
  }
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
