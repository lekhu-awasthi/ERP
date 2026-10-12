import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { of } from 'rxjs';

import { VatReturnLineDto, VatReturnSection, VatSummaryReportDto } from '../../../core/accounting/accounting.models';
import { AccountingService } from '../../../core/accounting/accounting.service';
import { currentFiscalYear } from '../../../shared/formatting/bs-fiscal-year';
import { VatSummaryReportPage } from './vat-summary-report-page';

/**
 * Phase 89 -- the VAT return a BS fiscal year at a time: twelve month columns, each line in English with the IRD form's
 * Nepali beside it, a credit in parentheses, and a nil month left blank.
 */
describe('VatSummaryReportPage', () => {
  const months = ['Shrawan', 'Bhadra', 'Aswin', 'Kartik', 'Mangsir', 'Poush', 'Magh', 'Falgun', 'Chaitra', 'Baisakh', 'Jestha', 'Asar'];

  function line(key: string, section: VatReturnSection, label: string, labelNepali: string, aswin: number, total: number | null, isTotal = false): VatReturnLineDto {
    return { key, section, label, labelNepali, isTotal, monthly: months.map((_, i) => (i === 2 ? aswin : 0)), total };
  }

  function report(fiscalYear: number): VatSummaryReportDto {
    return {
      fiscalYear,
      fromDate: '2026-07-17',
      toDate: '2027-07-16',
      columns: months.map((monthName, i) => ({
        bsYear: i < 9 ? fiscalYear : fiscalYear + 1,
        bsMonth: ((i + 3) % 12) + 1,
        monthName,
        fromDate: '2026-07-17',
        toDate: '2026-08-16',
        label: `${monthName} ${i < 9 ? fiscalYear : fiscalYear + 1}`,
      })),
      lines: [
        line('taxableSales', 'Sales', 'Taxable sales', 'करयोग्य बिक्री', 1000, 1000),
        line('totalSales', 'Sales', 'Total sales and exports', 'जम्मा बिक्री / निकासी', 1000, 1000, true),
        line('monthVat', 'Settlement', 'VAT for the month, payable / (credit)', 'महिनाको कर क्रेडिट / (डेबिट)', -26, -26),
        line('payable', 'Settlement', 'VAT payable / (credit carried forward)', 'तिर्नुपर्ने कर / (बाँकी क्रेडिट)', -26, null, true),
      ],
      netVat: -26,
    };
  }

  function page() {
    const asked: number[] = [];
    const exported: number[] = [];
    TestBed.configureTestingModule({
      imports: [VatSummaryReportPage],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        {
          provide: AccountingService,
          useValue: {
            getVatSummaryReport: (_org: string, fiscalYear: number) => {
              asked.push(fiscalYear);
              return of(report(fiscalYear));
            },
            exportVatSummaryReport: (_org: string, fiscalYear: number) => {
              exported.push(fiscalYear);
              return of(new Blob());
            },
          },
        },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: { get: () => 'org-1' } } } },
      ],
    });

    const fixture = TestBed.createComponent(VatSummaryReportPage);
    fixture.detectChanges();
    return { fixture, element: fixture.nativeElement as HTMLElement, asked, exported };
  }

  it('opens on this fiscal year with a column per BS month, Shrawan to Asar', () => {
    const { element, asked } = page();

    expect(asked).toEqual([currentFiscalYear()]);
    const headers = [...element.querySelectorAll('thead th')].map((th) => th.textContent!.trim());
    expect(headers.length).toBe(14);
    expect(headers[0]).toBe('Line');
    expect(headers[1]).toContain('Shrawan');
    expect(headers[12]).toContain('Asar');
    expect(headers[13]).toBe('Total');
  });

  it('labels each line in English with the form’s Nepali beside it, marked as Nepali', () => {
    const { element } = page();

    const row = element.querySelector('tr[data-line="taxableSales"]')!;
    expect(row.querySelector('th')!.textContent).toContain('Taxable sales');
    const nepali = row.querySelector('[lang="ne"]')!;
    expect(nepali.textContent!.trim()).toBe('करयोग्य बिक्री');
  });

  it('prints a credit in parentheses, a nil month as nil, and no year total on a balance line', () => {
    const { element } = page();

    const cells = [...element.querySelectorAll('tr[data-line="payable"] td')].map((td) => td.textContent!.trim());
    expect(cells[2]).toBe('(26.00)');
    expect(cells[0]).toContain('nil');
    expect(cells[12]).toBe('no year total');
    const sales = [...element.querySelectorAll('tr[data-line="taxableSales"] td')].map((td) => td.textContent!.trim());
    expect(sales[2]).toBe('1,000.00');
    expect(sales[12]).toBe('1,000.00');
  });

  it('reloads for the fiscal year picked, and exports that year', () => {
    const { fixture, element, asked, exported } = page();

    const select = element.querySelector('select#vat-summary-report-page-fiscal-year') as HTMLSelectElement;
    select.value = '2081';
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    (element.querySelector('button') as HTMLButtonElement).click();

    expect(asked).toEqual([currentFiscalYear(), 2081]);
    expect(exported).toEqual([2081]);
    expect(element.querySelector('caption')!.textContent).toContain('2081 - 2082');
  });
});
