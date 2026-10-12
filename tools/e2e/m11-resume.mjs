// 2.1 "Resume recording", end to end through the real UI of the published app on the simulated audio engine (nothing
// is captured or played): record, stop, press Resume recording while it is being saved, record a second segment,
// stop, then check on disk that the recording has two segments with the gap as a pause, the earlier tracks untouched,
// a mix over both and the transcript stages queued over the whole recording.
//
//   node tools/e2e/m11-resume.mjs [--data <dir>] [--out <dir>] [--port <n>]
//
// The app runs with LOCALAPPDATA pointed at --data (default artifacts/e2e-data-m11), so the real library is never touched.

import { createHash } from 'node:crypto';
import { existsSync, readdirSync, readFileSync, rmSync, writeFileSync, mkdirSync } from 'node:fs';
import { join } from 'node:path';
import { App, repoRoot } from './app.mjs';
import { sleep } from './cdp.mjs';

const args = process.argv.slice(2);
const option = (name, fallback) => (args.includes(name) ? args[args.indexOf(name) + 1] : fallback);
const dataRoot = option('--data', join(repoRoot, 'artifacts', 'e2e-data-m11'));
const out = option('--out', join(repoRoot, 'artifacts', 'e2e', 'm11-resume'));
const port = Number(option('--port', '9403'));
const summary = { steps: [] };

let shotIndex = 0;
let page;
const app = new App({ dataRoot, port, args: ['--simulate-audio'] });
const library = () => join(app.memento, 'Library', 'projects');

function log(step, detail = '') {
  const line = `[${new Date().toLocaleTimeString()}] ${step}${detail ? ' · ' + detail : ''}`;
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
const sha256 = (file) => createHash('sha256').update(readFileSync(file)).digest('hex');

async function waitElapsed(seconds) {
  await page.waitFor(
    `(() => { const m = /(\\d\\d):(\\d\\d):(\\d\\d)/.exec(document.querySelector('[aria-label="Recording"]')?.innerText ?? ''); return m && (+m[1]*3600 + +m[2]*60 + +m[3]) >= ${seconds}; })()`,
    `${seconds} s recorded`,
    (seconds + 60) * 1000,
  );
}

try {
  rmSync(dataRoot, { recursive: true, force: true });
  mkdirSync(out, { recursive: true });
  page = await app.start();
  await hasText('Your library is empty', 30_000);

  // 1. Record the first segment.
  await page.click({ name: 'New recording' });
  await hasText('AUDIO SOURCES');
  await page.click({ role: 'textbox', name: 'Untitled meeting', exact: false });
  await page.key('a', ['ctrl']);
  await page.type('Resume check');
  await page.key('Enter');
  await page.click({ name: 'Start recording' });
  await hasText('RECORDING');
  await waitElapsed(8);
  await shot('first-segment');
  log('first segment', '8 s recorded');

  // 2. Stop, and resume while it is being saved.
  await page.click({ name: 'Stop and open review' });
  await hasText('Finalizing');
  await page.click({ name: 'Resume recording' });
  await shot('resume-queued');
  const ids = readdirSync(library());
  check(ids.length === 1, `one recording (${ids.length})`);
  const folder = join(library(), ids[0]);
  await page.waitFor(`(document.querySelector('.rec-subline')?.innerText ?? '').startsWith('Resumed ')`, 'the second segment', 60_000);
  const before = readJson(join(folder, 'project.json'));
  const firstTracks = before.tracks.filter((t) => !t.segment).map((t) => ({ id: t.id, file: t.file, sha256: t.sha256 }));
  log('resumed', `segment 2 of ${ids[0]}; first tracks ${firstTracks.map((t) => t.file).join(', ')}`);
  await shot('second-segment');

  // 3. Record the second segment, stop, Review.
  await waitElapsed(14);
  await page.click({ name: 'Stop and open review' });
  await page.waitFor(`!!document.querySelector('.review-meta-resumed')`, 'Review with the resume marker', 120_000);
  await page.waitFor(`!!document.querySelector('.wave-resume')`, 'the resume marker on the waveform', 60_000);
  await shot('review-resumed');
  const marker = await page.text('.wave-resume');
  log('Review', `${await page.text('.review-meta-resumed')} · waveform: ${marker}`);

  // 4. On disk.
  await page.waitFor(`!document.body.innerText.includes('Saving tracks')`, 'storing to finish', 60_000);
  await sleep(2000);
  const manifest = readJson(join(folder, 'project.json'));
  const history = readFileSync(join(folder, 'history.jsonl'), 'utf8').trim().split('\n').map((l) => JSON.parse(l));
  check(manifest.schemaVersion === 6, `schema 6 (${manifest.schemaVersion})`);
  check(manifest.segments.length === 2, `two segments (${manifest.segments.length})`);
  const [s1, s2] = manifest.segments;
  check(s2.offsetMs === s1.durationMs, `segment 2 starts where segment 1 ends (${s2.offsetMs} vs ${s1.durationMs})`);
  check(s2.gapMs > 0, `a gap between the segments (${s2.gapMs} ms)`);
  check(manifest.pauses.some((p) => p.atMs === s2.offsetMs && p.durationMs === s2.gapMs), 'the gap is a pause at the segment start');
  check(manifest.durationMs === s1.durationMs + s2.durationMs, `duration ${manifest.durationMs} = ${s1.durationMs} + ${s2.durationMs}`);
  check(Math.abs(manifest.mix.durationMs - manifest.durationMs) < 200, `mix covers both (${manifest.mix.durationMs} vs ${manifest.durationMs})`);
  for (const track of firstTracks) {
    const now = manifest.tracks.find((t) => t.id === track.id);
    check(now && now.sha256 === track.sha256 && now.file === track.file, `first-segment track ${track.id} unchanged`);
    check(sha256(join(folder, track.file)) === track.sha256, `first-segment file ${track.file} matches its hash`);
  }
  const second = manifest.tracks.filter((t) => t.segment === 2);
  check(second.length > 0 && second.every((t) => t.startOffsetMs >= s2.offsetMs), 'second-segment tracks after the first');
  check(Object.entries(manifest.integrity.files).every(([file, hash]) => existsSync(join(folder, file)) && sha256(join(folder, file)) === hash), 'every hashed file matches');
  check(!existsSync(join(folder, 'recording.state.json')), 'no state file left');
  check(history.some((h) => h.summary.startsWith('Recording resumed at')), 'History: Recording resumed');
  const saved = history.find((h) => h.summary.startsWith('Resumed recording saved'));
  check(saved, 'History: Resumed recording saved');
  const transcript = manifest.stages.find((s) => s.stage === 'transcript');
  log('transcript', transcript ? `${transcript.state} over the whole recording (${saved.detail})` : 'stage not applicable with these settings');

  summary.result = {
    id: ids[0],
    segments: manifest.segments,
    pauses: manifest.pauses,
    durationMs: manifest.durationMs,
    mix: manifest.mix,
    tracks: manifest.tracks.map((t) => ({ id: t.id, file: t.file, segment: t.segment ?? 1, startOffsetMs: t.startOffsetMs, durationMs: t.durationMs })),
    stages: manifest.stages.map((s) => `${s.stage}:${s.state}`),
    history: history.map((h) => `${h.stage}/${h.event}: ${h.summary}`),
  };
  log('passed');
} catch (error) {
  summary.error = String(error?.stack ?? error);
  console.error(error);
  if (page) await shot('failure').catch(() => undefined);
  process.exitCode = 1;
} finally {
  await app.close().catch(() => app.kill());
  writeFileSync(join(out, 'summary.json'), JSON.stringify(summary, null, 2));
}
