// Small interface preferences remembered on this PC for this Windows user (the page's local storage
// lives in Memento's WebView2 profile under the user's AppData). Nothing here is sent anywhere or
// written to the library; a storage error just means the default is used.
import { useState } from 'preact/hooks';

const PREFIX = 'memento.ui.';

export type UiFlag = 'review.skipSilences';

export function readUiFlag(key: UiFlag, fallback: boolean): boolean {
  try {
    const value = globalThis.localStorage.getItem(PREFIX + key);
    return value === null ? fallback : value === '1';
  } catch {
    return fallback;
  }
}

export function writeUiFlag(key: UiFlag, value: boolean): void {
  try {
    globalThis.localStorage.setItem(PREFIX + key, value ? '1' : '0');
  } catch {
    // Storage is off or full: the choice lasts until the screen closes.
  }
}

/** A remembered on/off preference: read once, written on every change. */
export function useUiFlag(key: UiFlag, fallback: boolean): [boolean, (value: boolean) => void] {
  const [value, setValue] = useState(() => readUiFlag(key, fallback));
  return [
    value,
    (next) => {
      setValue(next);
      writeUiFlag(key, next);
    },
  ];
}
