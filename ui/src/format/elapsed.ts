// The recording timer (DESIGN.md §8). The host is the clock: recording.state carries elapsedMs
// (recorded time, excluding pauses) on every change and at least once a second. Between events the
// display moves on with the page clock so the seconds tick smoothly, but it never runs more than
// MAX_LEAD_MS past the host's latest value, and it never steps backwards within a session.

/** How far the display may run ahead of the host's last reported elapsed time. */
export const MAX_LEAD_MS = 1000;

export interface ElapsedClock {
  /** A recording.state arrived: `atMs` is the page clock (performance.now()) when it did. */
  observe(sessionId: string, elapsedMs: number, running: boolean, atMs: number): void;
  /** What the timer shows at page time `nowMs`. */
  read(nowMs: number): number;
  reset(): void;
}

export function createElapsedClock(): ElapsedClock {
  let sessionId: string | null = null;
  let hostMs = 0;
  let running = false;
  let receivedAt = 0;
  let shown = 0;

  return {
    observe(id, elapsedMs, isRunning, atMs) {
      if (id !== sessionId) {
        sessionId = id;
        shown = 0;
      }
      hostMs = Math.max(0, elapsedMs);
      running = isRunning;
      receivedAt = atMs;
    },
    read(nowMs) {
      if (sessionId === null) {
        return 0;
      }
      const lead = running ? Math.min(MAX_LEAD_MS, Math.max(0, nowMs - receivedAt)) : 0;
      const ceiling = hostMs + MAX_LEAD_MS;
      // Monotonic within the session, but always inside the one-second lead.
      shown = Math.min(ceiling, Math.max(shown, hostMs + lead));
      return shown;
    },
    reset() {
      sessionId = null;
      hostMs = 0;
      running = false;
      receivedAt = 0;
      shown = 0;
    },
  };
}
