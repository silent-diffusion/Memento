// The Follow-up email module's own settings on its expanded card (2.0, DESIGN.md §10.2): the tone of
// the fixed sentences (greeting, opening, closing) and the name it is signed with. What the email says
// (recap, decisions, action items with owners) comes from the transcript whatever the tone.
import type { JSX } from 'preact';
import type { EmailModuleSettings, EmailTone, ModuleSettings } from '../../bridge/types';
import { Segmented } from '../../components/Controls';

export const EMAIL_TONES: readonly { value: EmailTone; label: string }[] = [
  { value: 'neutral', label: 'Neutral' },
  { value: 'warm', label: 'Warm' },
  { value: 'brief', label: 'Brief' },
];

/** What each tone changes, under the control. */
export const TONE_NOTES: Record<EmailTone, string> = {
  neutral: 'Opens with “Hello”, closes with “Best regards”.',
  warm: 'Opens with “Hi”, thanks everyone for their time, closes with “Warm regards”.',
  brief: 'A short greeting, no summary heading and no closing line, signed “Thanks”.',
};

export const DEFAULT_EMAIL: EmailModuleSettings = { tone: 'neutral', signOff: null };

/** The longest sign-off name the host accepts. */
export const MAX_SIGN_OFF = 80;

/** The module's settings, or the defaults the host applies when it has none. */
export function emailOf(module: ModuleSettings): EmailModuleSettings {
  return module.email ?? DEFAULT_EMAIL;
}

interface EmailOptionsProps {
  module: ModuleSettings;
  name: string;
  update: (patch: Partial<Omit<ModuleSettings, 'id' | 'module'>>) => void;
}

export function EmailOptions({ module, name, update }: EmailOptionsProps): JSX.Element {
  const email = emailOf(module);
  const noteId = `email-tone-note-${module.id}`;
  return (
    <div class="mod-email" role="group" aria-label={`${name} settings`}>
      <div class="mod-option mod-option--wide">
        <span class="mod-option-label">Tone</span>
        <Segmented<EmailTone>
          label={`Tone of ${name}`}
          value={email.tone}
          options={EMAIL_TONES}
          onChange={(tone) => {
            update({ email: { ...email, tone } });
          }}
        />
      </div>
      <p class="mod-note" id={noteId}>
        {TONE_NOTES[email.tone]}
      </p>
      <label class="mod-field">
        <span class="lbl">Sign off as</span>
        <input
          class="field mod-input"
          type="text"
          value={email.signOff ?? ''}
          maxLength={MAX_SIGN_OFF}
          placeholder="Your name (optional)"
          aria-describedby={noteId}
          onInput={(event) => {
            // One line: a pasted line break becomes a space, as the host refuses more than one line.
            const value = event.currentTarget.value.replace(/[\r\n]+/g, ' ');
            update({ email: { ...email, signOff: value.trim() === '' ? null : value } });
          }}
        />
      </label>
    </div>
  );
}
