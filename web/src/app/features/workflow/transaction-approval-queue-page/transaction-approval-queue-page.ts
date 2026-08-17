import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { WorkflowService } from '../../../core/workflow/workflow.service';
import { TransactionApprovalRowDto } from '../../../core/workflow/workflow.models';

/**
 * Read-only v1 (roadmap Phase 8+ Workflow bullet / product-requirements.md FR-10.2) -- lists every
 * Draft-status document across all 13 ApprovableTransaction types the current user is permitted to
 * approve, each row linking into that document's own existing detail page where the existing
 * Approve button already works. No bulk-approve-from-this-list action this phase (a real stretch
 * goal, deliberately deferred -- see phase-12-status.md's scope decision).
 */
@Component({
  selector: 'app-transaction-approval-queue-page',
  imports: [RouterLink],
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
   * SalesOrder returns null -- no Angular detail page exists for it (Phase 5's "SalesOrder shipped
   * backend-only" scope decision, never retrofitted). Payment resolves to one of two existing
   * routes depending on Direction, since Customer and Supplier Payment share one aggregate but two
   * separate Angular detail pages.
   */
  protected detailRoute(row: TransactionApprovalRowDto): (string)[] | null {
    const org = this.organizationId;
    switch (row.documentType) {
      case 'Quotation':
        return ['/organizations', org, 'sales', 'quotations', row.documentId];
      case 'SalesOrder':
        return null;
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
