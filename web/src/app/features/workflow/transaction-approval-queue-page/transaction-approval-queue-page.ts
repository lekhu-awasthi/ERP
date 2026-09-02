import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { WorkflowService } from '../../../core/workflow/workflow.service';
import { TransactionApprovalRowDto } from '../../../core/workflow/workflow.models';
import { NepaliDatePipe } from '../../../shared/formatting/nepali-date-pipe';

/**
 * Read-only v1 (roadmap Phase 8+ Workflow bullet / product-requirements.md FR-10.2) -- lists every
 * Draft-status document across all 13 ApprovableTransaction types the current user is permitted to
 * approve, each row linking into that document's own existing detail page where the existing
 * Approve button already works. No bulk-approve-from-this-list action this phase (a real stretch
 * goal, deliberately deferred -- see phase-12-status.md's scope decision).
 */
@Component({
  selector: 'app-transaction-approval-queue-page',
  imports: [RouterLink, NepaliDatePipe],
  templateUrl: './transaction-approval-queue-page.html',
})
export class TransactionApprovalQueuePage {
  private readonly route = inject(ActivatedRoute);
  private readonly workflowService = inject(WorkflowService);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;

  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly rows = signal<TransactionApprovalRowDto[]>([]);

  constructor() {
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.errorMessage.set(null);

    this.workflowService.getTransactionApprovalQueue(this.organizationId).subscribe({
      next: (result) => {
        this.rows.set(result.rows);
        this.loading.set(false);
      },
      error: (err: unknown) => {
        this.loading.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load the Transaction Approval Queue.');
      },
    });
  }

  /**
   * Payment resolves to one of two existing routes depending on Direction, since Customer and
   * Supplier Payment share one aggregate but two separate Angular detail pages.
   *
   * SalesOrder used to return null here, on the grounds that Phase 5 shipped it backend-only. Phase
   * 18 built `sales-order-list-page`/`sales-order-detail-page` as a mid-phase scope expansion and
   * routed both, but neither of the two screens that link into the queue was updated -- so the row
   * kept rendering without an Open link for four phases while the page it needed existed. Fixed in
   * Phase 23; `transaction-approval-queue-page.spec.ts` now asserts every document type resolves.
   */
  protected detailRoute(row: TransactionApprovalRowDto): (string)[] | null {
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

  protected documentTypeLabel(documentType: TransactionApprovalRowDto['documentType']): string {
    switch (documentType) {
      case 'Quotation': return 'Quotation';
      case 'SalesOrder': return 'Sales Order';
      case 'Invoice': return 'Invoice';
      case 'CreditNote': return 'Credit Note';
      case 'PurchaseOrder': return 'Purchase Order';
      case 'PurchaseBill': return 'Purchase Bill';
      case 'Expense': return 'Expense';
      case 'DebitNote': return 'Debit Note';
      case 'JournalVoucher': return 'Journal Voucher';
      case 'CashTransfer': return 'Cash Transfer';
      case 'WarehouseTransfer': return 'Warehouse Transfer';
      case 'InventoryAdjustment': return 'Inventory Adjustment';
      case 'Payment': return 'Payment';
    }
  }
}
