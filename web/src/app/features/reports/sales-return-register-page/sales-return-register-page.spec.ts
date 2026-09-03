import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { Observable, of } from 'rxjs';

import { ContactsService } from '../../../core/contacts/contacts.service';
import {
  SalesReturnRegisterDto,
  SalesReturnRegisterRowDto,
} from '../../../core/reports/catalogue-reports.models';
import { CatalogueReportsService } from '../../../core/reports/catalogue-reports.service';
import { SalesReturnRegisterPage } from './sales-return-register-page';

/**
 * Phase 26c. This screen's job beyond listing credit notes is to say, on its face, that the same
 * documents also appear negatively in the Sales Register -- a reader who found them in both and had
 * not been told would reasonably conclude one of the two reports was double-counting.
 */
describe('SalesReturnRegisterPage', () => {
  const organizationId = '11111111-1111-1111-1111-111111111111';

  function row(overrides: Partial<SalesReturnRegisterRowDto> = {}): SalesReturnRegisterRowDto {
    return {
      date: '2026-05-03',
      documentCode: 'CN0001/83-84',
      contactId: '22222222-2222-2222-2222-222222222222',
      contactName: 'Acme Retail',
      contactPan: '301234567',
      totalReturnValue: 452,
      taxExemptReturnValue: 0,
      taxableReturnValue: 400,
      vatAmount: 52,
      ...overrides,
    };
  }

  function page(report: Partial<SalesReturnRegisterDto> = {}) {
    const reports = {
      getSalesReturnRegister: (): Observable<SalesReturnRegisterDto> =>
        of({
          fromDate: '2026-05-01',
          toDate: '2026-05-31',
          items: [row()],
          page: 1,
          pageSize: 50,
          totalCount: 7,
          // Deliberately larger than the single row: full-set totals, not a page reduce.
          totalReturnValue: 3164,
          totalTaxExemptReturnValue: 0,
          totalTaxableReturnValue: 2800,
          totalVatAmount: 364,
          ...report,
        }),
      exportSalesReturnRegister: (): Observable<Blob> => of(new Blob()),
    };

    TestBed.configureTestingModule({
      imports: [SalesReturnRegisterPage],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: CatalogueReportsService, useValue: reports },
        { provide: ContactsService, useValue: { listAllContacts: () => of([]) } },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: { get: () => organizationId } } } },
      ],
    });

    const fixture = TestBed.createComponent(SalesReturnRegisterPage);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;

    return { fixture, element, text: () => element.textContent ?? '' };
  }

  afterEach(() => TestBed.resetTestingModule());

  it('lists credit notes with positive values', () => {
    const { element } = page();

    const cells = Array.from(element.querySelectorAll('tbody tr td')).map((c) => c.textContent?.trim());
    expect(cells).toContain('CN0001/83-84');
    expect(cells).toContain('452.00');
    expect(cells).not.toContain('-452.00');
  });

  it('explains that these same notes also appear negatively in the Sales Register', () => {
    const { text } = page();

    expect(text()).toContain('Sales Register');
    expect(text()).toContain('negative rows');
    expect(text()).toContain('Both readings are correct');
  });

  it('shows the server-computed totals over the full filtered set, not the page', () => {
    const { text } = page();

    expect(text()).toContain('3,164.00');
    expect(text()).toContain('364.00');
  });

  it('shows an empty state rather than a blank table', () => {
    const { text } = page({
      items: [], totalCount: 0, totalReturnValue: 0, totalTaxableReturnValue: 0, totalVatAmount: 0,
    });

    expect(text()).toContain('No sales returns in this period');
  });
});
