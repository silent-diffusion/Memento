// Synthetic video for the browser preview: frames drawn on a canvas, encoded as VP8 with WebCodecs
// and written into a small WebM (Matroska) file with a cue for every key frame, so <video> can play
// and seek it from a blob: URL like the host's fragmented MP4 from https://library.memento/. Each
// frame shows its own timeline time, which makes the player's sync visible. Nothing here runs in
// the app: the real host returns files it recorded.

export interface SyntheticVideoSpec {
  durationMs: number;
  width: number;
  height: number;
  /** Time between frames; long recordings use few frames so the file builds in a second or two. */
  frameIntervalMs: number;
  draw: (context: OffscreenCanvasRenderingContext2D, atMs: number) => void;
}

/** WebCodecs and OffscreenCanvas exist (Edge, Chrome); jsdom and old browsers get no file. */
export function canGenerateVideo(): boolean {
  return typeof VideoEncoder !== 'undefined' && typeof VideoFrame !== 'undefined' && typeof OffscreenCanvas !== 'undefined';
}

interface Chunk {
  data: Uint8Array;
  atMs: number;
  key: boolean;
}

const KEY_EVERY = 10;

export async function generateWebm(spec: SyntheticVideoSpec): Promise<Blob> {
  const chunks: Chunk[] = [];
  let failure: unknown = null;
  const encoder = new VideoEncoder({
    output: (chunk) => {
      const data = new Uint8Array(chunk.byteLength);
      chunk.copyTo(data);
      chunks.push({ data, atMs: Math.round(chunk.timestamp / 1000), key: chunk.type === 'key' });
    },
    error: (error) => {
      failure = error;
    },
  });
  encoder.configure({ codec: 'vp8', width: spec.width, height: spec.height, bitrate: 150_000, framerate: Math.max(0.1, 1000 / spec.frameIntervalMs) });
  const canvas = new OffscreenCanvas(spec.width, spec.height);
  const context = canvas.getContext('2d');
  if (context === null) {
    throw new Error('No 2D canvas for the preview video.');
  }
  for (let i = 0, at = 0; at < spec.durationMs; i++, at += spec.frameIntervalMs) {
    spec.draw(context, at);
    const frame = new VideoFrame(canvas, { timestamp: at * 1000, duration: spec.frameIntervalMs * 1000 });
    encoder.encode(frame, { keyFrame: i % KEY_EVERY === 0 });
    frame.close();
    while (encoder.encodeQueueSize > 16) {
      await new Promise((resolve) => setTimeout(resolve, 0));
    }
    if (failure !== null) {
      throw failure instanceof Error ? failure : new Error('The preview video could not be encoded.');
    }
  }
  await encoder.flush();
  encoder.close();
  return new Blob([muxWebm(chunks, spec.width, spec.height, spec.durationMs) as BlobPart], { type: 'video/webm' });
}

// ---------------------------------------------------------------------------------------------
// Matroska / WebM
// ---------------------------------------------------------------------------------------------

function concat(parts: readonly Uint8Array[]): Uint8Array {
  const out = new Uint8Array(parts.reduce((sum, p) => sum + p.length, 0));
  let offset = 0;
  for (const part of parts) {
    out.set(part, offset);
    offset += part.length;
  }
  return out;
}

function idBytes(id: number): Uint8Array {
  const out: number[] = [];
  for (let v = id; v > 0; v = Math.floor(v / 256)) {
    out.unshift(v & 0xff);
  }
  return Uint8Array.from(out);
}

/** An 8-byte EBML size: fixed width keeps the arithmetic of positions simple. */
function sizeBytes(size: number): Uint8Array {
  const out = [0x01];
  for (let i = 6; i >= 0; i--) {
    out.push(Math.floor(size / 2 ** (8 * i)) & 0xff);
  }
  return Uint8Array.from(out);
}

function uint(value: number, width = 0): Uint8Array {
  const out: number[] = [];
  for (let v = Math.max(0, Math.round(value)); v > 0; v = Math.floor(v / 256)) {
    out.unshift(v & 0xff);
  }
  while (out.length < Math.max(1, width)) {
    out.unshift(0);
  }
  return Uint8Array.from(out);
}

