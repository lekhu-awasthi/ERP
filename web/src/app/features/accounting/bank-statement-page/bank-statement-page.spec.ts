import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, provideRouter } from '@angular/router';
import { Observable, of } from 'rxjs';

import { AccountingService } from '../../../core/accounting/accounting.service';
import { BankAccountDto, BankStatementLineDto } from '../../../core/accounting/accounting.models';
import { PagedResult } from '../../../core/common/paged-result';
import { BankStatementPage } from './bank-statement-page';

/**
 * Phase 55 -- one account's imported statement.
 *
 * <p>The assertions that matter here are about what the screen is allowed to <i>offer</i>: the
 * deposit/withdrawal split rendered from the server's pair rather than re-derived from a sign, the
 * two deletes and which selector each sends, and the absence of a Status column that phase 56 has
 * not yet earned.</p>
 */
describe('BankStatementPage', () => {
  const organizationId = '11111111-1111-1111-1111-111111111111';
  const bankAccountId = '22222222-2222-2222-2222-222222222222';

  function line(overrides: Partial<BankStatementLineDto> = {}): BankStatementLineDto {
    return {
      id: 'line-1',
      date: '2026-09-01',
      description: 'Salary credit',
      deposit: 1500,
      withdrawal: 0,
      signedAmount: 1500,
      importJobId: 'job-1',
      createdAt: '2026-09-21T04:00:00Z',
      ...overrides,
    };
  }

  function page(lines: BankStatementLineDto[]): {
    fixture: ComponentFixture<BankStatementPage>;
    text: () => string;
    element: () => HTMLElement;
    service: AccountingServiceStub;
  } {
    const service = new AccountingServiceStub(lines);

    TestBed.configureTestingModule({
      imports: [BankStatementPage],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: AccountingService, useValue: service },
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: {
              paramMap: { get: (key: string) => (key === 'id' ? organizationId : bankAccountId) },
              queryParamMap: { get: () => null },
            },
          },
        },
      ],
    });

    const fixture = TestBed.createComponent(BankStatementPage);
    fixture.detectChanges();

    const element = () => fixture.nativeElement as HTMLElement;
    return { fixture, element, text: () => element().textContent ?? '', service };
  }

  afterEach(() => TestBed.resetTestingModule());

  it('names the account and says plainly that importing posts nothing', () => {
    const { text } = page([line()]);

    expect(text()).toContain('Nabil Bank');
    expect(text()).toContain('General Ledger');
  });

  /**
   * The direction comes from the server's deposit/withdrawal pair, not from the sign of one
   * number the template inspects -- re-deriving it per screen is the bug `StatementAmount` exists
   * to prevent, and a withdrawal must never appear in the Deposit column.
   */
  it('renders a deposit and a withdrawal in their own columns', () => {
    const { element } = page([
      line({ id: 'a', deposit: 1500, withdrawal: 0, signedAmount: 1500 }),
      line({
        id: 'b',
        description: 'ATM withdrawal',
        deposit: 0,
        withdrawal: 250.75,
        signedAmount: -250.75,
      }),
    ]);

    const rows = element().querySelectorAll('tbody tr');
    expect(rows.length).toBe(2);

    const cells = (row: Element) => [...row.querySelectorAll('td')].map((c) => c.textContent?.trim() ?? '');

    // [checkbox, date, description, deposit, withdrawal]
    expect(cells(rows[0])[3]).toContain('1,500');
    expect(cells(rows[0])[4]).toBe('');
    expect(cells(rows[1])[3]).toBe('');
    expect(cells(rows[1])[4]).toContain('250.75');
  });

  /**
   * Phase 56 adds the reconciliation and the Reconciled/Pending filter together. Until then a
   * Status column would read "Pending" on every row forever, which is furniture rather than
   * information -- asserted so the omission is a decision somebody has to delete.
   */
  it('shows no Status column, because nothing can be reconciled yet', () => {
    const { element } = page([line()]);

    // The first header holds the select-all box and its visually-hidden label, so it is dropped
    // rather than asserted as empty -- the claim is about the data columns.
    const headers = [...element().querySelectorAll('thead th')]
      .slice(1)
      .map((h) => h.textContent?.trim() ?? '');

    expect(headers).toEqual(['Date', 'Description', 'Deposit', 'Withdrawal']);
  });

  it('deletes the ticked lines by id', () => {
    const { element, fixture, service } = page([line({ id: 'a' }), line({ id: 'b' })]);

    const boxes = element().querySelectorAll<HTMLInputElement>('tbody input[type="checkbox"]');
    boxes[1].click();
    fixture.detectChanges();

    element()
      .querySelectorAll<HTMLButtonElement>('button')
      .forEach((b) => {
        if (b.textContent?.includes('Delete selected')) b.click();
      });

    expect(service.deletions).toEqual([{ lineIds: ['b'] }]);
  });

  /**
   * The answer to a statement file uploaded twice. Nothing could have refused the second upload --
   * a bank statement has no natural key -- so the product owes one action that takes it back.
   */
  it('undoes a whole import by its job id', () => {
    const { element, service } = page([line({ id: 'a', importJobId: 'job-7' })]);

    element()
      .querySelectorAll<HTMLButtonElement>('button')
      .forEach((b) => {
        if (b.textContent?.includes('Undo import')) b.click();
      });

    expect(service.deletions).toEqual([{ importJobId: 'job-7' }]);
  });

  it('sends the user to the import screen with the type and this account already chosen', () => {
    const { element } = page([line()]);
    const router = TestBed.inject(Router);
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);

    element()
      .querySelectorAll<HTMLButtonElement>('button')
      .forEach((b) => {
        if (b.textContent?.includes('Import Statement')) b.click();
      });

    expect(navigate).toHaveBeenCalledWith(
      ['/organizations', organizationId, 'configuration', 'import'],
      { queryParams: { entityType: 'BankStatement', bankAccountId } },
    );
  });

  it('offers the import button rather than an empty screen when nothing has been imported', () => {
    const { text } = page([]);

    expect(text()).toContain('No Statement Lines Yet');
    expect(text()).toContain('Import Statement');
  });
});

class AccountingServiceStub {
  readonly deletions: unknown[] = [];

  constructor(private readonly lines: BankStatementLineDto[]) {}

  listBankAccounts(): Observable<PagedResult<BankAccountDto>> {
    const account: BankAccountDto = {
      id: '22222222-2222-2222-2222-222222222222',
      code: 'BC0001',
      name: 'Nabil Bank',
      kind: 'Bank',
      bankId: null,
      bankName: 'Nabil',
      accountNumber: '0101234567',
      isActive: true,
      balance: 0,
    };
    return of({ items: [account], page: 1, pageSize: 200, totalCount: 1 });
  }

  listBankStatementLines(): Observable<PagedResult<BankStatementLineDto>> {
    return of({ items: this.lines, page: 1, pageSize: 50, totalCount: this.lines.length });
  }

  deleteBankStatementLines(
    _organizationId: string,
    _bankAccountId: string,
    selector: unknown,
  ): Observable<{ deletedCount: number }> {
    this.deletions.push(selector);
    return of({ deletedCount: 1 });
  }
}
