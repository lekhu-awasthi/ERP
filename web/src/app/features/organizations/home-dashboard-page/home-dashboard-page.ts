import { Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { forkJoin, of } from 'rxjs';
import { catchError, map } from 'rxjs/operators';

import { AccountingService } from '../../../core/accounting/accounting.service';
import { BankAccountDto } from '../../../core/accounting/accounting.models';
import { extractErrorMessage } from '../../../core/auth/api-error';
import { PurchasingService } from '../../../core/purchasing/purchasing.service';
import { SalesService } from '../../../core/sales/sales.service';
import {
  RecentTransactionFilter,
  RecentTransactionRowDto,
} from '../../../core/workflow/workflow.models';
import { WorkflowService } from '../../../core/workflow/workflow.service';
import { AmountPipe } from '../../../shared/formatting/amount-pipe';
import { BsDateInput } from '../../../shared/formatting/bs-date-input';
import { CalendarToggle } from '../../../shared/formatting/calendar-toggle';
import { NepaliDatePipe } from '../../../shared/formatting/nepali-date-pipe';
import { PaginationControl } from '../../../shared/pagination/pagination-control';

/** One KPI card. `previous` is null when the prior-period query could not be read. */
interface Kpi {
  readonly label: string;
  readonly current: number;
  readonly previous: number | null;
  readonly denied: boolean;
}

/**
 * The Home dashboard (roadmap Phase 23 item 4, `erp-module-scan.md`'s Home Tab), confirmed live
 * against the reference product in Step 2 of this phase.
 *
 * <b>Decision F -- what this builds, and what it deliberately does not.</b> A dashboard is the
 * classic place to accidentally write five new report queries, so the rule for this screen is that
 * <b>every figure comes from a query handler that already existed</b>. Nothing in the Application
 * layer was added for it:
 *   - Sales      -> SalesRegisterQuery.TotalValue
 *   - Purchase   -> PurchaseRegisterQuery's own server-computed totals
 *   - Receipt    -> CashFlowSummaryQuery.ReceivedFromCustomerBalance
 *   - Payment    -> CashFlowSummaryQuery.PaidToSupplierBalance
 *   - Balances   -> ListBankAccountsQuery (each account's live running balance)
 * All four KPI queries are date-ranged and return server-computed totals over the full filtered set,
 * which is what lets this screen show a total at all without repeating phase-16c's bug #1.
 *
 * <b>Not built, deliberately:</b> the live product's personalisable Quick Links tray (the existing
 * organization launcher already is a link tray, and per-user link storage is a backend feature this
 * phase has no mandate for) and its unified recent-activity feed with All/Sales/Purchase/Payment/
 * Receipt tabs (there is no existing query that returns a mixed recent-transaction stream; building
 * one is a new aggregation, which is exactly what Decision F says to stop at). The date sub-filter
 * <i>is</i> built, since every card already takes a range.
 *
 * <b>% change vs prior period</b> is computed by running the same queries a second time over the
 * window of equal length immediately preceding the selected one. It renders as "--" rather than a
 * number whenever there is no prior data, because a change from zero is not a percentage.
 *
 * <b>Permissions (Decision G):</b> this screen has no permission key of its own. Each card rides the
 * key of the query behind it -- SalesRegisterView, PurchaseRegisterView, CashFlowSummaryView,
 * BankAccountView -- so a card a user cannot populate renders as "No access" instead of an error,
 * and a Member with few grants sees a smaller dashboard rather than a broken one.
 */
@Component({
  selector: 'app-home-dashboard-page',
  imports: [RouterLink, AmountPipe, NepaliDatePipe, BsDateInput, CalendarToggle, PaginationControl],
  templateUrl: './home-dashboard-page.html',
})
export class HomeDashboardPage {
  private readonly route = inject(ActivatedRoute);
  private readonly salesService = inject(SalesService);
  private readonly purchasingService = inject(PurchasingService);
  private readonly accountingService = inject(AccountingService);
  private readonly workflowService = inject(WorkflowService);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;

  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);

  protected readonly fromDate = signal(startOfNepalMonth());
  protected readonly toDate = signal(nepalToday());

  protected readonly kpis = signal<Kpi[]>([]);
  protected readonly accounts = signal<BankAccountDto[]>([]);
  protected readonly accountsDenied = signal(false);
  /** True when more accounts exist than were loaded -- the Total row is then suppressed rather than
   * shown as a partial sum (phase-16c bug #1: a footer total must cover the whole set). */
  protected readonly accountsTruncated = signal(false);

  protected readonly totalBalance = computed(() =>
    this.accounts().reduce((sum, a) => sum + a.balance, 0),
  );

  /** The recent-activity feed. Its tabs, ordering and paging are all server-side -- see
   * `RecentTransactionsQuery`. Loaded separately from the cards so switching a tab does not re-run
   * every KPI query. */
  protected readonly feedFilters: readonly RecentTransactionFilter[] = [
    'All',
    'Sales',
    'Purchase',
    'Payment',
    'Receipt',
  ];
  protected readonly feedFilter = signal<RecentTransactionFilter>('All');
  protected readonly feedRows = signal<RecentTransactionRowDto[]>([]);
  protected readonly feedTotalCount = signal(0);
  protected readonly feedPage = signal(1);
  // A signal, and wired to the pagination control's own size selector -- bound as a constant the
  // control would display a "Rows per page" value the feed did not actually use.
  protected readonly feedPageSize = signal(25);
  protected readonly feedLoading = signal(true);
  /** True when the whole feed query was refused -- the same degrade-don't-break rule as the cards. */
  protected readonly feedDenied = signal(false);

  constructor() {
    this.load();
  }

  protected onFromDateChange(value: string): void {
    this.fromDate.set(value);
    this.feedPage.set(1);
    this.load();
  }

  protected onToDateChange(value: string): void {
    this.toDate.set(value);
    this.feedPage.set(1);
    this.load();
  }

  /** Selects the last `days` days ending today, mirroring the live product's sub-filter presets. */
  protected selectRange(days: number): void {
    const end = nepalToday();
    this.toDate.set(end);
    this.fromDate.set(addDays(end, -(days - 1)));
    this.feedPage.set(1);
    this.load();
  }

  protected changePct(kpi: Kpi): number | null {
    if (kpi.previous === null || kpi.previous === 0) {
      return null;
    }
    return ((kpi.current - kpi.previous) / Math.abs(kpi.previous)) * 100;
  }

  protected selectFeedFilter(filter: RecentTransactionFilter): void {
    this.feedFilter.set(filter);
    this.feedPage.set(1);
    this.loadFeed();
  }

  protected feedPageChange(page: number): void {
    this.feedPage.set(page);
    this.loadFeed();
  }

  protected feedPageSizeChange(pageSize: number): void {
    this.feedPageSize.set(pageSize);
    this.feedPage.set(1);
    this.loadFeed();
  }

  /** Where a feed row opens. Payment resolves to one of two routes by Direction -- one aggregate,
   * two Angular detail pages, the same split `transaction-approval-queue-page` makes. */
  protected feedRoute(row: RecentTransactionRowDto): string[] {
    const org = this.organizationId;
    switch (row.documentType) {
      case 'Invoice':
        return ['/organizations', org, 'sales', 'invoices', row.documentId];
      case 'CreditNote':
        return ['/organizations', org, 'sales', 'credit-notes', row.documentId];
      case 'PurchaseBill':
        return ['/organizations', org, 'purchasing', 'purchase-bills', row.documentId];
      case 'DebitNote':
        return ['/organizations', org, 'purchasing', 'debit-notes', row.documentId];
      case 'Expense':
        return ['/organizations', org, 'purchasing', 'expenses', row.documentId];
      case 'Payment':
        return row.direction === 'Paid'
          ? ['/organizations', org, 'purchasing', 'supplier-payments', row.documentId]
          : ['/organizations', org, 'payments', row.documentId];
    }
  }

  protected feedLabel(row: RecentTransactionRowDto): string {
    switch (row.documentType) {
      case 'Invoice':
        return 'Invoice';
      case 'CreditNote':
        return 'Credit Note';
      case 'PurchaseBill':
        return 'Purchase Bill';
      case 'DebitNote':
        return 'Debit Note';
      case 'Expense':
        return 'Expense';
      case 'Payment':
        return row.direction === 'Paid' ? 'Payment' : 'Receipt';
    }
  }

  protected loadFeed(): void {
    const from = this.fromDate();
    const to = this.toDate();
    if (!from || !to || from > to) {
      return;
    }

    this.feedLoading.set(true);
    this.workflowService
      .getRecentTransactions(this.organizationId, from, to, this.feedFilter(), this.feedPage(), this.feedPageSize())
      .subscribe({
        next: (result) => {
          this.feedDenied.set(false);
          this.feedRows.set(result.items);
          this.feedTotalCount.set(result.totalCount);
          this.feedLoading.set(false);
        },
        error: () => {
          // Degrades exactly like a KPI card: the rest of the dashboard stays usable.
          this.feedDenied.set(true);
          this.feedRows.set([]);
          this.feedTotalCount.set(0);
          this.feedLoading.set(false);
        },
      });
  }

  protected load(): void {
    const from = this.fromDate();
    const to = this.toDate();
    if (!from || !to || from > to) {
      this.errorMessage.set('The From date must be on or before the To date.');
      return;
    }

    this.loading.set(true);
    this.errorMessage.set(null);
    this.loadFeed();

    const previous = previousWindow(from, to);

    // Each card is read independently and each failure is caught, so one missing permission dims one
    // card instead of emptying the screen.
    forkJoin({
      sales: this.guard(this.salesService.getSalesRegister(this.organizationId, from, to, null, [], 1, 1).pipe(
        map((r) => r.totalValue))),
      salesPrev: this.guard(this.salesService
        .getSalesRegister(this.organizationId, previous.from, previous.to, null, [], 1, 1)
        .pipe(map((r) => r.totalValue))),
      purchase: this.guard(this.purchasingService
        .getPurchaseRegister(this.organizationId, from, to, null, 1, 1)
        .pipe(map(purchaseTotal))),
      purchasePrev: this.guard(this.purchasingService
        .getPurchaseRegister(this.organizationId, previous.from, previous.to, null, 1, 1)
        .pipe(map(purchaseTotal))),
      cash: this.guard(this.accountingService.getCashFlowSummary(this.organizationId, from, to, null)),
      cashPrev: this.guard(this.accountingService
        .getCashFlowSummary(this.organizationId, previous.from, previous.to, null)),
      accounts: this.guard(this.accountingService.listBankAccounts(this.organizationId, true, 1, 200)),
    }).subscribe({
      next: (r) => {
        this.kpis.set([
          kpi('Sales', r.sales, r.salesPrev),
          kpi('Purchase', r.purchase, r.purchasePrev),
          kpi(
            'Receipt',
            r.cash === null ? null : r.cash.receivedFromCustomerCashIn - r.cash.receivedFromCustomerCashOut,
            r.cashPrev === null ? null : r.cashPrev.receivedFromCustomerCashIn - r.cashPrev.receivedFromCustomerCashOut,
          ),
          kpi(
            'Payment',
            r.cash === null ? null : r.cash.paidToSupplierCashOut - r.cash.paidToSupplierCashIn,
            r.cashPrev === null ? null : r.cashPrev.paidToSupplierCashOut - r.cashPrev.paidToSupplierCashIn,
          ),
        ]);

        this.accountsDenied.set(r.accounts === null);
        this.accounts.set(r.accounts?.items ?? []);
        this.accountsTruncated.set(r.accounts !== null && r.accounts.items.length < r.accounts.totalCount);
        this.loading.set(false);
      },
      error: (err) => {
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load the dashboard.');
        this.loading.set(false);
      },
    });
  }

  /** Turns a per-card failure (usually a 403) into a null result rather than a dead screen. */
  private guard<T>(source: import('rxjs').Observable<T>) {
    return source.pipe(catchError(() => of(null)));
  }
}

