import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { of } from 'rxjs';

import { PosOrderReport } from '../../../core/pos/pos-reports.models';
import { PosReportsService } from '../../../core/pos/pos-reports.service';
import { PosOrderStatus } from '../../../core/pos/pos-restaurant.models';
import { organizationsStub, visibleText } from '../../../core/pos/pos-reports.testing';
import { PosOrderReportPage } from './pos-order-report-page';

/**
 * Phase 66 -- the Order Report: every bill of a split order, what is still to bill, and the counts and
 * totals over the whole filter.
 */
describe('PosOrderReportPage', () => {
  const report: PosOrderReport = {
    fromDate: '2026-10-02', toDate: '2026-10-02', page: 1, pageSize: 50, totalCount: 2,
    openCount: 1, settledCount: 1, voidedCount: 0, billed: 633, toBill: 248.6,
    items: [
      {
        id: 'o-1', code: 'ORD0001', date: '2026-10-02', createdAt: '2026-10-02T06:00:00Z', status: 'Settled',
        orderType: 'DineIn', locationId: 'loc-1', locationName: 'Thamel', areaName: 'Ground Floor', tableName: 'T1',
        covers: 2, contactName: 'Walk-in', createdByName: 'Sita', orderValue: 632.8, billed: 633, billedRoundOff: 0.2,
        toBill: 0, voidReason: null, settledAt: '2026-10-02T07:00:00Z',
        invoices: [
          { id: 'inv-1', code: 'INV0001', grandTotal: 316, serviceCharge: 20, roundOff: -0.4, isVoided: false },
          { id: 'inv-2', code: 'INV0002', grandTotal: 317, serviceCharge: 20, roundOff: 0.6, isVoided: false },
        ],
      },
      {
        id: 'o-2', code: 'ORD0002', date: '2026-10-02', createdAt: '2026-10-02T08:00:00Z', status: 'Open',
        orderType: 'DineIn', locationId: 'loc-1', locationName: 'Thamel', areaName: 'Ground Floor', tableName: 'T2',
        covers: 2, contactName: 'Walk-in', createdByName: 'Sita', orderValue: 248.6, billed: 0, billedRoundOff: 0,
        toBill: 248.6, voidReason: null, settledAt: null, invoices: [],
      },
    ],
  };

  function page() {
    const statuses: (PosOrderStatus | null)[] = [];
    TestBed.configureTestingModule({
      imports: [PosOrderReportPage],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        organizationsStub,
        {
          provide: PosReportsService,
          useValue: {
            getOrderReport: (_o: string, _p: unknown, status: PosOrderStatus | null) => {
              statuses.push(status);
              return of(report);
            },
          },
        },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: { get: () => 'org-1' } } } },
      ],
    });

    const fixture = TestBed.createComponent(PosOrderReportPage);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    return { fixture, element, statuses, text: () => visibleText(element) };
  }

  afterEach(() => TestBed.resetTestingModule());

  it('lists both bills of a split order, each linked to its invoice', () => {
    const p = page();
    const links = [...p.element.querySelectorAll('a')].map((a) => [a.textContent?.trim(), a.getAttribute('href')]);
    expect(links).toContainEqual(['INV0001', '/organizations/org-1/sales/invoices/inv-1']);
    expect(links).toContainEqual(['INV0002', '/organizations/org-1/sales/invoices/inv-2']);
    expect(p.text()).toContain('632.80 633.00 0.00');
  });

  it('counts the orders in each state and totals what was billed and what the open ones still owe', () => {
    const p = page();
    expect(p.text()).toContain('2 order(s): 1 open, 1 settled, 0 voided.');
    expect(p.text()).toContain('Billed (all orders in the filter) 633.00');
    expect(p.text()).toContain('Still to bill (open orders) 248.60');
  });

  it('asks for one status when one is chosen', () => {
    const p = page();
    const select = p.element.querySelector<HTMLSelectElement>('#pos-order-report-page-status')!;
    select.value = 'Voided';
    select.dispatchEvent(new Event('change'));
    p.fixture.detectChanges();
    expect(p.statuses).toEqual([null, 'Voided']);
  });
});
