// The ghost button sizes of DESIGN.md §2.3 (2.4.0), for components that build a ghost's class list (the select
// menus). Markup that writes its class by hand uses the same names: `btn ghost` (36 px, toolbar), `btn ghost ghost-row`
// (28 px, a secondary action inside a card or a list row), `btn ghost ghost-main` (32 px, the main action of a card or
// row) and `btn ghost ghost-stage` (48 px, the Recording stage and the Library's centred states). base.css draws them.

export type GhostSize = 'row' | 'main' | 'toolbar' | 'stage';

/** The size modifier class for `btn ghost`, or '' for the 36 px toolbar default. */
export function ghostSizeClass(size: GhostSize): string {
  switch (size) {
    case 'row':
      return 'ghost-row';
    case 'main':
      return 'ghost-main';
    case 'stage':
      return 'ghost-stage';
    case 'toolbar':
      return '';
  }
}

/** `base` plus the size modifier, without a trailing space for the default. */
export function withGhostSize(base: string, size: GhostSize): string {
  const modifier = ghostSizeClass(size);
  return modifier === '' ? base : `${base} ${modifier}`;
}
