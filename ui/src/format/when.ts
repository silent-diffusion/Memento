// Date wording for the Library (DESIGN.md §4): buckets Today, Yesterday, Earlier this week, then one
// per month; `{when}` reads a time for Today/Yesterday ("10:00 AM"), weekday and time within the week
// ("Thu, 11:00 AM") and a date otherwise ("Sep 29", or "Sep 29, 2025" in another year).
// "This week" is the six days before yesterday, so a weekday name is never ambiguous. Formatting is
// done by hand rather than with Intl so the text is identical on every machine and in tests.

const WEEKDAYS_SHORT = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'] as const;
const MONTHS_SHORT = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'] as const;
const MONTHS_LONG = [
  'January',
  'February',
  'March',
  'April',
  'May',
  'June',
  'July',
  'August',
  'September',
  'October',
  'November',
  'December',
] as const;

const DAY_MS = 86_400_000;

export type BucketKind = 'today' | 'yesterday' | 'week' | 'month';

export interface DateBucket {
  /** Stable key: "today", "yesterday", "week", or "month:2026-09". */
  key: string;
  label: string;
  kind: BucketKind;
}

/** Whole calendar days from `date` to `now` in local time (0 = same day, 1 = yesterday). DST-safe. */
export function calendarDaysBetween(date: Date, now: Date): number {
  const a = Date.UTC(date.getFullYear(), date.getMonth(), date.getDate());
  const b = Date.UTC(now.getFullYear(), now.getMonth(), now.getDate());
  return Math.round((b - a) / DAY_MS);
}

export function dateBucket(date: Date, now: Date): DateBucket {
  const days = calendarDaysBetween(date, now);
  if (days <= 0) {
    return { key: 'today', label: 'Today', kind: 'today' };
  }
  if (days === 1) {
    return { key: 'yesterday', label: 'Yesterday', kind: 'yesterday' };
  }
  if (days <= 6) {
    return { key: 'week', label: 'Earlier this week', kind: 'week' };
  }
  const year = date.getFullYear();
  const month = date.getMonth();
  const name = MONTHS_LONG[month] ?? '';
  return {
    key: `month:${year}-${String(month + 1).padStart(2, '0')}`,
    label: year === now.getFullYear() ? name : `${name} ${year}`,
    kind: 'month',
  };
}

/** "10:00 AM", "2:15 PM", "12:05 AM". */
export function formatClock(date: Date): string {
  const hours = date.getHours();
  const hour12 = hours % 12 === 0 ? 12 : hours % 12;
  return `${hour12}:${String(date.getMinutes()).padStart(2, '0')} ${hours < 12 ? 'AM' : 'PM'}`;
}

/** "Sep 29", or "Sep 29, 2025" when the year differs from `now`. */
export function formatShortDate(date: Date, now: Date): string {
  const base = `${MONTHS_SHORT[date.getMonth()] ?? ''} ${date.getDate()}`;
  return date.getFullYear() === now.getFullYear() ? base : `${base}, ${date.getFullYear()}`;
}

export function weekdayShort(date: Date): string {
  return WEEKDAYS_SHORT[date.getDay()] ?? '';
}

/** The `{when}` part of a Library meta line. */
export function formatWhen(date: Date, now: Date): string {
  const bucket = dateBucket(date, now);
  switch (bucket.kind) {
    case 'today':
    case 'yesterday':
      return formatClock(date);
    case 'week':
      return `${weekdayShort(date)}, ${formatClock(date)}`;
    case 'month':
      return formatShortDate(date, now);
  }
}

/** The processing card's "recorded today at 10:00 AM" / "recorded Thu at 11:00 AM" / "recorded Sep 29". */
export function formatRecordedAt(date: Date, now: Date): string {
  const bucket = dateBucket(date, now);
  switch (bucket.kind) {
    case 'today':
      return `recorded today at ${formatClock(date)}`;
    case 'yesterday':
      return `recorded yesterday at ${formatClock(date)}`;
    case 'week':
      return `recorded ${weekdayShort(date)} at ${formatClock(date)}`;
    case 'month':
      return `recorded ${formatShortDate(date, now)}`;
  }
}

/** Parses an ISO 8601 string; an unreadable value becomes the epoch rather than throwing. */
export function parseIso(iso: string): Date {
  const date = new Date(iso);
  return Number.isNaN(date.getTime()) ? new Date(0) : date;
}
