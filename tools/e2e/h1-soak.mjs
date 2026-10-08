// H1 real-device soak, driven through the real UI of the published app over the DevTools protocol: records the
// default microphone, "everything this PC plays" and one application (a PowerShell process looping a quiet
// public-domain reading) for --hours, with the default 30 s checkpoints. Every minute it samples Memento.exe (working
// set, private bytes, handles, threads), its WebView2 processes, the worker and the graphics card into soak.csv, and
// how long the page takes to answer; every 30 minutes it saves a screenshot. Then it stops, times finalize, waits for
// the transcript and speakers, and writes summary.json (track durations and drift, file sizes, FLAC time, RTF, log
// warnings, crash reports).
//
//   node tools/e2e/h1-soak.mjs --play <speech.wav> [--hours 4] [--models <dir>] [--data <dir>] [--out <dir>]
//                              [--exe <Memento.exe>] [--port 9411] [--process-hours 8]
//
// The data folder holds room audio afterwards: delete it when the numbers are read.

import { execFile, spawn } from 'node:child_process';
import { cpSync, existsSync, mkdirSync, readdirSync, readFileSync, statSync, writeFileSync, appendFileSync } from 'node:fs';
import { createHash } from 'node:crypto';
import { join, resolve } from 'node:path';
import { promisify } from 'node:util';
import { App, repoRoot } from './app.mjs';
import { sleep } from './cdp.mjs';
import { appSwitchName } from './h1-lib.mjs';

const run = promisify(execFile);
const args = process.argv.slice(2);
const option = (name, fallback) => (args.includes(name) ? args[args.indexOf(name) + 1] : fallback);
const hours = Number(option('--hours', '4'));
const processHours = Number(option('--process-hours', '8'));
const dataRoot = resolve(option('--data', join(repoRoot, 'artifacts', 'e2e-data-soak')));
const out = resolve(option('--out', join(repoRoot, 'artifacts', 'e2e', 'soak')));
const play = option('--play', null) && resolve(option('--play', null));
const models = option('--models', null) && resolve(option('--models', null));
const exe = option('--exe', null) && resolve(option('--exe', null));
const port = Number(option('--port', '9411'));
const appSource = 'Windows PowerShell';
const csv = join(out, 'soak.csv');
const summary = { startedAt: new Date().toISOString(), hours, samples: 0, screenshots: [], slowAnswers: 0 };

const app = new App({ dataRoot, port, ...(exe ? { exe } : {}) });
const library = () => join(app.memento, 'Library', 'projects');
const t0 = Date.now();
let page;

function log(step, detail = '') {
  console.log(`[${((Date.now() - t0) / 1000).toFixed(0).padStart(6)} s] ${step}${detail ? ' · ' + detail : ''}`);
}

const hasText = (text, timeoutMs = 20_000) => page.waitFor(`document.body.innerText.includes(${JSON.stringify(text)})`, `"${text}"`, timeoutMs);
const readJson = (file) => JSON.parse(readFileSync(file, 'utf8'));
const projectIds = () => (existsSync(library()) ? readdirSync(library()).sort() : []);
const sha256 = (file) => createHash('sha256').update(readFileSync(file)).digest('hex');

async function elapsedSeconds() {
  const text = await page.text('[aria-label="Recording"]');
  const match = /(\d\d):(\d\d):(\d\d)/.exec(text);
  return match ? Number(match[1]) * 3600 + Number(match[2]) * 60 + Number(match[3]) : null;
}

/** One PowerShell call: the app, its WebView2 children and the worker, plus the card's memory. */
async function sampleProcesses(pid) {
  const script = `
$ErrorActionPreference = 'SilentlyContinue'
$p = Get-Process -Id ${pid}
$kids = Get-CimInstance Win32_Process -Filter "ParentProcessId=${pid}"
$all = @(); foreach ($k in $kids) { $all += $k.ProcessId; $all += (Get-CimInstance Win32_Process -Filter "ParentProcessId=$($k.ProcessId)").ProcessId }
$wv = Get-Process -Id $all | Where-Object ProcessName -eq 'msedgewebview2'
$wk = Get-Process -Name 'Memento.Worker'
$gpu = (& nvidia-smi --query-gpu=memory.used --format=csv,noheader,nounits) -join ''
$wkGpu = 0; foreach ($w in $wk) { $c = Get-Counter "\\GPU Process Memory(pid_$($w.Id)_*)\\Dedicated Usage"; if ($c) { $wkGpu += ($c.CounterSamples | Measure-Object CookedValue -Sum).Sum } }
'{0},{1},{2},{3},{4},{5},{6},{7}' -f [int]($p.WorkingSet64/1MB), [int]($p.PrivateMemorySize64/1MB), $p.HandleCount, $p.Threads.Count, [int](($wv | Measure-Object WorkingSet64 -Sum).Sum/1MB), [int](($wk | Measure-Object WorkingSet64 -Sum).Sum/1MB), [int]$gpu.Trim(), [int]($wkGpu/1MB)`;
  const { stdout } = await run('powershell.exe', ['-NoProfile', '-NonInteractive', '-Command', script], { windowsHide: true, timeout: 60_000 });
  return stdout.trim();
}

