// The 2.0 build on libraries made by 1.x, and recovery with video, on the published app (the integration pass after the
// 2.0 branches were merged). Nothing is played through the speakers; the audio is the simulated engine's.
//
//   node tools/e2e/x20-library.mjs --part upgrade --from <1.x data root> [--add <projects folder> ...] [--data <dir>]
//                                  [--out <dir>] [--port 9383]
//   node tools/e2e/x20-library.mjs --part recovery [--seconds 20] [--data <dir>] [--out <dir>] [--port 9383] [--keep]
//
// upgrade: a copy of a data root written by 1.x (its Library with library.db, settings.json and logs; models are not
// needed) gets the project folders of every --add folder beside its own (projects saved by other versions: schema
// v2 by 1.2.0, v3 by 1.3.0, v4 by 2.0), then the 2.0 build opens it: every recording is listed (after Rebuild index
// when the 1.x index did not know it), opens in Review with its transcript, documents and History, and exports its mix,
// transcript and details. After Memento closes, every file that was there is checked: media, transcripts, documents
// and History are byte for byte as they were, and every value of each JSON file is still in it (a file brought up to a
// newer schema may only add). The copy lives in --data (default artifacts/e2e-data-x20-upgrade); the source is
// never written.
//
// recovery: a recording of Display 1 and the simulated microphone is killed (TerminateProcess) after --seconds, the
// video file's last fragment is torn (its last 64 KB cut off, as a power cut mid-write leaves it), and Memento starts
// again: the recovery dialog must name the recording with both its audio and its video, the audio must be kept up to
// the last checkpoint and the video up to the last whole fragment, and both must play. The capture shows this PC's
// display, so the data root is deleted at the end (--keep leaves it); no screenshot shows the video.

import { cpSync, existsSync, mkdirSync, readdirSync, readFileSync, rmSync, statSync, truncateSync, writeFileSync } from 'node:fs';
import { execFileSync } from 'node:child_process';
import { join, relative, resolve } from 'node:path';
import { filesOf, option, repoRoot, Run, sha256, sleep } from './h1-lib.mjs';

const args = process.argv.slice(2);
const part = option(args, '--part', 'upgrade');
const dataRoot = resolve(option(args, '--data', join(repoRoot, 'artifacts', `e2e-data-x20-${part}`)));
const out = resolve(option(args, '--out', join(repoRoot, 'artifacts', 'e2e', `x20-${part}`)));
const port = Number(option(args, '--port', '9383'));
const run = new Run({ name: `2.0 ${part}`, dataRoot, out, port });
const measurements = {};
const readJson = (file) => JSON.parse(readFileSync(file, 'utf8'));

/** Every path in `before` (a parsed JSON value) whose value is missing or different in `after`; arrays by index. */
function lostValues(before, after, path = '') {
  if (before === null || typeof before !== 'object') {
    return before === after ? [] : [`${path || '/'}: ${JSON.stringify(before)} → ${JSON.stringify(after)}`];
  }
  if (after === null || typeof after !== 'object') return [`${path || '/'}: an object became ${JSON.stringify(after)}`];
  return Object.keys(before).flatMap((key) => lostValues(before[key], after[key], `${path}/${key}`));
}