function float64(value: number): Uint8Array {
  const out = new Uint8Array(8);
  new DataView(out.buffer).setFloat64(0, value);
  return out;
}

function text(value: string): Uint8Array {
  return new TextEncoder().encode(value);
}

function el(id: number, ...payload: Uint8Array[]): Uint8Array {
  const body = concat(payload);
  return concat([idBytes(id), sizeBytes(body.length), body]);
}

const ID = {
  ebml: 0x1a45dfa3,
  segment: 0x18538067,
  seekHead: 0x114d9b74,
  seek: 0x4dbb,
  seekId: 0x53ab,
  seekPosition: 0x53ac,
  info: 0x1549a966,
  tracks: 0x1654ae6b,
  cluster: 0x1f43b675,
  cues: 0x1c53bb6b,
} as const;

/** A WebM with one VP8 track: clusters start at each key frame (and at least every 30 s), with cues. */
export function muxWebm(chunks: readonly Chunk[], width: number, height: number, durationMs: number): Uint8Array {
  const header = el(
    ID.ebml,
    el(0x4286, uint(1)),
    el(0x42f7, uint(1)),
    el(0x42f2, uint(4)),
    el(0x42f3, uint(8)),
    el(0x4282, text('webm')),
    el(0x4287, uint(2)),
    el(0x4285, uint(2)),
  );
  const info = el(ID.info, el(0x2ad7b1, uint(1_000_000)), el(0x4489, float64(durationMs)), el(0x4d80, text('Memento preview')), el(0x5741, text('Memento preview')));
  const tracks = el(
    ID.tracks,
    el(0xae, el(0xd7, uint(1)), el(0x73c5, uint(1)), el(0x83, uint(1)), el(0x86, text('V_VP8')), el(0xe0, el(0xb0, uint(width)), el(0xba, uint(height)))),
  );

  const clusters: Uint8Array[] = [];
  const cuePoints: { atMs: number; offset: number }[] = [];
  let open: { startMs: number; blocks: Uint8Array[]; key: boolean } | null = null;
  const close = (): void => {
    if (open !== null) {
      clusters.push(el(ID.cluster, el(0xe7, uint(open.startMs)), ...open.blocks));
      open = null;
    }
  };
  const ordered = [...chunks].sort((a, b) => a.atMs - b.atMs);
  const clusterAt: number[] = [];
  for (const chunk of ordered) {
    if (open === null || chunk.key || chunk.atMs - open.startMs > 30_000) {
      close();
      open = { startMs: chunk.atMs, blocks: [], key: chunk.key };
      clusterAt.push(chunk.key ? chunk.atMs : -1);
    }
    const relative = chunk.atMs - open.startMs;
    const block = concat([Uint8Array.from([0x81, (relative >> 8) & 0xff, relative & 0xff, chunk.key ? 0x80 : 0x00]), chunk.data]);
    open.blocks.push(el(0xa3, block));
  }
  close();

  // Positions are relative to the start of the Segment's payload: SeekHead, Info, Tracks, clusters, Cues.
  const seekEntry = (id: number, position: number): Uint8Array => el(ID.seek, el(ID.seekId, idBytes(id)), el(ID.seekPosition, uint(position, 8)));
  const seekHeadSize = el(ID.seekHead, seekEntry(ID.info, 0), seekEntry(ID.tracks, 0), seekEntry(ID.cues, 0)).length;
  const infoAt = seekHeadSize;
  const tracksAt = infoAt + info.length;
  let offset = tracksAt + tracks.length;
  clusters.forEach((cluster, i) => {
    const at = clusterAt[i] ?? -1;
    if (at >= 0) {
      cuePoints.push({ atMs: at, offset });
    }
    offset += cluster.length;
  });
  const cuesAt = offset;
  const cues = el(ID.cues, ...cuePoints.map((p) => el(0xbb, el(0xb3, uint(p.atMs)), el(0xb7, el(0xf7, uint(1)), el(0xf1, uint(p.offset))))));
  const seekHead = el(ID.seekHead, seekEntry(ID.info, infoAt), seekEntry(ID.tracks, tracksAt), seekEntry(ID.cues, cuesAt));
  const segment = el(ID.segment, seekHead, info, tracks, ...clusters, cues);
  return concat([header, segment]);
}
