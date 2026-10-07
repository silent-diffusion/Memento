// Transcription languages offered in Settings and in Transcribe again (BCP-47 primary subtags).
// "auto" lets the engine detect the language per recording.

export const TRANSCRIPTION_LANGUAGES: readonly { value: string; label: string }[] = [
  { value: 'auto', label: 'Auto-detect' },
  { value: 'en', label: 'English' },
  { value: 'de', label: 'German' },
  { value: 'fr', label: 'French' },
  { value: 'es', label: 'Spanish' },
  { value: 'it', label: 'Italian' },
  { value: 'nl', label: 'Dutch' },
  { value: 'pt', label: 'Portuguese' },
  { value: 'pl', label: 'Polish' },
  { value: 'sv', label: 'Swedish' },
  { value: 'da', label: 'Danish' },
  { value: 'fi', label: 'Finnish' },
  { value: 'ja', label: 'Japanese' },
  { value: 'zh', label: 'Chinese' },
];

/** "English", or the code itself for a language the list does not name. */
export function languageName(code: string): string {
  return TRANSCRIPTION_LANGUAGES.find((l) => l.value === code)?.label ?? code;
}
