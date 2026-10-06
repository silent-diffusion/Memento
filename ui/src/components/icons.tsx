// Stroke icons from the design renders: 24x24 viewBox, currentColor, round caps and joins.
// Glyph icons use a 1.75 stroke; small UI chevrons and toggles use 2 as in the renders.
import type { JSX } from 'preact';
import type { RecordingType } from '../bridge/types';

interface IconProps {
  size: number;
  class?: string;
  strokeWidth?: number;
}

function Stroke({
  size,
  class: className,
  strokeWidth = 1.75,
  children,
}: IconProps & { children: JSX.Element | JSX.Element[] }): JSX.Element {
  return (
    <svg
      aria-hidden="true"
      class={className}
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      stroke-width={strokeWidth}
      stroke-linecap="round"
      stroke-linejoin="round"
    >
      {children}
    </svg>
  );
}

export function SearchIcon(props: IconProps): JSX.Element {
  return (
    <Stroke {...props}>
      <circle cx="11" cy="11" r="7" />
      <path d="M20 20l-3.5-3.5" />
    </Stroke>
  );
}

export function SettingsIcon(props: IconProps): JSX.Element {
  return (
    <Stroke {...props}>
      <circle cx="12" cy="12" r="3" />
      <path d="M19.4 15a1.7 1.7 0 0 0 .3 1.8l.1.1a2 2 0 1 1-2.8 2.8l-.1-.1a1.7 1.7 0 0 0-1.8-.3 1.7 1.7 0 0 0-1 1.5V21a2 2 0 1 1-4 0v-.1a1.7 1.7 0 0 0-1.1-1.5 1.7 1.7 0 0 0-1.8.3l-.1.1a2 2 0 1 1-2.8-2.8l.1-.1a1.7 1.7 0 0 0 .3-1.8 1.7 1.7 0 0 0-1.5-1H3a2 2 0 1 1 0-4h.1a1.7 1.7 0 0 0 1.5-1.1 1.7 1.7 0 0 0-.3-1.8l-.1-.1a2 2 0 1 1 2.8-2.8l.1.1a1.7 1.7 0 0 0 1.8.3H9a1.7 1.7 0 0 0 1-1.5V3a2 2 0 1 1 4 0v.1a1.7 1.7 0 0 0 1 1.5 1.7 1.7 0 0 0 1.8-.3l.1-.1a2 2 0 1 1 2.8 2.8l-.1.1a1.7 1.7 0 0 0-.3 1.8V9a1.7 1.7 0 0 0 1.5 1H21a2 2 0 1 1 0 4h-.1a1.7 1.7 0 0 0-1.5 1z" />
    </Stroke>
  );
}

export function MicrophoneIcon(props: IconProps): JSX.Element {
  return (
    <Stroke {...props}>
      <path d="M12 2a3 3 0 0 0-3 3v6a3 3 0 0 0 6 0V5a3 3 0 0 0-3-3z M19 11a7 7 0 0 1-14 0 M12 18v4 M8 22h8" />
    </Stroke>
  );
}

export function TranscriptLinesIcon(props: IconProps): JSX.Element {
  return (
    <Stroke {...props}>
      <path d="M4 6h16 M4 12h10 M4 18h13" />
    </Stroke>
  );
}

export function DocumentIcon(props: IconProps): JSX.Element {
  return (
    <Stroke {...props}>
      <path d="M14 3H7a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V8z M14 3v5h5 M9 13h6 M9 17h4" />
    </Stroke>
  );
}

export function ChevronDownIcon(props: IconProps): JSX.Element {
  return (
    <Stroke strokeWidth={2} {...props}>
      <path d="M6 9l6 6 6-6" />
    </Stroke>
  );
}

export function ChevronRightIcon(props: IconProps): JSX.Element {
  return (
    <Stroke strokeWidth={2} {...props}>
      <path d="M9 6l6 6-6 6" />
    </Stroke>
  );
}

export function ChevronLeftIcon(props: IconProps): JSX.Element {
  return (
    <Stroke strokeWidth={2} {...props}>
      <path d="M15 6l-6 6 6 6" />
    </Stroke>
  );
}

export function ListIcon(props: IconProps): JSX.Element {
  return (
    <Stroke strokeWidth={2} {...props}>
      <path d="M4 6h16M4 12h16M4 18h16" />
    </Stroke>
  );
}

