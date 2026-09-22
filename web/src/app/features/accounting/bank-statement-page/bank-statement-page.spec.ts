import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, provideRouter } from '@angular/router';
import { Observable, of } from 'rxjs';

import { AccountingService } from '../../../core/accounting/accounting.service';
import { ContactsService } from '../../../core/contacts/contacts.service';
import { BankAccountDto, BankStatementLineDto } from '../../../core/accounting/accounting.models';
import { PagedResult } from '../../../core/common/paged-result';
import { BankStatementPage } from './bank-statement-page';

/**
 * Phase 55 -- one account's imported statement.
 *
 * <p>The assertions that matter here are about what the screen is allowed to <i>offer</i>: the
 * deposit/withdrawal split rendered from the server's pair rather than re-derived from a sign, the
 * two deletes and which selector each sends, and -- from phase 56 -- the Status column and its
 * filter, both of which render off the reconciliation key rather than off a stored status.</p>
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
      reconciliationId: null,
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
        { provide: ContactsService, useValue: new ContactsServiceStub() },
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
   * Phase 56 added the Status column. It is *derived*: there is no stored status anywhere, and this
   * asserts both halves -- an unreconciled line reads Pending, a reconciled one reads Reconciled and
   * links to the reconciliation, which is the only way into that record from a list.
   */
  it('renders Status from the reconciliation key, not from a stored status', () => {
    const { element } = page([
      line({ id: 'a', reconciliationId: null }),
      line({ id: 'b', reconciliationId: 'recon-1' }),
    ]);

    // The first header holds the select-all box and its visually-hidden label, so it is dropped
    // rather than asserted as empty -- the claim is about the data columns.
    const headers = [...element().querySelectorAll('thead th')]
      .slice(1)
      .map((h) => h.textContent?.trim() ?? '');

    expect(headers).toEqual(['Date', 'Description', 'Deposit', 'Withdrawal', 'Status', 'Action']);

    const rows = element().querySelectorAll('tbody tr');
    // Phase 57 appended an Action column, so Status is no longer the last cell.
    const statusCell = (row: Element) => [...row.querySelectorAll('td')][5];

    expect(statusCell(rows[0]).textContent?.trim()).toBe('Pending');
    expect(statusCell(rows[1]).textContent?.trim()).toBe('Reconciled');

    // ...and the reconciled one is a way in, not just a label.
    const link = statusCell(rows[1]).querySelector('a');
    expect(link?.getAttribute('href')).toContain('/reconciliations/recon-1');
  });

  /**
   * The filter sends the same key the column renders off. Asserted as what reaches the service --
   * a filter a screen displays but does not apply is worse than no filter (phase 34b).
   */
  it('sends the Status filter to the server', () => {
    const { element, fixture, service } = page([line()]);

    const tab = [...element().querySelectorAll<HTMLButtonElement>('button')].find(
      (b) => b.textContent?.trim() === 'Reconciled',
    );
    tab!.click();
    fixture.detectChanges();

    expect(service.lastListOptions?.reconciled).toBe(true);

    const pending = [...element().querySelectorAll<HTMLButtonElement>('button')].find(
      (b) => b.textContent?.trim() === 'Pending',
    );
    pending!.click();
    fixture.detectChanges();

    expect(service.lastListOptions?.reconciled).toBe(false);

    // "All" sends nothing at all -- a `reconciled=` with no value binds as false server-side and
    // would silently hide every matched line.
    const all = [...element().querySelectorAll<HTMLButtonElement>('button')].find(
      (b) => b.textContent?.trim() === 'All',
    );
    all!.click();
    fixture.detectChanges();

    expect(service.lastListOptions?.reconciled).toBeUndefined();
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

  /**
   * Phase 57 -- Quick Approve is offered on a Pending row and withheld from a Reconciled one. The
   * second half is the one worth asserting: a line already matched has nothing left to approve and
   * the server answers 409, so offering the control would be a button whose only outcome is an
   * error message.
   */
  it('offers Quick Approve on a pending line and not on a reconciled one', () => {
    const { element } = page([
      line({ id: 'a', reconciliationId: null }),
      line({ id: 'b', reconciliationId: 'recon-1' }),
    ]);

    const rows = element().querySelectorAll('tbody tr');
    const actionCell = (row: Element) => [...row.querySelectorAll('td')][6];

    expect(actionCell(rows[0]).querySelector('button')).not.toBeNull();
    expect(actionCell(rows[1]).querySelector('button')).toBeNull();
  });

  /**
   * The form says which document it is about to create before it creates it, and the sentence
   * changes with both the direction and the chosen kind -- this codebase's whole routing table,
   * rendered.
   */
  it('names the document the tick will create, by direction and by target kind', () => {
    const { element, fixture, text } = page([
      line({ id: 'a', deposit: 1500, withdrawal: 0, signedAmount: 1500 }),
    ]);

    quickApproveButton(element())!.click();
    fixture.detectChanges();

    expect(text()).toContain('Customer Payment (received)');

    const kind = element().querySelector<HTMLSelectElement>('#quick-kind-a')!;
    kind.value = 'Account';
    kind.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    expect(text()).toContain('Journal Voucher (debit this bank account)');
  });

  /**
   * The statement's own account is never a sensible other side of its own journal voucher, and the
   * reference product filters it out of the same picker in the same way.
   */
  it('keeps the statement account out of its own Quick Approve picker', () => {
    const { element, fixture } = page([line({ id: 'a' })]);

    quickApproveButton(element())!.click();
    fixture.detectChanges();

    const kind = element().querySelector<HTMLSelectElement>('#quick-kind-a')!;
    kind.value = 'Account';
    kind.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    const options = [...element().querySelectorAll<HTMLOptionElement>('#quick-target-a option')].map(
      (o) => o.value,
    );

    expect(options).not.toContain(bankAccountId);
    expect(options).toContain('acct-fees');
  });

  it('sends the chosen kind and id, and reports the document it created', () => {
    const { element, fixture, service, text } = page([line({ id: 'a' })]);

    quickApproveButton(element())!.click();
    fixture.detectChanges();

    const target = element().querySelector<HTMLSelectElement>('#quick-target-a')!;
    target.value = 'contact-1';
    target.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    clickApprove(element());
    fixture.detectChanges();

    expect(service.quickApprovals).toEqual([
      { statementLineId: 'a', target: 'Contact', targetId: 'contact-1' },
    ]);

    expect(text()).toContain('JV0007 created and reconciled');
  });

  /**
   * Nothing is sent until something is chosen, and the message names the control rather than saying
   * the request failed (phase 48).
   */
  it('refuses to submit with no target chosen, and says which control to fix', () => {
    const { element, fixture, service, text } = page([line({ id: 'a' })]);

    quickApproveButton(element())!.click();
    fixture.detectChanges();

    clickApprove(element());
    fixture.detectChanges();

    expect(service.quickApprovals).toEqual([]);
    expect(text()).toContain('Select the customer, supplier or account');
    expect(element().querySelector('#quick-target-a')?.getAttribute('aria-invalid')).toBe('true');
  });

  /**
   * Bootstrap's JavaScript is not loaded anywhere in this app, so nothing sets aria-expanded for
   * free (phase 34a).
   */
  it('sets aria-expanded on the Quick Approve toggle itself', () => {
    const { element, fixture } = page([line({ id: 'a' })]);

    expect(quickApproveButton(element())!.getAttribute('aria-expanded')).toBe('false');

    quickApproveButton(element())!.click();
    fixture.detectChanges();

    expect(quickApproveButton(element())!.getAttribute('aria-expanded')).toBe('true');
  });

  function quickApproveButton(element: HTMLElement): HTMLButtonElement | undefined {
    return [...element.querySelectorAll<HTMLButtonElement>('button')].find((b) =>
      b.textContent?.includes('Quick Approve'),
    );
  }

  function clickApprove(element: HTMLElement): void {
    [...element.querySelectorAll<HTMLButtonElement>('button')]
      .filter((b) => b.textContent?.trim() === 'Approve')
      .forEach((b) => b.click());
  }
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

  /** What the page last asked for, so a filter can be asserted as what reaches the server. */
  lastListOptions: { reconciled?: boolean } | undefined;

  listBankStatementLines(
    _organizationId?: string,
    _bankAccountId?: string,
    _page?: number,
    _pageSize?: number,
    options?: { reconciled?: boolean },
  ): Observable<PagedResult<BankStatementLineDto>> {
    this.lastListOptions = options;
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

  /**
   * Phase 57 -- the ledger accounts the Quick Approve picker offers. It deliberately includes the
   * statement's own bank account, so a test can assert the page filters it out rather than relying
   * on a fixture that could not have contained it.
   */
  listAllAccounts(): Observable<never> {
    return of([
      { id: '22222222-2222-2222-2222-222222222222', code: 'BC0001', name: 'Nabil Bank' },
      { id: 'acct-fees', code: 'EX0004', name: 'Bank Charges' },
    ]) as unknown as Observable<never>;
  }

  readonly quickApprovals: unknown[] = [];

  quickApproveStatementLine(
    _organizationId: string,
    _bankAccountId: string,
    statementLineId: string,
    target: string,
    targetId: string,
  ): Observable<never> {
    this.quickApprovals.push({ statementLineId, target, targetId });
    return of({ documentCode: 'JV0007' }) as unknown as Observable<never>;
  }
}

class ContactsServiceStub {
  listAllContacts(): Observable<never> {
    return of([
      { id: 'contact-1', name: 'Cash Customer', type: 'Customer' },
    ]) as unknown as Observable<never>;
  }
}
