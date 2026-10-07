// Style editor (DESIGN.md §13), transcribed from design/renders/StyleEditor.dc.html: every style
// setting in four cards beside the host's sample page, re-rendered on every change. A built-in
// style stays as it is: saving it makes a copy (said inline).
import type { JSX } from 'preact';
import { useEffect, useRef, useState } from 'preact/hooks';
import type { HeadingColor, Style, StyleSettings } from '../../bridge/types';
import { Segmented, Toggle } from '../../components/Controls';
import { InfoIcon } from '../../components/icons';
import { moveFocus } from '../../components/keyboard';
import { Dialog } from '../../components/Overlay';
import { Paper } from '../../components/paper/Paper';
import { SpokeHeader } from '../../components/SpokeHeader';
import { paperName } from '../../format/documents';
import { useServices } from '../../state/context';
import { SETTINGS_RETURN, styleReturnOf } from './styleReturn';
import './styleeditor.css';

/** The sample page is asked for once changes pause this long. */
export const SAMPLE_DEBOUNCE_MS = 150;

export const SWATCHES: readonly { value: HeadingColor; name: string; hex: string }[] = [
  { value: 'navy', name: 'Navy', hex: '#1F3A5F' },
  { value: 'ink', name: 'Ink', hex: '#1D1C1A' },
  { value: 'forest', name: 'Forest', hex: '#2F6B4F' },
  { value: 'burgundy', name: 'Burgundy', hex: '#7A2E2E' },
];

const FACES = [
  { value: 'sans', label: 'Sans' },
  { value: 'serif', label: 'Serif' },
] as const;

function messageOf(error: unknown): string {
  return error instanceof Error ? error.message : 'Memento did not answer.';
}

const same = (a: StyleSettings, b: StyleSettings): boolean => (Object.keys(a) as (keyof StyleSettings)[]).every((key) => a[key] === b[key]);

function Row({ label, sub, children }: { label: string; sub?: string; children: JSX.Element | JSX.Element[] }): JSX.Element {
  return (
    <div class="style-row">
      {sub === undefined ? (
        <span class="style-row-label">{label}</span>
      ) : (
        <span class="style-row-text">
          <span class="style-row-label">{label}</span>
          <span class="style-row-sub">{sub}</span>
        </span>
      )}
      {children}
    </div>
  );
}