async function upgrade() {
  const from = resolve(option(args, '--from', ''));
  const adds = args.flatMap((a, i) => (a === '--add' ? [resolve(args[i + 1])] : []));
  rmSync(dataRoot, { recursive: true, force: true });
  mkdirSync(run.memento, { recursive: true });
  for (const entry of ['Library', 'settings.json', 'logs', 'templates', 'styles']) {
    if (existsSync(join(from, 'Memento', entry))) cpSync(join(from, 'Memento', entry), join(run.memento, entry), { recursive: true });
  }
  for (const projects of adds) {
    for (const id of readdirSync(projects)) cpSync(join(projects, id), join(run.library, id), { recursive: true });
  }
  const ids = run.projectIds();
  const versions = Object.fromEntries(ids.map((id) => [id, run.manifest(id).schemaVersion]));
  measurements.projects = versions;
  run.log('library', JSON.stringify(versions));
  const before = new Map(filesOf(run.memento).filter((f) => !f.file.startsWith('logs/') && !f.file.startsWith('webview2/') && !/^Library\/library\.db/.test(f.file)).map((f) => [f.file, { bytes: f.bytes, sha256: sha256(join(run.memento, f.file)) }]));
  const jsonBefore = new Map([...before.keys()].filter((f) => f.endsWith('.json')).map((f) => [f, readJson(join(run.memento, f))]));

  await run.start(['--simulate-audio', '--update-feed=off']);
  await run.page.waitFor(`/Newest first|Your library is empty/.test(document.body.innerText)`, 'the Library', 60_000);
  const old = jsonBefore.get('settings.json');
  if (old) {
    const now = await run.bridge('settings.get');
    measurements.settings = { theme: now.theme, listDensity: now.listDensity, keepOnlyMix: now.recording.storage.keepOnlyMix, timing: now.transcription.timing, expectedSpeakers: now.speakers.expectedSpeakers, rememberVoices: now.speakers.rememberVoices };
    run.check('the 1.x settings are read with their values; keep only the mix and the live transcript start off',
      now.theme === old.theme && now.listDensity === old.listDensity && now.recording.defaultType === old.recording.defaultType
        && now.history.keepDays === old.history.keepDays && now.transcription.lowConfidenceThreshold === old.transcription.lowConfidenceThreshold
        && String(now.speakers.expectedSpeakers) === String(old.speakers.expectedSpeakers ?? 'auto')
        && now.recording.storage.keepOnlyMix === false && now.transcription.timing === 'after' && now.speakers.rememberVoices === false,
      JSON.stringify(measurements.settings));
  }
  // (library.db is only an index, rebuilt from the folders, so it is left out of the comparison.)
  let listed = (await run.bridge('library.list', {})).recordings.map((r) => r.id);
  measurements.listedAtStart = listed.length;
  measurements.indexedAtStart = ids.every((id) => listed.includes(id));
  {
    // Rebuild index (Settings › Storage and history) on the mixed library, whether or not the start found them all.
    await run.bridge('library.rebuildIndex');
    await run.until(async () => {
      listed = (await run.bridge('library.list', {})).recordings.map((r) => r.id);
      return ids.every((id) => listed.includes(id));
    }, 'every recording listed after Rebuild index', 60_000, 500);
  }
  run.check('every recording is listed', ids.every((id) => listed.includes(id)), `${measurements.listedAtStart} at start; ${listed.length} after; ${ids.length} folders`);
  const rows = (await run.bridge('library.list', {})).recordings;
  const exportFolder = join(dataRoot, 'exports');
  mkdirSync(exportFolder, { recursive: true });
  for (const id of ids) {
    const row = rows.find((r) => r.id === id);
    const project = await run.bridge('project.get', { recordingId: id });
    const transcript = await run.bridge('transcript.get', { recordingId: id }).catch((e) => ({ error: e.message }));
    const documents = await run.bridge('documents.list', { recordingId: id });
    const tag = `v${versions[id]} ${id}`;
    run.check(`${tag}: opens with its details and duration`, project.summary.id === id && project.summary.durationMs > 0 && row?.title === project.summary.title, `${project.summary.title}, ${project.summary.durationMs} ms, hasVideo ${row?.hasVideo}`);
    if (existsSync(join(run.folder(id), 'transcript.json'))) {
      const stored = readJson(join(run.folder(id), 'transcript.json'));
      run.check(`${tag}: the transcript reads as stored`, transcript.transcript?.segments?.length === stored.segments.length && transcript.transcript.speakers.length === stored.speakers.length, transcript.error ?? `${stored.segments.length} lines, ${stored.speakers.length} speakers`);
    }
    run.check(`${tag}: its documents are listed`, documents.documents.length === (existsSync(join(run.folder(id), 'documents')) ? readdirSync(join(run.folder(id), 'documents')).filter((f) => f.endsWith('.json')).length : 0), `${documents.documents.length}`);
    // Review through the page: the transcript lines show.
    await run.page.eval(`location.hash = '#/review/${id}'`);
    await run.page.waitFor(`!!document.querySelector('.review-panes')`, `Review of ${id}`, 30_000);
    await sleep(1000);
    const lines = await run.page.eval(`document.querySelectorAll('.segm').length`);
    run.check(`${tag}: Review shows it`, lines > 0 || !existsSync(join(run.folder(id), 'transcript.json')), `${lines} lines shown`);
    const selection = {
      audioMixed: { on: true, format: 'flac', bitrateKbps: null },
      tracks: { on: false, format: 'flac', bitrateKbps: null },
      transcript: { on: existsSync(join(run.folder(id), 'transcript.json')), formats: ['markdown', 'srt'] },
      documents: { on: false, documentIds: [], format: 'docx' },
      details: { on: true },
      attachments: { on: false },
    };
    const job = await run.bridge('export.run', { recordingId: id, selection, destination: { folder: join(exportFolder, id), createSubfolder: false }, remember: false });
    const done = await run.until(async () => (await run.events('export.progress')).find((e) => e.payload.jobId === job.jobId && e.payload.state !== 'running')?.payload, `the export of ${id}`, 300_000, 500);
    const manifest = existsSync(join(exportFolder, id, 'manifest.json')) ? readJson(join(exportFolder, id, 'manifest.json')) : { files: [] };
    run.check(`${tag}: exports`, done.state === 'done' && manifest.files.some((f) => f.name.endsWith('.flac')), `${done.state} ${done.message ?? ''}: ${manifest.files.map((f) => f.name).join(', ')}`);
  }
  await run.page.eval(`location.hash = '#/'`);
  await run.shot('library-mixed');
  await run.app.close();
  run.app = null;
  // The copied 1.x logs are older; only this run's lines count (log lines start with the local date).
  const today = new Date().toLocaleDateString('sv-SE');
  const errors = run.logLines(/\[(ERR|FTL)\]/).filter((l) => l.startsWith(today));
  run.check('the log has no error from this run', errors.length === 0, errors.slice(0, 5).map((l) => l.slice(0, 200)).join(' | '));

  // Nothing lost: every file kept, media and text byte for byte, JSON values all still there.
  const changed = [];
  const lost = [];
  for (const [file, was] of before) {
    const path = join(run.memento, file);
    if (!existsSync(path)) {
      lost.push(`${file}: gone`);
      continue;
    }
    if (sha256(path) === was.sha256) continue;
    if (!file.endsWith('.json')) {
      lost.push(`${file}: changed (${was.bytes} → ${statSync(path).size} bytes)`);
      continue;
    }
    const now = readJson(path);
    changed.push(`${file} (schema ${jsonBefore.get(file).schemaVersion ?? '-'} → ${now.schemaVersion ?? '-'})`);
    lost.push(...lostValues(jsonBefore.get(file), now).filter((l) => !/^\/schemaVersion:/.test(l)).map((l) => `${file} ${l}`));
  }
  measurements.changedJson = changed;
  measurements.lost = lost;
  run.check('nothing was lost: media, transcripts, documents and History as they were; JSON only gained', lost.length === 0, lost.slice(0, 20).join(' | '));
  run.log('JSON rewritten', changed.join(', ') || 'none');
}

