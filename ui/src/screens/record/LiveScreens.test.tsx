import { render } from 'preact';
import { act } from 'preact/test-utils';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import type { VideoTrackStatus } from '../../bridge/types';
import { LiveScreens, MAX_LIVE_SCREENS } from './LiveScreens';

// 2.8.0: Add screen and Remove (with its confirmation) while recording.

function status(trackId: VideoTrackStatus['trackId'], sourceName: string, over: Partial<VideoTrackStatus> = {}): VideoTrackStatus {
  return {
    trackId,
    role: trackId === 'camera' ? 'camera' : 'screen',
    state: 'recording',
    sourceId: `display:${sourceName}`,
    sourceName,
    reason: null,
    encoder: 'e',
    hardware: true,
    width: 1920,
    height: 1080,
    frameRate: 30,
    targetBitsPerSecond: 0,
    actualBitsPerSecond: 0,
    framesWritten: 0,
    framesDropped: 0,
    bytesWritten: 0,
    durationMs: 0,
    ...over,
  };
}

let root: HTMLDivElement;

beforeEach(() => {
  root = document.createElement('div');
  document.body.append(root);
});

afterEach(() => {
  render(null, root);
  root.remove();
});

function buttons(): HTMLButtonElement[] {
  return [...root.querySelectorAll('button')];
}

function button(text: string): HTMLButtonElement {
  const found = buttons().find((b) => b.textContent === text || b.getAttribute('aria-label') === text);
  if (found === undefined) {
    throw new Error(`no button ${text}`);
  }
  return found;
}

describe('screens while recording', () => {
  it('lists the screens (not the camera), adds one, and removes one only after the confirmation', async () => {
    let adds = 0;
    const removed: string[] = [];
    await act(() => {
      render(
        <LiveScreens
          tracks={[status('screen', 'Display 1'), status('screen-2', 'Zoom Meeting'), status('camera', 'Logitech Brio')]}
          busy={new Set()}
          onAdd={() => {
            adds++;
          }}
          onRemove={(t) => {
            removed.push(t.trackId);
          }}
        />,
        root,
      );
    });
    expect([...root.querySelectorAll('.src-name')].map((n) => n.textContent)).toEqual(['Display 1', 'Zoom Meeting']);

    await act(() => {
      button('Add screen').click();
    });
    expect(adds).toBe(1);

    await act(() => {
      button('Remove Zoom Meeting').click();
    });
    expect(removed).toEqual([]);
    expect(root.textContent).toContain('Stop recording Zoom Meeting? What it recorded so far is kept; audio keeps recording.');
    await act(() => {
      button('Keep recording').click();
    });
    expect(removed).toEqual([]);

    await act(() => {
      button('Remove Zoom Meeting').click();
    });
    await act(() => {
      root.querySelector<HTMLButtonElement>('[role="alertdialog"] button')?.click();
    });
    expect(removed).toEqual(['screen-2']);
  });

  it('says when no screen is recording, and when no more can be added', async () => {
    await act(() => {
      render(<LiveScreens tracks={[]} busy={new Set()} onAdd={() => undefined} onRemove={() => undefined} />, root);
    });
    expect(root.textContent).toContain('No screen is being recorded. Audio keeps recording; Add screen starts one from now.');

    const full = Array.from({ length: MAX_LIVE_SCREENS }, (_, i) => status(i === 0 ? 'screen' : `screen-${i + 1}`, `Display ${i + 1}`));
    await act(() => {
      render(<LiveScreens tracks={full} busy={new Set()} onAdd={() => undefined} onRemove={() => undefined} />, root);
    });
    expect(button('Add screen').disabled).toBe(true);
    expect(root.textContent).toContain('4 screens are recording, the most at once. Remove one to add another.');
  });
});
