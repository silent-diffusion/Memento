// H1 recovery: Memento is killed (TerminateProcess, like a crash or a power cut for the app) at every stage and
// started again; each case must end in a consistent project with a History entry and the designed dialog or card.
// Stages: recording, finalizing, transcript, speakers, topics, optimize, import, export, library move. The moment to
// kill is taken from the project files and the bridge's progress events (polled every 50–100 ms).
//
// Recordings use the simulated engine (speed 20, so a 10-minute recording takes 30 s); the processing stages run on a
// 16-minute two-reader public-domain WAV imported with library.importMedia { path } (the import picker is Windows'
// own dialog; m3-flow covers it). Exports and the library move are started with their bridge methods for the same
// reason. Everything else is observed through the UI and the files.
//
//   node tools/e2e/h1-recovery.mjs --audio <16-min wav> --long <2-h wav> --models <dir> [--data <dir>] [--out <dir>]
//                                  [--port 9440] [--only <case,...>]
import { existsSync, readdirSync, rmSync, writeFileSync, mkdirSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { Run, filesOf, option, repoRoot, sha256, sleep } from './h1-lib.mjs';

const args = process.argv.slice(2);
const dataRoot = resolve(option(args, '--data', join(repoRoot, 'artifacts', 'e2e-data-h1-recovery')));
const out = resolve(option(args, '--out', join(repoRoot, 'artifacts', 'e2e', 'h1-recovery')));
const audio = resolve(option(args, '--audio', ''));
const longAudio = resolve(option(args, '--long', ''));
const models = option(args, '--models', null);
const only = option(args, '--only', null)?.split(',');
const APP_ARGS = ['--simulate-audio', 'speed=20', '--update-feed=off'];

const run = new Run({ name: 'H1 recovery', dataRoot, out, port: Number(option(args, '--port', '9440')), models });
const cases = [];

const stageOf = (id, name) => {
  try {
    return run.manifest(id).stages.find((s) => s.stage === name) ?? null;
  } catch {
    return null; // between the .tmp write and the move
  }
};

async function relaunch() {
  await run.start(APP_ARGS);
  await sleep(2500);
}

/** Integrity: every file the manifest names exists and matches its SHA-256. */
function integrity(id) {
  const manifest = run.manifest(id);
  const files = Object.entries(manifest.integrity?.files ?? {});
  const bad = files.filter(([file, hash]) => !existsSync(join(run.folder(id), file)) || sha256(join(run.folder(id), file)) !== hash).map(([file]) => file);
  const missingTracks = manifest.tracks.filter((t) => !existsSync(join(run.folder(id), t.file))).map((t) => t.file);
  return { files: files.length, bad, missingTracks, state: manifest.state };
}

async function recoveryDialog() {
  const dialog = await run.until(() => run.page.eval(`document.querySelector('[role="dialog"]')?.innerText.replace(/\\s+/g, ' ') ?? null`), 'the recovery dialog', 15_000).catch(() => null);
  return dialog;
}

async function closeDialog() {
  await run.page.click({ name: 'Later', within: '[role="dialog"]' }).catch(() => null);
  await sleep(500);
}

async function waitStagesSettled(id, minutes) {
  await run.until(() => {
    const stages = run.manifest(id).stages;
    return stages.length > 0 && stages.every((s) => s.state === 'done' || s.state === 'failed');
  }, `stages of ${id} to settle`, minutes * 60_000, 1000);
}

async function caseRecording() {
  const name = 'recording';
  await run.newRecording('H1 kill while recording');
  await run.page.click({ name: 'Start recording' });
  await run.hasText('RECORDING');
  const [id] = run.projectIds().filter((p) => run.manifest(p).state === 'recording');
  await run.waitElapsed(150); // 150 simulated seconds: five checkpoints
  const before = await run.elapsedSeconds();
  await run.killApp();
  await relaunch();
  const dialog = await recoveryDialog();
  await run.shot(`${name}-recovered-dialog`);
  const manifest = run.manifest(id);
  const history = run.history(id).map((h) => h.summary);
  cases.push({
    name,
    killedAt: `${before} s recorded`,
    dialog,
    state: manifest.state,
    durationMs: manifest.durationMs,
    recovery: manifest.recovery,
    history,
    integrity: integrity(id),
  });
  run.check('recording: the recovery dialog explains what was saved', !!dialog && dialog.includes('Recovered an interrupted recording'), dialog ?? '(none)');
  run.check('recording: the project is recovered with its audio', manifest.state === 'recovered' && manifest.durationMs >= (before - 2) * 1000, `${manifest.state}, ${manifest.durationMs} ms of ${before} s`);
  run.check('recording: History says so', history.some((h) => /Recovered after Memento closed/.test(h)), history.join(' | '));
  await closeDialog();
  return id;
}

async function caseFinalizing() {
  const name = 'finalizing';
  await run.page.click({ name: 'Library' }).catch(() => null);
  await run.newRecording('H1 kill while finalizing');
  await run.page.click({ name: 'Start recording' });
  await run.hasText('RECORDING');
  const id = run.projectIds().find((p) => run.manifest(p).state === 'recording');
  await run.waitElapsed(1200); // 20 simulated minutes, 3 tracks: finalize takes a few seconds
  await run.page.click({ name: 'Stop and open review' });
  const seen = await run.until(() => (stageOf(id, 'stored')?.state === 'active' ? stageOf(id, 'stored') : null), 'finalizing', 30_000, 30);
  await run.killApp();
  await relaunch();
  const dialog = await recoveryDialog();
  await run.shot(`${name}-recovered-dialog`);
  await closeDialog();
  await run.until(() => run.manifest(id).state !== 'finalizing', 'finalize after recovery', 60_000);
  const manifest = run.manifest(id);
  const history = run.history(id).map((h) => `${h.stage}/${h.event}: ${h.summary}`);
  cases.push({ name, killedAt: `stored ${seen.label}`, dialog, state: manifest.state, durationMs: manifest.durationMs, history, integrity: integrity(id) });
  run.check('finalizing: recovered and stored', ['recovered', 'ready'].includes(manifest.state) && integrity(id).bad.length === 0 && integrity(id).missingTracks.length === 0, JSON.stringify(integrity(id)));
  run.check('finalizing: History has the interruption and the store', history.some((h) => /recovered|Recovered/.test(h)) && history.some((h) => h.startsWith('stored/completed')), history.join(' | '));
  run.check('finalizing: the dialog or the Library says what happened', !!dialog, dialog ?? '(no dialog)');
}

async function killDuringStage(id, stage, label) {
  const seen = await run.until(() => {
    const s = stageOf(id, stage);
    return s?.state === 'active' && !/Paused|Queued/.test(s.label ?? '') && (s.percent ?? 0) >= (stage === 'transcript' ? 10 : 0) ? s : null;
  }, `${stage} running`, 15 * 60_000, 50);
  const workers = run.workerPids();
  await run.killApp();
  const workersAfter = run.workerPids().filter((p) => workers.includes(p));
  await relaunch();
  await run.shot(`${label}-relaunched`);
  return { seen, orphanWorkers: workersAfter };
}

async function caseStages(importedId) {
  // transcript → speakers → topics → optimize on one imported recording, killed once in each.
  const results = {};
  for (const stage of ['transcript', 'speakers', 'topics', 'optimize']) {
    if (only && !only.includes(stage)) continue;
    let seen;
    try {
      const killed = await killDuringStage(importedId, stage, stage);
      seen = killed.seen;
      run.check(`${stage}: the worker ended with Memento`, killed.orphanWorkers.length === 0, `orphans ${killed.orphanWorkers.join(',')}`);
    } catch (error) {
      run.check(`${stage}: caught running`, false, error.message);
      continue;
    }
    const after = stageOf(importedId, stage);
    const history = run.history(importedId);
    results[stage] = { killedAt: `${seen.percent ?? '-'}% ${seen.label ?? ''}`, afterRelaunch: after && `${after.state} ${after.label ?? ''}` };
    run.log(stage, `killed at ${results[stage].killedAt}; after relaunch ${results[stage].afterRelaunch}`);
  }

  await waitStagesSettled(importedId, 20);
  const manifest = run.manifest(importedId);
  const history = run.history(importedId).map((h) => `${h.stage}/${h.event}: ${h.summary}`);
  const transcriptFile = join(run.folder(importedId), 'transcript.json');
  const transcript = existsSync(transcriptFile) ? JSON.parse((await import('node:fs')).readFileSync(transcriptFile, 'utf8')) : null;
  cases.push({ name: 'transcript, speakers, topics, optimize', results, stages: manifest.stages.map((s) => `${s.stage}:${s.state}`), history, integrity: integrity(importedId), segments: transcript?.segments.length, speakers: transcript?.speakers.length, tracks: manifest.tracks.map((t) => `${t.file} ${t.codec}`) });
  run.check('stages: every stage finished after the kills', manifest.stages.every((s) => s.state === 'done'), manifest.stages.map((s) => `${s.stage}:${s.state}`).join(', '));
  run.check('stages: History shows each pass continuing', ['transcript', 'speakers'].every((s) => history.some((h) => h.startsWith(`${s}/started`) && /continuing|Transcribing|Identifying/.test(h))), history.filter((h) => /started/.test(h)).join(' | '));
  run.check('stages: no audio lost after optimize was killed', integrity(importedId).bad.length === 0 && integrity(importedId).missingTracks.length === 0, JSON.stringify(integrity(importedId)));
  run.check('stages: a transcript with speakers', (transcript?.segments.length ?? 0) > 50 && (transcript?.speakers.length ?? 0) >= 1, `${transcript?.segments.length} segments, ${transcript?.speakers.length} speakers`);
  await run.page.click({ name: 'Library' }).catch(() => null);
  await sleep(800);
  await run.shot('stages-library-after');
}

async function caseImport() {
  const name = 'import';
  const before = new Set(run.projectIds());
  const started = run.bridge('library.importMedia', { path: longAudio, title: 'H1 import killed' }).catch((e) => e);
  const id = await run.until(() => run.projectIds().find((p) => !before.has(p)), 'the import project', 30_000, 50);
  await run.until(() => stageOf(id, 'stored')?.state === 'active' && (stageOf(id, 'stored')?.percent ?? 0) >= 20, 'the import at 20%', 120_000, 50).catch(() => null);
  const seen = stageOf(id, 'stored');
  void started;
  await run.killApp();
  await relaunch();
  await run.page.click({ name: 'Library' }).catch(() => null);
  await sleep(1500);
  await run.shot(`${name}-relaunched`);
  const row = await run.page.eval(`[...document.querySelectorAll('.lib-item')].map((r) => r.innerText.replace(/\\s+/g, ' ')).find((t) => t.includes('H1 import killed')) ?? ''`);
  const manifest = run.manifest(id);
  const history = run.history(id).map((h) => `${h.stage}/${h.event}: ${h.summary}`);
  cases.push({ name, killedAt: `stored ${seen?.percent ?? '?'}%`, row, state: manifest.state, failures: manifest.failures, history, files: filesOf(run.folder(id)).map((f) => f.file) });
  run.check('import: the row offers Import again', row.includes('Import interrupted') && row.includes('Import again'), row);
  run.check('import: History says it was interrupted', history.some((h) => /interrupted/i.test(h)), history.join(' | '));
  run.check('import: the half-imported copy was removed', !filesOf(run.folder(id)).some((f) => /\.(wav|flac)$/i.test(f.file)), filesOf(run.folder(id)).map((f) => f.file).join(', '));
  // Import again, through the row's own button.
  await run.page.click('.pill-retry').catch(() => null);
  await run.until(() => stageOf(id, 'stored')?.state === 'done', 'import again to store', 10 * 60_000, 500).catch(() => null);
  run.check('import: Import again stores it', stageOf(id, 'stored')?.state === 'done', JSON.stringify(stageOf(id, 'stored')));
  return id;
}

async function caseExport(id) {
  const name = 'export';
  const destination = join(dataRoot, 'exports');
  mkdirSync(destination, { recursive: true });
  const selection = {
    audioMixed: { on: true, format: 'wav', bitrateKbps: null },
    tracks: { on: true, format: 'wav', bitrateKbps: null },
    transcript: { on: true, formats: ['json', 'markdown', 'srt'] },
    documents: { on: false, documentIds: [], format: 'docx' },
    details: { on: true },
    attachments: { on: false },
  };
  const { jobId } = await run.bridge('export.run', { recordingId: id, selection, destination: { folder: destination, createSubfolder: true }, remember: false });
  const progress = await run.until(async () => (await run.events('export.progress')).find((e) => e.payload.jobId === jobId && e.payload.state === 'running' && e.payload.percent >= 10), 'export at 10%', 120_000, 50).catch(() => null);
  // History gains the 'Export interrupted' line at the next launch; everything else must stay as it was.
  const audioAndData = () => filesOf(run.folder(id)).filter((f) => f.file !== 'history.jsonl').map((f) => `${f.file}:${f.bytes}`).sort();
  const projectBefore = audioAndData();
  await run.killApp();
  await relaunch();
  const projectAfter = audioAndData();
  const left = existsSync(destination) ? filesOf(destination).map((f) => f.file) : [];
  cases.push({ name, killedAt: progress ? `${progress.payload.percent}%` : 'unknown', leftInDestination: left, projectUnchanged: JSON.stringify(projectBefore) === JSON.stringify(projectAfter) });
  const changed = [...projectBefore.filter((f) => !projectAfter.includes(f)).map((f) => `-${f}`), ...projectAfter.filter((f) => !projectBefore.includes(f)).map((f) => `+${f}`)];
  run.check('export: the project is untouched', changed.length === 0, changed.join(', '));
  run.check('export: nothing it wrote is left in the destination', left.length === 0, left.join(', '));
  const exportHistory = run.history(id).find((h) => h.summary === 'Export interrupted');
  run.check('export: History says the export was interrupted', !!exportHistory, exportHistory?.detail ?? '(none)');
  run.log('export', `left in the destination: ${left.length} file(s) ${left.slice(0, 4).join(', ')}`);
}

async function caseMove() {
  const name = 'library move';
  const from = (await run.bridge('settings.get')).libraryPath;
  const target = join(dataRoot, 'moved-library');
  rmSync(target, { recursive: true, force: true });
  const filesBefore = filesOf(from).filter((f) => !f.file.startsWith('library.db')).map((f) => `${f.file}:${f.bytes}`).sort();
  const { jobId } = await run.bridge('library.move', { newPath: target });
  const progress = await run.until(async () => (await run.events('library.moveProgress')).find((e) => e.payload.jobId === jobId && e.payload.state === 'running' && e.payload.percent >= 20), 'move at 20%', 120_000, 30).catch(() => null);
  await run.killApp();
  await relaunch();
  const settings = await run.bridge('settings.get');
  const effective = settings.libraryPath;
  const filesAfter = filesOf(effective).filter((f) => !f.file.startsWith('library.db')).map((f) => `${f.file}:${f.bytes}`).sort();
  const listed = (await run.bridge('library.list', {})).totalCount;
  const leftover = existsSync(target) && effective.toLowerCase() !== target.toLowerCase() ? filesOf(target).length : 0;
  await run.page.click({ name: 'Library' }).catch(() => null);
  await sleep(800);
  await run.shot('move-relaunched');
  cases.push({ name, killedAt: progress ? `${progress.payload.percent}%` : 'unknown', libraryAfter: effective, filesSame: JSON.stringify(filesBefore) === JSON.stringify(filesAfter), listed, leftoverFilesAtTarget: leftover });
  run.check('move: the library in Settings holds every file', JSON.stringify(filesBefore) === JSON.stringify(filesAfter), `${effective}: ${filesAfter.length} of ${filesBefore.length}`);
  run.check('move: every recording is listed', listed === run.projectIds.call({ library: join(effective, 'projects') }).length, `${listed}`);
  run.log('move', `library after relaunch: ${effective}; ${leftover} leftover file(s) in the half-copied target`);
}

try {
  rmSync(dataRoot, { recursive: true, force: true });
  run.copyModels();
  await relaunch();
  // AAC storage so the optimize stage runs after topics.
  await run.bridge('settings.set', { recording: { ...(await run.bridge('settings.get')).recording, storage: { codec: 'aac', bitrateKbps: 128, downmixMono: false, keepOnlyMix: false } } });

  if (!only || only.includes('recording')) await caseRecording();
  if (!only || only.includes('finalizing')) await caseFinalizing();
  let importedId = null;
  if (!only || ['transcript', 'speakers', 'topics', 'optimize', 'export'].some((s) => only.includes(s))) {
    const before = new Set(run.projectIds());
    await run.bridge('library.importMedia', { path: audio, title: 'H1 stages' });
    importedId = run.projectIds().find((p) => !before.has(p));
    await caseStages(importedId);
  }
  if (!only || only.includes('import')) await caseImport();
  if ((!only || only.includes('export')) && importedId) await caseExport(importedId);
  if (!only || only.includes('move')) await caseMove();
  await run.app.close();
} catch (error) {
  run.check('the run reached the end', false, error.stack);
  await run.shot('failure').catch(() => null);
  await run.app?.kill();
} finally {
  const summary = { ...run.summary(), cases, warnings: run.logLines(/\[(WRN|ERR|FTL)\]/).map((l) => l.slice(0, 260)) };
  writeFileSync(join(out, 'summary.json'), JSON.stringify(summary, null, 2));
  console.log(JSON.stringify({ passed: summary.passed, failed: summary.failed, failures: summary.results.filter((r) => !r.passed), crashReports: summary.crashReports }, null, 2));
}
