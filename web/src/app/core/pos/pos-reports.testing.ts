import { of } from 'rxjs';

import { OrganizationsService } from '../organizations/organizations.service';
import { PosRefundsSummary, PosSalesSummary } from './pos.models';
import { PosPaymentBreakdown, PosSalesPoint } from './pos-reports.models';

/**
 * Phase 66 -- fixtures for the POS report specs: phase 59's written service as our reader reports it
 * (three sales of 633, 316 and 249, one refund of a momo for 249), so the specs read real arithmetic.
 */
export function refundsSummary(overrides: Partial<PosRefundsSummary> = {}): PosRefundsSummary {
  return {
    refundsCount: 1, subTotal: 200, serviceCharge: 20, vat: 28.6, roundOff: 0.4, grandTotal: 249,
    payouts: [{ paymentModeId: 'm-cash', paymentModeName: 'Cash', kind: 'Cash', amount: 249 }],
    paidOut: 249, toAccount: 0, cashRefunds: 249, taxable: 220, nonTaxable: 0,
    ...overrides,
  };
}

export function salesSummary(overrides: Partial<PosSalesSummary> = {}): PosSalesSummary {
  return {
    salesCount: 3, subTotal: 980, serviceCharge: 80, vat: 137.8, roundOff: 0.2, grandTotal: 1198,
    tenders: [
      { paymentModeId: 'm-cash', paymentModeName: 'Cash', kind: 'Cash', amount: 1300 },
      { paymentModeId: 'm-card', paymentModeName: 'Card', kind: 'Card', amount: 116 },
    ],
    tendered: 1416, change: 367, settled: 1049, credit: 149, cashSales: 933,
    refunds: refundsSummary(), netSales: 949, taxable: 1060, nonTaxable: 0,
    netRoundOff: -0.2, netCash: 684, netCredit: 149,
    ...overrides,
  };
}

export function paymentsBreakdown(): PosPaymentBreakdown {
  return {
    modes: [
      { paymentModeId: 'm-cash', paymentModeName: 'Cash', kind: 'Cash', received: 1300, paidBack: 249, net: 1051 },
      { paymentModeId: 'm-card', paymentModeName: 'Card', kind: 'Card', received: 116, paidBack: 0, net: 116 },
    ],
    change: 367,
    credit: 149,
    total: 949,
  };
}

/** A day's 24 hours, with the day's net in the noon hour and one refund-only hour. */
export function hourlySeries(date: string): PosSalesPoint[] {
  return Array.from({ length: 24 }, (_, hour) => ({
    date,
    hour,
    salesCount: hour === 12 ? 3 : 0,
    sales: hour === 12 ? 1198 : 0,
    refunds: hour === 15 ? 249 : 0,
    net: hour === 12 ? 1198 : hour === 15 ? -249 : 0,
  }));
}

/** What ReportLocationFilter's store reads: a single-location tenant, on which it renders nothing. */
export const organizationsStub = {
  provide: OrganizationsService,
  useValue: {
    listWarehouses: () => of([]),
    listBillingLocations: () => of([]),
    getBillingLocationSettings: () =>
      of({
        locationScopeMode: 'SalesTransactionsOnly',
        locationWiseReportPermission: false,
        multipleLocationsEnabled: false,
        locationBearingDocumentTypes: [],
      }),
  },
};

/** An element's text as a reader hears it: every text node, trimmed, one space apart -- so table cells
 *  read as separate words, which textContent runs together. */
export function visibleText(element: Element): string {
  const walker = element.ownerDocument.createTreeWalker(element, NodeFilter.SHOW_TEXT);
  const parts: string[] = [];
  while (walker.nextNode()) {
    const text = walker.currentNode.textContent?.replace(/\s+/g, ' ').trim();
    if (text) parts.push(text);
  }
  return parts.join(' ');
}
