// The browser-preview host's local model, as Core's LocalModelChoice and Generation's ProviderRegistry work it out
// (docs/BRIDGE.md "Settings (M4)", ARCHITECTURE.md §8): the model in effect is an installed one whenever any is
// installed, and the Local provider names the model that writes and where it runs. `?vram=low` stands for a graphics
// card another app is using: Qwen3.5 4B then gives way to an installed Ministral 3 3B, or runs on the processor.
import { mockGpuMemory, mockGpuNote, type VramFlag } from './mockGpu';
import { MODEL_IDS } from './mockModels';
import type { LocalModelPlanInfo, ModelInfo, ProviderInfo } from './types';

export type { VramFlag } from './mockGpu';

/** `?llm=`: which local models the preview starts with (default: Qwen with `?ai=local`, else none). */
export type LocalModelsFlag = 'qwen' | 'ministral' | 'both' | 'none' | 'moe' | 'moeOnly';

export function localModelsInstalled(flag: LocalModelsFlag): string[] {
  switch (flag) {
    case 'qwen':
      return [MODEL_IDS.qwen];
    case 'ministral':
      return [MODEL_IDS.ministral];
    case 'both':
      return [MODEL_IDS.qwen, MODEL_IDS.ministral];
    case 'moe':
      return [MODEL_IDS.qwen, MODEL_IDS.qwen35moe];
    case 'moeOnly':
      return [MODEL_IDS.qwen35moe];
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
  const any = [GPU_MODEL, CPU_MODEL, ...LARGER].find(installed);
  return any ?? chosen ?? recommended;
}

const LARGER: string[] = [MODEL_IDS.qwen27, MODEL_IDS.qwen35moe];
const GiB = 1024 ** 3;

/**
 * The larger models' plans as the host's MemoryPlanner makes them on the reference laptop (40 GB of RAM, a 6 GB card;
 * ENGINE-NOTES.md §T): with the card free the dense model is split and the mixture of experts keeps its experts in RAM;
 * with another program on the card (`?vram=low`) both run in RAM on the processor.
 */
export function largerModelPlans(vram: VramFlag): LocalModelPlanInfo[] {
  const card = vram === 'ok';
  const plan = (modelId: string, p: Omit<LocalModelPlanInfo, 'modelId' | 'fits' | 'contextTokens'>): LocalModelPlanInfo => ({ modelId, fits: true, contextTokens: 16384, ...p });
  return card
    ? [
        plan(MODEL_IDS.qwen27, { line: 'Runs 15 of 64 layers on the graphics card, the rest in RAM · about 3 tokens/s · 14 GB of RAM while writing', placement: 'split', gpuLayers: 16, cardBlocks: 15, totalLayers: 64, cpuExpertLayers: 0, vramBytes: 4.6 * GiB, ramBytes: 14 * GiB, tokensPerSecond: 2.9 }),
        plan(MODEL_IDS.qwen35moe, { line: 'Runs on the graphics card with the experts of 37 of 40 layers in RAM · about 12 tokens/s · 18 GB of RAM while writing', placement: 'experts', gpuLayers: 41, cardBlocks: 40, totalLayers: 40, cpuExpertLayers: 37, vramBytes: 4.5 * GiB, ramBytes: 18 * GiB, tokensPerSecond: 12.3 }),
      ]
    : [
        plan(MODEL_IDS.qwen27, { line: 'Runs in RAM on the processor · about 2 tokens/s · 18 GB of RAM while writing', placement: 'ram', gpuLayers: 0, cardBlocks: 0, totalLayers: 64, cpuExpertLayers: 0, vramBytes: 0, ramBytes: 18 * GiB, tokensPerSecond: 2.4 }),
        plan(MODEL_IDS.qwen35moe, { line: 'Runs in RAM on the processor · about 6 tokens/s · 22 GB of RAM while writing', placement: 'ram', gpuLayers: 0, cardBlocks: 0, totalLayers: 40, cpuExpertLayers: 0, vramBytes: 0, ramBytes: 22 * GiB, tokensPerSecond: 6.1 }),
      ];
}

function placementWords(plan: LocalModelPlanInfo): string {
  return plan.placement === 'card' ? 'graphics card' : plan.placement === 'ram' ? 'processor' : 'graphics card and RAM';
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
      modelListed: null,
      gpuMemory: mockGpuMemory(vram),
      gpuNote: null,
      memoryPlan: null,
      largerModels: largerModelPlans(vram),
    };
  }
  const larger = largerModelPlans(vram).find((p) => p.modelId === id);
  if (larger !== undefined) {
    return {
      ...base,
      ready: larger.fits,
      reason: larger.fits ? null : 'Not enough memory',
      modelLabel: larger.fits ? `${model.name} · ${placementWords(larger)}` : model.name,
      code: larger.fits ? null : 'ai.notEnoughMemory',
      detail: larger.fits ? `${larger.line}, with a 16k context. Nothing leaves this PC.` : larger.line,
      modelId: id,
      modelListed: null,
      gpuMemory: mockGpuMemory(vram),
      gpuNote: null,
      memoryPlan: larger,
      largerModels: largerModelPlans(vram),
    };
  }
  const notes: string[] = [];
  if (chosen !== null && chosen !== id && !models.some((m) => m.id === chosen && m.installed)) {
    notes.push(`${nameOf(chosen)}, chosen in Settings, is not installed, so ${model.name} writes the documents.`);
  }
  let writer = id;
  let onGpu = true;
  let gpuNote: string | null = null;
  if (vram === 'low') {
    onGpu = false;
    if (id === GPU_MODEL) {
      const standIn = models.some((m) => m.id === CPU_MODEL && m.installed);
      gpuNote = mockGpuNote(vram, model.name, '3.4 GB', standIn ? `${nameOf(CPU_MODEL)} writes instead` : 'it runs on the processor (several times slower)');
      notes.push(gpuNote ?? '');
      writer = standIn ? CPU_MODEL : id;
    }
  }
  const where = onGpu ? 'graphics card' : 'processor';
  notes.push(`Runs on the ${where} with ${onGpu ? 'a 16k' : 'an 8k'} context. Nothing leaves this PC.`);
  return {
    ...base,
    ready: true,
    reason: null,
    modelLabel: `${nameOf(writer)} · ${where}`,
    code: null,
    detail: notes.join(' '),
    modelId: writer,
    modelListed: null,
    gpuMemory: mockGpuMemory(vram),
    gpuNote,
    memoryPlan: null,
    largerModels: largerModelPlans(vram),
  };
}
