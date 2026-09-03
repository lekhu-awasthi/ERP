import { PaymentDirection } from '../../core/payments/payments.models';
import { GlSourceDocumentType } from '../../core/accounting/accounting.models';

/**
 * Phase 26a -- shared by the four GL report pages (Journal report, General Ledger Summary, Detail
 * General Ledger, GL Master Report), which all render the same Txn Type column and offer the same
 * Txn Type filter.
 *
 * <p>The list is the eleven document types that actually post a GlJournalEntry, not the whole
 * DocumentType enum: Quotation, Sales Order, Purchase Order and Warehouse Transfer post nothing, so
 * offering them as filters would give a user four options that can only ever return zero rows.</p>
 */
export const GL_SOURCE_DOCUMENT_TYPES: GlSourceDocumentType[] = [
  'Invoice',
  'CreditNote',
  'PurchaseBill',
  'Expense',
  'DebitNote',
  'JournalVoucher',
  'CashTransfer',
  'InventoryAdjustment',
  'Payment',
  'ProductionJournal',
  'OpeningBalance',
];

/**
 * One Payment aggregate renders as two labels, matching the live reports -- a reader has no other
 * way to tell a receipt from a payment in a flat ledger. The server's own .xlsx exporter applies
 * the identical rule, so the screen and the spreadsheet never disagree.
 */
export function txnTypeLabel(documentType: GlSourceDocumentType, direction: PaymentDirection | null): string {
  if (documentType === 'Payment') {
    return direction === 'Paid' ? 'Supplier Payment' : 'Customer Payment';
  }
  return documentType.replace(/([a-z])([A-Z])/g, '$1 $2');
}

/**
 * Where a GL row's source document lives. Opening Balance and Production Journal rows are the two
 * that can be null: an opening balance is not a document at all (it is a per-account line with no
 * detail page), and both are handled by the caller degrading to plain text rather than a dead link.
 */
export function glDetailRoute(
  organizationId: string,
  documentType: GlSourceDocumentType,
  documentId: string,
  direction: PaymentDirection | null,
): string[] | null {
  const org = organizationId;
  switch (documentType) {
    case 'Invoice':
      return ['/organizations', org, 'sales', 'invoices', documentId];
    case 'CreditNote':
      return ['/organizations', org, 'sales', 'credit-notes', documentId];
    case 'PurchaseBill':
      return ['/organizations', org, 'purchasing', 'purchase-bills', documentId];
    case 'Expense':
      return ['/organizations', org, 'purchasing', 'expenses', documentId];
    case 'DebitNote':
      return ['/organizations', org, 'purchasing', 'debit-notes', documentId];
    case 'JournalVoucher':
      return ['/organizations', org, 'accounting', 'journal-vouchers', documentId];
    case 'CashTransfer':
      return ['/organizations', org, 'accounting', 'cash-transfers', documentId];
    case 'InventoryAdjustment':
      return ['/organizations', org, 'inventory', 'inventory-adjustments', documentId];
    case 'Payment':
      return direction === 'Paid'
        ? ['/organizations', org, 'purchasing', 'supplier-payments', documentId]
        : ['/organizations', org, 'payments', documentId];
    case 'ProductionJournal':
      return ['/organizations', org, 'manufacturing', 'production-journals', documentId];
    case 'OpeningBalance':
      return null;
  }
}
