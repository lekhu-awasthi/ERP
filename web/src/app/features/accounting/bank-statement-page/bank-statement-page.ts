import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';

import { AccountingService } from '../../../core/accounting/accounting.service';
import { ContactsService } from '../../../core/contacts/contacts.service';
import { Contact } from '../../../core/contacts/contacts.models';
import {
  Account,
  BankAccountDto,
  BankStatementLineDto,
  QuickApproveTarget,
} from '../../../core/accounting/accounting.models';
import { extractErrorMessage } from '../../../core/auth/api-error';
import { DEFAULT_PAGE_SIZE } from '../../../core/common/paged-result';
import { DateRangeService } from '../../../shared/platform/date-range.service';
import { AmountPipe } from '../../../shared/formatting/amount-pipe';
import { NepaliDatePipe } from '../../../shared/formatting/nepali-date-pipe';
import { ListChrome, documentSortOptions } from '../../../shared/pagination/list-chrome';
import { ListFilter } from '../../../shared/pagination/list-query-options';
import { PaginationControl } from '../../../shared/pagination/pagination-control';
import { StatusBanner } from '../../../shared/a11y/status-banner';

/**
 * Phase 55 -- one cash-and-bank account's imported bank statement, and the feeder for phase 56's
 * reconciliation.
 *
 * <p>The account comes from the route, which is where the reference product puts it: its own
 * statement screen is `/accounting/bank-accounts/:id/bank-statement` and it offers no
 * all-accounts view. Reading a statement without knowing whose it is has no meaning.</p>
 *
 * <p><b>Import is a link, not a second implementation.</b> The upload, the dry-run review and the
 * per-row results all live on the Import / Export screen already, so this page's Import button
 * routes there with the upload type and this account preselected. One implementation, two doors.</p>
 *
 * <p><b>Phase 56 added the Status column and its filter.</b> Both render off whether a line carries
 * a `reconciliationId`, which is how the reference product does it -- its filter has exactly two
 * options and there is no stored status anywhere. A reconciled row links through to the
 * reconciliation it belongs to, which is the only way into that record from a list.</p>
 *
 * <p><b>Phase 57 added Quick Approve</b>, the reference product's green tick: a Pending row can be
 * turned into this tenant's own document and matched against itself in one action. It expands into
 * a row of its own rather than crowding three controls into the Status cell -- phase 52's lesson
 * about a control added to a cell that could not hold it.</p>
 */
@Component({
  selector: 'app-bank-statement-page',
  imports: [RouterLink, ListChrome, PaginationControl, StatusBanner, AmountPipe, NepaliDatePipe],
  templateUrl: './bank-statement-page.html',
})
export class BankStatementPage {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly accountingService = inject(AccountingService);
  private readonly contactsService = inject(ContactsService);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;
  protected readonly bankAccountId = this.route.snapshot.paramMap.get('accountId')!;

  protected readonly filter = new ListFilter(inject(DateRangeService), () => this.reloadForDateRange());
  protected readonly sortOptions = documentSortOptions('Statement date');

  protected readonly loading = signal(true);
  protected readonly deleting = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly statusMessage = signal<string | null>(null);
  protected readonly items = signal<BankStatementLineDto[]>([]);
  protected readonly account = signal<BankAccountDto | null>(null);

  protected readonly page = signal(1);
  protected readonly pageSize = signal(DEFAULT_PAGE_SIZE);
  protected readonly totalCount = signal(0);

  /**
   * The rows ticked for deletion. A plain signal holding ids rather than a flag on each row: the
   * rows are replaced wholesale on every reload, so a flag would be lost on a page change and a
   * user would not be able to tell why.
   */
  protected readonly selectedIds = signal<readonly string[]>([]);

  /**
   * Phase 56 -- the Reconciled/Pending filter. `null` is All, which the reference product's own
   * filter also offers by clearing it. Tracked in its own signal rather than derived from a control
   * because the app is zoneless and a `computed()` over a plain control value caches forever
   * (phase 17).
   */
  protected readonly statusFilter = signal<boolean | null>(null);

  protected readonly statusTabs: readonly { readonly label: string; readonly value: boolean | null }[] = [
    { label: 'All', value: null },
    { label: 'Pending', value: false },
    { label: 'Reconciled', value: true },
  ];

