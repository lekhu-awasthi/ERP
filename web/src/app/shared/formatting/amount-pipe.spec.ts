import { AmountPipe } from './amount-pipe';

/**
 * NFR-1.2. Note the testing bar's warning: <b>a value under 100,000 formats identically under both
 * the Western and the lakh/crore convention</b>, so asserting 1,234.50 proves nothing at all about
 * this pipe. Every grouping assertion below is on a value at or above one lakh.
 */
describe('AmountPipe', () => {
  const pipe = new AmountPipe();

  it('groups in lakhs and crores, not thousands', () => {
    expect(pipe.transform(100_000)).toBe('1,00,000.00');
    expect(pipe.transform(1_000_000)).toBe('10,00,000.00');
    expect(pipe.transform(10_000_000)).toBe('1,00,00,000.00');
    expect(pipe.transform(1_000_000_000)).toBe('1,00,00,00,000.00');
  });

  it('is demonstrably not Western grouping', () => {
    // The same values under en-US would be 100,000.00 / 1,000,000.00 / 10,000,000.00.
    expect(pipe.transform(100_000)).not.toBe('100,000.00');
    expect(pipe.transform(1_000_000)).not.toBe('1,000,000.00');
    expect(pipe.transform(10_000_000)).not.toBe('10,000,000.00');
  });

  it('groups negatives the same way, with the sign outside', () => {
    expect(pipe.transform(-1_378_340.43)).toBe('-13,78,340.43');
    expect(pipe.transform(-20_117_787_530)).toBe('-20,11,77,87,530.00');
  });

  it('always renders exactly two decimal places', () => {
    expect(pipe.transform(0)).toBe('0.00');
    expect(pipe.transform(5)).toBe('5.00');
    expect(pipe.transform(1234.5)).toBe('1,234.50');
    expect(pipe.transform(1234.567)).toBe('1,234.57');
  });

  it('normalises negative zero, which would otherwise read as a real negative', () => {
    expect(pipe.transform(-0)).toBe('0.00');
    expect(pipe.transform(-0.0001)).toBe('0.00');
  });

  it('renders nullish and non-numeric input as empty rather than throwing', () => {
    expect(pipe.transform(null)).toBe('');
    expect(pipe.transform(undefined)).toBe('');
    expect(pipe.transform('')).toBe('');
    expect(pipe.transform(Number.NaN)).toBe('');
    expect(pipe.transform(Number.POSITIVE_INFINITY)).toBe('');
  });

  it('accepts a numeric string, since some DTO fields arrive as strings', () => {
    expect(pipe.transform('2500000')).toBe('25,00,000.00');
  });

  it('rounds half away from zero (the documented departure from .toFixed(2))', () => {
    // (1.005).toFixed(2) is "1.00" because of binary float representation; Intl gives "1.01".
    // Asserted so the change is pinned rather than rediscovered as a bug.
    expect(pipe.transform(1.005)).toBe('1.01');
    expect(pipe.transform(2.675)).toBe('2.68');
  });

  it('takes an optional precision, defaulting to two (Phase 25)', () => {
    // The production cost roll-up's rounding residue is smaller than a cent by construction, so at
    // the default precision it renders as "0.00" -- a row that looks like a defect rather than a
    // disclosure. Found in Phase 25's browser pass.
    expect(pipe.transform(0.0001)).toBe('0.00');
    expect(pipe.transform(0.0001, 4)).toBe('0.0001');
    expect(pipe.transform(-0.008, 4)).toBe('-0.0080');

    // The default is unchanged, which is what keeps all 324 existing call sites correct.
    expect(pipe.transform(1234567.891)).toBe('12,34,567.89');
    expect(pipe.transform(1234567.891, 4)).toBe('12,34,567.8910');
  });

  it('never renders a negative that rounds to all zeros as "-0", at any precision', () => {
    expect(pipe.transform(-0.001)).toBe('0.00');
    expect(pipe.transform(-0.000001, 4)).toBe('0.0000');
  });
});
