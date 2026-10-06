// Duration formats from DESIGN.md §2.2: rows read h:mm:ss at an hour or more, otherwise m:ss;
// totals read "8 h 38 min".

function wholeSeconds(ms: number): number {
  return Number.isFinite(ms) && ms > 0 ? Math.floor(ms / 1000) : 0;
}

/** A recording's length: `1:02:14` or `6:41`. Partial seconds are dropped, never rounded up. */
export function formatDuration(ms: number): string {
  const total = wholeSeconds(ms);
  const hours = Math.floor(total / 3600);
  const minutes = Math.floor((total % 3600) / 60);
  const seconds = total % 60;
  const ss = String(seconds).padStart(2, '0');
  if (hours > 0) {
    return `${hours}:${String(minutes).padStart(2, '0')}:${ss}`;
  }
  return `${minutes}:${ss}`;
}

/** A summed length for the Library summary line: `8 h 38 min`, `45 min`, `2 h`. Rounded to the nearest minute. */
export function formatTotalDuration(ms: number): string {
  const totalMinutes = Math.round(wholeSeconds(ms) / 60);
  const hours = Math.floor(totalMinutes / 60);
  const minutes = totalMinutes % 60;
  if (hours === 0) {
    return `${minutes} min`;
  }
  return minutes === 0 ? `${hours} h` : `${hours} h ${minutes} min`;
}