  /**
   * Phase 57 -- which row's Quick Approve form is open, or null. One at a time: the form is a real
   * row in the table, and two open at once would separate a row from its own controls.
   */
  protected readonly quickApproveFor = signal<string | null>(null);
  protected readonly quickApproving = signal(false);
  protected readonly quickApproveTarget = signal<QuickApproveTarget>('Contact');
  protected readonly quickApproveTargetId = signal<string>('');
  protected readonly quickApproveError = signal<string | null>(null);

  /** The two things the picker offers, which are what the reference product's one account list
   * conflates -- see QuickApproveBankStatementLineCommand for why they are separate here. */
  protected readonly contacts = signal<Contact[]>([]);
  protected readonly accounts = signal<Account[]>([]);
  protected readonly targetsLoading = signal(false);

  constructor() {
    this.loadAccount();
    this.load();
  }

  /**
   * Opens the form under one row, and loads the two pick lists the first time it is needed rather
   * than on every page load: most visits to this screen never quick-approve anything.
   */
  protected openQuickApprove(item: BankStatementLineDto): void {
    this.quickApproveFor.set(item.id);
    this.quickApproveError.set(null);
    this.quickApproveTargetId.set('');
    // A deposit is money received and a withdrawal money paid, so a contact is the likelier target
    // either way; the user changes it in one click when the line is a bank charge.
    this.quickApproveTarget.set('Contact');
    this.loadQuickApproveTargets();
  }

  protected closeQuickApprove(): void {
    this.quickApproveFor.set(null);
    this.quickApproveError.set(null);
  }

  protected selectQuickApproveTarget(target: QuickApproveTarget): void {
    this.quickApproveTarget.set(target);
    // The chosen id belongs to the list that is no longer showing, so it is cleared rather than
    // carried across -- sending a contact id as an account id would be a 404 naming the wrong thing.
    this.quickApproveTargetId.set('');
  }

  protected onQuickApproveTargetId(value: string): void {
    this.quickApproveTargetId.set(value);
  }

  /** A deposit becomes a receipt, a withdrawal a payment. Shown so the row says what it will do
   * before it does it. */
  protected quickApproveVerb(item: BankStatementLineDto): string {
    if (this.quickApproveTarget() === 'Account') {
      return item.deposit > 0 ? 'Journal Voucher (debit this bank account)' : 'Journal Voucher (credit this bank account)';
    }

    return item.deposit > 0 ? 'Customer Payment (received)' : 'Supplier Payment (paid)';
  }

  protected submitQuickApprove(item: BankStatementLineDto): void {
    const targetId = this.quickApproveTargetId();

    if (!targetId) {
      this.quickApproveError.set('Select the customer, supplier or account this transaction belongs to.');
      return;
    }

    this.quickApproving.set(true);
    this.quickApproveError.set(null);
    this.statusMessage.set(null);

    this.accountingService
      .quickApproveStatementLine(
        this.organizationId,
        this.bankAccountId,
        item.id,
        this.quickApproveTarget(),
        targetId,
      )
      .subscribe({
        next: (result) => {
          this.quickApproving.set(false);
          this.quickApproveFor.set(null);
          this.statusMessage.set(
            `${result.documentCode} created and reconciled against this statement line.`,
          );
          this.load();
        },
        error: (err: unknown) => {
          this.quickApproving.set(false);
          this.quickApproveError.set(
            extractErrorMessage(err) ?? 'Could not approve that statement line.',
          );
        },
      });
  }

  private loadQuickApproveTargets(): void {
    if (this.contacts().length > 0 || this.accounts().length > 0 || this.targetsLoading()) {
      return;
    }

    this.targetsLoading.set(true);

    this.contactsService.listAllContacts(this.organizationId).subscribe({
      next: (items) => this.contacts.set(items),
      error: () => this.targetsLoading.set(false),
    });

    this.accountingService.listAllAccounts(this.organizationId).subscribe({
      next: (items) => {
        // The statement's own account is never a sensible other side -- it would be a journal
        // voucher from the bank account to itself. The reference product filters it out of its own
        // picker in exactly the same way (`filter(a => a.id !== bankId)`).
        this.accounts.set(items.filter((x) => x.id !== this.bankAccountId));
        this.targetsLoading.set(false);
      },
      error: () => this.targetsLoading.set(false),
    });
  }


