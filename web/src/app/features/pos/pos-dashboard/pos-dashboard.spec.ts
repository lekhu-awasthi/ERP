import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Observable, of, throwError } from 'rxjs';

import { PosDashboard } from '../../../core/pos/pos-reports.models';
import { PosReportPeriod, PosReportsService } from '../../../core/pos/pos-reports.service';
import {
  hourlySeries,
  organizationsStub,
  paymentsBreakdown,
  salesSummary,
  visibleText,
} from '../../../core/pos/pos-reports.testing';
import { nepalToday } from '../../../shared/formatting/nepal-time';
import { shiftDate } from '../../../shared/pos/pos-period-filter';
import { PosDashboardPanel } from './pos-dashboard';

/**
 * Phase 66 -- the POS overview. What matters is that its panels add up to its tiles on the screen (the
 * vendor's said 611 over a products panel of 610.20), that its chart draws its points, and that a
 * cashier without the key gets no overview rather than a wall of refusals.
 */
describe('PosDashboardPanel', () => {
  const organizationId = 'org-1';

  function dashboard(overrides: Partial<PosDashboard> = {}): PosDashboard {
    const today = nepalToday();
    return {
      fromDate: today, toDate: today, locationId: null,
      sales: salesSummary(),
      payments: paymentsBreakdown(),
      bucket: 'Hour',
      series: hourlySeries(today),
      topProducts: [
        { productId: 'p-momo', code: 'P1', name: 'Chicken Momo', unit: 'Plate', quantity: 3, total: 745.8 },
        { productId: 'p-coke', code: 'P2', name: 'Coke 250ml', unit: 'Bottle', quantity: 3, total: 203.4 },
      ],
      otherProducts: 0,
      openOrders: 1,
      openOrdersToBill: 248.6,
      openSessions: [{ id: 'ses-1', code: 'SES0001', locationId: 'loc-1', locationName: 'Thamel', userName: 'Sita', openedAt: '2026-10-02T03:00:00Z' }],
      ...overrides,
    };
  }

  function panel(load: () => Observable<PosDashboard>) {
    const asks: PosReportPeriod[] = [];
    TestBed.configureTestingModule({
      imports: [PosDashboardPanel],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        organizationsStub,
        {
          provide: PosReportsService,
          useValue: {
            getDashboard: (_org: string, period: PosReportPeriod) => {
              asks.push(period);
              return load();
            },
          },
        },
      ],
    });

    const fixture = TestBed.createComponent(PosDashboardPanel);
    fixture.componentRef.setInput('organizationId', organizationId);
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

  it('ends the products panel with the round off, so it adds up to the Net Sales tile', () => {
    const p = panel(() => of(dashboard()));
    const products = p.element.querySelector('table[class*="table-sm"]')!;
    const rows = [...products.querySelectorAll('tbody tr, tfoot tr')].map((r) => visibleText(r));

    expect(rows).toEqual([
      'Chicken Momo 3.00 Plate 745.80',
      'Coke 250ml 3.00 Bottle 203.40',
      'Round off -0.20',
      'Net sales 949.00',
    ]);
    expect(p.text()).toContain('Net sales 949.00');
  });

  it('ends the payments panel with change and credit, so it adds up to net sales too', () => {
    const p = panel(() => of(dashboard()));
    expect(p.text()).toContain('Cash 1,051.00 Card 116.00 Change given -367.00 Left on credit 149.00 Net sales 949.00');
  });

  it('draws a bar per hour from zero, a refund-only hour below the line, and gives the numbers as a table', () => {
    const p = panel(() => of(dashboard()));
    const hidden = p.element.querySelector('table.visually-hidden')!;

    expect(hidden.querySelectorAll('tbody tr').length).toBe(24);
    expect(hidden.querySelector('caption')?.textContent).toContain('949.00 in all');
    expect(p.element.querySelectorAll('.bg-primary.rounded-1').length).toBe(1);
    expect(p.element.querySelectorAll('.bg-danger.rounded-1').length).toBe(1);
  });

  it('asks for today first, and for the last seven days when asked', () => {
    const p = panel(() => of(dashboard()));
    const today = nepalToday();

    expect(p.asks[0]).toEqual({ fromDate: today, toDate: today, locationId: '' });
    p.press('Last 7 days');
    expect(p.asks[1]).toEqual({ fromDate: shiftDate(today, -6), toDate: today, locationId: '' });
  });

  it('shows the open orders, what they still have to bill, and the drawers open now', () => {
    const p = panel(() => of(dashboard()));
    expect(p.text()).toContain('Open orders 1 248.60 still to bill');
    expect(p.text()).toContain('SES0001 Thamel Sita');
  });

  it('shows nothing at all to a cashier who may not read every drawer', () => {
    const p = panel(() => throwError(() => new HttpErrorResponse({ status: 403, error: { title: 'Pos.Session.ViewAll' } })));
    expect(p.element.querySelector('section')).toBeNull();
    expect(p.text().trim()).toBe('');
  });
});
