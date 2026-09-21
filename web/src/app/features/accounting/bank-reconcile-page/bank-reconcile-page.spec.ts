import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { Observable, of } from 'rxjs';

import { AccountingService } from '../../../core/accounting/accounting.service';
import {
  BankAccountDto,
  BankStatementLineDto,
  BookTransactionDto,
  CreateBankReconciliationResult,
} from '../../../core/accounting/accounting.models';
import { PagedResult } from '../../../core/common/paged-result';
import { BankReconcilePage } from './bank-reconcile-page';

/**
 * Phase 56 -- the two-pane matcher.
 *
 * <p>What is worth pinning here is the <b>gate</b>, because it is the feature: the two selected sums
 * must be equal and each side must hold something. The server enforces it too (a 400 naming both
 * totals), so this is about the screen telling the truth before the round trip -- including the case
 * that looks like a rule and is not, where both sides net to zero.</p>
 */
describe('BankReconcilePage', () => {
  const organizationId = '11111111-1111-1111-1111-111111111111';
  const bankAccountId = '22222222-2222-2222-2222-222222222222';

  function bankLine(id: string, signedAmount: number): BankStatementLineDto {
    return {
      id,
      date: '2026-09-16',
      description: 'CLAUDE PROBE ' + id,
      deposit: signedAmount > 0 ? signedAmount : 0,
      withdrawal: signedAmount < 0 ? -signedAmount : 0,
      signedAmount,
      importJobId: 'job-1',
      reconciliationId: null,
      createdAt: '2026-09-21T04:00:00Z',
    };
  }

  function bookRow(id: string, signedAmount: number): BookTransactionDto {
    return {
      id,
      date: '2026-09-16',
      postedAt: '2026-09-16T10:00:00Z',
      documentType: 'Invoice',
      documentId: 'doc-' + id,
      documentCode: 'INV000' + id,
      reference: null,
      description: 'Trade Receivables',
      debit: signedAmount > 0 ? signedAmount : 0,
      credit: signedAmount < 0 ? -signedAmount : 0,
      signedAmount,
      reconciliationId: null,
    };
  }

  function page(
    bank: BankStatementLineDto[],
    book: BookTransactionDto[],
  ): {
    fixture: ComponentFixture<BankReconcilePage>;
    element: () => HTMLElement;
    service: AccountingServiceStub;
    tickBank: (index: number) => void;
    tickBook: (index: number) => void;
    reconcileButton: () => HTMLButtonElement;
  } {
    const service = new AccountingServiceStub(bank, book);

    TestBed.configureTestingModule({
      imports: [BankReconcilePage],
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

    const fixture = TestBed.createComponent(BankReconcilePage);
    fixture.detectChanges();

    const element = () => fixture.nativeElement as HTMLElement;

    const boxes = (prefix: string) =>
      [...element().querySelectorAll<HTMLInputElement>('input[type="checkbox"]')].filter((b) =>
        b.id.startsWith(prefix),
      );

    return {
      fixture,
      element,
      service,
      tickBank: (index) => {
        boxes('bank-row-')[index].click();
        fixture.detectChanges();
      },
      tickBook: (index) => {
        boxes('book-row-')[index].click();
        fixture.detectChanges();
      },
      reconcileButton: () =>
        [...element().querySelectorAll<HTMLButtonElement>('button')].find((b) =>
          b.textContent?.includes('Reconcile'),
        )!,
    };
  }

  it('renders both panes from their own feeds', () => {
    const { element } = page([bankLine('A', 678)], [bookRow('1', 678)]);

    expect(element().textContent).toContain('CLAUDE PROBE A');
    expect(element().textContent).toContain('INV0001');
  });

  it('refuses to reconcile until something is selected on both sides', () => {
    const { tickBank, reconcileButton } = page([bankLine('A', 678)], [bookRow('1', 678)]);

    expect(reconcileButton().disabled).toBe(true);

    tickBank(0);

    // One side only -- still refused, and this is the case a naive "sums are equal" check gets
    // wrong, because 678 against an empty selection is not 678 against 0.
    expect(reconcileButton().disabled).toBe(true);
  });

  it('refuses an unbalanced selection and says by how much', () => {
    const { tickBank, tickBook, reconcileButton, element } = page(
      [bankLine('A', 678)],
      [bookRow('1', 113)],
    );

    tickBank(0);
    tickBook(0);

    expect(reconcileButton().disabled).toBe(true);
    expect(element().textContent).toContain('Out by');
    expect(element().textContent).toContain('565');
  });

  /**
   * One statement line against two receipts -- the 1:2 match driven live against the reference
   * product on 2026-09-21. The ids that reach the service are what matters: the server takes a
   * GlLine id on the book side, not a document id.
   */
  it('reconciles many to many when the two sides agree', () => {
    const { tickBank, tickBook, reconcileButton, service } = page(
      [bankLine('C', 791)],
      [bookRow('1', 678), bookRow('2', 113)],
    );

    tickBank(0);
    tickBook(0);
    tickBook(1);

    expect(reconcileButton().disabled).toBe(false);

    reconcileButton().click();

    expect(service.reconciled).toEqual([{ statementLineIds: ['C'], glLineIds: ['1', '2'] }]);
  });

  /**
   * A selection that nets to nothing is a real match, not an empty one -- the reference product's
   * own gate permits it, and this is why the screen asks about the counts separately from the sums.
   */
  it('allows a selection that nets to zero on both sides', () => {
    const { tickBank, tickBook, reconcileButton } = page(
      [bankLine('A', 100), bankLine('B', -100)],
      [bookRow('1', 100), bookRow('2', -100)],
    );

    tickBank(0);
    tickBank(1);
    tickBook(0);
    tickBook(1);

    expect(reconcileButton().disabled).toBe(false);
  });

  it('clears the selection and reloads both panes after reconciling', () => {
    const { fixture, tickBank, tickBook, reconcileButton, element, service } = page(
      [bankLine('A', 678)],
      [bookRow('1', 678)],
    );

    tickBank(0);
    tickBook(0);
    reconcileButton().click();
    fixture.detectChanges();

    expect(service.bankLoads).toBe(2);
    expect(service.bookLoads).toBe(2);
    expect(element().textContent).toContain('Reconciled 1 statement line against 1 transaction.');
  });

  /** Every row's checkbox names what it selects -- a control with no accessible name is a row a
   * screen-reader user cannot tick (phase 40). */
  it('labels every checkbox with the row it selects', () => {
    const { element } = page([bankLine('A', 678)], [bookRow('1', 678)]);

    const boxes = [...element().querySelectorAll<HTMLInputElement>('input[type="checkbox"]')];
    expect(boxes.length).toBe(2);

    for (const box of boxes) {
      const label = element().querySelector(`label[for="${box.id}"]`);
      expect(label?.textContent?.trim()).toBeTruthy();
    }
  });
});

class AccountingServiceStub {
  bankLoads = 0;
  bookLoads = 0;
  readonly reconciled: { statementLineIds: readonly string[]; glLineIds: readonly string[] }[] = [];

  constructor(
    private readonly bank: BankStatementLineDto[],
    private readonly book: BookTransactionDto[],
  ) {}

  listBankAccounts(): Observable<PagedResult<BankAccountDto>> {
    return of({
      items: [
        {
          id: '22222222-2222-2222-2222-222222222222',
          code: 'BC0001',
          name: 'Cash In Hand',
          kind: 'Cash',
          bankId: null,
          bankName: null,
          accountNumber: null,
          isActive: true,
          balance: 791,
        },
      ],
      page: 1,
      pageSize: 200,
      totalCount: 1,
    });
  }

  listBankStatementLines(): Observable<PagedResult<BankStatementLineDto>> {
    this.bankLoads += 1;
    return of({ items: this.bank, page: 1, pageSize: 100, totalCount: this.bank.length });
  }

  listBookTransactions(): Observable<PagedResult<BookTransactionDto>> {
    this.bookLoads += 1;
    return of({ items: this.book, page: 1, pageSize: 100, totalCount: this.book.length });
  }

  createBankReconciliation(
    _organizationId: string,
    _bankAccountId: string,
    statementLineIds: readonly string[],
    glLineIds: readonly string[],
  ): Observable<CreateBankReconciliationResult> {
    this.reconciled.push({ statementLineIds, glLineIds });
    return of({
      id: 'recon-1',
      reconciledAmount: 0,
      statementLineCount: statementLineIds.length,
      bookTransactionCount: glLineIds.length,
    });
  }
}