function startPlayback(file) {
  // PlayLooping returns at once; the process must stay alive to keep its audio session, so it sleeps.
  const child = spawn('powershell.exe', ['-NoProfile', '-NonInteractive', '-Command', `$p = New-Object Media.SoundPlayer '${file}'; $p.PlayLooping(); Start-Sleep -Seconds 100000`], { stdio: 'ignore', windowsHide: true });
  return { pid: child.pid, stop: () => spawn('taskkill', ['/F', '/T', '/PID', String(child.pid)], { stdio: 'ignore' }) };
}

function logLines(pattern) {
  const logs = join(app.memento, 'logs');
  if (!existsSync(logs)) return [];
  return readdirSync(logs).filter((f) => f.endsWith('.log') || f.endsWith('.txt')).flatMap((f) => readFileSync(join(logs, f), 'utf8').split('\n')).filter((l) => pattern.test(l));
}

/** Drift per source from the checkpoint log lines: first, last, min and max. */
function driftReport() {
  const bySource = {};
  for (const line of logLines(/Checkpoint .*drift/)) {
    const m = /Checkpoint (\S+): ([\d.]+) s durable, drift (-?[\d.]+|NaN) ppm, overrun frames (\d+)/.exec(line);
    if (!m) continue;
    const entry = (bySource[m[1]] ??= { checkpoints: 0, first: null, last: null, min: Infinity, max: -Infinity, overrunFrames: 0, durableSeconds: 0 });
    const ppm = Number(m[3]);
    entry.checkpoints += 1;
    entry.durableSeconds = Number(m[2]);
    entry.overrunFrames = Number(m[4]);
    if (Number.isFinite(ppm)) {
      entry.first ??= ppm;
      entry.last = ppm;
      entry.min = Math.min(entry.min, ppm);
      entry.max = Math.max(entry.max, ppm);
    }
  }
  return bySource;
}

function filesOf(folder, prefix = '') {
  return readdirSync(folder).flatMap((name) => {
    const full = join(folder, name);
    return statSync(full).isDirectory() ? filesOf(full, `${prefix}${name}/`) : [{ file: `${prefix}${name}`, bytes: statSync(full).size }];
  });
}

