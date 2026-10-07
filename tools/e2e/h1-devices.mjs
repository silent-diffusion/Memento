// H1 device events through the real app on real devices (WASAPI), driven over the DevTools protocol:
//  1. The microphone is disabled mid-recording (as Windows' Sound settings do; the stand-in for unplugging a USB or
//     Bluetooth microphone), then enabled again and turned back on in Memento.
//  2. The default output is switched (HDMI monitor ↔ laptop speakers) while "Everything this PC plays" and an app record.
//  3. Sleep stand-in: Modern Standby freezes desktop apps, so Memento itself is suspended for 2 minutes during a
//     recording, and the worker (then Memento and the worker together) during a transcription.
// A PowerShell process loops a quiet public-domain reading as the app source. Every endpoint change is undone in
// `finally`. The data folder holds room audio afterwards: delete it when the numbers are read.
//
//   node tools/e2e/h1-devices.mjs --play <wav> --short <wav of 10+ min> --models <dir> [--data <dir>] [--out <dir>] [--only 1,2,3]
import { execFileSync, spawn } from 'node:child_process';
import { readFileSync, rmSync, writeFileSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { Run, appSwitchName, option, repoRoot, sleep } from './h1-lib.mjs';

const args = process.argv.slice(2);
const dataRoot = resolve(option(args, '--data', join(repoRoot, 'artifacts', 'e2e-data-h1-devices')));
const out = resolve(option(args, '--out', join(repoRoot, 'artifacts', 'e2e', 'h1-devices')));
const play = resolve(option(args, '--play', ''));
const shortAudio = resolve(option(args, '--short', ''));
const only = option(args, '--only', null)?.split(',');
const run = new Run({ name: 'H1 device events', dataRoot, out, port: 9470, models: option(args, '--models', null), exe: option(args, '--exe', undefined) });
const cases = [];
const helper = join(repoRoot, 'tools', 'e2e', 'audio-endpoints.ps1');

function endpoints(...command) {
  return execFileSync('powershell.exe', ['-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', helper, ...command], { encoding: 'utf8', windowsHide: true });
}
const listEndpoints = () => endpoints('list').trim().split(/\r?\n/).map((l) => { const [flow, state, def, id, name] = l.split('\t'); return { flow, state, isDefault: def === 'default', id, name }; });

function suspend(pid, on) {
  const verb = on ? 'NtSuspendProcess' : 'NtResumeProcess';
  execFileSync('powershell.exe', ['-NoProfile', '-NonInteractive', '-Command', `Add-Type -Name N -Namespace W -MemberDefinition '[DllImport("ntdll.dll")] public static extern int ${verb}(System.IntPtr h); [DllImport("kernel32.dll")] public static extern System.IntPtr OpenProcess(int a, bool i, int p);'; $h = [W.N]::OpenProcess(0x0800, $false, ${pid}); [W.N]::${verb}($h)`], { windowsHide: true });
}

function startPlayback() {
  const child = spawn('powershell.exe', ['-NoProfile', '-NonInteractive', '-Command', `$p = New-Object Media.SoundPlayer '${play}'; $p.PlayLooping(); Start-Sleep -Seconds 100000`], { stdio: 'ignore', windowsHide: true });
  return { pid: child.pid, stop: () => spawn('taskkill', ['/F', '/T', '/PID', String(child.pid)], { stdio: 'ignore' }) };
}

const toasts = () => run.page.eval(`[...document.querySelectorAll('.toast')].map((t) => t.innerText.replace(/\\s+/g, ' '))`);
const tracksOf = (id) => run.manifest(id).tracks.map((t) => ({ id: t.id, name: t.name, startOffsetMs: t.startOffsetMs, durationMs: t.durationMs, endedEarlyAtMs: t.endedEarlyAtMs ?? null, endReason: t.endReason ?? null, codec: t.codec }));

async function record(title, appSource = 'Windows PowerShell') {
  await run.newRecording(title);
  await run.page.waitFor(`document.body.innerText.includes(${JSON.stringify(appSource)})`, appSource, 30_000).catch(async () => {
    await run.page.click({ name: 'Add a source: look again for microphones and apps' });
    await run.hasText(appSource, 20_000);
  });
  const appSwitch = await run.until(() => appSwitchName(run.page, playback.pid, appSource), `the switch of ${appSource} pid ${playback.pid}`, 20_000, 500);
  const appOn = await run.page.eval(`[...document.querySelectorAll('[role=switch]')].find((s) => s.getAttribute('aria-label') === ${JSON.stringify(appSwitch)})?.getAttribute('aria-checked')`);
  if (appOn !== 'true') await run.page.click({ role: 'switch', name: appSwitch });
  await run.hasText('3 audio sources selected');
  await run.page.click({ name: 'Start recording' });
  await run.hasText('RECORDING');
  return run.projectIds().find((p) => run.manifest(p).state === 'recording');
}

async function stopAndSettle(id) {
  await run.page.click({ name: 'Stop and open review' });
  await run.until(() => ['ready', 'failed'].includes(run.manifest(id).state), 'finalize', 120_000, 500);
  // The Record screen opens Review by itself once finalize reports ready.
  const left = await run.until(async () => !(await run.text()).includes('FINALIZING'), 'the Record screen to open Review', 30_000, 250).then(() => true, () => false);
  if (!left) {
    await run.shot('stuck-finalizing');
    run.log('stuck', JSON.stringify({ state: run.manifest(id).state, current: await run.bridge('recording.current').catch((e) => e.message), events: (await run.events('recording.state')).slice(-3) }).slice(0, 1500));
  }
  run.check(`Review opens after Stop (${run.manifest(id).details.title})`, left);
}

async function caseMicrophone(mic) {
  const id = await record('H1 microphone disabled');
  await run.waitElapsed(20);
  const disabledAt = await run.elapsedSeconds();
  endpoints('disable', mic.id);
  const toast = await run.until(async () => (await toasts()).find((t) => /stopped at/.test(t)), 'the source-lost toast', 20_000).catch(() => null);
  await run.shot('mic-disabled-toast');
  const footer = (await run.text('footer')).replace(/\s+/g, ' ');
  const before = await run.elapsedSeconds();
  await sleep(15_000);
  const after = await run.elapsedSeconds();
  const levels = (await run.events('recording.levels')).slice(-5).map((e) => e.payload.levels.map((l) => l.sourceId.split(':')[0]).join('+'));
  endpoints('enable', mic.id);
  await sleep(4000);
  await run.page.click({ role: 'switch', name: 'Microphone' }).catch(() => null);
  await sleep(2000);
  await run.shot('mic-reenabled');
  await run.waitElapsed(after + 20);
  await stopAndSettle(id);
  const tracks = tracksOf(id);
  const history = run.history(id).map((h) => `${h.stage}/${h.event}: ${h.summary}${h.detail ? ' — ' + h.detail : ''}`);
  cases.push({ name: 'microphone disabled and enabled', disabledAt, toast, footer, levelsWhileLost: levels, tracks, history, state: run.manifest(id).state });
  run.check('mic: a toast names the source and the time, and says the others are still recording', !!toast && /stopped at \d/.test(toast) && /still recording/.test(toast), toast ?? '(none)');
  run.check('mic: the footer shows the lost source', /stopped|lost|disconnected/i.test(footer), footer);
  run.check('mic: the other tracks keep recording', after - before >= 14 && levels.every((l) => l.includes('system') && l.includes('app')), `${before} → ${after} s; levels from ${levels.join(' | ')}`);
  const lost = tracks.find((t) => t.endReason === 'sourceLost');
  run.check('mic: the lost track ends at the time it was lost; a new one starts when turned on again', !!lost && Math.abs(lost.endedEarlyAtMs / 1000 - disabledAt) <= 3 && tracks.filter((t) => /mic/i.test(t.id)).length === 2, JSON.stringify(tracks));
  run.check('mic: History lists when it ended', history.some((h) => /ended at/.test(h)), history.join(' | '));
}

async function caseDefaultOutput(original, other) {
  const id = await record('H1 default output changed');
  await run.waitElapsed(20);
  endpoints('default', other.id);
  const switchedAt = await run.elapsedSeconds();
  await sleep(20_000);
  const lostToasts = await toasts();
  await run.shot('output-switched');
  endpoints('default', original.id);
  const backAt = await run.elapsedSeconds();
  await sleep(15_000);
  await stopAndSettle(id);
  const tracks = tracksOf(id);
  cases.push({ name: 'default output switched', switchedAt, backAt, from: original.name, to: other.name, toastsAfterSwitch: lostToasts, tracks, state: run.manifest(id).state, history: run.history(id).map((h) => h.summary) });
  run.check('output: no track is lost and nothing stops', tracks.every((t) => !t.endReason || t.endReason === 'sessionStopped') && !lostToasts.some((t) => /stopped/.test(t)), JSON.stringify(tracks));
  run.check('output: every track covers the whole recording', tracks.every((t) => Math.abs(t.durationMs - run.manifest(id).durationMs) <= 100), tracks.map((t) => `${t.id} ${t.durationMs}`).join(', '));
}

async function caseSuspendRecording() {
  const id = await record('H1 suspended while recording');
  await run.waitElapsed(20);
  const before = await run.elapsedSeconds();
  const startedAt = Date.now();
  suspend(run.app.process.pid, true);
  await sleep(120_000);
  suspend(run.app.process.pid, false);
  const frozenSeconds = (Date.now() - startedAt) / 1000;
  await sleep(5000);
  const afterResume = await run.elapsedSeconds();
  await run.shot('resumed-after-suspend');
  const pageToasts = await toasts();
  await sleep(10_000);
  await stopAndSettle(id);
  const manifest = run.manifest(id);
  const checkpoints = run.logLines(/Checkpoint .*drift/).slice(-6).map((l) => l.slice(l.indexOf('Checkpoint')));
  cases.push({ name: 'Memento suspended 2 minutes while recording', before, frozenSeconds, afterResume, durationMs: manifest.durationMs, tracks: tracksOf(id), toasts: pageToasts, checkpoints, warnings: run.logLines(/\[(WRN|ERR)\].*(overrun|lost|Source|Writing)/).slice(-6) });
  run.check('suspend while recording: still recording afterwards, no crash', manifest.state === 'ready' && run.crashReports().length === 0, `${manifest.state}`);
  run.check('suspend while recording: every track stays in step (same length)', tracksOf(id).every((t) => Math.abs(t.durationMs - manifest.durationMs) <= 200), tracksOf(id).map((t) => `${t.id} ${t.durationMs} ${t.endReason ?? ''}`).join(', '));
  run.log('suspend while recording', `timer ${before} s → ${afterResume} s after ${frozenSeconds.toFixed(0)} s frozen; recording ${manifest.durationMs} ms`);
}

async function caseSuspendTranscription() {
  const before = new Set(run.projectIds());
  await run.bridge('library.importMedia', { path: shortAudio, title: 'H1 suspended while transcribing' });
  const id = run.projectIds().find((p) => !before.has(p));
  const stage = () => run.manifest(id).stages.find((s) => s.stage === 'transcript');
  await run.until(() => stage()?.state === 'active' && run.ownWorkerPids().length > 0, 'transcription with a worker', 10 * 60_000, 200);
  await sleep(1000);
  const [worker] = run.ownWorkerPids();
  if (!worker) throw new Error('the worker had already finished: use a --short file of several minutes');
  suspend(worker, true);
  suspend(run.app.process.pid, true);
  await sleep(120_000);
  suspend(run.app.process.pid, false);
  suspend(worker, false);
  await run.until(() => ['done', 'failed'].includes(stage()?.state), 'transcription after the freeze', 15 * 60_000, 1000);
  const transcript = JSON.parse(readFileSync(join(run.folder(id), 'transcript.json'), 'utf8'));
  cases.push({ name: 'Memento and its worker suspended 2 minutes while transcribing', state: stage()?.state, segments: transcript.segments.length, history: run.history(id).map((h) => `${h.stage}/${h.event}: ${h.summary}`) });
  run.check('suspend while transcribing: the transcript completes after the freeze', stage()?.state === 'done' && transcript.segments.length > 10, `${stage()?.state}, ${transcript.segments.length} segments`);
}

let playback = null;
const all = listEndpoints();
const originalDefault = all.find((e) => e.flow === 'render' && e.isDefault);
const mic = all.find((e) => e.flow === 'capture' && e.isDefault && e.state === 'active');
const otherOutput = all.find((e) => e.flow === 'render' && e.state === 'active' && !e.isDefault);
try {
  rmSync(dataRoot, { recursive: true, force: true });
  run.copyModels();
  run.log('endpoints', `default output "${originalDefault?.name}", other output "${otherOutput?.name}", microphone "${mic?.name}"`);
  playback = startPlayback();
  await sleep(2500);
  await run.start(['--update-feed=off']);
  if (!only || only.includes('1')) await caseMicrophone(mic);
  if ((!only || only.includes('2')) && otherOutput) await caseDefaultOutput(originalDefault, otherOutput);
  if (!only || only.includes('3')) {
    await caseSuspendRecording();
    await caseSuspendTranscription();
  }
  await run.app.close();
} catch (error) {
  run.check('the run reached the end', false, error.stack);
  await run.shot('failure').catch(() => null);
  await run.app?.kill();
} finally {
  playback?.stop();
  // Put the PC back as it was.
  try {
    if (mic) endpoints('enable', mic.id);
    if (originalDefault) endpoints('default', originalDefault.id);
  } catch (error) {
    console.error('Restoring the audio endpoints failed:', error.message);
  }
  const summary = { ...run.summary(), cases, endpointsAfter: listEndpoints().filter((e) => e.state === 'active').map((e) => `${e.flow} ${e.isDefault ? 'default ' : ''}${e.name}`), warnings: run.logLines(/\[(WRN|ERR|FTL)\]/).map((l) => l.slice(0, 260)) };
  writeFileSync(join(out, 'summary.json'), JSON.stringify(summary, null, 2));
  console.log(JSON.stringify({ passed: summary.passed, failed: summary.failed, failures: summary.results.filter((r) => !r.passed), endpointsAfter: summary.endpointsAfter, crashReports: summary.crashReports }, null, 2));
}
