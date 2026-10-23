import { describe, expect, it } from 'vitest';
import { providerPlace, readServerAddress, recordPlace, serverHostOf, serverLine, serverModelOf, serverPlace, staysOnPc } from './serverAddress';

describe('Your own server: reading an address (2.5)', () => {
  it('reads this PC, the local network and anything else, with or without a scheme', () => {
    const place = (typed: string): string | undefined => readServerAddress(typed)?.place;
    expect(['http://localhost:11434', 'localhost:1234', 'http://127.0.0.1:8080', 'http://[::1]:11434', 'http://app.localhost'].map(place)).toEqual([
      'loopback',
      'loopback',
      'loopback',
      'loopback',
      'loopback',
    ]);
    expect(['http://192.168.1.20:11434', '10.0.0.5:8000', 'http://172.20.1.2', 'http://gpu-box:11434', 'http://studio.local:1234', 'http://[fd12::5]:8000', '100.101.1.2:11434'].map(place)).toEqual([
      'network',
      'network',
      'network',
      'network',
      'network',
      'network',
      'network',
    ]);
    expect(['https://llm.example.com', 'http://203.0.113.7:11434', 'http://172.32.0.1', 'http://[2001:db8::1]'].map(place)).toEqual(['remote', 'remote', 'remote', 'remote']);
  });

  it('marks plain http to a remote address insecure, and nothing else', () => {
    expect(readServerAddress('http://203.0.113.7:11434')).toEqual({ place: 'remote', hostPort: '203.0.113.7:11434', https: false, insecure: true });
    expect(readServerAddress('https://llm.example.com')?.insecure).toBe(false);
    expect(readServerAddress('http://192.168.1.20:11434')?.insecure).toBe(false);
    expect(readServerAddress('localhost:11434')).toEqual({ place: 'loopback', hostPort: 'localhost:11434', https: false, insecure: false });
  });

  it('refuses what is not an http(s) address', () => {
    expect(['', '   ', 'ftp://example.com', 'http://', 'not a url at all'].map((t) => readServerAddress(t))).toEqual([null, null, null, null, null]);
  });

  it('splits a server model label and says where the server is', () => {
    expect(serverHostOf('qwen3:8b at localhost:11434')).toBe('localhost:11434');
    expect(serverModelOf('qwen3:8b at localhost:11434')).toBe('qwen3:8b');
    expect(serverModelOf('hf.co/org/model:Q4 at gpu-box:8000')).toBe('hf.co/org/model:Q4');
    expect(serverHostOf(null)).toBeNull();
    // The host's flags win: leavesPc false is this PC whatever the address says.
    expect(serverPlace({ leavesPc: false }, 'http://192.168.1.20:11434')).toBe('loopback');
    expect(serverPlace({ leavesPc: true }, 'http://192.168.1.20:11434')).toBe('network');
    expect(serverPlace({ leavesPc: true }, 'https://llm.example.com')).toBe('remote');
    expect(serverPlace({ leavesPc: true }, null)).toBe('network');
    expect(recordPlace(true, 'qwen3:8b at localhost:11434')).toBe('loopback');
    expect(recordPlace(false, 'qwen3:8b at 192.168.1.20:11434')).toBe('network');
    expect(recordPlace(false, 'Qwen/Qwen3-8B at llm.example.com')).toBe('remote');
    expect(staysOnPc({ kind: 'local' })).toBe(true);
    expect(staysOnPc({ kind: 'cloud' })).toBe(false);
    expect(staysOnPc({ kind: 'server', leavesPc: false })).toBe(true);
    expect(providerPlace({ kind: 'server', leavesPc: true, address: 'http://192.168.1.20:11434' })).toBe('network');
    expect(serverLine('Your own server', 'qwen3:8b at localhost:11434', 'loopback')).toBe('Your own server · qwen3:8b at localhost:11434 (this PC)');
    expect(serverLine('Your own server', 'qwen3:8b at 192.168.1.20:11434', 'network')).toBe('Your own server · qwen3:8b at 192.168.1.20:11434 (on your network)');
  });
});
