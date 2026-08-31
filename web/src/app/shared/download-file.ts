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

/** Opens an already-fetched PDF Blob in a browser tab (Phase 20d's Print action) -- the browser's
 * own PDF viewer supplies Print/Save, so this app doesn't need its own print UI. Pass the Window
 * returned by `openBlankTabForPrint()` (opened synchronously in the click handler, before the
 * network round-trip) -- most browsers block `window.open()` called from an async callback (an
 * HTTP response handler, here) as an unrequested popup, since it's no longer inside the original
 * click's call stack. Falls back to a fresh `window.open` if the tab was blocked or closed, though
 * that fallback is likely to be blocked too. The object URL is intentionally not revoked
 * immediately (unlike triggerBlobDownload's synchronous click) -- the tab needs it to still be
 * alive after this function returns. */
export function openBlobInNewTab(blob: Blob, tab: Window | null): void {
  const url = URL.createObjectURL(blob);
  if (tab && !tab.closed) {
    tab.location.href = url;
  } else {
    window.open(url, '_blank');
  }
}

/** Opens a blank tab synchronously -- call this directly inside a (click) handler, before
 * subscribing to the print request, so the later `openBlobInNewTab` navigation isn't treated as a
 * popup. See that function's doc comment for why the two are split. */
export function openBlankTabForPrint(): Window | null {
  return window.open('', '_blank');
}
