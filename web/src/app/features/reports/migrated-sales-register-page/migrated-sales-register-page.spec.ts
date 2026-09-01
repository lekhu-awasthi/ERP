import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { Observable, of } from 'rxjs';

import { SalesRegisterDto, SalesRegisterRowDto } from '../../../core/sales/sales.models';
import { SalesService } from '../../../core/sales/sales.service';
import { MigratedSalesRegisterPage } from './migrated-sales-register-page';

/**
 * Phase 21c -- the Migrated Sales Register screen.
 *
 * <p>The two assertions worth having here are both about not being mistaken for the live register:
 * the page must say on its face that these rows are not in the General Ledger, and its footer totals
 * must come from the server's full-set figures rather than a reduce over the page it happens to hold
 * (phase-16c bug #1). The stub deliberately returns a page whose rows sum to <i>less</i> than the
 * report totals, so a client-side reduce would fail this test rather than accidentally pass it.</p>
 */
describe('MigratedSalesRegisterPage', () => {
  const organizationId = '11111111-1111-1111-1111-111111111111';

  function row(overrides: Partial<SalesRegisterRowDto> = {}): SalesRegisterRowDto {
    return {
      date: '2024-07-30',
      documentType: 'MigratedSalesEntry',
      documentCode: 'INV-0912',
      contactId: null,
      contactName: 'Himalayan Traders Private Limited',
      contactPan: '301234567',
      totalValue: 113,
      taxExemptValue: 0,
      taxableValue: 100,
      vatAmount: 13,
      exportValue: 0,
      exportCountry: null,
      exportDeclarationNo: null,
      exportDeclarationDate: null,
      ...overrides,
    };
  }

  function page(report: Partial<SalesRegisterDto> = {}) {
    const salesService = new SalesServiceStub({
      fromDate: '2024-01-01',
      toDate: '2024-12-31',
      items: [row()],
      page: 1,
      pageSize: 25,
      totalCount: 40,
      // Deliberately larger than the single row above: these are the full-set totals.
      totalValue: 4520,
      totalTaxExemptValue: 0,
      totalTaxableValue: 4000,
      totalVatAmount: 520,
      ...report,
    });

    TestBed.configureTestingModule({
      imports: [MigratedSalesRegisterPage],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: SalesService, useValue: salesService },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: { get: () => organizationId } } },
        },
      ],
    });

    const fixture = TestBed.createComponent(MigratedSalesRegisterPage);
    fixture.detectChanges();

    return { fixture, text: () => (fixture.nativeElement as HTMLElement).textContent ?? '', salesService };
  }

  afterEach(() => TestBed.resetTestingModule());

  it('warns on its face that these rows are not this organization\'s books', () => {
    const { text } = page();

    expect(text()).toContain('Migrated Sales Register');
    expect(text()).toContain('not posted to the General Ledger');
    expect(text()).toContain('never appear in the');
  });

  it('shows the server-computed totals over the full filtered set, not the page', () => {
    const { text } = page();

    expect(text()).toContain('4520.00');
    expect(text()).toContain('520.00');
    // The single loaded row's own value must not be the footer figure.
    expect(text()).not.toContain('Total113.00');
  });

  it('reads the migrated register endpoint, never the live one', () => {
    const { salesService } = page();

    expect(salesService.migratedCalls).toBe(1);
    expect(salesService.liveCalls).toBe(0);
  });

  it('shows the four Export columns the live register can never populate', () => {
    const { text } = page({
      items: [
        row({
          exportValue: 500,
          exportCountry: 'India',
          exportDeclarationNo: 'DEC-77',
          exportDeclarationDate: '2024-03-02',
        }),
      ],
    });

    expect(text()).toContain('Export Country');
    expect(text()).toContain('India');
    expect(text()).toContain('DEC-77');
  });

  it('shows an empty state rather than a blank table when nothing was migrated', () => {
    const { text } = page({ items: [], totalCount: 0, totalValue: 0, totalTaxableValue: 0, totalVatAmount: 0 });

    expect(text()).toContain('No migrated sales rows match these filters');
  });
});

class SalesServiceStub {
  migratedCalls = 0;
  liveCalls = 0;

  constructor(private readonly report: SalesRegisterDto) {}

  getMigratedSalesRegister(): Observable<SalesRegisterDto> {
    this.migratedCalls++;
    return of(this.report);
  }

  getSalesRegister(): Observable<SalesRegisterDto> {
    this.liveCalls++;
    return of(this.report);
  }

  exportMigratedSalesRegister(): Observable<Blob> {
    return of(new Blob(['x']));
  }
}
