// Navigation between the screens of a built app (2.3.1), through its real UI over the DevTools protocol: mouse
// clicks, key presses and the mouse's back button; the bridge is only read. It walks the paths of
// docs/audits/USABILITY-REVIEW-2026-10-22.md and asserts where each Back lands (DESIGN.md §3, state/navigation.ts):
//   1. Library → Review → Back: the Library keeps its view (Folders, the type chip) and focuses the row opened.
//   2. Review → Create document → Builder → Open AI and privacy settings → Settings: Back names the template and
//      returns to the Builder (the unsaved name kept), whose Back returns to Review, whose Back returns to the Library.
//   3. Review's waiting-for-a-model card → Settings › Transcription → Back returns to Review.
//   4. The Library's gear reopens Settings at the section shown last; Esc goes back to the Library.
//   5. Esc closes an open menu first and only then goes back.
//   6. The Recording session ignores Esc (§8); Alt+← goes back and the recording carries on.
//   7. The mouse's back button presses the back control; a deep link (#/settings/storage) goes back to the Library.
// The audio is the simulated engine's: nothing is played and no device is recorded. No model is installed, so the
// recordings wait for one (that card is one of the paths).
//
//   node tools/e2e/m13-navigation.mjs [--exe <Memento.exe>] [--data <dir>] [--out <dir>] [--port 9431]
//
// --exe defaults to the published app (artifacts/publish/win-x64/Memento.exe). The app runs with LOCALAPPDATA pointed
// at --data (default artifacts/e2e-data-m13), so the real library is never touched. Screenshots and summary.json go
// to --out (default artifacts/e2e/m13).

