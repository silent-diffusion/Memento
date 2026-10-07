import { describe, expect, it } from 'vitest';
import { sampleModels } from '../bridge/mockModels';
import type { ModelsProgressPayload } from '../bridge/types';
import { INITIAL_MODELS, isBusy, modelsReducer, phaseOf, type ModelsAction, type ModelsState } from './models';

const run = (actions: ModelsAction[], start: ModelsState = INITIAL_MODELS): ModelsState => actions.reduce(modelsReducer, start);

const MEDIUM = 'whisper-medium';

const progress = (state: ModelsProgressPayload['state'], percent: number, message: string | null = null): ModelsAction => ({
  type: 'progress',
  payload: { modelId: MEDIUM, percent, bytesDone: percent * 10, bytesTotal: 1000, state, message },
});

describe('model install state machine', () => {
  const loaded = run([{ type: 'loaded', models: sampleModels() }]);

  it('loads the catalog idle, picking up a download already running', () => {
    expect(loaded.loaded).toBe(true);
    expect(phaseOf(loaded, MEDIUM)).toEqual({ kind: 'idle' });
    const running = sampleModels().map((m) => (m.id === MEDIUM ? { ...m, installing: { percent: 30, bytesDone: 300 } } : m));
    expect(phaseOf(run([{ type: 'loaded', models: running }]), MEDIUM)).toMatchObject({ kind: 'downloading', percent: 30 });
  });

  it('goes install → starting → downloading → verifying → done, then is installed', () => {
    const steps: ModelsAction[] = [{ type: 'installRequested', modelId: MEDIUM }, progress('downloading', 4), progress('downloading', 48)];
    const mid = run(steps, loaded);
    expect(phaseOf(mid, MEDIUM)).toEqual({ kind: 'downloading', percent: 48, bytesDone: 480, bytesTotal: 1000 });
    expect(isBusy(mid)).toBe(true);
    expect(mid.models.find((m) => m.id === MEDIUM)?.installing).toEqual({ percent: 48, bytesDone: 480 });
    const verifying = run([progress('verifying', 100)], mid);
    expect(phaseOf(verifying, MEDIUM)).toEqual({ kind: 'verifying' });
    const done = run([progress('done', 100)], verifying);
    expect(phaseOf(done, MEDIUM)).toEqual({ kind: 'idle' });
    expect(done.models.find((m) => m.id === MEDIUM)).toMatchObject({ installed: true, installing: null });
    expect(isBusy(done)).toBe(false);
  });

  it('cancels: neither a late progress event nor the host\'s "cancelled" failure brings the download back', () => {
    const cancelling = run([{ type: 'installRequested', modelId: MEDIUM }, progress('downloading', 20), { type: 'cancelRequested', modelId: MEDIUM }], loaded);
    expect(phaseOf(cancelling, MEDIUM)).toEqual({ kind: 'cancelling' });
    const late = run([progress('downloading', 24)], cancelling);
    expect(phaseOf(late, MEDIUM)).toEqual({ kind: 'cancelling' });
    // The host reports a cancelled download as failed with a message, before the call answers.
    const reported = run([progress('failed', 0, 'The download of Medium was cancelled; the partial file was removed.')], late);
    expect(phaseOf(reported, MEDIUM)).toEqual({ kind: 'cancelling' });
    const cancelled = run([{ type: 'cancelled', modelId: MEDIUM }], reported);
    expect(phaseOf(cancelled, MEDIUM)).toEqual({ kind: 'idle' });
    expect(cancelled.models.find((m) => m.id === MEDIUM)).toMatchObject({ installed: false, installing: null });
    // Even arriving after the answer, a failure for a download that is not running is not news.
    expect(phaseOf(run([progress('failed', 0, 'cancelled')], cancelled), MEDIUM)).toEqual({ kind: 'idle' });
  });

  it('keeps the host message when a download fails part-way or is refused', () => {
    const failed = run([{ type: 'installRequested', modelId: MEDIUM }, progress('downloading', 40), progress('failed', 44, 'The connection was lost.')], loaded);
    expect(phaseOf(failed, MEDIUM)).toEqual({ kind: 'failed', message: 'The connection was lost.', code: 'models.downloadFailed' });
    expect(isBusy(failed)).toBe(false);
    const refused = run([{ type: 'installRefused', modelId: 'whisper-base', code: 'models.noSpace', message: 'Base needs 142 MB.' }], loaded);
    expect(phaseOf(refused, 'whisper-base')).toEqual({ kind: 'failed', message: 'Base needs 142 MB.', code: 'models.noSpace' });
    // A failure survives a reload of the list until dismissed.
    const reloaded = run([{ type: 'loaded', models: sampleModels() }], refused);
    expect(phaseOf(reloaded, 'whisper-base').kind).toBe('failed');
    expect(phaseOf(run([{ type: 'dismiss', modelId: 'whisper-base' }], reloaded), 'whisper-base')).toEqual({ kind: 'idle' });
  });

  it('removes a model, or keeps it with the host reason', () => {
    const removed = run([{ type: 'removeRequested', modelId: 'whisper-small' }, { type: 'removed', modelId: 'whisper-small' }], loaded);
    expect(removed.models.find((m) => m.id === 'whisper-small')?.installed).toBe(false);
    const turbo = 'whisper-large-v3-turbo';
    const refused = run([{ type: 'removeRequested', modelId: turbo }, { type: 'removeRefused', modelId: turbo, code: 'models.inUse', message: 'In use.' }], loaded);
    expect(phaseOf(refused, turbo)).toEqual({ kind: 'failed', message: 'In use.', code: 'models.inUse' });
    expect(refused.models.find((m) => m.id === turbo)?.installed).toBe(true);
  });
});
