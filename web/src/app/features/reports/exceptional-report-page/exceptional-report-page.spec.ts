import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { Observable, of } from 'rxjs';

import { ExceptionalReportDto, ExceptionalReportRowDto } from '../../../core/reports/catalogue-reports.models';
import { CatalogueReportsService } from '../../../core/reports/catalogue-reports.service';
import { ExceptionalReportPage } from './exceptional-report-page';

/**
 * Phase 26c. The two things worth pinning here are the two details a tidy-minded refactor would
 * smooth away: the inventory rows carry no DR/CR marker at all (a stock valuation does not sit on a
 * side of the ledger, and the live report leaves those cells empty), and the one row this codebase
 * has no concept behind says so rather than presenting its zero as a real finding.
 */
describe('ExceptionalReportPage', () => {
  const organizationId = '11111111-1111-1111-1111-111111111111';

  function row(overrides: Partial<ExceptionalReportRowDto> = {}): ExceptionalReportRowDto {
    return {
      particulars: 'Expense Accounts with Credit Balances',
      balance: 1200,
      balanceType: 'CR',
      isModelled: true,
      ...overrides,
    };
  }

  function page(rows: ExceptionalReportRowDto[]) {
    const reports = {
      getExceptionalReport: (): Observable<ExceptionalReportDto> =>
        of({ fromDate: '2026-05-01', toDate: '2026-05-31', rows }),
      exportExceptionalReport: (): Observable<Blob> => of(new Blob()),
    };

    TestBed.configureTestingModule({
      imports: [ExceptionalReportPage],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: CatalogueReportsService, useValue: reports },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: { get: () => organizationId } } } },
      ],
    });

    const fixture = TestBed.createComponent(ExceptionalReportPage);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;

    return { fixture, element, text: () => element.textContent ?? '' };
  }

  afterEach(() => TestBed.resetTestingModule());

  it('renders a ledger row with its DR/CR marker', () => {
    const { element } = page([row()]);

    const cells = Array.from(element.querySelectorAll('tbody tr td')).map((c) => c.textContent?.trim());
    expect(cells).toContain('CR');
    expect(cells).toContain('1,200.00');
  });

  it('leaves the DR/CR cell blank on an inventory row rather than inventing a side', () => {
    const { element } = page([
      row({ particulars: 'Negative Inventory Balances', balance: 42.5, balanceType: null }),
    ]);

    const cells = Array.from(element.querySelectorAll('tbody tr td')).map((c) => c.textContent?.trim());
    expect(cells).toContain('42.50');
    expect(cells).toContain('—');
    expect(cells).not.toContain('DR');
    expect(cells).not.toContain('CR');
  });

  it('flags the row this system has no concept behind instead of passing its zero off as a finding', () => {
    const { text } = page([
      row({ particulars: 'Non-actionable Account Balances', balance: 0, balanceType: 'DR', isModelled: false }),
    ]);

    expect(text()).toContain('not modelled');
    expect(text()).toContain('every account in this chart of accounts is');
  });

  it('keeps all twelve rows in the order the server returned them', () => {
    const particulars = [
      'Inactive Accounts with Outstanding Balances',
      'Minor Account Balance Exception',
      'Expense Accounts with Credit Balances',
      'Income Accounts with Debit Balances',
      'Asset Accounts with Credit Balances',
      'Liability Accounts with Debit Balances',
      'Customers with Credit Balances',
      'Bank and Cash Accounts with Negative Balances',
      'Suppliers with Debit Balances',
      'Inactive Inventory Items with Balances',
      'Negative Inventory Balances',
      'Non-actionable Account Balances',
    ];
    const { element } = page(particulars.map((p) => row({ particulars: p })));

    const rendered = Array.from(element.querySelectorAll('tbody tr td:first-child')).map(
      (c) => c.textContent?.trim().replace(' not modelled', ''),
    );
    expect(rendered).toEqual(particulars);
  });
});
