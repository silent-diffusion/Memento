const MIB = 1024 * 1024;
const GIB = 1024 * MIB;

/**
 * Free space as people read it in File Explorer: binary units labelled GB/MB.
 * Values are rounded down so Memento never claims more room than there is.
 * `212 GB`, `4.5 GB`, `4 GB`, `640 MB`.
 */
export function formatFreeSpace(bytes: number): string {
  const safe = Number.isFinite(bytes) && bytes > 0 ? bytes : 0;
  if (safe >= 10 * GIB) {
    return `${Math.floor(safe / GIB)} GB`;
  }
  if (safe >= GIB) {
    const tenths = Math.floor((safe / GIB) * 10) / 10;
    return `${Number.isInteger(tenths) ? tenths.toFixed(0) : tenths.toFixed(1)} GB`;
  }
  return `${Math.floor(safe / MIB)} MB`;
}

/**
 * The size of something stored: `410 MB`, `1.2 GB`, `48 GB`, `12 KB`. Rounded to the nearest unit
 * step (unlike free space, nothing is promised by rounding a size).
 */
export function formatSize(bytes: number): string {
  const safe = Number.isFinite(bytes) && bytes > 0 ? bytes : 0;
  if (safe >= 100 * GIB) {
    return `${Math.round(safe / GIB)} GB`;
  }
  if (safe >= GIB) {
    const tenths = Math.round((safe / GIB) * 10) / 10;
    return `${Number.isInteger(tenths) ? tenths.toFixed(0) : tenths.toFixed(1)} GB`;
  }
  if (safe >= MIB) {
    return `${Math.round(safe / MIB)} MB`;
  }
  return `${Math.max(safe > 0 ? 1 : 0, Math.round(safe / 1024))} KB`;
}

/** Whole gigabytes from bytes, rounded down: the unit of the low-space threshold setting. */
export function wholeGigabytes(bytes: number): number {
  return Math.floor(Math.max(0, bytes) / GIB);
}

export const BYTES_PER_GIB = GIB;
