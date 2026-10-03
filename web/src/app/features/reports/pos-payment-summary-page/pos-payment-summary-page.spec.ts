import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { of } from 'rxjs';

import { ConfigurationService } from '../../../core/configuration/configuration.service';
import { PosPaymentSummary, PosPaymentType } from '../../../core/pos/pos-reports.models';
import { PosReportsService } from '../../../core/pos/pos-reports.service';
import { organizationsStub, visibleText } from '../../../core/pos/pos-reports.testing';
import { PosPaymentSummaryPage } from './pos-payment-summary-page';

/**
 * Phase 66 -- the Payment Summary lists what was handed over and the change apart (the vendor netted
 * them into one row), and its type totals and grand total are the server's, over every row.
 */
describe('PosPaymentSummaryPage', () => {
  const summary: PosPaymentSummary = {
    fromDate: '2026-10-02', toDate: '2026-10-02', page: 1, pageSize: 50, totalCount: 3,
    items: [
      { date: '2026-10-02', at: '2026-10-02T06:00:00Z', documentType: 'Invoice', documentId: 'inv-1', code: 'INV0001',
        locationId: 'loc-1', locationName: 'Thamel', cashier: 'Sita', contactId: 'w', contactName: 'Walk-in',
        entry: 'Tender', type: 'Cash', paymentModeId: 'm-cash', paymentModeName: 'Cash', accountName: 'Cash In Hand', amount: 1000 },
      { date: '2026-10-02', at: '2026-10-02T06:00:00Z', documentType: 'Invoice', documentId: 'inv-1', code: 'INV0001',
        locationId: 'loc-1', locationName: 'Thamel', cashier: 'Sita', contactId: 'w', contactName: 'Walk-in',
        entry: 'Change', type: 'Cash', paymentModeId: 'm-cash', paymentModeName: 'Cash', accountName: 'Cash In Hand', amount: -367 },
      { date: '2026-10-02', at: '2026-10-02T07:00:00Z', documentType: 'CreditNote', documentId: 'cn-1', code: 'CN0001',
        locationId: 'loc-1', locationName: 'Thamel', cashier: 'Sita', contactId: 'w', contactName: 'Walk-in',
        entry: 'Payout', type: 'Cash', paymentModeId: 'm-cash', paymentModeName: 'Cash', accountName: 'Cash In Hand', amount: -249 },
    ],
    totals: [{ type: 'Cash', amount: 384 }],
    total: 384,
  };

  function page() {
    const types: (PosPaymentType | null)[] = [];
    TestBed.configureTestingModule({
      imports: [PosPaymentSummaryPage],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        organizationsStub,
        { provide: ConfigurationService, useValue: { listPaymentModes: () => of([]) } },
        {
          provide: PosReportsService,
          useValue: {
            getPaymentSummary: (_o: string, _p: unknown, type: PosPaymentType | null) => {
              types.push(type);
              return of(summary);
            },
          },
        },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: { get: () => 'org-1' } } } },
      ],
    });

    const fixture = TestBed.createComponent(PosPaymentSummaryPage);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    return { fixture, element, types, text: () => visibleText(element) };
  }

  afterEach(() => TestBed.resetTestingModule());

  it('lists the tender, the change and the payout as rows of their own, linked to their documents', () => {
    const p = page();
    expect(p.text()).toContain('Paid Cash Cash Cash In Hand 1,000.00');
    expect(p.text()).toContain('Change given Cash Cash Cash In Hand -367.00');
    expect(p.text()).toContain('Paid back Cash Cash Cash In Hand -249.00');
    const refund = [...p.element.querySelectorAll('a')].find((a) => a.textContent?.trim() === 'CN0001');
    expect(refund?.getAttribute('href')).toBe('/organizations/org-1/sales/credit-notes/cn-1');
  });

  it('totals by type and in all from the server', () => {
    const p = page();
    expect(p.text()).toContain('Total 384.00');
  });

  it('asks for one payment type when one is chosen', () => {
    const p = page();
    const select = p.element.querySelector<HTMLSelectElement>('#pos-payment-summary-page-type')!;
    select.value = 'Credit';
    select.dispatchEvent(new Event('change'));
    p.fixture.detectChanges();
    expect(p.types).toEqual([null, 'Credit']);
  });
});
