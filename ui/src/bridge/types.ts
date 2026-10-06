// Host <-> UI bridge contracts. Mirrors src/Memento.Core/Bridge/Contracts/*.cs exactly;
// ContractSerializationTests on the C# side pins the JSON these types describe.

/** UI -> host request. */
export interface BridgeRequest<M extends MethodName = MethodName> {
  id: number;
  method: M;
  params: MethodParams<M>;
}

/** A structured failure; `message` is written for people (DESIGN.md §17). */
export interface BridgeError {
  code: string;
  message: string;
  detail: string | null;
}

/** Host -> UI answer. `id` is null only when the request was too malformed to carry one. */
export type BridgeResponse =
  | { id: number | null; result: unknown; error?: never }
  | { id: number | null; error: BridgeError; result?: never };

/** Host -> UI notification. */
export interface BridgeEventEnvelope<E extends EventName = EventName> {
  event: E;
  payload: EventPayload<E>;
}

/** Parameters of a method that takes none. */
export type EmptyParams = Record<string, never>;

/** Result of a method that only acknowledges. */
export type EmptyResult = Record<string, never>;

export interface AppVersionResult {
  version: string;
  osVersion: string;
  isDarkTheme: boolean;
}

export type ThemePreference = 'system' | 'light' | 'dark';

export type ListDensity = 'comfortable' | 'compact';

export interface SettingsSnapshot {
  theme: ThemePreference;
  /** The library folder in effect. */
  libraryPath: string;
  listDensity: ListDensity;
}

/** Partial update; omitted or null fields keep their value. */
export interface SettingsSetParams {
  theme?: ThemePreference | null;
  /** Accepted only when equal to the current location; moving the library is a separate flow. */
  libraryPath?: string | null;
  listDensity?: ListDensity | null;
}

export type StageState = 'done' | 'active' | 'queued' | 'failed';

/** A status pill (DESIGN.md §5.4). `stage` is transcript, speakers, chapters or minutes. */
export interface StageStatus {
  stage: string;
  state: StageState;
  /** 0-100 while active, otherwise null. */
  percent: number | null;
}

/** One Library row (DESIGN.md §4). */
export interface RecordingSummary {
  id: string;
  title: string;
  /** meeting, interview, lecture, presentation, dictation, research or a custom type name. */
  type: string;
  /** ISO 8601 with offset. */
  createdAt: string;
  durationMs: number;
  participantCount: number;
  hasVideo: boolean;
  stages: StageStatus[];
}

export interface LibraryListResult {
  recordings: RecordingSummary[];
  totalDurationMs: number;
}

/** An https: link or an ms-settings: page. */
export interface OpenExternalParams {
  url: string;
}

export interface OpenExternalResult {
  opened: boolean;
}

export interface ThemeChangedPayload {
  isDark: boolean;
}

export interface EngineStatus {
  ready: boolean;
  /** GPU or CPU; null when no engine is ready. */
  device: string | null;
}

export interface StorageStatus {
  /** Free bytes on the library drive, or null when it cannot be read. */
  freeBytes: number | null;
  /** Below the low-space threshold (10 GB by default). */
  lowSpace: boolean;
}

export interface FooterStatusPayload {
  engine: EngineStatus;
  storage: StorageStatus;
}

/** Every host method: name -> params and result. Mirrors BridgeMethodNames.cs. */
export interface BridgeMethods {
  'app.version': { params: EmptyParams; result: AppVersionResult };
  'app.openExternal': { params: OpenExternalParams; result: OpenExternalResult };
  'settings.get': { params: EmptyParams; result: SettingsSnapshot };
  'settings.set': { params: SettingsSetParams; result: SettingsSnapshot };
  'library.list': { params: EmptyParams; result: LibraryListResult };
  'ui.ready': { params: EmptyParams; result: EmptyResult };
}

/** Every host event: name -> payload. Mirrors BridgeEventNames.cs. */
export interface BridgeEvents {
  'theme.changed': ThemeChangedPayload;
  'status.footer': FooterStatusPayload;
}

export type MethodName = keyof BridgeMethods;
export type MethodParams<M extends MethodName> = BridgeMethods[M]['params'];
export type MethodResult<M extends MethodName> = BridgeMethods[M]['result'];
export type EventName = keyof BridgeEvents;
export type EventPayload<E extends EventName> = BridgeEvents[E];

export const METHOD_NAMES = [
  'app.version',
  'app.openExternal',
  'settings.get',
  'settings.set',
  'library.list',
  'ui.ready',
] as const satisfies readonly MethodName[];

export const EVENT_NAMES = ['theme.changed', 'status.footer'] as const satisfies readonly EventName[];