export function GridIcon(props: IconProps): JSX.Element {
  return (
    <Stroke strokeWidth={2} {...props}>
      <rect x="4" y="4" width="6" height="6" rx="1" />
      <rect x="14" y="4" width="6" height="6" rx="1" />
      <rect x="4" y="14" width="6" height="6" rx="1" />
      <rect x="14" y="14" width="6" height="6" rx="1" />
    </Stroke>
  );
}

export function CheckIcon(props: IconProps): JSX.Element {
  return (
    <Stroke strokeWidth={3} {...props}>
      <path d="M5 12l5 5L20 7" />
    </Stroke>
  );
}

export function MoreIcon(props: IconProps): JSX.Element {
  return (
    <svg aria-hidden="true" class={props.class} width={props.size} height={props.size} viewBox="0 0 24 24" fill="currentColor">
      <circle cx="5" cy="12" r="1.8" />
      <circle cx="12" cy="12" r="1.8" />
      <circle cx="19" cy="12" r="1.8" />
    </svg>
  );
}

export function InfoIcon(props: IconProps): JSX.Element {
  return (
    <Stroke strokeWidth={2} {...props}>
      <circle cx="12" cy="12" r="9" />
      <path d="M12 8v5 M12 16h.01" />
    </Stroke>
  );
}

export function CloseIcon(props: IconProps): JSX.Element {
  return (
    <Stroke strokeWidth={2} {...props}>
      <path d="M6 6l12 12 M18 6L6 18" />
    </Stroke>
  );
}

/** Camera glyph before a row's meta text; carries its own accessible name. */
export function VideoIcon({ size }: { size: number }): JSX.Element {
  return (
    <svg
      role="img"
      aria-label="Includes video"
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      stroke-width="2"
      stroke-linejoin="round"
      style={{ flex: 'none' }}
    >
      <rect x="3" y="6" width="13" height="12" rx="2" />
      <path d="M16 10l5-3v10l-5-3z" />
    </svg>
  );
}

/** DESIGN.md §4 type icons. Custom types (and General) use the Research glyph. */
const TYPE_PATHS: Record<string, string> = {
  meeting: 'M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2 M9 11a4 4 0 1 0 0-8 4 4 0 0 0 0 8 M22 21v-2a4 4 0 0 0-3-3.87 M16 3.13a4 4 0 0 1 0 7.75',
  interview: 'M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2 M12 11a4 4 0 1 0 0-8 4 4 0 0 0 0 8',
  lecture: 'M2 4h6a2 2 0 0 1 2 2v14a2 2 0 0 0-2-2H2z M22 4h-6a2 2 0 0 0-2 2v14a2 2 0 0 1 2-2h6z',
  presentation: 'M3 4h18v12H3z M8 21h8 M12 16v5',
  dictation: 'M12 2a3 3 0 0 0-3 3v6a3 3 0 0 0 6 0V5a3 3 0 0 0-3-3z M19 11a7 7 0 0 1-14 0 M12 18v4 M8 22h8',
  research: 'M9 3h6v4H9z M9 5H6a2 2 0 0 0-2 2v13a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V7a2 2 0 0 0-2-2h-3 M8 12h8 M8 16h5',
};

export function TypeIcon({ type, size }: { type: RecordingType; size: number }): JSX.Element {
  return (
    <Stroke size={size}>
      <path d={TYPE_PATHS[type] ?? TYPE_PATHS.research} />
    </Stroke>
  );
}

/** Settings nav glyphs, from renders/Settings.dc.html. */
export const SETTINGS_ICON_PATHS = {
  general: 'M12 3v2 M12 19v2 M3 12h2 M19 12h2 M12 8a4 4 0 1 0 0 8 4 4 0 0 0 0-8z',
  recording: 'M12 2a3 3 0 0 0-3 3v6a3 3 0 0 0 6 0V5a3 3 0 0 0-3-3z M19 11a7 7 0 0 1-14 0 M12 18v4 M8 22h8',
  transcription: 'M4 6h16 M4 12h10 M4 18h13',
  speakers:
    'M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2 M9 11a4 4 0 1 0 0-8 4 4 0 0 0 0 8 M22 21v-2a4 4 0 0 0-3-3.87 M16 3.13a4 4 0 0 1 0 7.75',
  'ai-privacy': 'M12 2l8 4v6c0 5-3.5 8.5-8 10-4.5-1.5-8-5-8-10V6z M9 12l2 2 4-4',
  documents: 'M14 3H7a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V8z M14 3v5h5 M9 13h6 M9 17h4',
  export: 'M12 3v12 M7 8l5-5 5 5 M4 15v4a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2v-4',
  storage: 'M4 6a8 3 0 1 0 16 0 8 3 0 1 0-16 0z M4 6v12a8 3 0 0 0 16 0V6 M4 12a8 3 0 0 0 16 0',
} as const;

