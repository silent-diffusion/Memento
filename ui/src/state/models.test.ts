import { describe, expect, it } from 'vitest';
import { sampleModels } from '../bridge/mockModels';
import type { ModelsProgressPayload } from '../bridge/types';
import { INITIAL_MODELS, isBusy, modelsReducer, phaseOf, type ModelsAction, type ModelsState } from './models';

const run = (actions: ModelsAction[], start: ModelsState = INITIAL_MODELS): ModelsState => actions.reduce(modelsReducer, start);

const progress = (state: ModelsProgressPayload['state'], percent: number, message: string | null = null): ModelsAction => ({
  type: 'progress',
  payload: { modelId: 'medium', percent, bytesDone: percent * 10, bytesTotal: 1000, state, message },
});

describe('model install state machine', () => {
  const loaded = run([{ type: 'loaded', models: sampleModels() }]);

  it('loads the catalog idle, picking up a download already running', () => {
    expect(loaded.loaded).toBe(true);
    expect(phaseOf(loaded, 'medium')).toEqual({ kind: 'idle' });
    const running = sampleModels().map((m) => (m.id === 'medium' ? { ...m, installing: { percent: 30, bytesDone: 300 } } : m));
    expect(phaseOf(run([{ type: 'loaded', models: running }]), 'medium')).toMatchObject({ kind: 'downloading', percent: 30 });
  });

  it('goes install → starting → downloading → verifying → done, then is installed', () => {
    const steps: ModelsAction[] = [{ type: 'installRequested', modelId: 'medium' }, progress('downloading', 4), progress('downloading', 48)];
    const mid = run(steps, loaded);
    expect(phaseOf(mid, 'medium')).toEqual({ kind: 'downloading', percent: 48, bytesDone: 480, bytesTotal: 1000 });
    expect(isBusy(mid)).toBe(true);
    expect(mid.models.find((m) => m.id === 'medium')?.installing).toEqual({ percent: 48, bytesDone: 480 });
    const verifying = run([progress('verifying', 100)], mid);
    expect(phaseOf(verifying, 'medium')).toEqual({ kind: 'verifying' });
    const done = run([progress('done', 100)], verifying);
    expect(phaseOf(done, 'medium')).toEqual({ kind: 'idle' });
    expect(done.models.find((m) => m.id === 'medium')).toMatchObject({ installed: true, installing: null });
    expect(isBusy(done)).toBe(false);
  });

  it('cancels: a late progress event does not bring the download back', () => {
    const cancelling = run([{ type: 'installRequested', modelId: 'medium' }, progress('downloading', 20), { type: 'cancelRequested', modelId: 'medium' }], loaded);
    expect(phaseOf(cancelling, 'medium')).toEqual({ kind: 'cancelling' });
    const late = run([progress('downloading', 24)], cancelling);
    expect(phaseOf(late, 'medium')).toEqual({ kind: 'cancelling' });
    const cancelled = run([{ type: 'cancelled', modelId: 'medium' }], late);
    expect(phaseOf(cancelled, 'medium')).toEqual({ kind: 'idle' });
    expect(cancelled.models.find((m) => m.id === 'medium')).toMatchObject({ installed: false, installing: null });
  });

  it('keeps the host message when a download fails part-way or is refused', () => {
    const failed = run([{ type: 'installRequested', modelId: 'medium' }, progress('downloading', 40), progress('failed', 44, 'The connection was lost.')], loaded);
    expect(phaseOf(failed, 'medium')).toEqual({ kind: 'failed', message: 'The connection was lost.', code: 'models.downloadFailed' });
    expect(isBusy(failed)).toBe(false);
    const refused = run([{ type: 'installRefused', modelId: 'base', code: 'models.noSpace', message: 'Base needs 142 MB.' }], loaded);
    expect(phaseOf(refused, 'base')).toEqual({ kind: 'failed', message: 'Base needs 142 MB.', code: 'models.noSpace' });
    // A failure survives a reload of the list until dismissed.
    const reloaded = run([{ type: 'loaded', models: sampleModels() }], refused);
    expect(phaseOf(reloaded, 'base').kind).toBe('failed');
    expect(phaseOf(run([{ type: 'dismiss', modelId: 'base' }], reloaded), 'base')).toEqual({ kind: 'idle' });
  });

  it('removes a model, or keeps it with the host reason', () => {
    const removed = run([{ type: 'removeRequested', modelId: 'small' }, { type: 'removed', modelId: 'small' }], loaded);
    expect(removed.models.find((m) => m.id === 'small')?.installed).toBe(false);
    const refused = run([{ type: 'removeRequested', modelId: 'large-v3' }, { type: 'removeRefused', modelId: 'large-v3', code: 'models.inUse', message: 'In use.' }], loaded);
    expect(phaseOf(refused, 'large-v3')).toEqual({ kind: 'failed', message: 'In use.', code: 'models.inUse' });
    expect(refused.models.find((m) => m.id === 'large-v3')?.installed).toBe(true);
  });
});