export function StyleEditorScreen({ styleId }: { styleId: string }): JSX.Element {
  const services = useServices();
  const { bridge, store, router } = services;
  const back = styleReturnOf(store).current ?? SETTINGS_RETURN;
  const [style, setStyle] = useState<Style | null>(null);
  const [settings, setSettings] = useState<StyleSettings | null>(null);
  const [name, setName] = useState('');
  const [html, setHtml] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [sampleError, setSampleError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [leaving, setLeaving] = useState(false);

  useEffect(() => {
    let live = true;
    bridge
      .call('styles.get', { styleId })
      .then((s) => {
        if (live) {
          setStyle(s);
          setSettings(s.settings);
          setName(s.name);
          setError(null);
        }
      })
      .catch((e: unknown) => {
        if (live) {
          setError(messageOf(e));
        }
      });
    return () => {
      live = false;
    };
  }, [bridge, styleId]);

  // The sample page in the settings as they stand, after a short pause in changes.
  const sequence = useRef(0);
  const settingsKey = settings === null ? null : JSON.stringify(settings);
  useEffect(() => {
    if (settings === null) {
      return undefined;
    }
    const mine = ++sequence.current;
    const timer = setTimeout(() => {
      bridge
        .call('styles.sampleHtml', { settings })
        .then(({ html: sample }) => {
          if (mine === sequence.current) {
            setHtml(sample);
            setSampleError(null);
          }
        })
        .catch((e: unknown) => {
          if (mine === sequence.current) {
            setSampleError(`The sample page could not be drawn. ${messageOf(e)}`);
          }
        });
    }, SAMPLE_DEBOUNCE_MS);
    return () => {
      clearTimeout(timer);
    };
  }, [bridge, settingsKey]);

  const dirty = style !== null && settings !== null && (!same(style.settings, settings) || name.trim() !== style.name);
  const set = <K extends keyof StyleSettings>(key: K, value: StyleSettings[K]): void => {
    setSettings((s) => (s === null ? s : { ...s, [key]: value }));
  };

  const leave = (): void => {
    router.navigate(back.route);
  };

  const saved = (next: Style, copied: boolean): void => {
    back.onSaved?.(next);
    setStyle(next);
    setSettings(next.settings);
    setName(next.name);
    if (copied) {
      store.toasts.show({ tone: 'ok', title: `Saved as “${next.name}”`, body: `${style?.name ?? 'The built-in style'} is built in and stays as it is.` });
      router.navigate({ name: 'style', styleId: next.id });
    } else {
      store.toasts.show({ tone: 'ok', title: `Style “${next.name}” saved`, body: 'Documents in this style look like the sample from now on.' });
    }
  };

  const save = (): void => {
    if (style === null || settings === null || busy) {
      return;
    }
    setBusy(true);
    bridge
      .call('styles.save', { style: { ...style, name: name.trim() === '' ? style.name : name.trim(), settings } })
      .then((next) => {
        setBusy(false);
        saved(next, next.id !== style.id);
      })
      .catch((e: unknown) => {
        setBusy(false);
        store.toasts.show({ tone: 'danger', title: 'The style was not saved', body: `${messageOf(e)} Your changes are still here.` });
      });
  };

  const duplicate = (): void => {
    if (style === null || settings === null || busy) {
      return;
    }
    setBusy(true);
    bridge
      .call('styles.duplicate', { styleId: style.id })
      .then((copy) => (dirty ? bridge.call('styles.save', { style: { ...copy, settings } }) : copy))
      .then((copy) => {
        setBusy(false);
        back.onSaved?.(copy);
        store.toasts.show({ tone: 'ok', title: `“${copy.name}” made`, body: `A copy of ${style.name} to change as you like.` });
        router.navigate({ name: 'style', styleId: copy.id });
      })
      .catch((e: unknown) => {
        setBusy(false);
        store.toasts.show({ tone: 'danger', title: 'The style was not duplicated', body: `${messageOf(e)} Nothing was changed.` });
      });
  };

  const reset = (): void => {
    if (style !== null) {
      setSettings(style.settings);
      setName(style.name);
    }
  };

  const used = style?.usedByTemplates ?? 0;
  const s = settings;

  return (
    <>
      <SpokeHeader
        backLabel={back.label}
        onBack={() => {
          if (dirty) {
            setLeaving(true);
          } else {
            leave();
          }
        }}
        titleEditor={
          <>
          <label class="sr" for="style-name">
            Style name
          </label>
          <input
            id="style-name"
            class="field rec-title-input style-name"
            type="text"
            value={name}
            maxLength={60}
            autocomplete="off"
            disabled={style === null}
            onInput={(event) => {
              setName(event.currentTarget.value);
            }}
          />
          <span class="pill done">
            Style · used by {used} {used === 1 ? 'template' : 'templates'}
          </span>
          </>
        }
        actions={
          <>
          <button class="btn ghost spoke-ghost" type="button" disabled={style === null || busy} onClick={duplicate}>
            Duplicate
          </button>
          <button class="btn ghost spoke-ghost" type="button" disabled={!dirty} onClick={reset}>
            Reset
          </button>
          <button class="btn primary spoke-primary" type="button" disabled={style === null || busy || (!dirty && !style.builtIn)} onClick={save}>
            Save style
          </button>
          </>
        }
      />

      {error !== null ? (
        <main class="spoke-main">
          <div class="placeholder-card">
            <p class="placeholder-text" role="alert">
              {error}
            </p>
          </div>
        </main>
      ) : (
        <main class="style-main">
          <div class="style-layout">
            <section class="style-settings" aria-label="Style settings">
              {style?.builtIn === true ? (
                <p class="style-builtin" role="note">
                  <InfoIcon size={16} class="style-builtin-icon" />
                  <span>
                    <span class="style-builtin-lead">{style.name} is built in.</span> Save keeps it as it is and saves your changes as a new style of your own.
                  </span>
                </p>
              ) : null}
              {s === null ? (
                <p class="style-loading">Opening the style…</p>
              ) : (
                <>
                  <div class="style-card">
                    <div class="style-card-head">
                      <span class="lbl">Type</span>
                    </div>
                    <Row label="Headings">
                      <Segmented label="Heading typeface" value={s.headingFace} options={FACES} onChange={(v) => { set('headingFace', v); }} />
                    </Row>
                    <Row label="Body">
                      <Segmented label="Body typeface" value={s.bodyFace} options={FACES} onChange={(v) => { set('bodyFace', v); }} />
                    </Row>
                    <Row label="Base size">
                      <Segmented
                        label="Base size"
                        value={s.baseSize}
                        options={[
                          { value: 'small', label: 'Small' },
                          { value: 'normal', label: 'Normal' },
                          { value: 'large', label: 'Large' },
                        ]}
                        onChange={(v) => { set('baseSize', v); }}
                      />
                    </Row>
                    <Row label="Heading case">
                      <Segmented
                        label="Heading case"
                        value={s.headingCase}
                        options={[
                          { value: 'normal', label: 'Normal' },
                          { value: 'smallCaps', label: 'Small caps' },
                        ]}
                        onChange={(v) => { set('headingCase', v); }}
                      />
                    </Row>
                    <Row label="Numbered headings">
                      <Toggle label="Numbered headings" checked={s.numberedHeadings} onChange={(v) => { set('numberedHeadings', v); }} />
                    </Row>
                  </div>

                  <div class="style-card">
                    <div class="style-card-head">
                      <span class="lbl">Colour</span>
                    </div>
                    <Row label="Headings and rules">
                      <div
                        class="swatches"
                        role="group"
                        aria-label="Heading colour"
                        onKeyDown={(event) => {
                          moveFocus(event, event.currentTarget, '.sw', 'horizontal');
                        }}
                      >
                        {SWATCHES.map((c) => (
                          <button
                            key={c.value}
                            class={s.headingColor === c.value ? 'sw on' : 'sw'}
                            type="button"
                            aria-label={c.name}
                            aria-pressed={s.headingColor === c.value}
                            style={{ background: c.hex }}
                            onClick={() => {
                              set('headingColor', c.value);
                            }}
                          />
                        ))}
                      </div>
                    </Row>
                    <Row label="Body text">
                      <span class="style-ink">
                        <span class="style-ink-dot" aria-hidden="true" />
                        Ink
                      </span>
                    </Row>
                    <Row label="Table header fill">
                      <Toggle label="Table header fill" checked={s.tableHeaderFill} onChange={(v) => { set('tableHeaderFill', v); }} />
                    </Row>
                  </div>

                  <div class="style-card">
                    <div class="style-card-head">
                      <span class="lbl">Structure</span>
                    </div>
                    <Row label="Rule under the title">
                      <Toggle label="Rule under the title" checked={s.ruleUnderTitle} onChange={(v) => { set('ruleUnderTitle', v); }} />
                    </Row>
                    <Row label="Lines between sections">
                      <Toggle label="Lines between sections" checked={s.linesBetweenSections} onChange={(v) => { set('linesBetweenSections', v); }} />
                    </Row>
                    <Row label="Spacing">
                      <Segmented
                        label="Spacing"
                        value={s.spacing}
                        options={[
                          { value: 'tight', label: 'Tight' },
                          { value: 'normal', label: 'Normal' },
                          { value: 'airy', label: 'Airy' },
                        ]}
                        onChange={(v) => { set('spacing', v); }}
                      />
                    </Row>
                  </div>

                  <div class="style-card">
                    <div class="style-card-head">
                      <span class="lbl">Page</span>
                    </div>
                    <Row label="Paper">
                      <Segmented
                        label="Paper"
                        value={s.paper}
                        options={[
                          { value: 'letter', label: 'Letter' },
                          { value: 'a4', label: 'A4' },
                        ]}
                        onChange={(v) => { set('paper', v); }}
                      />
                    </Row>
                    <Row label="Page numbers">
                      <Toggle label="Page numbers" checked={s.pageNumbers} onChange={(v) => { set('pageNumbers', v); }} />
                    </Row>
                    <Row label="Running header" sub="Recording title and date on every page">
                      <Toggle label="Running header" checked={s.runningHeader} onChange={(v) => { set('runningHeader', v); }} />
                    </Row>
                  </div>
                </>
              )}
              <p class="style-foot">A style changes how a document looks, never what it says. Exports to Word and PDF follow it; Markdown ignores it.</p>
            </section>

            <section class="style-preview" aria-label="Preview">
              <div class="style-preview-caption">
                <span>
                  Preview · sample minutes · <span class="style-preview-strong">{paperName(s?.paper ?? 'letter')}</span>
                </span>
                <span class="style-preview-hint">Updates as you change settings</span>
              </div>
              {sampleError === null ? (
                <Paper html={html} label="Sample minutes in this style" class="style-paper" loadingText="Drawing the sample page…" />
              ) : (
                <p class="style-loading" role="alert">
                  {sampleError}
                </p>
              )}
            </section>
          </div>
        </main>
      )}

      {leaving ? (
        <Dialog
          titleId="leave-style-title"
          title={`Leave ${style?.name ?? 'the style'} without saving?`}
          onEscape={() => {
            setLeaving(false);
          }}
          actions={
            <>
              <button
                class="btn g"
                type="button"
                data-autofocus
                onClick={() => {
                  setLeaving(false);
                }}
              >
                Keep editing
              </button>
              <button
                class="btn p"
                type="button"
                onClick={() => {
                  setLeaving(false);
                  leave();
                }}
              >
                Discard changes
              </button>
            </>
          }
        >
          <p class="dialog-body">The changes you made here are not saved. The style stays as it was, and so do the documents that use it.</p>
        </Dialog>
      ) : null}
    </>
  );
}
