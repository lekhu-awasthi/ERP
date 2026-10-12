import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { of } from 'rxjs';

import { AnnexFiveReportDto, AnnexFiveReportRowDto } from '../../../core/sales/sales.models';
import { SalesService } from '../../../core/sales/sales.service';
import { currentFiscalYear } from '../../../shared/formatting/bs-fiscal-year';
import { nepalToday } from '../../../shared/formatting/nepal-time';
import { fiscalYearBounds } from '../../../shared/platform/date-range.service';
import { AnnexFiveReportPage } from './annex-five-report-page';

/**
 * Phase 89 -- Annex 5 opens on this fiscal year to date, as the vendor's does, and shows the columns this app can now
 * back: the fiscal year, the discount beside an Amount before it, the prints and who entered the bill.
 */
describe('AnnexFiveReportPage', () => {
  function row(overrides: Partial<AnnexFiveReportRowDto> = {}): AnnexFiveReportRowDto {
    return {
      contactId: 'c1', contactCode: 'CUS0001', contactName: 'Acme Traders', contactPan: '301234567',
      documentType: 'Invoice', billNo: 'INV0001/83-84', billDate: '2026-08-01',
      amount: 1006.5, taxableAmount: 956.5, taxAmount: 124.35, totalAmount: 1080.85, isActive: true,
      fiscalYear: 2083, discount: 50, printCount: 2, printedAt: '2026-08-01T05:00:00Z', printedBy: 'Hari Cashier',
      enteredBy: 'Sita Clerk',
      ...overrides,
    };
  }

  function page(rows: AnnexFiveReportRowDto[]) {
    const asked: { fromDate: string; toDate: string }[] = [];
    TestBed.configureTestingModule({
      imports: [AnnexFiveReportPage],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        {
          provide: SalesService,
          useValue: {
            getAnnexFiveReport: (_org: string, fromDate: string, toDate: string) => {
              asked.push({ fromDate, toDate });
              const report: AnnexFiveReportDto = { fromDate, toDate, rows, page: 1, pageSize: 50, totalCount: rows.length };
              return of(report);
            },
            exportAnnexFiveReport: () => of(new Blob()),
          },
        },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: { get: () => 'org-1' } } } },
      ],
    });

    const fixture = TestBed.createComponent(AnnexFiveReportPage);
    fixture.detectChanges();
    return { element: fixture.nativeElement as HTMLElement, asked };
  }

  afterEach(() => TestBed.resetTestingModule());

  it('opens on this fiscal year to date', () => {
    const { asked } = page([]);

    expect(asked[0]).toEqual({ fromDate: fiscalYearBounds(currentFiscalYear()).from, toDate: nepalToday() });
  });

  it('shows the fiscal year, the discount, the prints and who entered the bill', () => {
    const { element } = page([row(), row({ billNo: 'INV0002/83-84', printCount: 0, printedAt: null, printedBy: null, enteredBy: null })]);

    const headers = [...element.querySelectorAll('thead th')].map((th) => th.textContent!.trim());
    expect(headers).toEqual(expect.arrayContaining(['Fiscal Year', 'Discount', 'Prints', 'Printed', 'Entered By']));

    const [printed, unprinted] = [...element.querySelectorAll('tbody tr')].map((tr) => tr.textContent!.replace(/\s+/g, ' '));
    expect(printed).toContain('2083 - 2084');
    expect(printed).toContain('50.00');
    expect(printed).toContain('Hari Cashier');
    expect(printed).toContain('Sita Clerk');
    expect(unprinted).toContain('Not printed');
  });
});
