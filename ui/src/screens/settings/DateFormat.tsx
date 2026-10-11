// 2.1, Settings › General › Date format (DESIGN.md §11): five presets, each shown as the date it
// writes. It drives every full date (new titles, lists, Review, History); times are unchanged.
import type { JSX } from 'preact';
import { useState } from 'preact/hooks';
import type { DateFormat } from '../../bridge/types';
import { SelectMenu } from '../../components/Menus';
import { DATE_FORMAT_OPTIONS, formatFullDate, untitledTitle } from '../../format/dates';
import { updateSettings } from '../../state/actions';
import { useServices } from '../../state/context';
import { SettingsRow } from './SettingsParts';

/** The row's description: today in the chosen format, as a new title would carry it. */
export function dateFormatText(format: DateFormat, today: Date): string {
  return `New recordings are titled like "${untitledTitle('meeting', today, format)}". Used for every full date; times are unchanged.`;
}

export function DateFormatRow({ today = new Date() }: { today?: Date }): JSX.Element | null {
  const services = useServices();
  const settings = services.store.settings.value;
  const [error, setError] = useState<string | null>(null);
  if (settings === null) {
    return null;
  }
  const format = settings.general.dateFormat;
  return (
    <SettingsRow
      label="Date format"
      description={dateFormatText(format, today)}
      below={
        error === null ? null : (
          <p class="settings-inline" role="alert">
            {error}
          </p>
        )
      }
    >
      <SelectMenu<DateFormat>
        label="Date format"
        value={format}
        // "Windows setting" shows what it gives on this PC.
        options={DATE_FORMAT_OPTIONS.map((o) => (o.value === 'windows' ? { ...o, label: `Windows setting (${formatFullDate(today, 'windows')})` } : o))}
        onChange={(next) => {
          setError(null);
          void updateSettings(services, { general: { dateFormat: next } }).then(setError);
        }}
      />
    </SettingsRow>
  );
}
