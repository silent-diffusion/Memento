// The browser-preview host's local model, as Core's LocalModelChoice and Generation's ProviderRegistry work it out
// (docs/BRIDGE.md "Settings (M4)", ARCHITECTURE.md §8): the model in effect is an installed one whenever any is
// installed, and the Local provider names the model that writes and where it runs. `?vram=low` stands for a graphics
// card another app is using: Qwen3.5 4B then gives way to an installed Ministral 3 3B, or runs on the processor.
import { MODEL_IDS } from './mockModels';
import type { ModelInfo, ProviderInfo } from './types';

export type VramFlag = 'ok' | 'low';

/** `?llm=`: which local models the preview starts with (default: Qwen with `?ai=local`, else none). */
export type LocalModelsFlag = 'qwen' | 'ministral' | 'both' | 'none';

export function localModelsInstalled(flag: LocalModelsFlag): string[] {
  switch (flag) {
    case 'qwen':
      return [MODEL_IDS.qwen];
    case 'ministral':
      return [MODEL_IDS.ministral];
    case 'both':
      return [MODEL_IDS.qwen, MODEL_IDS.ministral];
    default:
      return [];
  }
}

const GPU_MODEL: string = MODEL_IDS.qwen;
const CPU_MODEL: string = MODEL_IDS.ministral;

/** The hardware's recommendation: the graphics-card model when the card has room, else the processor model. */
export function recommendedLocalModelId(vram: VramFlag): string {
  return vram === 'ok' ? GPU_MODEL : CPU_MODEL;
}

/** The model in effect: the installed choice, else the installed recommendation, else any installed one, else the choice or the recommendation. */
export function effectiveLocalModelId(models: readonly ModelInfo[], chosen: string | null, vram: VramFlag): string {
  const installed = (id: string): boolean => models.some((m) => m.id === id && m.engine === 'llm' && m.installed);
  if (chosen !== null && installed(chosen)) {
    return chosen;
  }
  const recommended = recommendedLocalModelId(vram);
  if (installed(recommended)) {
    return recommended;
  }
  const any = [GPU_MODEL, CPU_MODEL].find(installed);
  return any ?? chosen ?? recommended;
}

/** The Local provider's readiness, with the model, the device and why. */
export function localProviderInfo(models: readonly ModelInfo[], chosen: string | null, vram: VramFlag): ProviderInfo {
  const base = { id: 'local', name: 'Local model', vendor: 'This PC', kind: 'local' } as const;
  const id = effectiveLocalModelId(models, chosen, vram);
  const model = models.find((m) => m.id === id);
  const nameOf = (modelId: string): string => models.find((m) => m.id === modelId)?.name ?? modelId;
  if (model?.installed !== true) {
    return {
      ...base,
      ready: false,
      reason: 'Model not installed',
      modelLabel: model?.name ?? null,
      code: 'ai.modelNotInstalled',
      detail: `The local model ${nameOf(id)} is not installed. Nothing was sent anywhere. Download it in Settings › AI and privacy.`,
      modelId: id,
    };
  }
  const notes: string[] = [];
  if (chosen !== null && chosen !== id && !models.some((m) => m.id === chosen && m.installed)) {
    notes.push(`${nameOf(chosen)}, chosen in Settings, is not installed, so ${model.name} writes the documents.`);
  }
  let writer = id;
  let onGpu = true;
  if (vram === 'low') {
    onGpu = false;
    if (id === GPU_MODEL) {
      const standIn = models.some((m) => m.id === CPU_MODEL && m.installed);
      notes.push(`The graphics card has 2.0 GB free and ${model.name} needs 3.4 GB on it, so ${standIn ? `${nameOf(CPU_MODEL)} writes instead this time.` : 'it runs on the processor, which takes several times longer.'}`);
      writer = standIn ? CPU_MODEL : id;
    }
  }
  const where = onGpu ? 'graphics card' : 'processor';
  notes.push(`Runs on the ${where} with ${onGpu ? 'a 16k' : 'an 8k'} context. Nothing leaves this PC.`);
  return { ...base, ready: true, reason: null, modelLabel: `${nameOf(writer)} · ${where}`, code: null, detail: notes.join(' '), modelId: writer };
}
