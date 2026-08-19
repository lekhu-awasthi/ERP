/** Triggers a browser save of an already-fetched Blob (report exports, Phase 16c) -- no library
 * needed, the server does all the file generation. */
export function triggerBlobDownload(blob: Blob, fileName: string): void {
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = fileName;
  a.click();
  URL.revokeObjectURL(url);
}
