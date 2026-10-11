// Review › More › Reprocess › Rebuild the mix (after 2.0.0-beta.1, BRIDGE.md "Even out source levels"): with evened
// levels when the mix sums the tracks as recorded, and as recorded once it was evened, which is the way back.
import type { Project } from '../../bridge/types';
import type { MenuAction } from '../../components/Menus';

export const REBUILD_EVENED = 'Rebuild the mix with evened levels';
export const REBUILD_AS_RECORDED = 'Rebuild the mix as recorded';

/** Why the rebuild is not offered now, or null when it is. */
function unavailable(project: Project | null, inProgress: boolean, rebuilding: boolean): string | null {
  if (project === null) {
    return 'Opening the recording';
  }
  if (inProgress) {
    return 'Once the recording is stored';
  }
  if (project.mixOnly !== null) {
    return 'Only the mix was kept';
  }
  return rebuilding ? 'Rebuilding the mix now' : null;
}

export function rebuildMixAction(project: Project | null, inProgress: boolean, rebuilding: boolean, run: (evenLevels: boolean) => void): MenuAction {
  const evenLevels = project?.mixLevels == null;
  const note = unavailable(project, inProgress, rebuilding);
  return {
    label: evenLevels ? REBUILD_EVENED : REBUILD_AS_RECORDED,
    disabled: note !== null,
    ...(note === null ? {} : { note }),
    run: () => {
      run(evenLevels);
    },
  };
}
