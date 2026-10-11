// 2.1, Settings › General › Date format (DESIGN.md §11): how a full date reads everywhere Memento
// shows one: new titles, the Library (a date in another year), Review's header and details, History.
// Times and timecodes are not affected. Every preset but "Windows setting" is written by hand with
// English month names, so the text is the same on every PC and in tests, as format/when.ts does.
import { signal } from '@preact/signals';
import type { DateFormat } from '../bridge/types';

const MONTHS = [
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

export const DEFAULT_DATE_FORMAT: DateFormat = 'yearMonthDay';

/** The presets in Settings' order, each labelled with how 16 February 2026 reads in it. */
export const DATE_FORMAT_OPTIONS: readonly { value: DateFormat; label: string }[] = [
  { value: 'yearMonthDay', label: '2026 February 16' },
  { value: 'dayMonthYear', label: '16 February 2026' },
  { value: 'monthDayYear', label: 'February 16, 2026' },
  { value: 'iso', label: '2026-02-16' },
  { value: 'windows', label: 'Windows setting' },
];

/**
 * The format in effect, kept in step with `settings.general.dateFormat` (main.tsx). A signal, so a
 * component that shows a date renders again when the setting changes.
 */
export const dateFormat = signal<DateFormat>(DEFAULT_DATE_FORMAT);

/** "2026 February 16" (or the chosen preset) for `date` in local time. */
export function formatFullDate(date: Date, format: DateFormat = dateFormat.value): string {
  const year = date.getFullYear();
  const month = MONTHS[date.getMonth()] ?? '';
  const day = date.getDate();
  switch (format) {
    case 'dayMonthYear':
      return `${day} ${month} ${year}`;
    case 'monthDayYear':
      return `${month} ${day}, ${year}`;
    case 'iso':
      return `${year}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(day).padStart(2, '0')}`;
    case 'windows':
      // WebView2 formats with the language Windows shows; the host uses the region's long date.
      return new Intl.DateTimeFormat(undefined, { dateStyle: 'long' }).format(date);
    case 'yearMonthDay':
      return `${year} ${month} ${day}`;
  }
}

/** A new recording's title when none is typed: "Untitled meeting · 2026 February 16". */
export function untitledTitle(typeWord: string, date: Date, format: DateFormat = dateFormat.value): string {
  return `Untitled ${typeWord} · ${formatFullDate(date, format)}`;
}
