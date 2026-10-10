/// <reference types="vite/client" />

import { VatRate } from '../catalog/catalog.models';
import { BillLineInput, computeBill, fromPaisa, toPaisa } from './pos-bill';
import { PackagingFeeBasis } from './pos.models';

/**
 * Phase 62 -- the client's half of the contract in `pos-bill-cases.json`; the server's half is
 * `PosBillSharedCasesTests`, which embeds the same file. The till shows a total the cashier takes
 * money against before the server has seen the sale, so the two must agree to the paisa.
 *
 * Phase 70 -- a line's `packagingFeeBasis`/`packagingFeeValue` and an expectation's `packagingFee` are
 * optional in the file; absent means none, exactly as the server reads them.
 *
 * Phase 74 -- the same for a line's `discountPerUnit` and a case's `discountAmount`.
 */

const fixture = import.meta.glob('./pos-bill-cases.json', {
  query: '?raw',
  import: 'default',
  eager: true,
}) as Record<string, string>;

interface SharedCase {
  readonly why: string;
  readonly roundOff: boolean;
  readonly discountPct: number;
  readonly discountAmount?: number;
  readonly lines: readonly {
    quantity: number;
    rate: number;
    vatRate: VatRate;
    discountPct: number;
    serviceChargeRate: number;
    discountPerUnit?: number;
    packagingFeeBasis?: PackagingFeeBasis;
    packagingFeeValue?: number;
  }[];
  readonly expected: {
    lines: readonly { amount: number; serviceCharge: number; packagingFee?: number; vat: number; total: number }[];
    subTotal: number;
    serviceCharge: number;
    packagingFee?: number;
    vat: number;
    roundOff: number;
    grandTotal: number;
  };
}

function sharedCases(): readonly SharedCase[] {
  const raw = fixture['./pos-bill-cases.json'];

  // Phase 34a: assert the input is non-empty, not merely defined -- Vite hands back '' for a `?raw`
  // it could not resolve, and every assertion over it would pass vacuously.
  expect(typeof raw).toBe('string');
  expect(raw.length).toBeGreaterThan(1000);

  return (JSON.parse(raw) as { cases: SharedCase[] }).cases;
}

function inputs(c: SharedCase): BillLineInput[] {
  return c.lines.map((l) => ({
    ...l,
    packagingFeeBasis: l.packagingFeeBasis ?? 'None',
    packagingFeeValue: l.packagingFeeValue ?? 0,
    discountPerUnit: l.discountPerUnit ?? 0,
  }));
}

describe('the shared till-bill contract', () => {
  it('has not lost its cases', () => {
    expect(sharedCases().length).toBeGreaterThanOrEqual(20);

    // Phase 70 -- both bases are in the table, so neither is silently read as no fee.
    const bases = sharedCases().flatMap((c) => c.lines.map((l) => l.packagingFeeBasis ?? 'None'));
    expect(bases).toContain('Percent');
    expect(bases).toContain('PerUnit');

    // Phase 74 -- and both Amount discounts.
    expect(sharedCases().some((c) => (c.discountAmount ?? 0) > 0)).toBe(true);
    expect(sharedCases().some((c) => c.lines.some((l) => (l.discountPerUnit ?? 0) > 0))).toBe(true);
  });

  for (const [index, c] of sharedCases().entries()) {
    it(`bills case ${index + 1} exactly as the server does: ${c.why}`, () => {
      const bill = computeBill(inputs(c), c.discountPct, c.roundOff, c.discountAmount ?? 0);

      expect(bill.lines).toEqual(c.expected.lines.map((l) => ({ ...l, packagingFee: l.packagingFee ?? 0 })));
      expect(bill.subTotal).toBe(c.expected.subTotal);
      expect(bill.serviceCharge).toBe(c.expected.serviceCharge);
      expect(bill.packagingFee).toBe(c.expected.packagingFee ?? 0);
      expect(bill.vat).toBe(c.expected.vat);
      expect(bill.roundOff).toBe(c.expected.roundOff);
      expect(bill.grandTotal).toBe(c.expected.grandTotal);
    });
  }

  it('would have got the float case wrong without integer arithmetic', () => {
    // The point of the bigint path, stated: naive rounding of 1.005 gives 1.00.
    expect(Math.round(1.005 * 100) / 100).toBe(1);
    expect(
      computeBill(
        [{ quantity: 1, rate: 1.005, vatRate: 'NoVat', discountPct: 0, discountPerUnit: 0, serviceChargeRate: 0, packagingFeeBasis: 'None', packagingFeeValue: 0 }],
        0,
        false,
        0,
      ).grandTotal,
    ).toBe(1.01);
  });
});

describe('tender arithmetic in paisa', () => {
  it('adds tenders without float drift', () => {
    expect(0.1 + 0.2).not.toBe(0.3);
    expect(fromPaisa(toPaisa(0.1) + toPaisa(0.2))).toBe(0.3);
  });
});
