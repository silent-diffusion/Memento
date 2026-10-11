// H1 update check end to end on an installed copy: starts the installed Memento (data in --data, never the real
// %LOCALAPPDATA%\Memento) with --update-feed pointing at a local Velopack feed that holds a newer version, then through
// the UI: Settings › General › Updates › Check now, waits for the download, the toast and the row, presses
// "Restart to update", and connects to the restarted Memento to read its version in About.
//
//   node tools/e2e/h1-update.mjs --exe <installed Memento.exe> --feed <feed folder> --expect 0.5.1 [--data <dir>] [--out <dir>]
//     [--manual [--notes <text in the release notes>]] [--port <DevTools port>]
// Since 2.1.0 Check now only checks and Update now downloads; the default run clicks both when it sees them.
import { execFileSync } from 'node:child_process';
import { existsSync, readdirSync, writeFileSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { Page } from './cdp.mjs';
import { Run, option, repoRoot, sleep } from './h1-lib.mjs';

const args = process.argv.slice(2);
const expect = option(args, '--expect', '0.5.1');
const feed = resolve(option(args, '--feed', ''));
const run = new Run({
  name: 'H1 update',
  dataRoot: resolve(option(args, '--data', join(repoRoot, 'artifacts', 'e2e-data-h1-update'))),
  out: resolve(option(args, '--out', join(repoRoot, 'artifacts', 'e2e', 'h1-update'))),
  port: Number(option(args, '--port', '9480')),
  exe: option(args, '--exe', undefined),
});
const result = {};
const rowText = () => run.page.eval(`[...document.querySelectorAll('.settings-row')].find((r) => r.querySelector('.settings-row-label')?.textContent === 'Updates')?.innerText.replace(/\\s+/g, ' ') ?? ''`);
// 2.1.0 --manual: "Check now" only checks. A first run with updates off turns "Install updates automatically" off (so
// no automatic check downloads at launch); the second run with the feed clicks Check now, expects "{expect} is
// available" with the feed's release notes (--notes <text expected in them>) and no download, then Update now.
const manual = args.includes('--manual');
const notesText = option(args, '--notes', 'Test release');
const packagesDir = () => join(resolve(run.exe, '..', '..'), 'packages');
const packageFiles = () => (existsSync(packagesDir()) ? readdirSync(packagesDir()).filter((f) => f.endsWith('.nupkg')).sort() : []);
try {
  if (manual) {
    await run.start(['--update-feed=off']);
    await run.bridge('settings.set', { general: { autoUpdate: false } });
    await run.app.close();
  }
  await run.start([`--update-feed=${feed}`]);
  result.before = await run.bridge('updates.status');
  await run.page.click({ name: 'Settings' });
  await run.page.click({ name: 'General' });
  await run.until(async () => /Version/.test(await rowText()), 'the Updates row');
  await run.shot('updates-row-before');
  result.rowBefore = await rowText();
  if (manual) {
    const packagesBefore = packageFiles();
    await run.page.click({ name: 'Check now' });
    await run.until(async () => (await rowText()).includes(`Memento ${expect} is available.`), 'the available version', 120_000, 100);
    await sleep(3000); // long enough for a download to start, if Check now (wrongly) began one
    await run.shot('update-available-with-notes');
    result.rowAvailable = await rowText();
    const notes = await run.page.eval(`document.querySelector('.update-notes')?.innerText ?? ''`);
    result.notes = notes;
    const states = (await run.events('updates.progress')).map((e) => e.payload.state);
    run.check('Check now shows the newer version', result.rowAvailable.includes(`Memento ${expect} is available.`), result.rowAvailable);
    run.check('the release notes of that version are shown', notes.includes(notesText), notes);
    run.check('Check now downloads nothing', !states.includes('downloading') && JSON.stringify(packageFiles()) === JSON.stringify(packagesBefore), `${states.join(',')} · ${packageFiles().join(',')}`);
    run.check('Update now is offered', !!(await run.page.locate({ name: 'Update now' })), result.rowAvailable);
    await run.page.click({ name: 'Update now' });
    await run.until(async () => (await rowText()).includes(`Downloading Memento ${expect}`) || (await rowText()).includes('Restart to update'), 'the download progress', 60_000, 50);
    await run.shot('update-downloading');
  } else if (await run.page.locate({ name: 'Check now' })) {
    // An automatic check may already have started at launch; either way the row ends with Restart to update.
    await run.page.click({ name: 'Check now' }).catch(() => null);
    const available = await run.until(async () => (await rowText()).includes('Update now') || (await run.events('updates.progress')).some((e) => e.payload.state === 'downloading'), 'the check', 120_000, 100).catch(() => null);
    if (available && (await run.page.locate({ name: 'Update now' }))) await run.page.click({ name: 'Update now' });
  }
  const downloading = await run.until(async () => (await run.events('updates.progress')).find((e) => e.payload.state === 'downloading')?.payload, 'the download', 120_000, 100).catch(() => null);
  await run.page.eval(`true`);
  const footerDuring = (await run.text('footer')).replace(/\s+/g, ' ');
  const ready = await run.until(async () => (await run.events('updates.progress')).find((e) => e.payload.state === 'ready')?.payload, 'the update to be ready', 300_000, 200);
  await sleep(800);
  await run.shot('update-ready');
  result.downloading = downloading;
  result.footerDuring = footerDuring;
  result.ready = ready;
  result.toast = await run.page.eval(`[...document.querySelectorAll('.toast')].map((t) => t.innerText.replace(/\\s+/g, ' '))`);
  result.rowReady = await rowText();
  run.check('the newer version is found and downloaded', ready.availableVersion === expect, JSON.stringify(ready));
  run.check('a toast offers the restart', result.toast.some((t) => t.includes(`Restart to update to ${expect}`)), result.toast.join(' | '));
  run.check('the Updates row offers the restart', result.rowReady.includes(`Restart to update to ${expect}`), result.rowReady);

  await run.page.click({ name: `Restart to update to ${expect}`, within: '.settings-card' }).catch(() => run.page.click({ name: `Restart to update to ${expect}` }));
  const exitCode = await Promise.race([run.app.exited, sleep(60_000).then(() => 'still running')]);
  result.oldExit = exitCode;
  run.check('Memento closes normally to update', exitCode === 0, String(exitCode));
  run.page.close();
  // The updater installs and starts the new version with the same environment (data folder, DevTools port).
  const page = await Page.connect(run.port, { timeoutMs: 180_000 });
  run.page = page;
  await run.installEventTap();
  await sleep(3000);
  const version = await run.bridge('app.version');
  result.after = { version: version.version, status: await run.bridge('updates.status') };
  await page.click({ name: 'Settings' });
  await page.click({ name: 'General' });
  await sleep(1500);
  await run.shot('after-restart');
  run.check(`the restarted Memento is ${expect}`, version.version === expect, version.version);
  run.check('nothing is left to install', result.after.status.state !== 'ready', result.after.status.state);
  // The updated Memento was started by the updater, not by this script: close it the normal way (WM_CLOSE).
  const updatedPid = Number(execFileSync('powershell.exe', ['-NoProfile', '-Command', `(Get-CimInstance Win32_Process -Filter "Name='Memento.exe'" | Where-Object { $_.ExecutablePath -eq '${run.exe.replace(/'/g, "''")}' }).ProcessId`], { encoding: 'utf8' }).trim().split(/\s+/)[0]);
  page.close();
  if (updatedPid) execFileSync('taskkill', ['/PID', String(updatedPid)]);
} catch (error) {
  run.check('the run reached the end', false, error.stack);
  await run.shot('failure').catch(() => null);
} finally {
  writeFileSync(join(run.out, 'summary.json'), JSON.stringify({ ...run.summary(), result }, null, 2));
  console.log(JSON.stringify({ ...run.summary(), result }, null, 2));
}
