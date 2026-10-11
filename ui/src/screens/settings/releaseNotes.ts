// 2.1.0: the release notes of an available update, read-only. The feed's notes are Markdown (the GitHub release
// body); only headings, bullet lists and paragraphs are kept, every inline mark is stripped to its words, and HTML
// tags are dropped. The result is rendered as plain Preact text, so nothing from the feed is ever injected as HTML
// and no link can be opened from it.

export type NotesBlock = { kind: 'heading'; text: string } | { kind: 'paragraph'; text: string } | { kind: 'list'; items: string[] };

/** Inline Markdown and HTML to plain words: [text](url) → text, `code` → code, **bold** / *em* / __b__ / _em_ → words. */
export function plainNotesText(text: string): string {
  return text
    .replace(/<[^>]*>/g, '')
    .replace(/!\[([^\]]*)\]\((?:[^()]|\([^()]*\))*\)/g, '$1')
    .replace(/\[([^\]]+)\]\((?:[^()]|\([^()]*\))*\)/g, '$1')
    .replace(/`([^`]*)`/g, '$1')
    .replace(/(\*\*|__)(.+?)\1/g, '$2')
    .replace(/(^|[^\w*])[*_]([^*_\s][^*_]*?)[*_](?=[^\w*]|$)/g, '$1$2')
    .replace(/\s+/g, ' ')
    .trim();
}

const BULLET = /^\s*(?:[-*+]|\d+[.)])\s+(.*)$/;
const HEADING = /^\s{0,3}#{1,6}\s+(.*?)\s*#*\s*$/;
const FENCE = /^\s*(```|~~~)/;
const RULE = /^\s*([-*_])(\s*\1){2,}\s*$/;

/** Splits release notes into headings, bullet lists and paragraphs; blank lines end a block. */
export function parseReleaseNotes(markdown: string | null | undefined): NotesBlock[] {
  if (markdown === null || markdown === undefined) {
    return [];
  }
  const blocks: NotesBlock[] = [];
  let paragraph: string[] = [];
  let list: string[] | null = null;
  const flush = (): void => {
    const text = plainNotesText(paragraph.join(' '));
    if (text !== '') {
      blocks.push({ kind: 'paragraph', text });
    }
    paragraph = [];
    if (list !== null && list.length > 0) {
      blocks.push({ kind: 'list', items: list });
    }
    list = null;
  };
  for (const raw of markdown.split(/\r?\n/)) {
    const line = raw.replace(/^\s*>\s?/, '');
    if (line.trim() === '' || FENCE.test(line) || RULE.test(line)) {
      flush();
      continue;
    }
    const heading = HEADING.exec(line);
    if (heading !== null) {
      flush();
      const text = plainNotesText(heading[1] ?? '');
      if (text !== '') {
        blocks.push({ kind: 'heading', text });
      }
      continue;
    }
    const bullet = BULLET.exec(line);
    if (bullet !== null) {
      if (paragraph.length > 0) {
        flush();
      }
      list ??= [];
      const item = plainNotesText(bullet[1] ?? '');
      if (item !== '') {
        list.push(item);
      }
      continue;
    }
    if (list !== null && list.length > 0 && /^\s+\S/.test(raw)) {
      // A continuation line of the last bullet.
      list[list.length - 1] = plainNotesText(`${list[list.length - 1] ?? ''} ${line}`);
      continue;
    }
    if (list !== null) {
      flush();
    }
    paragraph.push(line);
  }
  flush();
  return blocks;
}
