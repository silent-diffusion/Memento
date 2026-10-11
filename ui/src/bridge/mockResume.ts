// 2.1 "Resume recording" in the browser preview: the host's rule for which recordings take another segment
// (ResumePolicy.cs) and the segments a recording made in the preview keeps.
import type { RecordingSegment } from './types';
import type { MockProject } from './mockData';

/** Same calendar day in local time. */
function sameDay(a: Date, b: Date): boolean {
  return a.getFullYear() === b.getFullYear() && a.getMonth() === b.getMonth() && a.getDate() === b.getDate();
}

/** Why the recording cannot be resumed now (the host's sentences), or null when it can. */
export function mockWhyNotResume(project: MockProject, nowMs: number, activeRecordingId: string | null): string | null {
  const title = project.summary.title;
  if (activeRecordingId !== null) {
    return activeRecordingId === project.summary.id
      ? `"${title}" is still being recorded or saved.`
      : 'Another recording is running. Stop it first to resume this one.';
  }
  if (project.summary.state === 'recording' || project.summary.state === 'finalizing') {
    return `"${title}" is still being recorded or saved. Resume it once it shows as saved in the Library; nothing is lost.`;
  }
  if (project.summary.state !== 'ready' && project.summary.state !== 'recovered') {
    return `"${title}" was not saved completely, so Memento does not add to it. Its files are kept; Memento tries to save it again at the next start. Start a new recording instead.`;
  }
  const created = new Date(project.summary.createdAt);
  if (!sameDay(created, new Date(nowMs))) {
    const day = created.toLocaleDateString(undefined, { day: 'numeric', month: 'long' });
    return `"${title}" was recorded on ${day}; only recordings started today can be resumed. Start a new recording instead; this one is unchanged.`;
  }
  if (project.mixOnly != null) {
    return `"${title}" keeps only its mix (Keep only the mix removed the separate tracks), so a new segment could not be mixed with it. Start a new recording instead; this one is unchanged.`;
  }
  return null;
}

/** The segments of a project: the ones a preview recording kept, or one for a sample recorded in one go. */
export function mockSegments(project: MockProject): RecordingSegment[] {
  return (
    project.segments ?? [
      { index: 1, startedAt: project.summary.createdAt, offsetMs: 0, durationMs: project.summary.durationMs, gapMs: 0 },
    ]
  );
}
