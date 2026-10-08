import { act } from 'preact/test-utils';
import { afterEach, describe, expect, it } from 'vitest';
import { linkHistory } from '../../bridge/mockHistory';
import type { HistoryEntry } from '../../bridge/types';
import { versionTitle } from '../../format/history';
import { formatRoute, parseRoute } from '../../state/router';
import { undoOf } from '../../state/undo';
import { click, mountApp, settle, until, type Harness } from '../../testing/appHarness';

const NOW = new Date(2026, 9, 6, 15, 0);
const DESIGN_REVIEW = '20261005-160000-dsrev';

function must<T>(value: T | null | undefined, what: string): T {
  if (value === null || value === undefined) {
    throw new Error(`no ${what}`);
  }
  return value;
}

describe('History opens the versions it made (against the browser-preview host)', () => {
  let h: Harness;

  afterEach(() => {
    h.unmount();
  });

  const openHistory = async (): Promise<void> => {
    h = await mountApp({ name: 'review', recordingId: DESIGN_REVIEW }, {}, NOW);
    await until(() => h.container.querySelector('.segm') !== null);
    await click(h.container.querySelector('#review-tab-history'));
    await until(() => h.container.querySelector('.history-open') !== null);
  };

  const firstText = (): string | null | undefined => h.container.querySelector('.review-centre .segm .segm-text')?.textContent;
  const bannerButton = (name: string): HTMLButtonElement | undefined =>
    [...h.container.querySelectorAll<HTMLButtonElement>('.tx-version-banner button')].find((b) => b.textContent === name);

  it('makes only lines with a kept version clickable and says on hover why the others are not', async () => {
    await openHistory();
    const { links } = await h.bridge.call('history.links', { recordingId: DESIGN_REVIEW });
    const items = [...h.container.querySelectorAll('.history-item')];
    const openable = links.filter((l) => l.versionId !== null).map((l) => l.index);

    expect(openable.length).toBeGreaterThan(0);
    items.forEach((item, i) => {
      const opener = item.querySelector('.history-open');
      expect(opener !== null, `line ${String(i)}`).toBe(openable.includes(i));
      if (opener === null) {
        expect(item.querySelector('.history-title')?.getAttribute('title')).toMatch(/^(Nothing to open|No copy of)/);
      } else {
        expect(opener.getAttribute('aria-label')).toMatch(/^Open the (transcript|document) as of .+, after /);
      }
    });
  });

  it('opens a kept transcript version read-only, restores it and Undo puts the current one back', async () => {
    await openHistory();
    const before = firstText();
    const { links } = await h.bridge.call('history.links', { recordingId: DESIGN_REVIEW });
    const kept = must(
      links.find((l) => l.kind === 'transcript' && l.versionId !== null && l.versionId !== 'current'),
      'kept transcript version',
    );
    const { transcript: version } = await h.bridge.call('transcript.getVersion', { recordingId: DESIGN_REVIEW, versionId: must(kept.versionId, 'version id') });
    const versionFirst = version.segments[0]?.text;

    await click(h.container.querySelectorAll('.history-item')[kept.index]?.querySelector('.history-open'));
    await until(() => h.container.querySelector('.tx-version-list .segm') !== null);

    expect(h.container.querySelector('.tx-version-banner .banner-lead')?.textContent).toMatch(/^Transcript as of .+, after .+\.$/);
    expect(h.container.querySelector('.tx-version-list .segm-text')?.textContent).toBe(versionFirst);
    // Read-only: nothing to edit, no speaker menus.
    expect(h.container.querySelector('.tx-version-list .segm-text--editable, .tx-version-list button.segm-speaker')).toBeNull();
    expect(h.container.querySelector('.history-item--open')).not.toBeNull();

    await click(bannerButton('Restore this version'));
    await until(() => h.container.querySelector('.tx-version-banner') === null);
    await until(() => firstText() === versionFirst);
    expect(undoOf(h.store).undoLabel.value).toMatch(/^restore the transcript as of /);

    await act(async () => {
      await undoOf(h.store).undo();
    });
    await until(() => firstText() === before);
    await act(async () => {
      await undoOf(h.store).redo();
    });
    await until(() => firstText() === versionFirst);
  });

  it('goes back to the current transcript with Back to current and Escape', async () => {
    await openHistory();
    const before = firstText();
    await click(h.container.querySelector('.history-open'));
    await until(() => h.container.querySelector('.tx-version-banner') !== null);
    await click(bannerButton('Back to current'));
    await until(() => h.container.querySelector('.tx-version-banner') === null);
    expect(firstText()).toBe(before);

    await click(h.container.querySelector('.history-open'));
    await until(() => h.container.querySelector('.tx-version-banner') !== null);
    await act(async () => {
      h.container.querySelector('.tx-version-back')?.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }));
      await Promise.resolve();
    });
    await settle(20);
    expect(h.container.querySelector('.tx-version-banner')).toBeNull();
  });
});

describe('history links (the host rule, as the preview applies it)', () => {
  const at = (minutes: number): string => new Date(Date.UTC(2026, 9, 8, 16, minutes)).toISOString();
  const line = (minutes: number, stage: HistoryEntry['stage'], event: HistoryEntry['event'], summary: string): HistoryEntry => ({
    at: at(minutes),
    stage,
    event,
    summary,
    detail: null,
  });

  it('opens each copy from the last line inside it', () => {
    const history = [
      line(0, 'transcript', 'completed', 'Transcribed · 30 lines'),
      line(4, 'speakers', 'completed', 'Found 2 speakers'),
      line(10, 'edited', 'info', 'Transcript edited'),
      line(11, 'edited', 'info', 'Details edited'),
      line(12, 'minutes', 'completed', 'Meeting minutes generated with Local model'),
    ];
    const links = linkHistory(history, [
      { kind: 'transcript', documentId: null, versionId: 'v1', start: at(0), end: at(10) },
      { kind: 'transcript', documentId: null, versionId: 'current', start: at(10), end: null },
      { kind: 'document', documentId: 'd1', versionId: 'current', start: at(12), end: null },
    ]);

    expect(links.map((l) => [l.index, l.versionId, l.documentId])).toEqual([
      [0, null, null],
      [1, 'v1', null],
      [2, 'current', null],
      [4, 'current', 'd1'],
    ]);
    expect(versionTitle(must(links[1], 'link'), null, must(history[1], 'line').at, new Date(2026, 9, 8, 18))).toMatch(
      /^Transcript as of \d{1,2}:\d\d [AP]M, after speakers were identified$/,
    );
  });

  it('carries a document version in the route', () => {
    const route = { name: 'document', recordingId: 'r1', documentId: 'd1', versionId: '20261008T160800000Z' } as const;
    expect(parseRoute(formatRoute(route))).toEqual(route);
    expect(parseRoute('#/document/r1/d1')).toEqual({ name: 'document', recordingId: 'r1', documentId: 'd1' });
  });
});