function kpi(label: string, current: number | null, previous: number | null): Kpi {
  return {
    label,
    current: current ?? 0,
    previous,
    denied: current === null,
  };
}

/** The Purchase Register reports its totals split across six statutory columns; the card wants the
 * one figure a user means by "Purchase", so they are summed here rather than in a new query. */
function purchaseTotal(r: {
  totalTaxExemptValue: number;
  totalTaxableNonCapitalLocalValue: number;
  totalTaxableNonCapitalImportValue: number;
  totalTaxableCapitalValue: number;
}): number {
  return (
    r.totalTaxExemptValue +
    r.totalTaxableNonCapitalLocalValue +
    r.totalTaxableNonCapitalImportValue +
    r.totalTaxableCapitalValue
  );
}

/** The window of equal length immediately preceding [from, to]. */
function previousWindow(from: string, to: string): { from: string; to: string } {
  const days = daysBetween(from, to) + 1;
  return { from: addDays(from, -days), to: addDays(from, -1) };
}

function daysBetween(from: string, to: string): number {
  return Math.round((Date.parse(`${to}T00:00:00Z`) - Date.parse(`${from}T00:00:00Z`)) / 86_400_000);
}

function addDays(iso: string, days: number): string {
  return new Date(Date.parse(`${iso}T00:00:00Z`) + days * 86_400_000).toISOString().slice(0, 10);
}

/**
 * Today on the Nepal wall clock (UTC+05:45), never UTC -- mirrors `Domain/Common/NepalTime`. Between
 * 18:15 and 24:00 UTC the Nepal date is already tomorrow, so a UTC "today" would silently key the
 * dashboard to the wrong day.
 */
function nepalToday(): string {
  return new Date(Date.now() + (5 * 60 + 45) * 60_000).toISOString().slice(0, 10);
}

function startOfNepalMonth(): string {
  return `${nepalToday().slice(0, 7)}-01`;
}
