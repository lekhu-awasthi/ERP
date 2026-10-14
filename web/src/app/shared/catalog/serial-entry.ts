/**
 * The serial-number box on the Invoice, Purchase Bill and Credit Note forms, as a barcode scanner uses it.
 *
 * A scanner is a keyboard: it types the code and presses Enter. In a one-line text box that Enter did nothing, so
 * the next scan ran straight on (`IMEI91AIMEI91B`) and the line went to the server naming one serial for two units.
 * Enter now ends a serial: the list is tidied and a separator written, so scan, scan, scan reads
 * `IMEI91A, IMEI91B, IMEI91C, `. A repeat is taken out there and then, the way the server compares serials (trimmed,
 * case-insensitive), instead of waiting for the save to refuse the line.
 */

/** Commas and newlines, trimmed, blanks dropped: what a line sends. */
export function parseSerials(text: string): string[] {
  return text
    .split(/[,\n]/)
    .map((s) => s.trim())
    .filter((s) => s.length > 0);
}

/** What Enter leaves in the box, and the serial it took out as a repeat (null when there was none). */
export interface SerialEntry {
  text: string;
  repeated: string | null;
}

/** Enter in the box: drop repeats (the first naming wins) and end with a separator, ready for the next scan. */
export function acceptSerialEntry(text: string): SerialEntry {
  const seen = new Set<string>();
  const kept: string[] = [];
  let repeated: string | null = null;

  for (const serial of parseSerials(text)) {
    const key = serial.toLowerCase();

    if (seen.has(key)) {
      repeated ??= serial;
      continue;
    }

    seen.add(key);
    kept.push(serial);
  }

  return { text: kept.length > 0 ? kept.join(', ') + ', ' : '', repeated };
}

/** "2 of 3 serial numbers", for the line's hint; empty while the box is. */
export function serialProgress(text: string, quantity: number): string {
  const count = parseSerials(text).length;
  return count === 0 ? '' : `${count} of ${quantity} serial number${quantity === 1 ? '' : 's'}`;
}
