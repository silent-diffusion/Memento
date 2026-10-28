// 2.8.0: the screens being recorded while a recording runs, under the VIDEO section. "Add screen" opens
// the display/window picker and records one more source from now (video.addSource); each screen has a
// Remove with a confirmation (video.removeSource). Audio is never touched; the last screen may go.
import type { JSX } from "preact";
import { useState } from "preact/hooks";
import type { VideoTrackId, VideoTrackStatus } from "../../bridge/types";

/** The most screens recorded at once (the host refuses more with video.tooManySources). */
export const MAX_LIVE_SCREENS = 4;

interface LiveScreensProps {
  tracks: readonly VideoTrackStatus[];
  busy: ReadonlySet<VideoTrackId | "add">;
  onAdd: () => void;
  onRemove: (track: VideoTrackStatus) => void;
}

function stateText(track: VideoTrackStatus): string {
  switch (track.state) {
    case "recording":
      return "Recording";
    case "paused":
      return "Paused with the recording";
    case "waiting":
      return track.reason ?? "Waiting";
    case "ended":
      return track.reason ?? "Ended";
    case "stopped":
      return "Stopped";
  }
}

/** The screens running now (an ended one stays listed so it can be removed or replaced). */
export function liveScreens(
  tracks: readonly VideoTrackStatus[],
): VideoTrackStatus[] {
  return tracks.filter((t) => t.role === "screen" && t.state !== "stopped");
}

export function LiveScreens({
  tracks,
  busy,
  onAdd,
  onRemove,
}: LiveScreensProps): JSX.Element {
  const [confirming, setConfirming] = useState<VideoTrackId | null>(null);
  const screens = liveScreens(tracks);
  const running = screens.filter((t) => t.state !== "ended").length;
  const full = running >= MAX_LIVE_SCREENS;
  return (
    <div
      class="rec-live-screens"
      role="group"
      aria-labelledby="rec-live-screens-label"
    >
      <div class="rec-card-head">
        <span class="lbl" id="rec-live-screens-label">
          Screens recording
        </span>
        <button
          class="btn ghost ghost-row"
          type="button"
          aria-haspopup="dialog"
          disabled={full || busy.has("add")}
          aria-busy={busy.has("add")}
          onClick={onAdd}
        >
          Add screen
        </button>
      </div>
      {full ? (
        <p class="rec-note">
          {MAX_LIVE_SCREENS} screens are recording, the most at once. Remove one
          to add another.
        </p>
      ) : null}
      {screens.length === 0 ? (
        <p class="rec-note">
          No screen is being recorded. Audio keeps recording; Add screen starts
          one from now.
        </p>
      ) : null}
      <ul class="rec-live-screen-list">
        {screens.map((track) => {
          const removing = busy.has(track.trackId);
          return (
            <li key={track.trackId} class="rec-live-screen">
              <div class="src-text">
                <span class="src-name">{track.sourceName}</span>
                <span class="src-sub">{stateText(track)}</span>
              </div>
              {confirming === track.trackId ? (
                <div
                  class="rec-live-screen-confirm"
                  role="alertdialog"
                  aria-label={`Stop recording ${track.sourceName}?`}
                >
                  <span class="rec-live-screen-question">
                    Stop recording {track.sourceName}? What it recorded so far
                    is kept; audio keeps recording.
                  </span>
                  <button
                    class="btn ghost ghost-row"
                    type="button"
                    disabled={removing}
                    onClick={() => {
                      setConfirming(null);
                      onRemove(track);
                    }}
                  >
                    Remove
                  </button>
                  <button
                    class="btn ghost ghost-row"
                    type="button"
                    onClick={() => {
                      setConfirming(null);
                    }}
                  >
                    Keep recording
                  </button>
                </div>
              ) : (
                <button
                  class="btn ghost ghost-row"
                  type="button"
                  aria-label={`Remove ${track.sourceName}`}
                  disabled={removing}
                  aria-busy={removing}
                  onClick={() => {
                    setConfirming(track.trackId);
                  }}
                >
                  Remove
                </button>
              )}
            </li>
          );
        })}
      </ul>
    </div>
  );
}