export function PathIcon({ d, size }: { d: string; size: number }): JSX.Element {
  return (
    <Stroke size={size}>
      <path d={d} />
    </Stroke>
  );
}

/** Record and Review glyphs, from renders/Record.dc.html, Review.dc.html and AgendaImport.dc.html. */
export function FlagIcon(props: IconProps): JSX.Element {
  return (
    <Stroke {...props}>
      <path d="M5 21V4a1 1 0 0 1 1-1h11l-2 4 2 4H6" />
    </Stroke>
  );
}

export function NotesIcon(props: IconProps): JSX.Element {
  return (
    <Stroke {...props}>
      <path d="M14 3H7a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V8z M14 3v5h5 M9 13h6 M9 17h4" />
    </Stroke>
  );
}

export function DocumentPlusIcon(props: IconProps): JSX.Element {
  return (
    <Stroke strokeWidth={2} {...props}>
      <path d="M14 3H7a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V8z M14 3v5h5 M12 11v6 M9 14h6" />
    </Stroke>
  );
}

export function UploadDocumentIcon(props: IconProps): JSX.Element {
  return (
    <Stroke strokeWidth={1.5} {...props}>
      <path d="M14 3H7a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V8z M14 3v5h5 M12 18v-6 M9 15l3-3 3 3" />
    </Stroke>
  );
}

export function PlayIcon(props: IconProps): JSX.Element {
  return (
    <svg aria-hidden="true" class={props.class} width={props.size} height={props.size} viewBox="0 0 24 24" fill="currentColor">
      <path d="M8 5v14l11-7z" />
    </svg>
  );
}

export function PauseIcon(props: IconProps): JSX.Element {
  return (
    <svg aria-hidden="true" class={props.class} width={props.size} height={props.size} viewBox="0 0 24 24" fill="currentColor">
      <rect x="6" y="5" width="4" height="14" rx="1" />
      <rect x="14" y="5" width="4" height="14" rx="1" />
    </svg>
  );
}

export function StopIcon(props: IconProps): JSX.Element {
  return (
    <svg aria-hidden="true" class={props.class} width={props.size} height={props.size} viewBox="0 0 24 24" fill="#FFFFFF">
      <rect x="5" y="5" width="14" height="14" rx="3" />
    </svg>
  );
}

export function BackTenIcon(props: IconProps): JSX.Element {
  return (
    <Stroke {...props}>
      <path d="M3 12a9 9 0 1 0 3-6.7 M3 4v5h5" />
    </Stroke>
  );
}

export function ForwardTenIcon(props: IconProps): JSX.Element {
  return (
    <Stroke {...props}>
      <path d="M21 12a9 9 0 1 1-3-6.7 M21 4v5h-5" />
    </Stroke>
  );
}

export function PlusIcon(props: IconProps): JSX.Element {
  return (
    <Stroke strokeWidth={2.5} {...props}>
      <path d="M12 5v14M5 12h14" />
    </Stroke>
  );
}

export function PencilIcon(props: IconProps): JSX.Element {
  return (
    <Stroke strokeWidth={2} {...props}>
      <path d="M12 20h9 M16.5 3.5a2.1 2.1 0 0 1 3 3L7 19l-4 1 1-4z" />
    </Stroke>
  );
}

/** The six-dot drag handle (DESIGN.md §5.14). */
export function DragHandleIcon(props: IconProps): JSX.Element {
  return (
    <svg aria-hidden="true" class={props.class} width={props.size} height={props.size} viewBox="0 0 24 24" fill="currentColor">
      <circle cx="9" cy="6" r="1.6" />
      <circle cx="15" cy="6" r="1.6" />
      <circle cx="9" cy="12" r="1.6" />
      <circle cx="15" cy="12" r="1.6" />
      <circle cx="9" cy="18" r="1.6" />
      <circle cx="15" cy="18" r="1.6" />
    </svg>
  );
}
