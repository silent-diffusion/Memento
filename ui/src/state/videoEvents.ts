// 2.0 video events into the store (BRIDGE.md "Video (2.0, proposed)" and its additions):
// recording.videoStatus once a second, and recording.videoProblem, which becomes a §18.6 toast when a
// screen track waits for a display or ends mid-recording. One toast per track (a newer problem of the
// same track replaces it), so a flapping source never stacks a storm of toasts.
import type { BridgeClient } from '../bridge/client';
import type { RecordingVideoProblemPayload, VideoRole } from '../bridge/types';
import { isToastProblem, videoProblemToast } from '../format/videoProblems';
import type { AppStore } from './store';

function messageOf(error: unknown): string {
  return error instanceof Error ? error.message : 'Memento did not answer.';
}

/** Ends the session's video tracks: "Audio only from here", "Record audio only", "Stop video now". */
export async function endVideoTracks(bridge: BridgeClient, store: AppStore, sessionId: string, roles: readonly VideoRole[] = ['screen', 'camera']): Promise<void> {
  const present = new Set(store.videoStatus.value?.sessionId === sessionId ? store.videoStatus.value.videoTracks.map((t) => t.trackId) : roles);
  for (const role of roles.filter((r) => present.has(r))) {
    const { videoTracks } = await bridge.call('recording.setVideoSource', { sessionId, role, sourceId: null });
    const status = store.videoStatus.value;
    if (status?.sessionId === sessionId) {
      store.videoStatus.value = { ...status, videoTracks };
    }
  }
}

/** A problem is over once its track records again; the encoder fallback stays for the session (its banner). */
export function openProblems(problems: readonly RecordingVideoProblemPayload[], recordingRoles: ReadonlySet<VideoRole>): RecordingVideoProblemPayload[] {
  return problems.filter((p) => p.code === 'video.encoderFallback' || !recordingRoles.has(p.trackId));
}

function showProblemToast(bridge: BridgeClient, store: AppStore, problem: RecordingVideoProblemPayload): void {
  const cameraRecording = store.videoStatus.value?.videoTracks.some((t) => t.trackId === 'camera' && (t.state === 'recording' || t.state === 'paused')) ?? false;
  const show = (displays: Parameters<typeof videoProblemToast>[1]): void => {
    const copy = videoProblemToast(problem, displays, cameraRecording);
    const fail = (title: string) => (error: unknown) => {
      store.toasts.show({ tone: 'danger', title, body: `${messageOf(error)} Audio keeps recording.` });
    };
    store.toasts.show({
      key: `video:${problem.trackId}`,
      tone: 'warning',
      title: copy.title,
      body: copy.body,
      actions: [
        ...(copy.capture === null || copy.captureLabel === null
          ? []
          : [
              {
                label: copy.captureLabel,
                run: () => {
                  const target = copy.capture;
                  if (target === null) {
                    return;
                  }
                  bridge
                    .call('recording.setVideoSource', { sessionId: problem.sessionId, role: 'screen', sourceId: target.id })
                    .then(({ videoTracks }) => {
                      const status = store.videoStatus.value;
                      if (status?.sessionId === problem.sessionId) {
                        store.videoStatus.value = { ...status, videoTracks };
                      }
                    })
                    .catch(fail(`The screen track could not switch to ${target.name}`));
                },
              },
            ]),
        {
          label: copy.quiet.label,
          quiet: true,
          run: () => {
            if (copy.quiet.endVideo) {
              endVideoTracks(bridge, store, problem.sessionId).catch(fail('Video could not be turned off'));
            }
          },
        },
      ],
    });
  };
  bridge
    .call('video.listSources')
    .then((sources) => {
      show(sources.displays);
    })
    .catch(() => {
      show([]);
    });
}

export function connectVideoEvents(bridge: BridgeClient, store: AppStore): () => void {
  const offs = [
    bridge.on('recording.videoStatus', (payload) => {
      store.videoStatus.value = payload;
      const recording = new Set(payload.videoTracks.filter((t) => t.state === 'recording').map((t) => t.trackId));
      const open = openProblems(store.videoProblems.value, recording);
      if (open.length !== store.videoProblems.value.length) {
        store.videoProblems.value = open;
      }
    }),
    bridge.on('recording.videoProblem', (payload) => {
      store.videoProblems.value = [...store.videoProblems.value.filter((p) => !(p.trackId === payload.trackId && p.code === payload.code)), payload];
      if (isToastProblem(payload)) {
        showProblemToast(bridge, store, payload);
      }
    }),
  ];
  return () => {
    for (const off of offs) {
      off();
    }
  };
}
