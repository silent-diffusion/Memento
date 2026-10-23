// Back from the Builder with unsaved template changes (2.4.0, usability review R1 option A): ask, as switching templates
// and the Style editor do. Keep editing is the default and Esc; Save template saves through the header's save path and
// then leaves; Leave without saving drops the changes (the saved template stays as it was) and leaves.
import type { JSX } from 'preact';
import { Dialog } from '../../components/Overlay';

interface LeaveTemplateDialogProps {
  /** The template's name as it stands in the Builder (possibly renamed and unsaved). */
  templateName: string;
  /** Save template is running: the choices wait for it. */
  saving: boolean;
  onKeep: () => void;
  onSave: () => void;
  onLeave: () => void;
}

export function LeaveTemplateDialog({ templateName, saving, onKeep, onSave, onLeave }: LeaveTemplateDialogProps): JSX.Element {
  const name = templateName.trim() === '' ? 'this template' : `“${templateName.trim()}”`;
  return (
    <Dialog
      titleId="leave-template-title"
      title="Leave without saving the template?"
      onEscape={onKeep}
      actions={
        <>
          <button class="btn g" type="button" data-autofocus disabled={saving} onClick={onKeep}>
            Keep editing
          </button>
          <button class="btn g" type="button" disabled={saving} aria-busy={saving} onClick={onSave}>
            {saving ? 'Saving…' : 'Save template'}
          </button>
          <button class="btn p" type="button" disabled={saving} onClick={onLeave}>
            Leave without saving
          </button>
        </>
      }
    >
      <p class="dialog-body">
        Your changes to {name} are only in this document builder. Save template keeps them for every recording; leaving without saving keeps the template as it was.
      </p>
    </Dialog>
  );
}