  protected isSelected(id: string): boolean {
    return this.selectedIds().includes(id);
  }

  protected toggleSelected(id: string, checked: boolean): void {
    this.selectedIds.update((ids) => (checked ? [...ids, id] : ids.filter((x) => x !== id)));
  }

  protected toggleAll(checked: boolean): void {
    this.selectedIds.set(checked ? this.items().map((x) => x.id) : []);
  }

  /** Every distinct upload on the page, so "undo that import" can name one. */
  protected importsOnPage(): readonly string[] {
    return [...new Set(this.items().map((x) => x.importJobId).filter((x): x is string => x !== null))];
  }

  protected deleteSelected(): void {
    const lineIds = this.selectedIds();

    if (lineIds.length === 0) {
      return;
    }

    this.runDelete(
      { lineIds },
      `${lineIds.length === 1 ? '1 statement line' : `${lineIds.length} statement lines`} deleted.`,
    );
  }

  /**
   * The answer to a statement file uploaded twice. Nothing could have refused the second upload --
   * a bank statement has no natural key, so two identical rows are two real rows -- so what the
   * product owes instead is one action that takes the mistake back.
   */
  protected undoImport(importJobId: string): void {
    this.runDelete({ importJobId }, 'That import was removed from this statement.');
  }

  protected selectStatus(value: boolean | null): void {
    this.statusFilter.set(value);
    // Every sibling filter handler on this page sets the page back to 1, and a narrowing filter
    // applied on page 3 otherwise reads as "no data" (phase 35b).
    this.page.set(1);
    this.load();
  }

  protected onSearch(term: string): void {
    this.filter.search.set(term);
    this.page.set(1);
    this.load();
  }

  protected onSort(sort: string): void {
    this.filter.sort.set(sort);
    this.page.set(1);
    this.load();
  }

  protected onPageChange(page: number): void {
    this.page.set(page);
    this.load();
  }

  protected onPageSizeChange(pageSize: number): void {
    this.pageSize.set(pageSize);
    this.page.set(1);
    this.load();
  }

  /** Lands on the Import / Export screen with this upload type and this account already chosen. */
  protected importStatement(): void {
    void this.router.navigate(['/organizations', this.organizationId, 'configuration', 'import'], {
      queryParams: { entityType: 'BankStatement', bankAccountId: this.bankAccountId },
    });
  }

  private runDelete(selector: { lineIds: readonly string[] } | { importJobId: string }, success: string): void {
    this.deleting.set(true);
    this.errorMessage.set(null);
    this.statusMessage.set(null);

    this.accountingService
      .deleteBankStatementLines(this.organizationId, this.bankAccountId, selector)
      .subscribe({
        next: () => {
          this.deleting.set(false);
          this.selectedIds.set([]);
          this.statusMessage.set(success);
          this.page.set(1);
          this.load();
        },
        error: (err: unknown) => {
          this.deleting.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not delete those statement lines.');
        },
      });
  }

  private reloadForDateRange(): void {
    this.page.set(1);
    this.load();
  }

  private loadAccount(): void {
    // The list endpoint is the only read of a bank account this app has, so the heading's account
    // name comes from a page of them rather than from a detail call that does not exist. A page of
    // 200 covers every plausible tenant; the name simply stays blank past that, which is a missing
    // caption rather than a broken screen.
    this.accountingService.listBankAccounts(this.organizationId, true, 1, 200).subscribe({
      next: (result) => this.account.set(result.items.find((a) => a.id === this.bankAccountId) ?? null),
    });
  }

  private load(): void {
    this.loading.set(true);
    this.selectedIds.set([]);

    this.accountingService
      .listBankStatementLines(
        this.organizationId,
        this.bankAccountId,
        this.page(),
        this.pageSize(),
        { ...this.filter.options(), reconciled: this.statusFilter() ?? undefined },
      )
      .subscribe({
        next: (result) => {
          this.items.set(result.items);
          this.totalCount.set(result.totalCount);
          this.loading.set(false);
        },
        error: (err: unknown) => {
          this.loading.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load this account’s statement.');
        },
      });
  }
}
