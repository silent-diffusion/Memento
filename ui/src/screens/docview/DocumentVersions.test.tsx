import { act } from 'preact/test-utils';
import { afterEach, describe, expect, it } from 'vitest';
import { undoOf } from '../../state/undo';
import { click, mountApp, until, type Harness } from '../../testing/appHarness';

const DESIGN = '20261005-160000-dsrev';
const MINUTES = 'doc-20261005-minutes';

describe('Earlier document versions open read-only in the viewer', () => {
  let h: Harness;

  afterEach(() => {
    h.unmount();
  });

  const paperText = (): string => document.querySelector('.doc-paper article.paper')?.textContent ?? '';
  const bannerButton = (name: string): HTMLButtonElement | undefined =>
    [...document.querySelectorAll<HTMLButtonElement>('.doc-version-banner button')].find((b) => b.textContent === name);

  const olderVersionId = async (harness: Harness): Promise<string> => {
    const { versions } = await harness.bridge.call('documents.versions', { recordingId: DESIGN, documentId: MINUTES });
    const older = versions.find((v) => v.id !== 'current');
    if (older === undefined) {
      throw new Error('no earlier version');
    }
    return older.id;
  };

  it('opens a version from the Versions list, restores it, and Undo brings the text back', async () => {
    h = await mountApp({ name: 'document', recordingId: DESIGN, documentId: MINUTES }, { m4: { ai: 'ready' } });
    await until(() => document.querySelector('.doc-paper article.paper') !== null && document.querySelectorAll('.doc-ver-open').length > 1);
    const current = paperText();
    const { html } = await h.bridge.call('documents.getVersion', { recordingId: DESIGN, documentId: MINUTES, versionId: await olderVersionId(h) });
    const expected = new DOMParser().parseFromString(html, 'text/html').querySelector('article')?.textContent ?? '';
    expect(expected).not.toBe(current);

    await click([...document.querySelectorAll('.doc-ver-open')].find((b) => b.getAttribute('aria-pressed') === 'false'));
    await until(() => document.querySelector('.doc-version-banner') !== null && paperText() === expected);
    expect(document.querySelector('.doc-paper article.paper')?.getAttribute('contenteditable')).toBeNull();
    expect(document.querySelector('.doc-version-banner .banner-lead')?.textContent).toMatch(/: version \d+\.$/);
    expect(document.querySelector('.doc-ver--open')).not.toBeNull();

    await click(bannerButton('Restore this version'));
    await until(() => document.querySelector('.doc-version-banner') === null && paperText() === expected);
    expect(document.querySelector('.doc-paper article.paper')?.getAttribute('contenteditable')).toBe('true');
    expect(undoOf(h.store).undoLabel.value).toMatch(/^restore version \d+$/);

    await act(async () => {
      await undoOf(h.store).undo();
    });
    await until(() => paperText() === current);
  });

  it('opens the version named in the route (from Review History) and goes back to current', async () => {
    const probe = await mountApp({ name: 'library' }, { m4: { ai: 'ready' } });
    const versionId = await olderVersionId(probe);
    probe.unmount();

    h = await mountApp({ name: 'document', recordingId: DESIGN, documentId: MINUTES, versionId }, { m4: { ai: 'ready' } });
    await until(() => document.querySelector('.doc-version-banner') !== null && document.querySelector('.doc-paper article.paper') !== null);
    await click(bannerButton('Back to current'));
    await until(() => document.querySelector('.doc-version-banner') === null && document.querySelector('.doc-paper article.paper')?.getAttribute('contenteditable') === 'true');
  });
});
