import { DatePipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { OrganizationsService } from '../../../core/organizations/organizations.service';
import { OrganizationMember } from '../../../core/organizations/organizations.models';
import { WorkflowService } from '../../../core/workflow/workflow.service';
import { AuditRowDto, SystemAuditAction, SystemAuditDocumentType } from '../../../core/workflow/workflow.models';
import { PagedResult, DEFAULT_PAGE_SIZE } from '../../../core/common/paged-result';
import { PaginationControl } from '../../../shared/pagination/pagination-control';
import { triggerBlobDownload } from '../../../shared/download-file';
import { BsDateInput } from '../../../shared/formatting/bs-date-input';

const EMPTY_REPORT: PagedResult<AuditRowDto> = { items: [], page: 1, pageSize: DEFAULT_PAGE_SIZE, totalCount: 0 };

const ACTIONS: SystemAuditAction[] = ['Create', 'Update', 'Approve', 'Void', 'Extract'];

/** The 13 ApprovableTransaction types AuditBehavior ever writes a row for -- the report's Document
 * Type dropdown deliberately doesn't offer the other 5 DocumentType enum values (Account/Contact/
 * Product numbering-pool-only entries, ProductionOrder/ProductionJournal), since an Audit row can
 * never carry one of those (see SystemAuditReportQuery's own doc comment). */
const DOCUMENT_TYPES: SystemAuditDocumentType[] = [
  'Quotation', 'SalesOrder', 'Invoice', 'CreditNote', 'PurchaseOrder', 'PurchaseBill', 'Expense',
  'DebitNote', 'JournalVoucher', 'CashTransfer', 'WarehouseTransfer', 'InventoryAdjustment', 'Payment',
  // Phase 22 -- so an Admin can filter the audit trail down to "which documents were sent to the
  // extraction service, by whom", which is the reason that row is written at all.
  'DocumentExtraction',
];

/**
 * Roadmap Phase 16d -- read-only System Audit report (architecture-spec.md §3.9, FR-9.6/NFR-3.3),
 * filterable by User/Action/DocumentType/date range, each row linking into that document's own
 * existing detail page. Admin-only (Reports.SystemAudit.View). Mirrors tds-report-page's shape
 * (Phase 16c); detailRoute(row) is adapted from transaction-approval-queue-page.ts's own 13-branch
 * switch (Phase 12) -- SalesOrder still has no Angular detail page, so it still degrades to plain
 * text rather than a broken link.
 */
@Component({
  selector: 'app-system-audit-report-page',
  imports: [RouterLink, PaginationControl, DatePipe, BsDateInput],
  templateUrl: './system-audit-report-page.html',
})
export class SystemAuditReportPage {
  private readonly route = inject(ActivatedRoute);
  private readonly workflowService = inject(WorkflowService);
  private readonly organizationsService = inject(OrganizationsService);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;
  protected readonly actions = ACTIONS;
  protected readonly documentTypes = DOCUMENT_TYPES;

  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly report = signal<PagedResult<AuditRowDto>>(EMPTY_REPORT);
  protected readonly members = signal<OrganizationMember[]>([]);

  protected readonly userId = signal<string>('');
  protected readonly action = signal<SystemAuditAction | ''>('');
  protected readonly documentType = signal<SystemAuditDocumentType | ''>('');
  protected readonly fromDate = signal<string>('');
  protected readonly toDate = signal<string>('');

  protected readonly page = signal(1);
  protected readonly pageSize = signal(DEFAULT_PAGE_SIZE);
  protected readonly exporting = signal(false);

  constructor() {
    this.organizationsService.listMembers(this.organizationId).subscribe({
      next: (members) => this.members.set(members),
      error: () => {
        // Member picker degrading to an empty dropdown isn't fatal to the report itself.
      },
    });
    this.load();
  }

  protected onUserChange(event: Event): void {
    this.userId.set((event.target as HTMLSelectElement).value);
    this.page.set(1);
    this.load();
  }

  protected onActionChange(event: Event): void {
    this.action.set((event.target as HTMLSelectElement).value as SystemAuditAction | '');
    this.page.set(1);
    this.load();
  }

  protected onDocumentTypeChange(event: Event): void {
    this.documentType.set((event.target as HTMLSelectElement).value as SystemAuditDocumentType | '');
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
      .exportSystemAuditReport(
        this.organizationId, this.userId() || null, this.action() || null, this.documentType() || null,
        this.fromDate() || null, this.toDate() || null, full, page, pageSize,
      )
      .subscribe({
        next: (blob) => {
          this.exporting.set(false);
          triggerBlobDownload(blob, `SystemAuditReport_${new Date().toISOString().slice(0, 10)}.xlsx`);
        },
        error: (err: unknown) => {
          this.exporting.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not export the System Audit report.');
        },
      });
  }

  private load(): void {
    this.loading.set(true);
    this.errorMessage.set(null);

    this.workflowService
      .getSystemAuditReport(
        this.organizationId, this.userId() || null, this.action() || null, this.documentType() || null,
        this.fromDate() || null, this.toDate() || null, this.page(), this.pageSize(),
      )
      .subscribe({
        next: (report) => {
          this.report.set(report);
          this.loading.set(false);
        },
        error: (err: unknown) => {
          this.loading.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load the System Audit report.');
        },
      });
  }

  /** Payment resolves to one of two existing routes depending on Direction. Copied from
   * transaction-approval-queue-page.ts's own detailRoute method (Phase 12) -- including, until
   * Phase 23, its stale "SalesOrder has no detail page" hole. See that file for the history. */
  protected detailRoute(row: AuditRowDto): string[] | null {
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
      // Phase 22 -- an extraction row's DocumentId is an inbox document, and the inbox is a list
      // with no per-document route, so there is nothing honest to link to. Degrades to plain text
      // exactly as SalesOrder does above.
      case 'DocumentExtraction':
        return null;
    }
  }
}
