// 2.5, Your own server: where an OpenAI-compatible server's address points, in the words Settings and the Builder use
// (DESIGN.md §11 "Your own server"). The host reads the address itself and its flags (loopback, leavesPc, insecure)
// are what count; this reading only splits "leaves this PC" into "on your network" and "remote" for the words, fills
// in the page's optimistic copy before the host answers, and drives the browser-preview host.

/** this PC · a private address or a plain name on the local network · anything else. */
export type ServerPlace = 'loopback' | 'network' | 'remote';

export interface ServerAddressInfo {
  place: ServerPlace;
  /** "localhost:11434", "192.168.1.20:1234": what the labels show. */
  hostPort: string;
  https: boolean;
  /** Plain http to a remote address: Memento never sends to it. */
  insecure: boolean;
}

const IPV4 = /^(\d{1,3})\.(\d{1,3})\.(\d{1,3})\.(\d{1,3})$/;

function ipv4Place(host: string): ServerPlace | null {
  const m = IPV4.exec(host);
  if (m === null) {
    return null;
  }
  const [a, b] = [Number(m[1]), Number(m[2])];
  if (a === 127) {
    return 'loopback';
  }
  // 10/8, 172.16/12, 192.168/16, link-local 169.254/16 and the shared 100.64/10 (VPN overlays).
  if (a === 10 || (a === 172 && b >= 16 && b <= 31) || (a === 192 && b === 168) || (a === 169 && b === 254) || (a === 100 && b >= 64 && b <= 127)) {
    return 'network';
  }
  return 'remote';
}

function ipv6Place(host: string): ServerPlace | null {
  if (!host.includes(':')) {
    return null;
  }
  const h = host.toLowerCase();
  if (h === '::1') {
    return 'loopback';
  }
  // Unique local fc00::/7 and link-local fe80::/10.
  if (/^f[cd][0-9a-f]{0,2}:/.test(h) || /^fe[89ab][0-9a-f]?:/.test(h)) {
    return 'network';
  }
  return 'remote';
}

function namePlace(host: string): ServerPlace {
  const h = host.toLowerCase().replace(/\.$/, '');
  if (h === 'localhost' || h.endsWith('.localhost')) {
    return 'loopback';
  }
  // A single-label name ("gpu-box") or a local-only suffix resolves on the local network.
  if (!h.includes('.') || /\.(local|lan|home|internal|home\.arpa)$/.test(h)) {
    return 'network';
  }
  return 'remote';
}

/** Reads a typed address ("localhost:11434" gets http://); null when it is not an http(s) address. */
export function readServerAddress(typed: string): ServerAddressInfo | null {
  const text = typed.trim();
  if (text === '') {
    return null;
  }
  const withScheme = /^[a-z][a-z0-9+.-]*:\/\//i.test(text) ? text : `http://${text}`;
  let url: URL;
  try {
    url = new URL(withScheme);
  } catch {
    return null;
  }
  if ((url.protocol !== 'http:' && url.protocol !== 'https:') || url.hostname === '') {
    return null;
  }
  const host = url.hostname.replace(/^\[|\]$/g, '');
  const place = ipv4Place(host) ?? ipv6Place(host) ?? namePlace(host);
  const https = url.protocol === 'https:';
  return { place, hostPort: url.host, https, insecure: place === 'remote' && !https };
}

/** The host part of a server model label: "qwen3:8b at localhost:11434" → "localhost:11434". */
export function serverHostOf(modelLabel: string | null): string | null {
  if (modelLabel === null) {
    return null;
  }
  const at = modelLabel.lastIndexOf(' at ');
  return at < 0 ? null : modelLabel.slice(at + 4);
}

/** The model part of a server model label: "qwen3:8b at localhost:11434" → "qwen3:8b". */
export function serverModelOf(modelLabel: string | null): string | null {
  if (modelLabel === null) {
    return null;
  }
  const at = modelLabel.lastIndexOf(' at ');
  return at < 0 ? modelLabel : modelLabel.slice(0, at);
}

/** Where a server is, from the host's flags (they win) and, to tell the network from remote, the address. */
export function serverPlace(flags: { leavesPc: boolean }, address: string | null): ServerPlace {
  if (!flags.leavesPc) {
    return 'loopback';
  }
  const place = address === null ? null : (readServerAddress(address)?.place ?? null);
  return place === 'remote' ? 'remote' : 'network';
}

/** Nothing a provider is given leaves this PC: as its flag says, else as its kind does (the local model). */
export function staysOnPc(provider: { kind: string; leavesPc?: boolean | undefined }): boolean {
  return provider.leavesPc === undefined ? provider.kind === 'local' : !provider.leavesPc;
}

/** Where a provider runs that is Your own server, from its flags and address. */
export function providerPlace(provider: { kind: string; leavesPc?: boolean | undefined; address?: string | null | undefined }): ServerPlace {
  return serverPlace({ leavesPc: !staysOnPc(provider) }, provider.address ?? null);
}

/** Where a document's server was, from its record: it stayed on this PC, else the host part of its model label. */
export function recordPlace(stayedOnPc: boolean, modelLabel: string | null): ServerPlace {
  return serverPlace({ leavesPc: !stayedOnPc }, serverHostOf(modelLabel));
}

/** "(this PC)", "(on your network)", "(remote)" after a server's label. */
export const SERVER_PLACE_WORDS: Record<ServerPlace, string> = {
  loopback: 'this PC',
  network: 'on your network',
  remote: 'remote',
};

/** "Your own server · qwen3:8b at localhost:11434 (this PC)": the preview sheet, How this was made and Live output. */
export function serverLine(name: string, modelLabel: string | null, place: ServerPlace): string {
  return `${name}${modelLabel === null ? '' : ` · ${modelLabel}`} (${SERVER_PLACE_WORDS[place]})`;
}
