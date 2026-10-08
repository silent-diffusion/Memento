// The Builder's template list (DESIGN.md §10 header, 1.1.0): the built-in templates first, then the ones you saved,
// read from templates.list and again whenever the host says templates changed. Choosing one opens it in the Builder;
// with changes that are not saved, the chooser asks first.
import type { JSX } from 'preact';
import { useEffect, useState } from 'preact/hooks';
import type { Template } from '../../bridge/types';
import { SelectMenu, type SelectOption } from '../../components/Menus';
import { useServices } from '../../state/context';
import './templateChooser.css';

function messageOf(error: unknown): string {
  return error instanceof Error ? error.message : 'Memento did not answer.';
}

export const BUILT_IN_GROUP = 'Built in';
export const SAVED_GROUP = 'Your templates';

/** The options in list order: built-ins, then your own (templates.list already sorts them so). */
export function templateOptions(templates: readonly Template[], currentId: string | null, currentName: string): SelectOption<string>[] {
  const options = templates.map((t) => ({ value: t.id, label: t.name, group: t.builtIn ? BUILT_IN_GROUP : SAVED_GROUP }));
  const builtIns = options.filter((o) => o.group === BUILT_IN_GROUP);
  const saved = options.filter((o) => o.group === SAVED_GROUP);
  // A template that is open but no longer listed (deleted meanwhile) stays visible as the choice.
  const missing = currentId !== null && !templates.some((t) => t.id === currentId) ? [{ value: currentId, label: currentName, group: SAVED_GROUP }] : [];
  return [...builtIns, ...saved, ...missing];
}

interface TemplateChooserProps {
  /** The id of the template in the Builder, or null while it opens. */
  currentId: string | null;
  currentName: string;
  /** The arrangement has changes that are not saved. */
  dirty: boolean;
  disabled: boolean;
  onOpen: (template: Template) => void;
}

export function TemplateChooser({ currentId, currentName, dirty, disabled, onOpen }: TemplateChooserProps): JSX.Element {
  const { bridge } = useServices();
  const [templates, setTemplates] = useState<Template[]>([]);
  const [reload, setReload] = useState(0);
  const [pending, setPending] = useState<{ id: string; name: string } | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let live = true;
    bridge
      .call('templates.list')
      .then((result) => {
        if (live) {
          setTemplates(result.templates);
        }
      })
      .catch((e: unknown) => {
        if (live) {
          setError(`The templates could not be listed. ${messageOf(e)}`);
        }
      });
    return () => {
      live = false;
    };
  }, [bridge, reload, currentId]);
  useEffect(() => bridge.on('templates.changed', () => { setReload((n) => n + 1); }), [bridge]);

  const open = (id: string): void => {
    setPending(null);
    setError(null);
    bridge
      .call('templates.get', { templateId: id })
      .then(onOpen)
      .catch((e: unknown) => {
        setError(`That template could not be opened. ${messageOf(e)} The arrangement you had is still here.`);
      });
  };

  const options = templateOptions(templates, currentId, currentName);
  return (
    <div class="tpl-chooser">
      <SelectMenu<string>
        label="Template"
        variant="chip"
        value={currentId ?? ''}
        options={options}
        disabled={disabled || options.length === 0}
        onChange={(id) => {
          if (dirty) {
            setPending({ id, name: options.find((o) => o.value === id)?.label ?? id });
          } else {
            open(id);
          }
        }}
      />
      {pending === null ? null : (
        <div class="popover tpl-confirm" role="alertdialog" aria-labelledby="tpl-confirm-text">
          <p id="tpl-confirm-text" class="tpl-confirm-text">
            Open “{pending.name}”? The changes to “{currentName}” are not saved and will be lost. Save template keeps them.
          </p>
          <span class="tpl-confirm-actions">
            <button
              class="btn ghost small-btn"
              type="button"
              ref={(el) => el?.focus()}
              onClick={() => {
                setPending(null);
              }}
            >
              Keep editing
            </button>
            <button
              class="btn ghost small-btn"
              type="button"
              onClick={() => {
                open(pending.id);
              }}
            >
              Open {pending.name}
            </button>
          </span>
        </div>
      )}
      {error === null ? null : (
        <p class="popover tpl-confirm tpl-error" role="alert">
          {error}{' '}
          <button
            class="btn link-btn"
            type="button"
            onClick={() => {
              setError(null);
            }}
          >
            Dismiss
          </button>
        </p>
      )}
    </div>
  );
}
