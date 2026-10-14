import { acceptSerialEntry, parseSerials, serialProgress } from './serial-entry';

describe('serial entry', () => {
  it('parses commas and newlines, trimmed, blanks dropped', () => {
    expect(parseSerials(' IMEI91A,IMEI91B ,\n, IMEI91C, ')).toEqual(['IMEI91A', 'IMEI91B', 'IMEI91C']);
  });

  it('ends a scanned serial with a separator so the next scan starts a new one', () => {
    expect(acceptSerialEntry('IMEI91A')).toEqual({ text: 'IMEI91A, ', repeated: null });
    expect(acceptSerialEntry('IMEI91A, IMEI91B')).toEqual({ text: 'IMEI91A, IMEI91B, ', repeated: null });
  });

  it('leaves an empty box empty and does not stack separators', () => {
    expect(acceptSerialEntry('')).toEqual({ text: '', repeated: null });
    expect(acceptSerialEntry('IMEI91A, ')).toEqual({ text: 'IMEI91A, ', repeated: null });
  });

  it('takes a repeat out, as the server compares them (case-insensitive), and names it', () => {
    expect(acceptSerialEntry('IMEI91A, IMEI91B, imei91a')).toEqual({ text: 'IMEI91A, IMEI91B, ', repeated: 'imei91a' });
  });

  it('counts the serials against the quantity', () => {
    expect(serialProgress('', 3)).toBe('');
    expect(serialProgress('IMEI91A, IMEI91B, ', 3)).toBe('2 of 3 serial numbers');
    expect(serialProgress('IMEI91A', 1)).toBe('1 of 1 serial number');
  });
});
