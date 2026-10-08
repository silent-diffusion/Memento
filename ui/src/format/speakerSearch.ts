// The speaker menu's "Find or add a speaker" field (DESIGN.md §9): matching ignores case and
// accents, names that start with what was typed come first, then names with a word that does,
// then names that hold it anywhere; within each group the transcript's order is kept.

/** Lower case without accents or extra spaces: "  Zoë  Ångström " → "zoe angstrom". */
export function foldName(text: string): string {
  return text
    .normalize('NFD')
    .replace(/\p{M}+/gu, '')
    .toLocaleLowerCase()
    .replace(/\s+/g, ' ')
    .trim();
}

/** 0: the name starts with the query, 1: a word in it does, 2: it holds it, null: no match. */
export function matchRank(name: string, query: string): 0 | 1 | 2 | null {
  const q = foldName(query);
  if (q === '') {
    return 0;
  }
  const n = foldName(name);
  if (n.startsWith(q)) {
    return 0;
  }
  if (n.split(/[\s\-'’.]+/).some((word) => word.startsWith(q))) {
    return 1;
  }
  return n.includes(q) ? 2 : null;
}

/** The items whose name matches `query`, best first; all of them, in order, for an empty query. */
export function filterByName<T>(items: readonly T[], query: string, nameOf: (item: T) => string): T[] {
  return items
    .map((item, index) => ({ item, index, rank: matchRank(nameOf(item), query) }))
    .filter((x): x is { item: T; index: number; rank: 0 | 1 | 2 } => x.rank !== null)
    .sort((a, b) => a.rank - b.rank || a.index - b.index)
    .map((x) => x.item);
}

/** Whether a speaker already has exactly this name (ignoring case, accents and spacing); then "Add" is not offered. */
export function hasExactName<T>(items: readonly T[], query: string, nameOf: (item: T) => string): boolean {
  const q = foldName(query);
  return q !== '' && items.some((item) => foldName(nameOf(item)) === q);
}
