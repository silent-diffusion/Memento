// The agenda items the Details sheet edits (DESIGN.md §14), whether they are a parsed preview that
// is not saved yet or the recording's agenda, and the limits agenda.apply enforces (BRIDGE.md M3).
import {
  AGENDA_MAX_ITEM_LENGTH,
  AGENDA_MAX_ITEMS,
  type AgendaApplyItem,
  type AgendaItem,
  type AgendaParsePreview,
} from '../../bridge/types';

/** One editable row. `key` is the agenda item id, or a local key for preview rows. */
export interface EditableItem {
  key: string;
  text: string;
  uncertain: boolean;
  uncertainReason: string | null;
  /** Preview rows only: 0 top level, 1 nested. */
  level: number;
  /** Preview rows only: "page 2, line 14". */
  location: string | null;
  covered: boolean;
}

let keyCounter = 0;
// Also the id of a row added to the recording's agenda: unique within the recording is enough.
const localKey = (): string => `agenda-${Date.now().toString(36)}-${(++keyCounter).toString(36)}`;

export function blankItem(): EditableItem {
  return { key: localKey(), text: '', uncertain: false, uncertainReason: null, level: 0, location: null, covered: false };
}

export function previewItems(preview: AgendaParsePreview): EditableItem[] {
  return preview.items.map((item) => ({
    key: localKey(),
    text: item.text,
    uncertain: item.uncertain,
    uncertainReason: item.uncertainReason,
    level: Math.max(0, Math.min(2, item.level)),
    location: item.location,
    covered: false,
  }));
}

export function fromAgendaItems(items: readonly AgendaItem[]): EditableItem[] {
  return items.map((item) => ({
    key: item.id,
    text: item.text,
    uncertain: item.uncertain,
    uncertainReason: item.uncertainReason,
    level: 0,
    location: null,
    covered: item.covered,
  }));
}

/** Back to the recording's agenda items: a row's key is its item id (new rows bring their own). */
export function toAgendaItems(items: readonly EditableItem[]): AgendaItem[] {
  return items.map((item) => ({
    id: item.key,
    text: item.text,
    covered: item.covered,
    uncertain: item.uncertain,
    uncertainReason: item.uncertain ? item.uncertainReason : null,
  }));
}

/** What agenda.apply receives: blank rows are left out. */
export function applyItems(items: readonly EditableItem[]): AgendaApplyItem[] {
  return items
    .filter((item) => item.text.trim() !== '')
    .map((item) => ({ text: item.text.trim(), uncertain: item.uncertain, uncertainReason: item.uncertain ? item.uncertainReason : null }));
}

/** Changing an item's text is how it is fixed: it is no longer uncertain. */
export function withText(item: EditableItem, text: string): EditableItem {
  return { ...item, text, uncertain: false, uncertainReason: null };
}

export interface AgendaLimits {
  /** Keys of rows longer than AGENDA_MAX_ITEM_LENGTH. */
  tooLong: ReadonlySet<string>;
  /** Non-blank rows above AGENDA_MAX_ITEMS, or 0. */
  overCount: number;
  ok: boolean;
}

export function checkLimits(items: readonly EditableItem[]): AgendaLimits {
  const tooLong = new Set(items.filter((i) => i.text.trim().length > AGENDA_MAX_ITEM_LENGTH).map((i) => i.key));
  const count = items.filter((i) => i.text.trim() !== '').length;
  const overCount = Math.max(0, count - AGENDA_MAX_ITEMS);
  return { tooLong, overCount, ok: tooLong.size === 0 && overCount === 0 };
}

/** "Item 3 is 245 characters. Agenda items can be up to 200; shorten it or split it in two." */
export function tooLongMessage(index: number, length: number): string {
  return `Item ${index + 1} is ${length} characters. Agenda items can be up to ${AGENDA_MAX_ITEM_LENGTH}; shorten it or split it in two.`;
}

/** "This agenda has 214 items. Memento keeps up to 200; remove 14 to apply it." */
export function tooManyMessage(count: number): string {
  const extra = count - AGENDA_MAX_ITEMS;
  return `This agenda has ${count} items. Memento keeps up to ${AGENDA_MAX_ITEMS}; remove ${extra} to apply it.`;
}

/**
 * The notice under the list (DESIGN.md §14): how many items are uncertain and why, with the two
 * ways out. Returns null when nothing is uncertain.
 */
export function uncertainNotice(items: readonly EditableItem[]): { lead: string; reasons: string[] } | null {
  const doubtful = items
    .map((item, index) => ({ item, index }))
    .filter(({ item }) => item.uncertain);
  if (doubtful.length === 0) {
    return null;
  }
  const reasonOf = ({ item }: { item: EditableItem }): string => {
    const reason = item.uncertainReason ?? 'The parser was not sure where this item ends.';
    return item.location === null ? reason : `${reason.replace(/\.$/, '')} (${item.location}).`;
  };
  const [first] = doubtful;
  if (doubtful.length === 1 && first !== undefined) {
    return { lead: `One item is uncertain (dotted). ${reasonOf(first)} Fix it here, or try the AI option below.`, reasons: [] };
  }
  return {
    lead: `${doubtful.length} items are uncertain (dotted). Fix them here, or try the AI option below.`,
    reasons: doubtful.map((d) => `Item ${d.index + 1}: ${reasonOf(d)}`),
  };
}
