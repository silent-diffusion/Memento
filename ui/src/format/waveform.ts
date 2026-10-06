// The grid card's decorative audio strip (DESIGN.md §16): 48 bars from a pseudo-random sequence
// seeded by the recording id, so a card always draws the same shape. Same generator as the render.

/** A small positive seed from an id (FNV-1a, folded into the generator's range). */
export function seedFromId(id: string): number {
  let hash = 0x811c9dc5;
  for (let i = 0; i < id.length; i++) {
    hash ^= id.charCodeAt(i);
    hash = Math.imul(hash, 0x01000193) >>> 0;
  }
  return hash % 233_280;
}

/** Bar heights in px, 4 to 60, from renders/LibraryGrid.dc.html's generator. */
export function waveformBars(id: string, count = 48): number[] {
  const bars: number[] = [];
  let value = seedFromId(id);
  for (let i = 0; i < count; i++) {
    value = (value * 9301 + 49_297) % 233_280;
    const r = value / 233_280;
    bars.push(Math.round(4 + r * r * 56));
  }
  return bars;
}
