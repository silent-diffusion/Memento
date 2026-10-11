// The 2.0 features together on the published app (the integration pass after the 2.0 branches were merged): one
// recording with video, the live transcript and known voices on, through to a document with the follow-up email and
// an export with "One file" video; then Undo across the 2.0 actions in Review (accept a suggested chapter, remove the
// video, Undo). Nothing is played through the speakers: the simulated microphone loops a speech WAV made by
// tools/e2e/speech.ps1 (-Script chapters), and every media element on the page is muted.
//
//   node tools/e2e/x20-cross.mjs --models <dir> [--voices <wav>] [--speech <wav>] [--seconds 470] [--data <dir>]
//                                [--out <dir>] [--port 9383] [--keep]
//
// --models is a folder with whisper/ (Turbo and Small), sherpa-onnx/ and llama/ (one local model is enough); its files
// are hard-linked into the private data root (LOCALAPPDATA = --data, default artifacts/e2e-data-x20); the run turns on
// Remember speakers by voice and the live transcript and chooses the export folder through settings.set.
// --voices (default artifacts/e2e-fixtures/two-voices.wav) is imported first and its two speakers are named in Review,
// so the voices are known; --speech (default artifacts/e2e-fixtures/chapters.wav, the same two synthetic voices on
// three subjects) is what the microphone hears while Display 1 is recorded. The capture shows this PC's display, so
// the recording and its export are deleted at the end (--keep leaves them; delete them yourself after). Results go
// to <out>/summary.json; no picture of the display is saved (screenshots are taken only before recording and of the
// Review page with the video pane hidden).

