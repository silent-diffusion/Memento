// A child PowerShell process playing a synthetic 440 Hz tone (as tools/AudioCheck does), so there is one
// application with an audio session to record with per-app capture.

import { spawn } from 'node:child_process';
import { existsSync } from 'node:fs';
import { writeFile } from 'node:fs/promises';
import { join } from 'node:path';

function toneWav(seconds, rate = 48_000) {
  const frames = seconds * rate;
  const data = Buffer.alloc(frames * 4);
  for (let i = 0; i < frames; i++) {
    const v = Math.round(0.1 * 32767 * Math.sin((2 * Math.PI * 440 * i) / rate));
    data.writeInt16LE(v, i * 4);
    data.writeInt16LE(v, i * 4 + 2);
  }
  const header = Buffer.alloc(44);
  header.write('RIFF', 0);
  header.writeUInt32LE(36 + data.length, 4);
  header.write('WAVEfmt ', 8);
  header.writeUInt32LE(16, 16);
  header.writeUInt16LE(1, 20);
  header.writeUInt16LE(2, 22);
  header.writeUInt32LE(rate, 24);
  header.writeUInt32LE(rate * 4, 28);
  header.writeUInt16LE(4, 32);
  header.writeUInt16LE(16, 34);
  header.write('data', 36);
  header.writeUInt32LE(data.length, 40);
  return Buffer.concat([header, data]);
}

/** Starts playing; returns { pid, stop() }. */
export async function playTone(directory, seconds) {
  const wav = join(directory, `tone440-${seconds}s.wav`);
  if (!existsSync(wav)) await writeFile(wav, toneWav(seconds));
  const child = spawn('powershell.exe', ['-NoProfile', '-NonInteractive', '-Command', `(New-Object Media.SoundPlayer '${wav}').PlaySync()`], {
    stdio: 'ignore',
    windowsHide: true,
  });
  child.unref();
  return {
    pid: child.pid,
    stop: () => spawn('taskkill', ['/F', '/T', '/PID', String(child.pid)], { stdio: 'ignore' }),
  };
}
