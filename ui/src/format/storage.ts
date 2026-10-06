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
