// Browser-preview stand-ins for the 2.0 video methods (docs/BRIDGE.md, Video (2.0)). The preview cannot see this
// PC's displays, so it lists one made-up display and answers that video cannot be recorded here; the host does the
// real work. Kept small on purpose: the Recording screen's VIDEO section brings its own richer mock.

import { MockHostError } from './mockSession';
import type { BridgeMethods, VideoSourcesResult } from './types';

type VideoMethod = 'video.listSources' | 'video.thumbnail' | 'recording.setVideoSource' | 'recording.setPip' | 'video.openPrivacySettings' | 'video.remove';

type VideoHandlers = { [M in VideoMethod]: (params: BridgeMethods[M]['params']) => BridgeMethods[M]['result'] };

const PREVIEW_SOURCES: VideoSourcesResult = {
  displays: [
    {
      id: 'display:\\\\.\\DISPLAY1',
      kind: 'display',
      name: 'Display 1',
      detail: '1920 × 1080 · 60 Hz · primary',
      width: 1920,
      height: 1080,
      refreshRate: 60,
      primary: true,
      excluded: false,
      protected: false,
      processName: null,
    },
  ],
  windows: [],
  cameras: [],
};

const UNAVAILABLE = 'Video can\'t be recorded in the browser preview. Audio recording is not affected.';

/** A 16 × 9 grey still, so the picker has something to draw. */
const STILL = 'data:image/svg+xml;base64,' + btoa('<svg xmlns="http://www.w3.org/2000/svg" width="16" height="9"><rect width="16" height="9" fill="#8a857c"/></svg>');

export function createMockVideo(): { handlers: VideoHandlers } {
  return {
    handlers: {
      'video.listSources': () => PREVIEW_SOURCES,
      'video.thumbnail': (params) => ({ dataUrl: STILL, width: params.width ?? 320, height: Math.round(((params.width ?? 320) * 9) / 16) }),
      'recording.setVideoSource': (params) => {
        if (params.sourceId === null) {
          return { videoTracks: [] };
        }
        throw new MockHostError('video.unavailable', UNAVAILABLE, params.sourceId);
      },
      'recording.setPip': () => ({}),
      'video.openPrivacySettings': () => ({}),
      'video.remove': (params) => {
        throw new MockHostError('video.nothingToRemove', 'This recording has no video files to remove. Nothing was changed.', params.recordingId);
      },
    },
  };
}