import { execFileSync, spawn } from 'node:child_process';
import { copyFileSync, existsSync, linkSync, mkdirSync, readdirSync, readFileSync, rmSync, statSync, writeFileSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { option, repoRoot, Run, sleep } from './h1-lib.mjs';

const args = process.argv.slice(2);
const dataRoot = resolve(option(args, '--data', join(repoRoot, 'artifacts', 'e2e-data-x20')));
const out = resolve(option(args, '--out', join(repoRoot, 'artifacts', 'e2e', 'x20')));
const models = option(args, '--models', null) && resolve(option(args, '--models', null));
const voices = resolve(option(args, '--voices', join(repoRoot, 'artifacts', 'e2e-fixtures', 'two-voices.wav')));
const speech = resolve(option(args, '--speech', join(repoRoot, 'artifacts', 'e2e-fixtures', 'chapters.wav')));
const seconds = Number(option(args, '--seconds', '470'));
const keep = args.includes('--keep');
const exportFolder = join(dataRoot, 'exports');
const NAMES = ['Avery Quinn', 'Blake Rivera'];

const run = new Run({ name: '2.0 cross-feature check', dataRoot, out, port: Number(option(args, '--port', '9383')) });
const page = () => run.page;
const readJson = (file) => JSON.parse(readFileSync(file, 'utf8'));
const annotationsOf = (id) => (existsSync(join(run.folder(id), 'annotations.json')) ? readJson(join(run.folder(id), 'annotations.json')) : {});
const undoLabel = () => page().eval(`document.querySelector('.undo-btn')?.getAttribute('aria-label') ?? null`);
const measurements = {};

function answerDialog(title, path) {
  return new Promise((done, fail) => {
    const child = spawn('powershell.exe', ['-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', join(repoRoot, 'tools', 'e2e', 'answer-dialog.ps1'), '-Title', title, '-Path', path], { stdio: ['ignore', 'ignore', 'pipe'], windowsHide: true });
    let errors = '';
    child.stderr.on('data', (d) => (errors += d));
    child.once('exit', (code) => (code === 0 ? done() : fail(new Error(`answer-dialog "${title}" exited ${code}: ${errors.trim()}`))));
  });
}

function linkModels(from) {
  for (const kind of readdirSync(from)) {
    mkdirSync(join(run.memento, 'models', kind), { recursive: true });
    for (const file of readdirSync(join(from, kind))) {
      const target = join(run.memento, 'models', kind, file);
      if (existsSync(target)) continue;
      try {
        linkSync(join(from, kind, file), target);
      } catch {
        copyFileSync(join(from, kind, file), target);
      }
    }
  }
}

/** Mutes every media element on the page now and whenever one is added, so nothing is ever heard. */
async function muteEverything() {
  await page().eval(`(() => {
    const mute = (el) => { el.muted = true; el.volume = 0; };
    document.querySelectorAll('audio, video').forEach(mute);
    if (!window.__muted) {
      window.__muted = new MutationObserver(() => document.querySelectorAll('audio, video').forEach(mute));
      window.__muted.observe(document.body, { childList: true, subtree: true });
      const play = HTMLMediaElement.prototype.play;
      HTMLMediaElement.prototype.play = function () { mute(this); return play.call(this); };
    }
    return true;
  })()`);
}

async function waitProcessed(id, what, timeoutMs = 30 * 60_000) {
  await run.until(() => {
    const stages = run.manifest(id).stages;
    return stages.length > 2 && stages.every((s) => s.state === 'done' || s.state === 'failed');
  }, `${what} to be processed`, timeoutMs, 1000);
  const stages = run.manifest(id).stages;
  run.check(`${what}: every stage done`, stages.every((s) => s.state === 'done'), stages.map((s) => `${s.stage}:${s.state}`).join(', '));
}

async function openReview(id) {
  await page().eval(`location.hash = '#/review/${id}'`);
  await page().waitFor(`!!document.querySelector('.review-panes') && document.querySelectorAll('.segm').length > 1`, 'Review with the transcript', 60_000);
  await muteEverything();
}

let recordingId = null;
try {
  rmSync(dataRoot, { recursive: true, force: true });
  mkdirSync(join(dataRoot, 'Memento'), { recursive: true });
  mkdirSync(exportFolder, { recursive: true });
  if (models) linkModels(models);
  run.check('the fixtures exist', existsSync(voices) && existsSync(speech), `${voices}; ${speech}`);

  await run.start([`--simulate-audio=speech=${speech}`, '--update-feed=off']);
  await page().waitFor(`/Newest first|Your library is empty/.test(document.body.innerText)`, 'the Library', 60_000);
  await muteEverything();
  // Settings set up through the bridge (m5-review and m7-live turn the same switches on through the page): a partial
  // settings.json written by hand would read every missing init-only field of a block as false or zero.
  await run.bridge('settings.set', {
    speakers: { rememberVoices: true },
    transcription: { timing: 'during' },
    export: { defaultFolder: exportFolder, askWhereEachTime: false },
  });
  // The page read the settings when it started: load it again so Review knows the switch is on.
  await page().eval(`location.reload()`);
  await sleep(1500);
  await page().waitFor(`/Newest first|Your library is empty/.test(document.body.innerText)`, 'the Library again', 60_000);
  await run.installEventTap();
  await muteEverything();
  const settings = await run.bridge('settings.get');
  run.check('Remember speakers by voice and the live transcript are on', settings.speakers.rememberVoices === true && settings.transcription.timing === 'during' && settings.transcription.auto === true);

  // 1. Known voices: import the two voices and name both speakers (Review's rename learns the voice).
  const answered = answerDialog('Import audio or video', voices);
  if (await page().locate({ name: 'Import audio or video' })) await page().click({ name: 'Import audio or video' });
  else {
    await page().click({ name: 'More library actions' });
    await page().click({ name: 'Import audio or video…' });
  }
  await answered;
  await run.until(() => run.projectIds().length === 1, 'the import');
  const [learnedFrom] = run.projectIds();
  await waitProcessed(learnedFrom, 'the two-voice import', 15 * 60_000);
  await openReview(learnedFrom);
  const t1 = readJson(join(run.folder(learnedFrom), 'transcript.json'));
  for (const [i, speaker] of t1.speakers.slice(0, 2).entries()) {
    await page().click({ name: `Rename ${speaker.name}` });
    await page().waitFor(`document.activeElement?.classList.contains('person-input')`, 'the rename field');
    await page().eval(`document.activeElement.select()`);
    await page().type(NAMES[i]);
    await page().key('Enter');
    await sleep(800);
  }
  const knownFile = join(run.memento, 'Library', 'voices', 'known.json');
  await run.until(() => existsSync(knownFile) && NAMES.every((n) => readJson(knownFile).voices.some((v) => v.name === n)), 'both voices in voices/known.json', 30_000);
  run.check('both voices are known', true);

  // 2. The recording: Display 1 + the simulated microphone, with the live transcript on.
  const sources = await run.bridge('sources.list');
  const video = await run.bridge('video.listSources');
  const display = video.displays.find((d) => d.primary) ?? video.displays[0];
  const mic = sources.audio.find((s) => s.kind === 'microphone') ?? sources.audio[0];
  await page().eval(`location.hash = '#/'`);
  const started = await run.bridge('recording.start', { title: '2.0 cross check', type: 'meeting', sourceIds: [mic.id, display.id] });
  recordingId = started.recordingId;
  run.log('recording', `${recordingId} · ${display.width} × ${display.height} · ${seconds} s`);
  const startedAt = Date.now();
  await run.until(async () => (await run.events('recording.liveTranscript')).some((e) => e.payload.sessionId === started.sessionId && e.payload.segments.length >= 2), 'two live lines', 180_000, 1000);
  const firstLive = (await run.events('recording.liveTranscript')).find((e) => e.payload.sessionId === started.sessionId && e.payload.segments.length >= 2);
  measurements.firstLiveLinesAfterSeconds = Math.round((firstLive.at - startedAt) / 1000);
  await sleep(Math.max(0, seconds * 1000 - (Date.now() - startedAt)));
  const live = (await run.events('recording.liveTranscript')).filter((e) => e.payload.sessionId === started.sessionId);
  const lastLive = live.at(-1).payload;
  measurements.live = { events: live.length, lines: lastLive.segments.length, engine: lastLive.engine, state: lastLive.state, note: lastLive.note ?? null };
  run.check('the live transcript ran beside video capture', lastLive.segments.length >= 10 && /small|base/i.test(lastLive.engine ?? ''), JSON.stringify(measurements.live));
  const statuses = (await run.events('recording.videoStatus')).filter((e) => e.payload.sessionId === started.sessionId);
  const lastVideo = statuses.at(-1)?.payload.videoTracks[0];
  measurements.video = lastVideo && { encoder: lastVideo.encoder, hardware: lastVideo.hardware, written: lastVideo.framesWritten, dropped: lastVideo.framesDropped };
  run.check('video was written while the live transcript ran', lastVideo && lastVideo.framesWritten > (seconds - 5) * 20 && lastVideo.framesDropped <= lastVideo.framesWritten / 10, JSON.stringify(measurements.video));
  await run.bridge('recording.stop', { sessionId: started.sessionId });
  await waitProcessed(recordingId, 'the cross recording');
  const manifest = run.manifest(recordingId);
  const track = manifest.videoTracks?.[0];
  run.check('schema 4 with the video track as long as the recording', manifest.schemaVersion === 4 && track && Math.abs(track.startOffsetMs + track.durationMs - manifest.durationMs) < 1500, track ? `${track.startOffsetMs} + ${track.durationMs} of ${manifest.durationMs} ms` : 'no video track');
  run.check('the audio track is as long as the recording', Math.abs(manifest.tracks[0].durationMs - manifest.durationMs) <= 250, `${manifest.tracks[0].durationMs} of ${manifest.durationMs} ms`);
  run.check('no live draft was saved', !readdirSync(run.folder(recordingId)).some((f) => /live/i.test(f)));

  // 3. Known voices suggested in the new recording; Use name on each.
  const { matches } = await run.bridge('voices.matches', { recordingId });
  measurements.matches = matches.map((m) => `${m.name} ${m.score?.toFixed?.(2) ?? ''}`);
  run.check('both known voices are suggested', NAMES.every((n) => matches.some((m) => m.name === n)), measurements.matches.join(', '));

  // 4. Review: suggested chapters, the match prompts through the page, then a document with the follow-up email.
  await openReview(recordingId);
  if (await page().eval(`!!document.querySelector('[aria-label="Audio only"], [aria-pressed][aria-label*="Audio only"]')`)) {
    await page().click({ name: 'Audio only' }).catch(() => undefined);
  }
  await page().waitFor(`!!document.querySelector('.voice-match')`, 'the match prompt', 30_000);
  for (let i = 0; i < 2 && (await page().eval(`!!document.querySelector('.voice-match .btn.p')`)); i++) {
    await page().click({ selector: '.voice-match .btn.p' });
    await sleep(1500);
  }
  const t2 = readJson(join(run.folder(recordingId), 'transcript.json'));
  run.check('Use name named the speakers in the recording', NAMES.every((n) => t2.speakers.some((s) => s.name === n && s.renamed)), t2.speakers.map((s) => s.name).join(', '));
  const { suggestions } = await run.bridge('annotations.suggestChapters', { recordingId });
  measurements.suggestions = suggestions.map((s) => `${Math.round(s.atMs / 1000)} s ${s.title}`);
  run.check('chapters are suggested on the recording with video', suggestions.length >= 2, measurements.suggestions.join(' | '));

  await page().click({ name: 'Create document' });
  await page().waitFor(`!!document.querySelector('[data-card="m01"]')`, 'the Builder', 30_000);
  await page().click({ selector: '.pal[data-module="followUpEmail"]' });
  await page().waitFor(`!!document.querySelector('.mod-email')`, 'the Follow-up email card', 10_000);
  const generateStarted = Date.now();
  await page().click({ selector: '.spoke-primary' }, { timeoutMs: 180_000 });
  const ended = await page().waitFor(`document.querySelector('.ai-failure') ? 'failed' : document.querySelector('.doc-paper article') ? 'done' : ''`, 'the document', 30 * 60_000);
  measurements.documentSeconds = Math.round((Date.now() - generateStarted) / 1000);
  const email = ended === 'done' ? await page().waitFor(`document.querySelector('.doc-paper section[data-module="followUpEmail"]')?.innerText ?? ''`, 'the email', 30_000) : '';
  run.check('the local model wrote the document with the follow-up email', ended === 'done' && /Subject/.test(email), `${ended} in ${measurements.documentSeconds} s`);
  run.check('the email names the known speakers', NAMES.some((n) => email.includes(n.split(' ')[0])), email.slice(0, 200).replace(/\s+/g, ' '));
  const { documents } = await run.bridge('documents.list', { recordingId });

  // 5. Export: One file video with the mix, the transcript and the document.
  const selection = {
    audioMixed: { on: true, format: 'flac', bitrateKbps: null },
    tracks: { on: false, format: 'flac', bitrateKbps: null },
    transcript: { on: true, formats: ['markdown'] },
    documents: { on: true, documentIds: documents.map((d) => d.id), format: 'docx' },
    details: { on: true },
    attachments: { on: false },
    video: { on: true, format: 'mp4', layout: 'oneFile' },
  };
  const job = await run.bridge('export.run', { recordingId, selection, destination: { folder: exportFolder, createSubfolder: true }, remember: false });
  const done = await run.until(async () => (await run.events('export.progress')).find((e) => e.payload.jobId === job.jobId && e.payload.state !== 'running')?.payload, 'the export', 15 * 60_000, 500);
  const exportDir = readdirSync(exportFolder).map((f) => join(exportFolder, f)).find((f) => statSync(f).isDirectory()) ?? exportFolder;
  const exportManifest = existsSync(join(exportDir, 'manifest.json')) ? readJson(join(exportDir, 'manifest.json')) : { files: [] };
  measurements.export = exportManifest.files.map((f) => `${f.name} ${f.bytes ?? ''}`);
  run.check('the export holds one MP4, the mix, the transcript and the document', done.state === 'done' && ['.mp4', '.flac', '.md', '.docx'].every((ext) => exportManifest.files.some((f) => f.name.endsWith(ext))), `${done.state} ${done.message ?? ''}: ${measurements.export.join(', ')}`);

  // 6. Undo across the 2.0 actions: accept a suggested chapter, remove the video, Undo.
  await openReview(recordingId);
  await page().waitFor(`document.querySelectorAll('.sug-chapter').length > 0`, 'suggested chapters in the outline', 30_000);
  const before = annotationsOf(recordingId).chapters?.length ?? 0;
  await page().click({ selector: '.sug-chapter .sug-icon-btn--ok' });
  await run.until(() => (annotationsOf(recordingId).chapters?.length ?? 0) === before + 1, 'the accepted chapter');
  const acceptLabel = await undoLabel();
  run.log('after accept', acceptLabel);
  await page().click({ name: 'Remove video, keep audio…' });
  await page().waitFor(`!!document.querySelector('[role="dialog"]')`, 'the Remove video dialog');
  await page().click({ name: 'Remove video', within: '[role="dialog"]' });
  await run.until(() => run.manifest(recordingId).videoRemoved?.reason === 'user', 'the video removed');
  await page().waitFor(`!!document.querySelector('.vid-line')`, 'the removed line in Review', 20_000);
  const afterRemove = await undoLabel();
  run.check('Remove video is not an Undo step; Undo still names the accepted chapter', afterRemove === acceptLabel, `${acceptLabel} → ${afterRemove}`);
  await page().click({ selector: '.undo-btn' });
  await run.until(() => (annotationsOf(recordingId).chapters?.length ?? 0) === before, 'the chapter gone after Undo', 15_000).catch(() => undefined);
  const chaptersAfterUndo = annotationsOf(recordingId).chapters?.length ?? 0;
  const status = await page().eval(`document.querySelector('.undo-status')?.textContent ?? ''`);
  run.check('Undo takes the accepted chapter back after the video was removed', chaptersAfterUndo === before, `${chaptersAfterUndo} chapters (was ${before}); status "${status}"`);
  run.check('the video stays removed and the audio kept', run.manifest(recordingId).videoRemoved?.reason === 'user' && existsSync(join(run.folder(recordingId), run.manifest(recordingId).mix.file)));
  await page().key('y', ['ctrl']);
  await run.until(() => (annotationsOf(recordingId).chapters?.length ?? 0) === before + 1, 'the chapter back after Redo', 15_000).catch(() => undefined);
  run.check('Redo brings the chapter back', (annotationsOf(recordingId).chapters?.length ?? 0) === before + 1);
  await run.shot('review-after-undo');
} catch (error) {
  run.check('the run finished', false, error.stack ?? String(error));
} finally {
  try {
    if (run.app) await run.app.close();
  } catch (error) {
    run.log('close failed', String(error));
    await run.app?.kill();
  }
  const summary = { ...run.summary(), measurements };
  writeFileSync(join(run.out, 'summary.json'), JSON.stringify(summary, null, 2));
  if (!keep) {
    // The capture and the export show this PC's display: never leave them behind (stored tracks are read-only).
    for (const folder of [exportFolder, recordingId && run.folder(recordingId)].filter(Boolean)) {
      try {
        rmSync(folder, { recursive: true, force: true });
      } catch {
        // PowerShell clears the read-only attribute itself.
      }
      if (existsSync(folder)) {
        execFileSync('powershell.exe', ['-NoProfile', '-NonInteractive', '-Command', `Remove-Item -LiteralPath '${folder.replace(/'/g, "''")}' -Recurse -Force`], { windowsHide: true });
      }
      run.log(existsSync(folder) ? 'NOT DELETED' : 'deleted', folder);
    }
  }
  run.log('done', `${summary.passed} passed, ${summary.failed} failed · ${join(run.out, 'summary.json')}`);
  process.exitCode = summary.failed === 0 ? 0 : 1;
}
