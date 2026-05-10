// Spec 007 US6 — client-side magic-byte sniff. Used only as UX feedback ("we won't even
// try to upload .exe disguised as .pdf"); the backend re-validates authoritatively.

export type AllowedMime =
  | 'image/jpeg'
  | 'image/png'
  | 'image/gif'
  | 'image/webp'
  | 'application/pdf'
  | 'application/vnd.openxmlformats-officedocument.wordprocessingml.document'
  | 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet';

export const allowedAccept =
  'image/jpeg,image/png,image/gif,image/webp,application/pdf,' +
  'application/vnd.openxmlformats-officedocument.wordprocessingml.document,' +
  'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet';

export async function detectMime(file: File): Promise<AllowedMime | null> {
  const head = new Uint8Array(await file.slice(0, 12).arrayBuffer());

  if (head.length < 4) return null;
  if (startsWith(head, [0xff, 0xd8, 0xff])) return 'image/jpeg';
  if (startsWith(head, [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a])) return 'image/png';
  if (startsWith(head, [0x47, 0x49, 0x46, 0x38])) return 'image/gif';
  if (startsWith(head, [0x52, 0x49, 0x46, 0x46])
      && head[8] === 0x57 && head[9] === 0x45 && head[10] === 0x42 && head[11] === 0x50)
    return 'image/webp';
  if (startsWith(head, [0x25, 0x50, 0x44, 0x46])) return 'application/pdf';
  if (startsWith(head, [0x50, 0x4b, 0x03, 0x04])) {
    // ZIP container — could be docx/xlsx. Decline ambiguity client-side and let the
    // server's ZipArchive lookup decide. Returning null here just disables the local
    // accept; the server still receives + validates the bytes.
    return null;
  }
  return null;
}

function startsWith(buf: Uint8Array, prefix: number[]): boolean {
  if (buf.length < prefix.length) return false;
  for (let i = 0; i < prefix.length; i++) if (buf[i] !== prefix[i]) return false;
  return true;
}
