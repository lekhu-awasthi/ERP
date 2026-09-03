import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { WorkflowService } from '../../../core/workflow/workflow.service';
import {
  TransactionApprovalDocumentType,
  TransactionListRowDto,
  TransactionListStatus,
} from '../../../core/workflow/workflow.models';
import { PagedResult, DEFAULT_PAGE_SIZE } from '../../../core/common/paged-result';
import { PaginationControl } from '../../../shared/pagination/pagination-control';
import { AmountPipe } from '../../../shared/formatting/amount-pipe';
import { NepaliDatePipe } from '../../../shared/formatting/nepali-date-pipe';
import { BsDateInput } from '../../../shared/formatting/bs-date-input';
import { triggerBlobDownload } from '../../../shared/download-file';

const EMPTY_REPORT: PagedResult<TransactionListRowDto> = {
  items: [],
  page: 1,
  pageSize: DEFAULT_PAGE_SIZE,
  totalCount: 0,
};

/** The thirteen ApprovableTransaction types, the same set the approval queue names. */
const DOCUMENT_TYPES: TransactionApprovalDocumentType[] = [
  'Quotation', 'SalesOrder', 'Invoice', 'CreditNote', 'PurchaseOrder', 'PurchaseBill', 'Expense',
  'DebitNote', 'JournalVoucher', 'CashTransfer', 'WarehouseTransfer', 'InventoryAdjustment', 'Payment',
];

const STATUSES: TransactionListStatus[] = ['Draft', 'Approved', 'Void', 'Converted'];

/**
 * Phase 26a -- the Transaction list report (Reports &gt; Accounting), Admin-only
 * (Reports.TransactionList.View). Every document in the tenant, of every type and every status,
 * with the live report's own columns.
 *
 * <p>Both filters are multi-select because the live product's own dashboard deep-links into this
 * report with repeated <code>transaction_type[]</code> and <code>status[]</code> params. Selections
 * are held in plain signals written by the change handlers -- the app is zoneless, and a
 * <code>computed()</code> over a raw control value caches forever (phase-17).</p>
 *
 * <p>There is deliberately no footer total: the Amount column is not additive across document
 * types (see TransactionListQuery). That is not the phase-16c bug it might look like -- the bug is
 * a footer that silently shows a page subtotal, and the fix here is to have no footer at all
 * rather than one that adds up different units of account.</p>
 */
@Component({
  selector: 'app-transaction-list-page',
  imports: [RouterLink, PaginationControl, AmountPipe, NepaliDatePipe, BsDateInput],
  templateUrl: './transaction-list-page.html',
})
export class TransactionListPage {
  private readonly route = inject(ActivatedRoute);
  private readonly workflowService = inject(WorkflowService);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;
  protected readonly documentTypes = DOCUMENT_TYPES;
  protected readonly statuses = STATUSES;

  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly report = signal<PagedResult<TransactionListRowDto>>(EMPTY_REPORT);

  protected readonly selectedTypes = signal<TransactionApprovalDocumentType[]>([]);
  protected readonly selectedStatuses = signal<TransactionListStatus[]>([]);
  protected readonly fromDate = signal<string>('');
  protected readonly toDate = signal<string>('');

  protected readonly page = signal(1);
  protected readonly pageSize = signal(DEFAULT_PAGE_SIZE);
  protected readonly exporting = signal(false);

  constructor() {
    this.load();
  }

  protected isTypeSelected(type: TransactionApprovalDocumentType): boolean {
    return this.selectedTypes().includes(type);
  }

  protected isStatusSelected(status: TransactionListStatus): boolean {
    return this.selectedStatuses().includes(status);
  }

  protected onTypeToggle(type: TransactionApprovalDocumentType, event: Event): void {
    const checked = (event.target as HTMLInputElement).checked;
    this.selectedTypes.update((current) =>
      checked ? [...current, type] : current.filter((x) => x !== type),
    );
    this.page.set(1);
    this.load();
  }

  protected onStatusToggle(status: TransactionListStatus, event: Event): void {
    const checked = (event.target as HTMLInputElement).checked;
    this.selectedStatuses.update((current) =>
      checked ? [...current, status] : current.filter((x) => x !== status),
    );
    this.page.set(1);
    this.load();
  }

  protected onFromDateChange(value: string): void {
    this.fromDate.set(value);
    this.page.set(1);
    this.load();
  }

  protected onToDateChange(value: string): void {
    this.toDate.set(value);
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

  protected exportCurrentView(): void {
    this.runExport(false, this.page(), this.pageSize());
  }

  protected exportFullDataset(): void {
    this.runExport(true, 1, this.pageSize());
  }

  private runExport(full: boolean, page: number, pageSize: number): void {
    this.exporting.set(true);
    this.workflowService
      .exportTransactionList(
        this.organizationId, this.selectedTypes(), this.selectedStatuses(),
        this.fromDate() || null, this.toDate() || null, full, page, pageSize,
      )
      .subscribe({
        next: (blob) => {
          this.exporting.set(false);
          triggerBlobDownload(blob, `TransactionList_${new Date().toISOString().slice(0, 10)}.xlsx`);
        },
        error: (err: unknown) => {
          this.exporting.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not export the Transaction list.');
        },
      });
  }

  private load(): void {
    this.loading.set(true);
    this.errorMessage.set(null);

    this.workflowService
      .getTransactionList(
        this.organizationId, this.selectedTypes(), this.selectedStatuses(),
        this.fromDate() || null, this.toDate() || null, this.page(), this.pageSize(),
      )
      .subscribe({
        next: (report) => {
          this.report.set(report);
          this.loading.set(false);
        },
        error: (err: unknown) => {
          this.loading.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load the Transaction list.');
        },
      });
  }

  /** Adapted from system-audit-report-page's own switch -- one aggregate, two Angular routes for
   * Payment; every other type has exactly one detail route. */
  protected detailRoute(row: TransactionListRowDto): string[] | null {
    const org = this.organizationId;
    switch (row.documentType) {
      case 'Quotation':
        return ['/organizations', org, 'sales', 'quotations', row.documentId];
      case 'SalesOrder':
        return ['/organizations', org, 'sales', 'sales-orders', row.documentId];
      case 'Invoice':
        return ['/organizations', org, 'sales', 'invoices', row.documentId];
      case 'CreditNote':
        return ['/organizations', org, 'sales', 'credit-notes', row.documentId];
      case 'PurchaseOrder':
        return ['/organizations', org, 'purchasing', 'purchase-orders', row.documentId];
      case 'PurchaseBill':
        return ['/organizations', org, 'purchasing', 'purchase-bills', row.documentId];
      case 'Expense':
        return ['/organizations', org, 'purchasing', 'expenses', row.documentId];
      case 'DebitNote':
        return ['/organizations', org, 'purchasing', 'debit-notes', row.documentId];
      case 'JournalVoucher':
        return ['/organizations', org, 'accounting', 'journal-vouchers', row.documentId];
      case 'CashTransfer':
        return ['/organizations', org, 'accounting', 'cash-transfers', row.documentId];
      case 'WarehouseTransfer':
        return ['/organizations', org, 'inventory', 'warehouse-transfers', row.documentId];
      case 'InventoryAdjustment':
        return ['/organizations', org, 'inventory', 'inventory-adjustments', row.documentId];
      case 'Payment':
        return row.direction === 'Paid'
          ? ['/organizations', org, 'purchasing', 'supplier-payments', row.documentId]
          : ['/organizations', org, 'payments', row.documentId];
    }
  }
}
