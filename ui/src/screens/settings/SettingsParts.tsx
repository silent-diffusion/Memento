// Settings building blocks (DESIGN.md §5.16): a group label over a card list of rows. Each row has
// its label, a one-sentence description and its control at the right.
import type { ComponentChildren, JSX } from 'preact';

interface SettingsGroupProps {
  label: string;
  children: ComponentChildren;
}

export function SettingsGroup({ label, children }: SettingsGroupProps): JSX.Element {
  return (
    <div class="settings-group">
      <h2 class="lbl settings-group-label">{label}</h2>
      <div class="settings-card">{children}</div>
    </div>
  );
}

interface SettingsRowProps {
  label: string;
  description: string;
  /** The control (toggle, segmented, select, path + Change, plain value). */
  children?: ComponentChildren;
  /** Full-width content beneath the label: a checklist, or an inline message. */
  below?: ComponentChildren;
  /** "Available in a later version", shown before a disabled control. */
  note?: string;
}

export function SettingsRow({ label, description, children, below, note }: SettingsRowProps): JSX.Element {
  return (
    <div class="settings-row">
      <div class="settings-row-text">
        <span class="settings-row-label">{label}</span>
        <span class="settings-row-desc">{description}</span>
      </div>
      {children === undefined && note === undefined ? null : (
        <div class="settings-row-control">
          {note === undefined ? null : <span class="settings-note">{note}</span>}
          {children}
        </div>
      )}
      {below}
    </div>
  );
}

/** The On/Off word that sits before a toggle. */
export function OnOff({ on }: { on: boolean }): JSX.Element {
  return <span class="settings-note">{on ? 'On' : 'Off'}</span>;
}

export const LATER = 'Available in a later version';

/** A section that is not built yet: says so plainly instead of showing controls that do nothing. */
export function LaterCard(): JSX.Element {
  return (
    <div class="settings-card settings-later">
      <span class="settings-row-label">{LATER}.</span>
      <span class="settings-row-desc">These settings arrive with the features they control. Nothing here is in effect yet.</span>
    </div>
  );
}
