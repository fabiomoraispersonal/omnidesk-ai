// Spec 007 T166 — client-side mime-detect unit tests.

import { describe, it, expect } from 'vitest';
import { detectMime, allowedAccept } from '../src/lib/mime-detect';

function makeFile(bytes: number[], name = 'file.bin', type = 'application/octet-stream'): File {
  return new File([new Uint8Array(bytes)], name, { type });
}

describe('detectMime', () => {
  it('detects JPEG by magic bytes regardless of declared MIME', async () => {
    const file = makeFile([0xff, 0xd8, 0xff, 0xe0, 0x00, 0x10], 'photo.exe', 'application/octet-stream');
    expect(await detectMime(file)).toBe('image/jpeg');
  });

  it('detects PNG', async () => {
    const file = makeFile([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]);
    expect(await detectMime(file)).toBe('image/png');
  });

  it('detects GIF', async () => {
    const file = makeFile([0x47, 0x49, 0x46, 0x38, 0x39, 0x61]);
    expect(await detectMime(file)).toBe('image/gif');
  });

  it('detects WebP', async () => {
    const file = makeFile([0x52, 0x49, 0x46, 0x46, 0x10, 0x00, 0x00, 0x00, 0x57, 0x45, 0x42, 0x50]);
    expect(await detectMime(file)).toBe('image/webp');
  });

  it('detects PDF', async () => {
    const file = makeFile([0x25, 0x50, 0x44, 0x46, 0x2d, 0x31, 0x2e, 0x37]);
    expect(await detectMime(file)).toBe('application/pdf');
  });

  it('returns null for unknown bytes', async () => {
    const file = makeFile([0x00, 0x01, 0x02, 0x03]);
    expect(await detectMime(file)).toBeNull();
  });

  it('returns null for tiny files', async () => {
    const file = makeFile([0xff]);
    expect(await detectMime(file)).toBeNull();
  });

  it('exposes the accept attribute string with all 7 MIMEs', () => {
    expect(allowedAccept).toContain('image/jpeg');
    expect(allowedAccept).toContain('application/pdf');
    expect(allowedAccept).toContain('wordprocessingml.document');
    expect(allowedAccept).toContain('spreadsheetml.sheet');
  });
});
