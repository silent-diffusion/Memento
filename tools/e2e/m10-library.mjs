// The 2.1 Library check on a built app, through its real UI over the DevTools protocol (mouse clicks and typing; the
// bridge is only read, and used once to turn automatic transcription off so no stage holds the audio): the dated
// default title and Settings › General › Date format, Library folders (Folders view, New folder, Move to folder…),
// Delete audio with its confirmation, and Settings › Storage and history › Delete audio tracks older than with its
// estimate and confirmation. The audio is the simulated engine's: nothing is played and no device is recorded.
//
//   node tools/e2e/m10-library.mjs [--exe <Memento.exe>] [--data <dir>] [--out <dir>] [--port 9401]
//
// --exe defaults to the published app (artifacts/publish/win-x64/Memento.exe); a Debug build's Memento.exe works too.
// The app runs with LOCALAPPDATA pointed at --data (default artifacts/e2e-data-m10), so the real library is never
// touched. Each step saves a screenshot into --out; the results go to <out>/summary.json.

import { existsSync, readdirSync, statSync, writeFileSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { Run, option, repoRoot, sleep } from './h1-lib.mjs';

const args = process.argv.slice(2);
const run = new Run({
  name: 'm10-library',
  dataRoot: option(args, '--data', join(repoRoot, 'artifacts', 'e2e-data-m10')),
  out: option(args, '--out', join(repoRoot, 'artifacts', 'e2e', 'm10')),
  port: Number(option(args, '--port', '9401')),
  exe: option(args, '--exe', undefined),
});

const MONTHS = ['January', 'February', 'March', 'April', 'May', 'June', 'July', 'August', 'September', 'October', 'November', 'December'];
const ymd = (d) => `${d.getFullYear()} ${MONTHS[d.getMonth()]} ${d.getDate()}`;
const dmy = (d) => `${d.getDate()} ${MONTHS[d.getMonth()]} ${d.getFullYear()}`;

/** Every file of a recording's folder with its size, but the manifest and History (which a move or a delete writes). */
function filesOf(id) {
  const root = run.folder(id);
  const result = {};
  const walk = (dir, prefix) => {
    for (const name of readdirSync(dir)) {
      const full = join(dir, name);
      if (statSync(full).isDirectory()) walk(full, `${prefix}${name}/`);
      else if (!['project.json', 'history.jsonl'].includes(`${prefix}${name}`)) result[`${prefix}${name}`] = statSync(full).size;
    }
  };
  walk(root, '');
  return result;
}

const seen = new Set();

async function record(title, seconds) {
  if (!(await run.page.locate({ name: 'New recording' }))) {
    await run.page.click({ name: 'Library' });
  }
  await run.page.click({ name: 'New recording' });
  await run.hasText('AUDIO SOURCES');
  const shown = await run.page.eval(`document.querySelector('#rec-title')?.value ?? ''`);
  if (title !== null) {
    await run.page.click('#rec-title');
    await run.page.key('a', ['ctrl']);
    await run.page.type(title);
    await run.page.key('Enter');
  }
  await run.page.click({ name: 'Start recording' });
  await run.waitElapsed(seconds);
  await run.page.click({ name: 'Stop and open review' });
  const ready = await run.until(
    async () => (await run.events('recording.state')).find((e) => e.payload.state === 'ready' && !seen.has(e.payload.recordingId))?.payload.recordingId,
    'the recording to be ready',
    120_000,
  );
  seen.add(ready);
  return { id: ready, shown };
}

/** Opens a Library row's ⋯ menu and picks an item. */
async function rowMenu(title, item) {
  await run.page.click({ name: `More actions for ${title}` });
  await run.page.click({ role: 'menuitem', name: item });
}

try {
  await run.start(['--simulate-audio', '--update-feed=off']);
  run.log('started', run.dataRoot);
  // Setup without a click: no stage may hold the audio while it is deleted (nothing else is set through the bridge).
  await run.bridge('settings.set', { transcription: { auto: false } });

  // 1. The dated default title.
  const today = new Date();
  const first = await record(null, 3);
  run.check('a new recording is titled "Untitled meeting · <today>" (2026 February 16 style)', first.shown === `Untitled meeting · ${ymd(today)}`, first.shown);
  run.check('the title is kept in project.json', run.manifest(first.id).details.title === first.shown);
  await run.shot('review-dated-title');
  const second = await record('Acme kickoff', 3);
  run.check('a typed title is kept as typed', run.manifest(second.id).details.title === 'Acme kickoff');

  // 2. Settings › General › Date format.
  await run.page.click({ name: 'Library' });
  await run.page.click({ name: 'Settings' });
  await run.page.click({ name: 'General', exact: false });
  await run.page.click({ name: 'Date format', exact: false });
  await run.page.click({ role: 'option', name: '16 February 2026' });
  await run.until(async () => (await run.bridge('settings.get')).general.dateFormat === 'dayMonthYear', 'the date format to be saved');
  await run.shot('settings-date-format');
  run.check('the Date format row says how a new title reads', (await run.text()).includes(`Untitled meeting · ${dmy(today)}`));
  run.check('existing titles are untouched', run.manifest(first.id).details.title === first.shown);

  // 3. Folders: switch the view, make a folder, file a recording there.
  await run.page.click({ name: 'Library' });
  await run.page.click({ role: 'button', name: 'Folders' });
  await run.until(async () => (await run.bridge('settings.get')).general.libraryView === 'folders', 'the view to be remembered');
  await run.page.click({ name: 'New folder' });
  await run.page.click('#folder-name-input');
  await run.page.type('Clients');
  await run.page.click({ name: 'Create folder' });
  await run.hasText('This folder has no recordings yet');
  await run.page.click({ name: 'Library, recordings in no folder: 2' });
  const before = filesOf(second.id);
  await rowMenu('Acme kickoff', 'Move to folder…');
  await run.hasText('Move “Acme kickoff” to a folder');
  await run.page.click('.move-list label:nth-of-type(2) input');
  await run.shot('move-to-folder');
  await run.page.click({ name: 'Move to “Clients”' });
  await run.until(() => run.manifest(second.id).folder === 'Clients', 'project.json to name the folder');
  run.check('Move to folder writes the folder into project.json (schema 5)', run.manifest(second.id).schemaVersion === 5 && run.manifest(second.id).folder === 'Clients');
  run.check('filing a recording moves no file', JSON.stringify(filesOf(second.id)) === JSON.stringify(before), Object.keys(before).join(', '));
  run.check('folders.json keeps the folder', existsSync(join(run.memento, 'Library', 'folders.json')));
  await run.page.click({ name: 'Clients, 1 recording' });
  await run.hasText('Acme kickoff');
  await run.shot('folders-view');

  // 4. Delete audio, with its confirmation.
  const audioBefore = Object.keys(filesOf(second.id)).filter((f) => /\.(wav|flac|m4a|mp3)$|peaks\.json$/.test(f));
  await rowMenu('Acme kickoff', 'Delete audio…');
  await run.hasText('Delete the audio of “Acme kickoff”?');
  const dialogText = await run.text('[role="dialog"]');
  run.check('the confirmation names the recording, its length and size, and what stays', /0:0\d of audio, [\d.]+ (KB|MB)/.test(dialogText) && dialogText.includes('The transcript and documents stay') && dialogText.includes('can’t be brought back'), dialogText.replace(/\s+/g, ' '));
  await run.shot('delete-audio-confirmation');
  await run.page.click({ name: 'Cancel' });
  run.check('Cancel deletes nothing', run.manifest(second.id).audioRemoved == null);
  await rowMenu('Acme kickoff', 'Delete audio…');
  await run.page.click({ role: 'button', name: 'Delete audio', within: '[role="dialog"]' });
  await run.until(() => run.manifest(second.id).audioRemoved != null, 'audioRemoved in project.json');
  const after = filesOf(second.id);
  run.check('every audio file is gone and nothing else', audioBefore.length > 0 && audioBefore.every((f) => !(f in after)) && Object.keys(before).filter((f) => !audioBefore.includes(f)).every((f) => f in after), `${audioBefore.join(', ')} removed; kept ${Object.keys(after).join(', ')}`);
  run.check('History says the audio was deleted', run.history(second.id).some((h) => h.summary?.startsWith('Deleted the audio')));
  await run.hasText('audio deleted');
  await run.shot('row-audio-deleted');

  // 5. Settings › Storage and history › Delete audio tracks older than: the estimate and the confirmation.
  await run.page.click({ name: 'Settings' });
  await run.page.click({ name: 'Storage and history', exact: false });
  await run.page.click({ name: 'Delete audio tracks older than', exact: false });
  const ages = await run.page.eval(`[...document.querySelectorAll('[role="option"]')].map((o) => o.textContent.trim())`);
  run.check('the ages are exactly Never, a day … five years', JSON.stringify(ages) === JSON.stringify(['Never', 'a day', 'a week', 'a month', '90 days', 'a year', '2 years', '3 years', '4 years', '5 years']), ages.join(', '));
  await run.page.click({ role: 'option', name: 'a year' });
  await run.until(async () => (await run.bridge('settings.get')).storage.deleteAudioOlderThanDays === 365, 'the age to be saved (nothing is that old: no confirmation)');
  await run.hasText('No audio is older than a year now.');
  await run.shot('settings-delete-audio');
  const estimate = await run.bridge('audio.removeEstimate', { recordingIds: null, olderThanDays: null });
  run.check('the estimate counts the recording that still has audio', estimate.recordings === 1 && estimate.bytes > 0, JSON.stringify(estimate));
  await run.page.click({ name: 'Delete audio tracks older than', exact: false });
  await run.page.click({ role: 'option', name: 'Never' });
  await run.until(async () => (await run.bridge('settings.get')).storage.deleteAudioOlderThanDays === null, 'Never to be saved');
  await sleep(500);
  run.check('the first recording still has its audio', run.manifest(first.id).audioRemoved == null);
} catch (error) {
  run.check('the run finished', false, error.stack ?? String(error));
  try {
    await run.shot('failure');
  } catch {
    // No page to capture.
  }
} finally {
  try {
    if (run.app) await run.app.close();
  } catch (error) {
    run.log('close failed', String(error));
    await run.app?.kill();
  }
  const summary = run.summary();
  writeFileSync(join(run.out, 'summary.json'), JSON.stringify(summary, null, 2));
  run.log('done', `${summary.passed} passed, ${summary.failed} failed · ${resolve(run.out, 'summary.json')}`);
  process.exitCode = summary.failed === 0 ? 0 : 1;
}