let playback = null;
try {
  mkdirSync(out, { recursive: true });
  if (models) {
    cpSync(models, join(dataRoot, 'Memento', 'models'), { recursive: true });
    log('models copied', models);
  }
  if (!play || !existsSync(play)) throw new Error('--play names the speech WAV to loop');
  playback = startPlayback(play);
  log('reading playing', `PowerShell pid ${playback.pid}`);
  await sleep(2500);

  page = await app.start();
  log('app started', `pid ${app.process.pid}`);
  await page.click({ name: 'New recording' });
  await hasText('AUDIO SOURCES');
  await page.click({ role: 'textbox', name: 'Untitled meeting', exact: false });
  await page.key('a', ['ctrl']);
  await page.type('H1 soak');
  await page.key('Enter');
  await page.waitFor(`document.body.innerText.includes(${JSON.stringify(appSource)})`, `the ${appSource} source`, 30_000).catch(async () => {
    await page.click({ name: 'Add a source: look again for microphones and apps' });
    await hasText(appSource, 20_000);
  });
  let appSwitch = null;
  for (let i = 0; i < 40 && !appSwitch; i++) {
    appSwitch = await appSwitchName(page, playback.pid, appSource);
    if (!appSwitch) await sleep(500);
  }
  await page.click({ name: appSwitch ?? appSource });
  await hasText('3 audio sources selected');
  await page.screenshot(join(out, 'soak-00-sources.png'));
  await page.click({ name: 'Start recording' });
  await hasText('RECORDING');
  const startedAt = Date.now();
  log('recording', `${hours} h, microphone + system audio + ${appSource}`);

  writeFileSync(csv, 'wall_s,recorded_s,ws_mb,private_mb,handles,threads,webview_ws_mb,worker_ws_mb,gpu_used_mb,worker_gpu_mb,ui_answer_ms,checkpoint_line\n');
  const endAt = startedAt + hours * 3_600_000;
  let nextShot = startedAt + 30 * 60_000;
  let shotIndex = 1;
  while (Date.now() < endAt) {
    const asked = performance.now();
    let recorded = null;
    let footer = '';
    try {
      recorded = await elapsedSeconds();
      footer = (await page.text('footer')).replace(/\s+/g, ' ').replace(/,/g, ';').slice(0, 120);
    } catch (error) {
      footer = `page error: ${error.message}`.replace(/,/g, ';');
    }
    const answerMs = Math.round(performance.now() - asked);
    if (answerMs > 1000) summary.slowAnswers += 1;
    let processes = ',,,,,,,';
    try {
      processes = await sampleProcesses(app.process.pid);
    } catch (error) {
      log('sample failed', error.message);
    }
    appendFileSync(csv, `${Math.round((Date.now() - startedAt) / 1000)},${recorded ?? ''},${processes},${answerMs},${footer}\n`);
    summary.samples += 1;
    if (Date.now() >= nextShot) {
      const file = join(out, `soak-${String(shotIndex).padStart(2, '0')}-${shotIndex * 30}min.png`);
      await page.screenshot(file);
      summary.screenshots.push(file);
      log('screenshot', `${shotIndex * 30} min, recorded ${recorded} s, ${processes}`);
      shotIndex += 1;
      nextShot += 30 * 60_000;
    }
    await sleep(Math.max(1000, 60_000 - (performance.now() - asked)));
  }

  summary.recordedAtStop = await elapsedSeconds();
  const stopClicked = Date.now();
  await page.click({ name: 'Stop and open review' });
  playback.stop();
  const [id] = projectIds();
  summary.recordingId = id;
  const manifestPath = join(library(), id, 'project.json');
  await page.waitFor('true', 'page', 5000);
  log('stopped', `recorded ${summary.recordedAtStop} s`);

  // Finalize (stored), then transcript, speakers, topics.
  const deadline = Date.now() + processHours * 3_600_000;
  const seen = {};
  for (;;) {
    const manifest = readJson(manifestPath);
    for (const s of manifest.stages) {
      const key = `${s.stage}:${s.state}`;
      if (!seen[key]) {
        seen[key] = Math.round((Date.now() - stopClicked) / 1000);
        log('stage', `${key} at +${seen[key]} s (${s.label ?? ''})`);
      }
    }
    if (manifest.stages.length > 0 && !manifest.stages.some((s) => s.state === 'active' || s.state === 'queued')) break;
    if (Date.now() > deadline) throw new Error(`still processing after ${processHours} h`);
    await sleep(10_000);
  }
  summary.stageTimes = seen;
  await sleep(3000);
  await page.screenshot(join(out, 'soak-review.png'));
  const folder = join(library(), id);
  const manifest = readJson(manifestPath);
  summary.manifest = {
    state: manifest.state,
    durationMs: manifest.durationMs,
    tracks: manifest.tracks.map((t) => ({ id: t.id, name: t.name, file: t.file, codec: t.codec, channels: t.channels, sampleRate: t.sampleRate, durationMs: t.durationMs, endedEarlyAtMs: t.endedEarlyAtMs, endReason: t.endReason })),
    mix: manifest.mix,
    stages: manifest.stages.map((s) => `${s.stage}:${s.state}`),
    failures: manifest.failures,
  };
  summary.files = filesOf(folder);
  summary.hashes = Object.entries(manifest.integrity?.files ?? {}).map(([file, hash]) => ({ file, matches: existsSync(join(folder, file)) && sha256(join(folder, file)) === hash }));
  summary.history = readFileSync(join(folder, 'history.jsonl'), 'utf8').trim().split('\n').map((l) => JSON.parse(l)).map((h) => `${h.at} ${h.stage}/${h.event}: ${h.summary}${h.detail ? ' — ' + h.detail : ''}`);
  const transcript = existsSync(join(folder, 'transcript.json')) ? readJson(join(folder, 'transcript.json')) : null;
  summary.transcript = transcript && { segments: transcript.segments.length, speakers: transcript.speakers.length, engine: transcript.engine, coverageGaps: transcript.coverageGaps?.length };
  summary.drift = driftReport();
  summary.logProblems = logLines(/\[(WRN|ERR|FTL)\]/).map((l) => l.slice(0, 300));
  summary.crashReports = existsSync(join(app.memento, 'logs')) ? readdirSync(join(app.memento, 'logs')).filter((f) => /crash/i.test(f)) : [];
  await app.close();
  summary.result = 'passed';
} catch (error) {
  summary.result = `failed: ${error.message}`;
  console.error(error);
  try {
    await page?.screenshot(join(out, 'soak-failure.png'));
  } catch {
    // The page is gone.
  }
  process.exitCode = 1;
} finally {
  playback?.stop();
  summary.endedAt = new Date().toISOString();
  writeFileSync(join(out, 'summary.json'), JSON.stringify(summary, null, 2));
  console.log(JSON.stringify({ ...summary, history: summary.history?.length, logProblems: summary.logProblems?.length }, null, 2));
}
