// H1 storage checks through the real UI, with the simulated recording engine:
//  A. Low space with --free-space-override=<file> (a VHDX needs elevation): the footer and banner when idle and while
//     recording, transcription paused at the threshold and running again when there is room, and the clean stop when
//     free space falls below the floor, with the §17 copy and a History line.
//  B. A write that fails because the disk is full (--simulate-audio disk-full=<s>): the recording stops cleanly at a
//     stated time and everything before it is kept.
//  C. A library whose folder disappears (renamed while idle; its drive letter removed with `subst /D` while recording).
//
//   node tools/e2e/h1-storage.mjs [--data <dir>] [--out <dir>] [--models <dir>] [--port 9421] [--only A|B|C]
import { execFileSync } from 'node:child_process';
import { existsSync, mkdirSync, renameSync, rmSync, writeFileSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { Run, option, repoRoot, sleep } from './h1-lib.mjs';

const args = process.argv.slice(2);
const base = resolve(option(args, '--data', join(repoRoot, 'artifacts', 'e2e-data-h1-storage')));
const out = resolve(option(args, '--out', join(repoRoot, 'artifacts', 'e2e', 'h1-storage')));
const models = option(args, '--models', null);
const port = Number(option(args, '--port', '9421'));
const only = option(args, '--only', null);
const GB = 1024 ** 3;
const summaries = [];

const footerText = (run) => run.text('footer');
const bannerText = (run) => run.page.eval(`document.querySelector('.banner')?.innerText ?? ''`);
const toastTexts = (run) => run.page.eval(`[...document.querySelectorAll('.toast')].map((t) => t.innerText.replace(/\\s+/g, ' '))`);

async function caseLowSpace() {
  const run = new Run({ name: 'A. low space and a full drive', dataRoot: join(base, 'A'), out: join(out, 'A'), port, models });
  rmSync(run.dataRoot, { recursive: true, force: true });
  run.copyModels();
  const freeFile = join(run.dataRoot, 'free-bytes.txt');
  const setFree = (bytes) => writeFileSync(freeFile, String(Math.round(bytes)));
  setFree(500 * GB);
  try {
    await run.start(['--simulate-audio', `--free-space-override=${freeFile}`]);
    await run.hasText('Your library is empty');
    run.check('normal footer shows the free space', /500 GB free/.test(await footerText(run)), await footerText(run));

    // Idle, below the 10 GB threshold.
    setFree(5 * GB);
    await run.until(async () => (await footerText(run)).includes('5 GB free'), 'the footer to show 5 GB', 15_000);
    await sleep(1500);
    await run.shot('idle-low-space');
    const idleFooter = await footerText(run);
    const lowClass = await run.page.eval(`!!document.querySelector('.footer-storage--low')`);
    run.check('idle: footer turns to the storage warning', lowClass, idleFooter);
    const idleBanner = await bannerText(run);
    run.check('idle: the low-space banner appears (DESIGN §17)', idleBanner.includes('Low disk space · 5 GB free.'), idleBanner || '(no banner)');

    // Recording, then below the threshold.
    setFree(500 * GB);
    await run.until(async () => !(await footerText(run)).includes('5 GB free'), 'free space back', 15_000);
    await run.newRecording('H1 low space');
    await run.page.click({ name: 'Start recording' });
    await run.hasText('RECORDING');
    await run.waitElapsed(8);
    setFree(5 * GB);
    await run.until(async () => (await bannerText(run)).includes('Low disk space'), 'the low-space banner while recording', 15_000).catch(() => null);
    await run.shot('recording-low-space-record');
    run.check('recording: the banner is on the Record screen', (await bannerText(run)).includes('Recording continues.'), (await bannerText(run)) || '(no banner)');
    await run.page.click({ name: 'Library' }).catch(() => null);
    await sleep(1000);
    await run.shot('recording-low-space-library');
    const recBanner = await bannerText(run);
    run.check(
      'recording: banner says what continues and what pauses',
      recBanner.includes('Low disk space · 5 GB free.') && recBanner.includes('Recording continues.') && recBanner.includes('Transcription is paused until there is room.'),
      recBanner || '(no banner)',
    );
    const status = await run.bridge('status.get');
    run.check('recording: processing paused for low disk space', status.processingPaused === 'Low disk space', String(status.processingPaused));
    const [id] = run.projectIds();
    const before = run.manifest(id);
    run.check('recording continues below the threshold', before.state === 'recording', before.state);

    // Fill the drive: below the 256 MB floor.
    setFree(100 * 1024 * 1024);
    const stopToast = await run.until(async () => (await toastTexts(run)).find((t) => t.includes('drive full')), 'the drive-full toast', 20_000).catch(() => null);
    await run.shot('drive-full-toast');
    run.check('drive fills: toast "Recording stopped at m:ss · drive full" + what is kept', !!stopToast && /Recording stopped at [\d:]+ · drive full/.test(stopToast) && stopToast.includes('Everything up to that point is saved and will transcribe once there is room.'), stopToast ?? '(no toast)');
    await run.until(() => ['ready', 'failed'].includes(run.manifest(id).state), 'finalize', 60_000);
    const stopped = run.manifest(id);
    const history = run.history(id);
    const line = history.find((h) => h.stage === 'recorded' && /drive full/.test(h.summary));
    run.check('History names the stop and the time', !!line, line ? `${line.summary}` : history.map((h) => h.summary).join(' | '));
    run.check('everything recorded is kept (tracks have audio)', stopped.tracks.every((t) => t.durationMs > 5000), stopped.tracks.map((t) => `${t.id} ${t.durationMs} ms ${t.codec}`).join(', '));
    const saved = history.filter((h) => h.stage === 'stored').map((h) => `${h.event}: ${h.summary}${h.detail ? ' — ' + h.detail : ''}`);
    run.log('stored', saved.join(' | '));
    await run.page.click({ name: 'Library' }).catch(() => null);
    await sleep(800);
    await run.shot('library-after-drive-full');
    const row = await run.text('.lib-item');
    run.log('library row', row.replace(/\s+/g, ' '));

    // With room again, transcription runs.
    const stagesWhileLow = run.manifest(id).stages.map((s) => `${s.stage}:${s.state}:${s.label ?? ''}`);
    run.log('stages while low', stagesWhileLow.join(', '));
    run.check('transcription waits while space is low', !run.manifest(id).stages.some((s) => s.stage === 'transcript' && s.state === 'done'), stagesWhileLow.join(', '));
    setFree(500 * GB);
    await run.until(() => run.manifest(id).stages.every((s) => s.state !== 'active' && s.state !== 'queued'), 'processing after room', 180_000).catch(() => null);
    run.check('with room again, processing finishes', run.manifest(id).stages.every((s) => s.state === 'done' || s.state === 'failed'), run.manifest(id).stages.map((s) => `${s.stage}:${s.state}`).join(', '));
    await run.app.close();
  } catch (error) {
    run.check('case A ran to the end', false, error.message);
    await run.shot('A-failure').catch(() => null);
    await run.app?.kill();
  }
  summaries.push(run.summary());
}

async function caseWriteFails() {
  const run = new Run({ name: 'B. a write fails: disk full', dataRoot: join(base, 'B'), out: join(out, 'B'), port: port + 1 });
  rmSync(run.dataRoot, { recursive: true, force: true });
  try {
    await run.start(['--simulate-audio', 'disk-full=15']);
    await run.newRecording('H1 write fails');
    await run.page.click({ name: 'Start recording' });
    await run.hasText('RECORDING');
    const toast = await run.until(async () => (await toastTexts(run)).find((t) => t.includes('Recording stopped')), 'the stop toast', 60_000).catch(() => null);
    await run.shot('write-failed-toast');
    run.check('the stop is announced with the time and what is kept', !!toast && /Recording stopped at (00:)?0?0:1\d/.test(toast) && toast.includes('saved'), toast ?? '(no toast)');
    const [id] = run.projectIds();
    await run.until(() => ['ready', 'failed'].includes(run.manifest(id).state), 'finalize', 60_000);
    const manifest = run.manifest(id);
    run.check('the recording is kept up to the stop', manifest.state === 'ready' && manifest.durationMs >= 14_000 && manifest.durationMs <= 16_500, `${manifest.state}, ${manifest.durationMs} ms`);
    const line = run.history(id).find((h) => h.stage === 'recorded' && h.event !== 'started');
    run.check('History says why and when', !!line && /drive full|could not be written/.test(line.summary), line?.summary ?? '(none)');
    await run.page.click({ name: 'Library' }).catch(() => null);
    await sleep(500);
    await run.shot('write-failed-library');
    await run.app.close();
  } catch (error) {
    run.check('case B ran to the end', false, error.message);
    await run.app?.kill();
  }
  summaries.push(run.summary());
}

/** subst needs no elevation: a drive letter for a folder, removed again with /D. */
function subst(letter, folder) {
  try {
    execFileSync('subst', [`${letter}:`, '/D'], { stdio: 'ignore' });
  } catch {
    // Not mapped yet.
  }
  if (folder) execFileSync('subst', [`${letter}:`, folder], { stdio: 'ignore' });
}

async function caseRemovable() {
  const run = new Run({ name: 'C. the library disappears', dataRoot: join(base, 'C'), out: join(out, 'C'), port: port + 2 });
  rmSync(run.dataRoot, { recursive: true, force: true });
  const volume = join(base, 'C-volume');
  rmSync(volume, { recursive: true, force: true });
  rmSync(`${volume}-gone`, { recursive: true, force: true });
  mkdirSync(volume, { recursive: true });
  const letter = 'R';
  subst(letter, volume);
  const libraryPath = `${letter}:\\Memento Library`;
  mkdirSync(run.memento, { recursive: true });
  // Settings › General › Library location would ask through Windows' folder picker and move the library there, which
  // creates the folder; the stored setting and the folder are the same.
  mkdirSync(join(volume, 'Memento Library'), { recursive: true });
  writeFileSync(join(run.memento, 'settings.json'), JSON.stringify({ schemaVersion: 1, libraryPath }));
  try {
    // 1. Recorded once, then the folder disappears while Memento is closed (the USB drive is not plugged in).
    await run.start(['--simulate-audio']);
    await run.newRecording('H1 on the removable drive');
    await run.page.click({ name: 'Start recording' });
    await run.hasText('RECORDING');
    await run.waitElapsed(12);
    await run.page.click({ name: 'Stop and open review' });
    await sleep(6000);
    await run.app.close();
    subst(letter, null);
    await run.start(['--simulate-audio']);
    await sleep(3000);
    await run.shot('launch-library-drive-missing');
    const body = (await run.text()).replace(/\s+/g, ' ');
    run.log('launch without the drive', body.slice(0, 400));
    run.check('launch without the drive: Memento opens and says the library is not available', /not (available|connected)|cannot be (found|reached)|could not be opened/i.test(body), body.slice(0, 300));
    run.check('no crash report', run.crashReports().length === 0, run.crashReports().join(', '));
    run.check('no empty library was created in its place', !existsSync(libraryPath), existsSync(libraryPath) ? 'created' : 'not created');
    // Starting a recording now must fail with a specific message.
    await run.page.click({ name: 'New recording' }).catch(() => null);
    await sleep(1500);
    await run.hasText('Microphone', 15_000);
    await sleep(1000);
    if ((await run.page.locate({ name: 'Start recording' }))?.disabled) {
      await run.page.click({ role: 'switch', name: 'Microphone' });
      await run.hasText('1 audio source selected', 10_000).catch(async () => run.log('after toggling', JSON.stringify(await run.page.controls())));
    }
    await run.page.click({ name: 'Start recording' });
    await sleep(2500);
    await run.shot('start-without-the-drive');
    const refusal = await run.page.eval(`document.querySelector('.rec-stage')?.innerText.replace(/\\s+/g, ' ') ?? ''`);
    run.check('start without the drive is refused with a specific message', refusal.includes('is not available because drive R: is not connected') && refusal.includes('The recording did not start and nothing was recorded.'), refusal);
    await run.app.close();

    // 2. Back again: the recording is there.
    subst(letter, volume);
    await run.start(['--simulate-audio']);
    await run.until(async () => (await run.text()).includes('H1 on the removable drive'), 'the recording to be listed again', 20_000).catch(() => null);
    run.check('with the drive back, the recording is listed and ready', (await run.text()).includes('H1 on the removable drive'));

    // 3. Renamed while Memento is open and idle.
    renameSync(volume, `${volume}-gone`);
    await sleep(6000);
    await run.shot('idle-folder-renamed');
    const idleFooter = await footerText(run);
    run.log('idle, folder gone', idleFooter);
    await run.page.click({ name: 'H1 on the removable drive', exact: false }).catch(() => null);
    await sleep(2500);
    await run.shot('open-recording-folder-gone');
    run.log('open recording, folder gone', (await run.text()).replace(/\s+/g, ' ').slice(0, 400));
    renameSync(`${volume}-gone`, volume);
    await run.page.click({ name: 'Library' }).catch(() => null);

    // 4. Removed while recording: the drive letter goes away.
    await run.newRecording('H1 drive removed while recording');
    await run.page.click({ name: 'Start recording' });
    await run.hasText('RECORDING');
    await run.waitElapsed(10);
    let renameRefused = null;
    try {
      renameSync(volume, `${volume}-gone`);
      renameRefused = false;
    } catch (error) {
      renameRefused = error.code;
    }
    run.log('rename while recording', renameRefused ? `refused by Windows (${renameRefused}): open track files hold the folder` : 'succeeded');
    if (!renameRefused) renameSync(`${volume}-gone`, volume);
    subst(letter, null);
    await sleep(12_000);
    await run.shot('recording-drive-removed');
    const during = (await run.text()).replace(/\s+/g, ' ');
    run.log('recording, drive letter removed', during.slice(0, 400));
    const elapsed = await run.elapsedSeconds();
    run.log('recorded after removal', `${elapsed} s`);
    await run.page.click({ name: 'Stop and open review' }).catch(() => null);
    await sleep(8000);
    await run.shot('stopped-drive-removed');
    const afterStop = (await run.text()).replace(/\s+/g, ' ');
    run.log('after stop', afterStop.slice(0, 400));
    run.check('drive gone while recording: Stop says the folder could not be reached and that everything is kept', /STOPPED/.test(afterStop) && afterStop.includes('could not be reached') && afterStop.includes('Everything recorded is kept'), afterStop.slice(0, 300));
    const toastsAfter = await toastTexts(run);
    run.log('toasts', toastsAfter.join(' | '));
    await run.app.close();
    subst(letter, volume);
    await run.start(['--simulate-audio']);
    await sleep(5000);
    await run.shot('relaunch-drive-back');
    const ids = run.projectIds.call({ ...run, library: join(volume, 'Memento Library', 'projects') });
    for (const id of ids) {
      const manifest = JSON.parse(execFileSync('powershell', ['-NoProfile', '-Command', `Get-Content -Raw '${join(volume, 'Memento Library', 'projects', id, 'project.json')}'`], { encoding: 'utf8' }));
      run.log('project', `${manifest.details.title}: ${manifest.state}, ${manifest.durationMs} ms, ${manifest.tracks.map((t) => `${t.id} ${t.durationMs} ms ${t.codec}`).join(', ')}`);
      if (manifest.details.title === 'H1 drive removed while recording') {
        run.check('drive back: the recording made while it was gone is recovered with every second', manifest.state === 'recovered' && manifest.durationMs >= (elapsed - 1) * 1000 && manifest.tracks.every((t) => t.codec === 'flac' && t.durationMs >= manifest.durationMs - 50), `${manifest.state}, ${manifest.durationMs} ms of ${elapsed} s`);
      }
    }
    run.check('no crash report after the drive came and went', run.crashReports().length === 0, run.crashReports().join(', '));
    await run.app.close();
  } catch (error) {
    run.check('case C ran to the end', false, error.message);
    await run.shot('C-failure').catch(() => null);
    await run.app?.kill();
  } finally {
    subst(letter, null);
  }
  summaries.push(run.summary());
}

if (!only || only === 'A') await caseLowSpace();
if (!only || only === 'B') await caseWriteFails();
if (!only || only === 'C') await caseRemovable();
writeFileSync(join(out, 'summary.json'), JSON.stringify(summaries, null, 2));
console.log(JSON.stringify(summaries.map((s) => ({ name: s.name, passed: s.passed, failed: s.failed, failures: s.results.filter((r) => !r.passed) })), null, 2));
