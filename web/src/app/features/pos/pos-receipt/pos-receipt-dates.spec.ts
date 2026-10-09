/// <reference types="vite/client" />
import { Type } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { PosReceipt, PosRefundReceipt } from '../../../core/pos/pos.models';
import { DatePreferenceService } from '../../../shared/formatting/date-preference';
import { toBillDisplay } from '../../../shared/formatting/nepali-date-pipe';
import { PosReceiptView } from './pos-receipt';
import { PosRefundReceiptView } from './pos-refund-receipt';

/**
 * Phase 71 -- a till receipt is a bill, so its dates are BS with AD beside it whatever the viewer's calendar toggle
 * says, and a full tax invoice carries both of its form's dates (transaction and issue) where an abbreviated one
 * carries one. 2026-08-01 AD is 2083-04-16 BS; 2026-08-01T09:00:00Z is 14:45 in Kathmandu.
 */
const SALE = {
  invoiceId: 'i1', code: 'INV0001', title: 'TaxInvoice', printNumber: 1,
  printedAt: '2026-08-01T09:05:00Z', printedByName: 'Asha',
  sellerName: 'Moonbeam', sellerAddress: null, sellerPan: '301234567', locationName: 'Head Office',
  date: '2026-08-01', soldAt: '2026-08-01T09:00:00Z', sessionCode: 'S1', cashierName: 'Asha',
  orderType: null, customerName: 'Acme', customerAddress: null, customerPan: null, isWalkIn: false,
  lines: [], grossAmount: 100, discountAmount: 0, subTotal: 100, serviceCharge: 0, packagingFee: 0,
  taxableAmount: 100, nonTaxableAmount: 0, vat: 13, roundOff: 0, grandTotal: 113, amountInWords: 'One hundred thirteen',
  tenders: [], tendered: 113, changeAmount: 0, creditAmount: 0,
} as unknown as PosReceipt;

const REFUND = {
  creditNoteId: 'c1', code: 'CN0001', printNumber: 1, printedAt: '2026-08-02T09:05:00Z', printedByName: 'Asha',
  sellerName: 'Moonbeam', sellerAddress: null, sellerPan: '301234567', sellerVatRegistered: true, locationName: 'Head Office',
  date: '2026-08-02', refundedAt: '2026-08-02T09:00:00Z', sessionCode: 'S1', cashierName: 'Asha',
  invoiceCode: 'INV0001', invoiceDate: '2026-08-01', customerName: 'Acme', customerAddress: null, customerPan: null,
  isWalkIn: false, reason: 'Damaged', lines: [], grossAmount: 100, discountAmount: 0, subTotal: 100, serviceCharge: 0,
  packagingFee: 0, taxableAmount: 100, nonTaxableAmount: 0, vat: 13, roundOff: 0, grandTotal: 113,
  amountInWords: 'One hundred thirteen', payouts: [], paidOut: 113, toAccount: 0,
} as unknown as PosRefundReceipt;

/** The no-break spaces that keep a date beside its BS/AD label, read back as plain spaces (pinned on their own below). */
const plain = (text: string | null) => (text ?? '').replace(/[\u00a0\s]+/g, ' ').trim();

function rows(host: HTMLElement): Record<string, string> {
  const out: Record<string, string> = {};
  host.querySelectorAll('.r-row').forEach((row) => {
    const [label, value] = Array.from(row.querySelectorAll('span')).map((s) => plain(s.textContent));
    if (label) {
      out[label] = value;
    }
  });
  return out;
}

function render<T>(component: Type<T>, input: object, bs: boolean): HTMLElement {
  TestBed.configureTestingModule({
    imports: [component],
    providers: [{ provide: DatePreferenceService, useValue: { isBs: () => bs } }],
  });
  const fixture = TestBed.createComponent(component);
  fixture.componentRef.setInput('receipt', input);
  fixture.detectChanges();
  return fixture.nativeElement as HTMLElement;
}

describe('Phase 71 -- a bill dates itself in BS with AD beside it', () => {
  it('renders a bill date labelled in both calendars, and a labelled AD date outside the BS table', () => {
    // A no-break space ties each date to its label, so a narrow receipt can wrap only between the two halves.
    // ... and a line break between the halves, which the receipt's pre-line cells honour (BS over AD).
    expect(toBillDisplay('2026-08-01')).toBe('16-04-2083\u00a0BS\n(01-08-2026\u00a0AD)');
    expect(toBillDisplay('2040-01-01')).toBe('01-01-2040\u00a0AD');
  });

  for (const bs of [false, true]) {
    it(`prints a full tax invoice's two dates in BS with AD whatever the toggle (${bs ? 'BS' : 'AD'})`, () => {
      const r = rows(render(PosReceiptView, SALE, bs));

      expect(r['Date']).toBe('16-04-2083 BS (01-08-2026 AD)');
      expect(r['Issue Date']).toBe('16-04-2083 BS (01-08-2026 AD)');
      expect(r['Time']).toBe('14:45');
    });

    it(`prints a refund's date, issue date and its bill's date the same way (${bs ? 'BS' : 'AD'})`, () => {
      const host = render(PosRefundReceiptView, REFUND, bs);
      const r = rows(host);

      expect(r['Date']).toBe('17-04-2083 BS (02-08-2026 AD)');
      expect(r['Issue Date']).toBe('17-04-2083 BS (02-08-2026 AD)');
      expect(r['Bill Date']).toBe('16-04-2083 BS (01-08-2026 AD)');
      expect(plain(host.textContent)).toContain('Printed 17-04-2083 BS (02-08-2026 AD) 14:50 by Asha');
    });
  }

  it('prints an abbreviated tax invoice with its one date (Schedule 6)', () => {
    const r = rows(render(PosReceiptView, { ...SALE, title: 'AbbreviatedTaxInvoice' }, false));

    expect(r['Date']).toBe('16-04-2083 BS (01-08-2026 AD)');
    expect(r['Issue Date']).toBeUndefined();
  });
});

/**
 * The guard: every date the two receipts render names the bill calendar. A new `| nepaliDate` added to either
 * without `'bill'` would follow the toggle again, which is the bug this phase fixed; only a time alone, which has no
 * calendar, is exempt.
 */
const receiptSources = import.meta.glob('/src/app/features/pos/pos-receipt/pos-*receipt.ts', {
  query: '?raw',
  import: 'default',
  eager: true,
}) as Record<string, string>;

describe('Phase 71 guard -- a receipt never renders a date in the toggle calendar', () => {
  it('finds both receipts (guards against a glob that matches nothing)', () => {
    expect(Object.keys(receiptSources).sort()).toEqual([
      '/src/app/features/pos/pos-receipt/pos-receipt.ts',
      '/src/app/features/pos/pos-receipt/pos-refund-receipt.ts',
    ]);
  });

  it("passes 'bill' to every nepaliDate except a time alone", () => {
    const offenders: string[] = [];
    for (const [path, source] of Object.entries(receiptSources)) {
      const uses = source.match(/\|\s*nepaliDate[^}]*/g) ?? [];
      expect(uses.length, `${path} renders no date at all`).toBeGreaterThan(0);
      for (const use of uses) {
        if (!/'bill'/.test(use) && !/:\s*'time'/.test(use)) {
          offenders.push(`${path}: ${use.trim()}`);
        }
      }
    }

    expect(offenders, `A bill's date must be '| nepaliDate: <mode> : 'bill''`).toEqual([]);
  });
});
