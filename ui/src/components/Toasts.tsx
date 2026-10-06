import type { JSX } from 'preact';
import type { ToastQueue } from '../state/toasts';

/** Bottom-right toast stack (DESIGN.md §5.19, §17). Newest on top; hover or focus pauses the 6 s timer. */
export function ToastStack({ queue }: { queue: ToastQueue }): JSX.Element {
  const toasts = queue.items.value;
  return (
    <div class="toast-stack" aria-live="polite" aria-relevant="additions">
      {toasts.map((toast) => (
        <div
          key={toast.id}
          class="toast"
          role={toast.tone === 'danger' ? 'alert' : 'status'}
          onMouseEnter={() => {
            queue.pause(toast.id);
          }}
          onMouseLeave={() => {
            queue.resume(toast.id);
          }}
          onFocusIn={() => {
            queue.pause(toast.id);
          }}
          onFocusOut={(event) => {
            const next = event.relatedTarget;
            if (!(next instanceof Node) || !event.currentTarget.contains(next)) {
              queue.resume(toast.id);
            }
          }}
        >
          <span class={toast.tone === 'danger' ? 'toast-dot toast-dot--danger' : 'toast-dot'} aria-hidden="true" />
          <div class="toast-content">
            <div class="toast-text">
              <span class="toast-title">{toast.title}</span>
              <span class="toast-body">{toast.body}</span>
            </div>
            {toast.actions.length === 0 ? null : (
              <div class="toast-actions">
                {toast.actions.map((action) => (
                  <button
                    key={action.label}
                    class={action.quiet === true ? 'btn toast-quiet' : 'btn g'}
                    type="button"
                    onClick={() => {
                      queue.dismiss(toast.id);
                      action.run();
                    }}
                  >
                    {action.label}
                  </button>
                ))}
              </div>
            )}
          </div>
        </div>
      ))}
    </div>
  );
}
