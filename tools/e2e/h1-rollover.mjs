// H1 rollover: a real-device recording with tracks rolling over at 64 MB (--rollover-bytes, instead of 3.5 GiB), long
// enough for .part2.wav and .part3.wav, then finalize: each track must become one FLAC with the full duration, the
// FLAC bit-exact against the joined parts (the finalizer verifies it), and the SHA-256 in project.json matching.
//
//   node tools/e2e/h1-rollover.mjs --play <wav> [--minutes 9] [--data <dir>] [--out <dir>] [--exe <Memento.exe>]
import { spawn } from 'node:child_process';
import { existsSync, readdirSync, rmSync, statSync, writeFileSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { Run, appSwitchName, option, repoRoot, sha256, sleep } from './h1-lib.mjs';

const args = process.argv.slice(2);
const minutes = Number(option(args, '--minutes', '9'));
const ROLLOVER = 64 * 1024 * 1024;
const run = new Run({
  name: 'H1 rollover at 64 MB',
  dataRoot: resolve(option(args, '--data', join(repoRoot, 'artifacts', 'e2e-data-h1-rollover'))),
  out: resolve(option(args, '--out', join(repoRoot, 'artifacts', 'e2e', 'h1-rollover'))),
  port: 9475,
  exe: option(args, '--exe', undefined),
});
const play = resolve(option(args, '--play', ''));
const result = {};

const partsIn = (folder) => (existsSync(folder) ? readdirSync(folder).filter((f) => f.endsWith('.wav')).sort().map((f) => ({ file: f, bytes: statSync(join(folder, f)).size })) : []);

let player = null;
try {
  rmSync(run.dataRoot, { recursive: true, force: true });
  player = spawn('powershell.exe', ['-NoProfile', '-NonInteractive', '-Command', `$p = New-Object Media.SoundPlayer '${play}'; $p.PlayLooping(); Start-Sleep -Seconds 100000`], { stdio: 'ignore', windowsHide: true });
  await sleep(2500);
  await run.start([`--rollover-bytes=${ROLLOVER}`, '--update-feed=off']);
  await run.newRecording('H1 rollover');
  await run.page.waitFor(`document.body.innerText.includes('Windows PowerShell')`, 'the app source', 30_000);
  await run.page.click({ role: 'switch', name: await run.until(() => appSwitchName(run.page, player.pid, 'Windows PowerShell'), 'the player switch', 20_000, 500) });
  await run.hasText('3 audio sources selected');
  await run.page.click({ role: 'switch', name: 'Microphone' }); // off: the room mic is not needed here
  await run.hasText('2 audio sources selected');
  await run.page.click({ name: 'Start recording' });
  await run.hasText('RECORDING');
  const id = run.projectIds()[0];
  const tracksFolder = join(run.folder(id), 'tracks');
  await run.waitElapsed(minutes * 60);
  result.partsBeforeStop = partsIn(tracksFolder);
  await run.shot('before-stop');
  await run.page.click({ name: 'Stop and open review' });
  await run.until(() => ['ready', 'failed'].includes(run.manifest(id).state), 'finalize', 300_000, 500);
  const manifest = run.manifest(id);
  result.durationMs = manifest.durationMs;
  result.tracks = manifest.tracks.map((t) => ({ id: t.id, file: t.file, codec: t.codec, sampleRate: t.sampleRate, channels: t.channels, durationMs: t.durationMs }));
  result.filesAfter = readdirSync(tracksFolder).map((f) => ({ file: f, bytes: statSync(join(tracksFolder, f)).size }));
  result.hashes = manifest.tracks.map((t) => ({ file: t.file, matches: sha256(join(run.folder(id), t.file)) === manifest.integrity.files[t.file] }));
  result.encodeLog = run.logLines(/FLAC encoded|rolled over|part\d/).map((l) => l.slice(l.indexOf('] ') + 2, l.indexOf('] ') + 220));
  result.history = run.history(id).map((h) => `${h.stage}/${h.event}: ${h.summary}${h.detail ? ' — ' + h.detail : ''}`);
  const parts3 = result.partsBeforeStop.filter((p) => /\.part3\.wav$/.test(p.file));
  run.check('rollover: tracks rolled over to .part2.wav and .part3.wav', parts3.length >= 1 && result.partsBeforeStop.some((p) => /\.part2\.wav$/.test(p.file)), result.partsBeforeStop.map((p) => `${p.file} ${p.bytes}`).join(', '));
  run.check('rollover: each full part is 64 MB plus its header', result.partsBeforeStop.filter((p) => !/part3/.test(p.file) && /part2|^[^.]+\.wav$/.test(p.file)).every((p) => p.bytes >= ROLLOVER && p.bytes <= ROLLOVER + 128));
  run.check('rollover: one FLAC per track, no WAV left', result.filesAfter.every((f) => f.file.endsWith('.flac')) && result.filesAfter.length === manifest.tracks.length, result.filesAfter.map((f) => f.file).join(', '));
  run.check('rollover: every track has the whole duration', manifest.tracks.every((t) => Math.abs(t.durationMs - manifest.durationMs) <= 100 && manifest.durationMs >= minutes * 60_000 - 2000), result.tracks.map((t) => `${t.id} ${t.durationMs} ms`).join(', '));
  run.check('rollover: the SHA-256 of every FLAC matches project.json', result.hashes.every((h) => h.matches));
  // The finalizer encodes the joined parts and verifies the FLAC bit-exact before the WAVs go; the log has the byte counts.
  const expected = {};
  for (const p of result.partsBeforeStop) {
    const stem = p.file.replace(/\.part\d+\.wav$|\.wav$/, '');
    expected[stem] = (expected[stem] ?? 0) + p.bytes;
  }
  result.wavBytesBeforeStop = expected;
  await run.app.close();
  result.verdict = 'passed';
} catch (error) {
  run.check('the run reached the end', false, error.stack);
  await run.app?.kill();
} finally {
  if (player) spawn('taskkill', ['/F', '/T', '/PID', String(player.pid)], { stdio: 'ignore' });
  writeFileSync(join(run.out, 'summary.json'), JSON.stringify({ ...run.summary(), result }, null, 2));
  console.log(JSON.stringify({ ...run.summary(), result }, null, 2));
}
