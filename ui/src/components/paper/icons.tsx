// The M4 screens' glyphs, copied from renders/Builder.dc.html and DocView.dc.html (24x24,
// currentColor, round caps and joins).
import type { JSX } from 'preact';

interface IconProps {
  size: number;
  class?: string;
}

function Glyph({ size, class: className, strokeWidth, children }: IconProps & { strokeWidth: number; children: JSX.Element | JSX.Element[] }): JSX.Element {
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

/** The trailing arrow of Generate. */
export function ArrowRightIcon(props: IconProps): JSX.Element {
  return (
    <Glyph strokeWidth={2} {...props}>
      <path d="M5 12h14 M13 6l6 6-6 6" />
    </Glyph>
  );
}

export function RegenerateIcon(props: IconProps): JSX.Element {
  return (
    <Glyph strokeWidth={1.75} {...props}>
      <path d="M3 12a9 9 0 1 0 3-6.7 M3 4v5h5" />
    </Glyph>
  );
}

export function SmallUpIcon(props: IconProps): JSX.Element {
  return (
    <Glyph strokeWidth={2.25} {...props}>
      <path d="M6 15l6-6 6 6" />
    </Glyph>
  );
}

export function SmallDownIcon(props: IconProps): JSX.Element {
  return (
    <Glyph strokeWidth={2.25} {...props}>
      <path d="M6 9l6 6 6-6" />
    </Glyph>
  );
}

export function SmallCloseIcon(props: IconProps): JSX.Element {
  return (
    <Glyph strokeWidth={2.25} {...props}>
      <path d="M6 6l12 12M18 6L6 18" />
    </Glyph>
  );
}

export function BulletedListIcon(props: IconProps): JSX.Element {
  return (
    <Glyph strokeWidth={2} {...props}>
      <path d="M9 6h11M9 12h11M9 18h11" />
      <circle cx="4.5" cy="6" r="1.2" fill="currentColor" />
      <circle cx="4.5" cy="12" r="1.2" fill="currentColor" />
      <circle cx="4.5" cy="18" r="1.2" fill="currentColor" />
    </Glyph>
  );
}

export function NumberedListIcon(props: IconProps): JSX.Element {
  return (
    <Glyph strokeWidth={2} {...props}>
      <path d="M10 6h10M10 12h10M10 18h10M4 5l1.5-1v5M4 14.5h2.5l-2.5 3H7" />
    </Glyph>
  );
}

export function TableIcon(props: IconProps): JSX.Element {
  return (
    <Glyph strokeWidth={2} {...props}>
      <rect x="3" y="4" width="18" height="16" rx="2" />
      <path d="M3 10h18M9 4v16" />
    </Glyph>
  );
}

export function ClockIcon(props: IconProps): JSX.Element {
  return (
    <Glyph strokeWidth={2} {...props}>
      <circle cx="12" cy="12" r="9" />
      <path d="M12 7v5l3 2" />
    </Glyph>
  );
}

/** The §17 failure mark: a circle with an exclamation. */
export function AlertIcon(props: IconProps): JSX.Element {
  return (
    <Glyph strokeWidth={2} {...props}>
      <circle cx="12" cy="12" r="9" />
      <path d="M12 7.5v5.5 M12 16.5v.01" />
    </Glyph>
  );
}