async function recovery() {
  const keep = args.includes('--keep');
  const seconds = Number(option(args, '--seconds', '20'));
  rmSync(dataRoot, { recursive: true, force: true });
  let id = null;
  try {
    await run.start(['--simulate-audio', '--update-feed=off']);
    await run.page.waitFor(`/Newest first|Your library is empty/.test(document.body.innerText)`, 'the Library', 60_000);
    const sources = await run.bridge('sources.list');
    const video = await run.bridge('video.listSources');
    const display = video.displays.find((d) => d.primary) ?? video.displays[0];
    const mic = sources.audio.find((s) => s.kind === 'microphone') ?? sources.audio[0];
    const started = await run.bridge('recording.start', { title: 'Recovery with video', type: 'meeting', sourceIds: [mic.id, display.id] });
    id = started.recordingId;
    await sleep(seconds * 1000);
    await run.killApp();
    const state = readJson(join(run.folder(id), 'recording.state.json'));
    const videoFile = join(run.folder(id), state.videoTracks[0].parts[0].file);
    const size = statSync(videoFile).size;
    truncateSync(videoFile, Math.max(0, size - 64 * 1024));
    measurements.killed = { afterSeconds: seconds, videoBytes: size, tornTo: statSync(videoFile).size, trackFiles: readdirSync(join(run.folder(id), 'tracks')) };
    run.log('killed and torn', JSON.stringify(measurements.killed));

    await run.start(['--simulate-audio', '--update-feed=off']);
    const dialog = await run.page.waitFor(`[...document.querySelectorAll('[role=dialog], [role=alertdialog]')].map((d) => d.innerText).find((t) => /Recovered/.test(t)) ?? ''`, 'the recovery dialog', 90_000);
    measurements.dialog = dialog.replace(/\s+/g, ' ');
    await run.shot('recovery-dialog');
    run.check('the recovery dialog names the interrupted recording', dialog.includes('Recovery with video'), measurements.dialog);
    run.check('the dialog says the video was kept too', /video/i.test(dialog), measurements.dialog);
    const manifest = run.manifest(id);
    const track = manifest.videoTracks?.[0];
    measurements.recovered = { state: manifest.state, durationMs: manifest.durationMs, audioMs: manifest.tracks.map((t) => t.durationMs), video: track && { durationMs: track.durationMs, frames: track.framesWritten, bytes: statSync(join(run.folder(id), track.file)).size }, recovery: manifest.recovery ?? null };
    run.check('the recording is recovered with its audio', manifest.state === 'recovered' && manifest.durationMs >= (seconds - 32) * 1000, JSON.stringify(measurements.recovered));
    run.check('the video is kept up to its last whole fragment', !!track && track.durationMs > 0 && track.durationMs <= manifest.durationMs + 1500, JSON.stringify(measurements.recovered.video));
    run.check('the torn bytes were cut off', run.logLines(/screen header rewritten .* trailing bytes cut/).length > 0, run.logLines(/trailing bytes cut/).map((l) => l.slice(0, 200)).join(' | '));
    run.check('History says it was recovered', run.history(id).some((h) => /Recovered/.test(h.summary)), run.history(id).map((h) => h.summary).join(' | '));
    await run.page.eval(`[...document.querySelectorAll('[role=dialog] button, [role=alertdialog] button')].find((b) => /OK|Close|Got it|Done/.test(b.innerText))?.click()`);
    const project = await run.bridge('project.get', { recordingId: id });
    const info = project.video?.tracks?.[0];
    const media = await run.page.eval(`(async () => {
      const load = (tag, src) => new Promise((done) => {
        const el = document.createElement(tag); el.muted = true; el.preload = 'metadata';
        const timer = setTimeout(() => done({ error: 'timeout' }), 15000);
        el.addEventListener('loadedmetadata', () => { clearTimeout(timer); done({ duration: el.duration }); el.removeAttribute('src'); el.load(); }, { once: true });
        el.addEventListener('error', () => { clearTimeout(timer); done({ error: el.error && el.error.code }); }, { once: true });
        el.src = src;
      });
      return { audio: await load('audio', ${JSON.stringify(project.mixUrl ?? '')}), video: ${info ? `await load('video', ${JSON.stringify(info.parts?.[0]?.url ?? '')})` : 'null'} };
    })()`);
    measurements.playback = media;
    run.check('the recovered mix and video both open in WebView2', media.audio.duration > 0 && media.video?.duration > 0, JSON.stringify(media));
  } catch (error) {
    run.check('the run finished', false, error.stack ?? String(error));
  } finally {
    if (run.app) await run.app.close().catch(() => run.app.kill());
    run.app = null;
    if (!keep && existsSync(dataRoot)) {
      try {
        rmSync(dataRoot, { recursive: true, force: true });
      } catch {
        execFileSync('powershell.exe', ['-NoProfile', '-NonInteractive', '-Command', `Remove-Item -LiteralPath '${dataRoot.replace(/'/g, "''")}' -Recurse -Force`], { windowsHide: true });
      }
      run.log(existsSync(dataRoot) ? 'NOT DELETED' : 'deleted', dataRoot);
    }
  }
}

try {
  if (part === 'upgrade') await upgrade();
  else if (part === 'recovery') await recovery();
  else throw new Error(`--part is upgrade or recovery, not ${part}`);
} catch (error) {
  run.check('the run finished', false, error.stack ?? String(error));
} finally {
  if (run.app) await run.app.close().catch(() => run.app.kill());
  const summary = { ...run.summary(), measurements };
  writeFileSync(join(run.out, 'summary.json'), JSON.stringify(summary, null, 2));
  run.log('done', `${summary.passed} passed, ${summary.failed} failed · ${relative(repoRoot, join(run.out, 'summary.json'))}`);
  process.exitCode = summary.failed === 0 ? 0 : 1;
}
