// The cloud models Settings › AI and privacy offers (BRIDGE.md "Settings (M4)", the cloud models table), mirrored from
// src/Memento.Core/Ai/AiModelDefaults.cs for the browser-preview host, plus the check of a model id typed under
// "Other model…" that the host also makes. The page shows what the snapshot carries; this module only builds it in
// the preview and checks typing as it happens.
import type { AiModelChoice, AiProvider, AiProviderStatus } from './types';

/** The models listed per provider, the default first (the host's AiModelDefaults). */
export const CLOUD_MODELS: Record<AiProvider, readonly AiModelChoice[]> = {
  anthropic: [
    {
      id: 'claude-opus-5-5',
      note: "The most capable for long transcripts. Memento's default.",
      contextTokens: 1_000_000,
    },
    {
      id: 'claude-fable-5-1',
      note: "Anthropic's top tier: the newest and most intelligent. Costs more and takes longer; the account needs 30-day data retention.",
      contextTokens: 1_000_000,
    },
    {
      id: 'claude-sonnet-5-5',
      note: 'Fast and strong, at about half the price of Opus.',
      contextTokens: 1_000_000,
    },
    {
      id: 'claude-haiku-5-5',
      note: 'The fastest and cheapest.',
      contextTokens: 1_000_000,
    },
  ],
  openai: [
    {
      id: 'gpt-6-astra',
      note: "OpenAI's most capable model. Memento's default.",
      contextTokens: 1_050_000,
    },
    {
      id: 'gpt-6.1-sol',
      note: 'Nearly as capable, at a lower cost.',
      contextTokens: 1_050_000,
    },
    {
      id: 'gpt-6-luna',
      note: "OpenAI's most efficient: the fastest and cheapest.",
      contextTokens: 1_050_000,
    },
  ],
};

/** What the host assumes about a model it does not know: Claude 200k, ChatGPT 128k. */
const UNLISTED_CONTEXT: Record<AiProvider, number> = {
  anthropic: 200_000,
  openai: 128_000,
};

export const MAX_MODEL_ID_LENGTH = 100;

const MODEL_ID = /^[A-Za-z0-9][A-Za-z0-9._:/-]*$/;

/** "1M", "1.05M", "200k". */
export function tokenSize(tokens: number): string {
  return tokens >= 1_000_000 ? `${String(Math.round((tokens / 1_000_000) * 100) / 100)}M` : `${String(Math.floor(tokens / 1000))}k`;
}

/**
 * Why a typed model id cannot be saved, in the words Settings shows under the field, or null when it is a plausible id
 * (the host checks the same: letters, digits and . _ : / -, starting with a letter or digit, at most 100 characters).
 */
export function modelIdProblem(typed: string, example = 'claude-opus-4-8'): string | null {
  const id = typed.trim();
  if (id === '') {
    return `Type the model id, for example ${example}. Nothing was changed.`;
  }
  if (id.length > MAX_MODEL_ID_LENGTH) {
    return `A model id is at most ${String(MAX_MODEL_ID_LENGTH)} characters; this one has ${String(id.length)}. Nothing was changed.`;
  }
  if (!MODEL_ID.test(id)) {
    return `'${id}' is not a model id. Use letters, digits and . _ : / - and start with a letter or digit. Nothing was changed.`;
  }
  return null;
}

/** The note for a model that is not in Memento's list (as the host's AiModelDefaults.Describe words it). */
export function unlistedNote(provider: AiProvider): string {
  return `Not in Memento's list. Memento assumes it reads up to ${tokenSize(UNLISTED_CONTEXT[provider])} tokens a request and sends it no effort setting.`;
}

/** A provider's snapshot entry with `model` in effect (null: the default), as the host's settings snapshot builds it. */
export function providerState(provider: AiProvider, hasKey: boolean, model: string | null): AiProviderStatus {
  const choices = CLOUD_MODELS[provider];
  const id = model !== null && modelIdProblem(model) === null ? model.trim() : (choices[0]?.id ?? '');
  const listed = choices.find((c) => c.id === id);
  return {
    hasKey,
    model: id,
    models: choices.map((c) => c.id),
    choices: choices.map((c) => ({ ...c })),
    custom: listed === undefined,
    modelNote: listed?.note ?? unlistedNote(provider),
  };
}