import { rmSync, writeFileSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { Run, option, repoRoot, sleep } from './h1-lib.mjs';

const args = process.argv.slice(2);
const dataRoot = resolve(option(args, '--data', join(repoRoot, 'artifacts', 'e2e-data-m13')));
rmSync(dataRoot, { recursive: true, force: true });
const run = new Run({
  name: 'm13-navigation',
  dataRoot,
  out: option(args, '--out', join(repoRoot, 'artifacts', 'e2e', 'm13')),
  port: Number(option(args, '--port', '9431')),
  exe: option(args, '--exe', undefined),
});
const page = () => run.page;

const route = () => page().eval('location.hash');
const backLabel = () => page().eval(`document.querySelector('[data-spoke-back]')?.innerText.trim() ?? null`);
const at = (hash, what) => run.until(async () => (await route()).startsWith(hash), what, 20_000);
const spoke = (hash, what) => at(hash, what).then(() => page().waitFor(`!!document.querySelector('[data-spoke-back]')`, `${what}: its back control`));
const onLibrary = (what) => at('#/library', what).then(() => page().waitFor(`!!document.querySelector('[aria-label="Settings"]')`, `${what}: the Library`));

/** Presses a key with nothing focused but the page (so no field or control takes it first). */
async function keyOnPage(key, modifiers = []) {
  await page().eval(`(() => { document.activeElement?.blur?.(); return true; })()`);
  await page().key(key, modifiers);
  await sleep(500);
}

/** The mouse's back button (XButton1) on the middle of the page. */
async function mouseBack() {
  const x = 600;
  const y = 400;
  await page().send('Input.dispatchMouseEvent', { type: 'mouseMoved', x, y });
  await page().send('Input.dispatchMouseEvent', { type: 'mousePressed', x, y, button: 'back', buttons: 8, clickCount: 1 });
  await page().send('Input.dispatchMouseEvent', { type: 'mouseReleased', x, y, button: 'back', buttons: 0, clickCount: 1 });
  await sleep(600);
}

async function record(title, seconds) {
  await page().click({ name: 'New recording' });
  await run.hasText('AUDIO SOURCES');
  await page().click({ role: 'textbox', name: 'Untitled meeting', exact: false });
  await page().key('a', ['ctrl']);
  await page().type(title);
  await page().key('Enter');
  await page().click({ name: 'Start recording' });
  await run.waitElapsed(seconds);
  await page().click({ name: 'Stop and open review' });
  await at('#/review/', 'Review after Stop');
  return (await route()).split('/')[2];
}

try {
  await run.start(['--simulate-audio', '--update-feed=off']);
  run.log('started', run.dataRoot);
  await page().send('Emulation.setDeviceMetricsOverride', { width: 1280, height: 860, deviceScaleFactor: 1, mobile: false });

  const first = await record('Navigation one', 3);
  await page().click('[data-spoke-back]');
  await onLibrary('Back from Review after Stop');
  run.check('Review opened by Stop goes back to the Library, not to the Recording session', (await route()) === '#/library');
  await record('Navigation two', 3);
  await page().click('[data-spoke-back]');
  await onLibrary('the Library');

  // 1. The Library keeps its view across a spoke.
  await page().click({ role: 'button', name: 'Folders' });
  await page().click({ role: 'button', name: 'Meetings' });
  await sleep(600);
  await page().click(`[data-recording-id="${first}"]`);
  await spoke(`#/review/${first}`, 'Review of the first recording');
  run.check('Review’s back control reads Library', (await backLabel()) === 'Library', await backLabel());
  await run.shot('review');
  await page().click('[data-spoke-back]');
  await onLibrary('Back to the Library');
  const kept = await page().eval(`({
    folders: document.querySelector('[aria-pressed="true"]')?.innerText ?? '',
    chip: [...document.querySelectorAll('.chip[aria-pressed="true"]')].map((c) => c.innerText.trim()),
    focused: document.activeElement?.getAttribute('data-recording-id') ?? null,
  })`);
  const foldersOn = await page().eval(`[...document.querySelectorAll('button')].some((b) => b.innerText.trim() === 'Folders' && (b.getAttribute('aria-pressed') === 'true' || b.classList.contains('on')))`);
  run.check('the Library comes back in the Folders view', foldersOn, JSON.stringify(kept));
  run.check('the Library keeps the Meetings chip', kept.chip.includes('Meetings'), kept.chip.join(', '));
  run.check('the row opened is focused again', kept.focused === first, String(kept.focused));
  await run.shot('library-kept');
  await page().click({ role: 'button', name: 'Timeline' });
  await page().click({ role: 'button', name: 'All' });

  // 2. Builder → AI and privacy settings → Back → Builder → Back → Review → Back → Library.
  await page().click(`[data-recording-id="${first}"]`);
  await spoke(`#/review/${first}`, 'Review');
  await page().click({ name: 'Create document' });
  await spoke('#/builder/', 'the Builder');
  await page().waitFor(`!!document.querySelector('#tpl-name') && document.querySelector('#tpl-name').value !== ''`, 'the template');
  run.check('the Builder’s back control names the recording', (await backLabel()) === 'Navigation one', await backLabel());
  await page().click('#tpl-name');
  await page().key('a', ['ctrl']);
  await page().type('Board minutes');
  await page().key('Tab');
  await page().click('#builder-tab-inputs');
  await page().click({ name: 'Open AI and privacy settings' });
  await spoke('#/settings/ai-privacy', 'Settings › AI and privacy');
  run.check('Settings opened from the Builder names the template in its back control', (await backLabel()) === 'Board minutes', await backLabel());
  await page().click({ name: 'General', exact: false });
  run.check('changing section keeps the way back', (await backLabel()) === 'Board minutes', await backLabel());
  await run.shot('settings-from-builder');
  await page().click('[data-spoke-back]');
  await spoke('#/builder/', 'Back to the Builder');
  await page().waitFor(`document.querySelector('#tpl-name')?.value === 'Board minutes'`, 'the unsaved template name to come back', 10_000).then(
    () => run.check('Back from Settings returns to the Builder with its unsaved name', true),
    () => run.check('Back from Settings returns to the Builder with its unsaved name', false),
  );
  await keyOnPage('Escape');
  run.check('Esc does not leave the Builder while the template has unsaved changes', (await route()).startsWith('#/builder/'), await route());
  await page().click('[data-spoke-back]');
  await spoke(`#/review/${first}`, 'Back to Review');
  run.check('the Builder’s Back returns to its recording’s Review', (await route()) === `#/review/${first}`, await route());
  await keyOnPage('Escape');
  await onLibrary('Esc on Review');
  run.check('Esc on Review (nothing open) goes back to the Library', (await route()) === '#/library', await route());

  // 3. The waiting-for-a-model card's link to Settings › Transcription, and back.
  await page().click(`[data-recording-id="${first}"]`);
  await spoke(`#/review/${first}`, 'Review');
  await page().click({ name: 'Open Settings › Transcription', exact: false });
  await spoke('#/settings/transcription', 'Settings › Transcription');
  run.check('Settings opened from Review names the recording', (await backLabel()) === 'Navigation one', await backLabel());
  await page().click('[data-spoke-back]');
  await spoke(`#/review/${first}`, 'Back to Review');
  run.check('Back from Settings returns to Review', (await route()) === `#/review/${first}`, await route());
  await page().click('[data-spoke-back]');
  await onLibrary('Back to the Library');

  // 4. The gear reopens the section shown last; Esc goes back.
  await page().click({ name: 'Settings' });
  await spoke('#/settings/', 'Settings from the gear');
  run.check('the gear opens Settings at the section shown last (Transcription)', (await route()) === '#/settings/transcription', await route());
  run.check('Settings from the gear goes back to the Library', (await backLabel()) === 'Library', await backLabel());
  // 5. Esc closes a menu first.
  await page().click({ name: 'Storage and history', exact: false });
  await page().click({ name: 'Delete audio tracks older than', exact: false });
  await page().waitFor(`!!document.querySelector('[role="option"]')`, 'the menu');
  await page().key('Escape');
  await sleep(400);
  const menuGone = await page().eval(`!document.querySelector('[role="option"]')`);
  run.check('Esc closes the open menu and stays on Settings', menuGone && (await route()) === '#/settings/storage', await route());
  await keyOnPage('Escape');
  await onLibrary('Esc on Settings');
  run.check('a second Esc goes back to the Library', (await route()) === '#/library', await route());

  // 6. The Recording session: Esc does nothing, Alt+← goes back and the session carries on.
  await page().click({ name: 'New recording' });
  await run.hasText('AUDIO SOURCES');
  await page().click({ name: 'Start recording' });
  await run.waitElapsed(2);
  await keyOnPage('Escape');
  run.check('Esc does nothing on the Recording session', (await route()).startsWith('#/record'), await route());
  await keyOnPage('ArrowLeft', ['alt']);
  await onLibrary('Alt+← on the Recording session');
  const states = await run.events('recording.state');
  run.check('Alt+← went back to the Library and the recording carried on', (await route()) === '#/library' && states.at(-1)?.payload.state === 'recording', states.at(-1)?.payload.state);
  await run.shot('library-while-recording');
  await page().click({ name: 'Back to recording' });
  await at('#/record', 'back to the session');
  await page().click({ name: 'Stop and open review' });
  await at('#/review/', 'Review after Stop');

  // 7. The mouse's back button, and a deep link.
  // A deep link opened fresh (a reload on #/settings/storage): no trail, so Back is the designed one.
  await page().eval(`(() => { location.hash = '#/settings/storage'; location.reload(); return true; })()`);
  await sleep(2500);
  await spoke('#/settings/storage', 'the deep link after a reload');
  run.check('a deep link to Settings goes back to the Library', (await backLabel()) === 'Library', await backLabel());
  await mouseBack();
  await onLibrary('the mouse’s back button');
  run.check('the mouse’s back button presses the back control', (await route()) === '#/library', await route());
  await mouseBack();
  run.check('the mouse’s back button does nothing on the Library', (await route()) === '#/library', await route());
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
