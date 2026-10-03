import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { of } from 'rxjs';

import { PosDayReport } from '../../../core/pos/pos-reports.models';
import { PosReportPeriod, PosReportsService } from '../../../core/pos/pos-reports.service';
import { hourlySeries, organizationsStub, paymentsBreakdown, salesSummary, visibleText } from '../../../core/pos/pos-reports.testing';
import { nepalToday } from '../../../shared/formatting/nepal-time';
import { shiftDate } from '../../../shared/pos/pos-period-filter';
import { PosDayReportPage } from './pos-day-report-page';

/**
 * Phase 66 -- the Day Report prints the round off the vendor's left out, and the line that takes the
 * till's net sales to the Sales Register's, and counts voids without summing them.
 */
describe('PosDayReportPage', () => {
  function report(): PosDayReport {
    const today = nepalToday();
    return {
      fromDate: today, toDate: today, locationId: null,
      sales: salesSummary(), payments: paymentsBreakdown(), bucket: 'Hour', series: hourlySeries(today),
      voidedSales: 1, voidedRefunds: 0, voidedOrders: 2,
      sessions: [{
        id: 'ses-1', code: 'SES0001', locationId: 'loc-1', locationName: 'Thamel', userId: 'u1', userName: 'Sita',
        status: 'Closed', openedAt: '2026-10-02T03:00:00Z', closedAt: '2026-10-02T12:00:00Z', openingFloat: 1000,
        cashIn: 0, cashOut: 0, countedCash: 1679, cashDifference: -5,
      }],
    };
  }

  function page() {
    const asks: PosReportPeriod[] = [];
    TestBed.configureTestingModule({
      imports: [PosDayReportPage],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        organizationsStub,
        {
          provide: PosReportsService,
          useValue: {
            getDayReport: (_org: string, period: PosReportPeriod) => {
              asks.push(period);
              return of(report());
            },
            exportDayReport: () => of(new Blob()),
          },
        },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: { get: () => 'org-1' } } } },
      ],
    });

    const fixture = TestBed.createComponent(PosDayReportPage);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    return {
      element,
      asks,
      text: () => visibleText(element),
      press: (label: string) => {
        [...element.querySelectorAll<HTMLButtonElement>('button')].find((b) => b.textContent?.trim() === label)!.click();
        fixture.detectChanges();
      },
    };
  }

  afterEach(() => TestBed.resetTestingModule());

  it('prints the round off and the Sales Register figure, which is net sales less it', () => {
    const p = page();
    expect(p.text()).toContain('Net sales 949.00');
    expect(p.text()).toContain('of which round off (sales less refunds) -0.20');
    expect(p.text()).toContain('Net sales in the Sales Register The register lists supplies, and a round off is not one. 949.20');
  });

  it('shows sales and refunds apart, each with its round off, and counts voids without summing them', () => {
    const p = page();
    expect(p.text()).toContain('Round off 0.20 Total sales 1,198.00');
    expect(p.text()).toContain('Round off 0.40 Total refunds 249.00');
    expect(p.text()).toContain('Voided sales (not in any figure) 1');
    expect(p.text()).toContain('Voided restaurant orders 2');
  });

  it('adds the payments up to net sales, change handed back as a negative', () => {
    const p = page();
    expect(p.text()).toContain('Cash 1,300.00 249.00 1,051.00');
    expect(p.text()).toContain('Change given -367.00');
    expect(p.text()).toContain('Left on customers\' accounts 149.00 Net sales 949.00');
  });

  it('links each session to its own page and shows a short count', () => {
    const p = page();
    const link = [...p.element.querySelectorAll('a')].find((a) => a.textContent?.trim() === 'SES0001');
    expect(link?.getAttribute('href')).toBe('/organizations/org-1/pos/sessions/ses-1');
    expect(p.text()).toContain('1,679.00 -5.00');
  });

  it('reads today first, and the period a preset names', () => {
    const p = page();
    const today = nepalToday();
    expect(p.asks[0]).toEqual({ fromDate: today, toDate: today, locationId: '' });
    p.press('Last 30 days');
    expect(p.asks[1]).toEqual({ fromDate: shiftDate(today, -29), toDate: today, locationId: '' });
  });
});
