import { PaymentDirection } from '../payments/payments.models';

/**
 * All 13 ApprovableTransaction document types (roadmap Phase 12) -- a superset of sales.models.ts'
 * own DocumentType union, which only names the 9 types the Sales/Purchasing/Payments feature areas
 * reference directly. Kept local to this feature rather than widening that shared union, since
 * nothing outside the approval queue needs to discriminate against the Accounting/Inventory types.
 */
export type TransactionApprovalDocumentType =
  | 'Quotation'
  | 'SalesOrder'
  | 'Invoice'
  | 'CreditNote'
  | 'PurchaseOrder'
  | 'PurchaseBill'
  | 'Expense'
  | 'DebitNote'
  | 'JournalVoucher'
  | 'CashTransfer'
  | 'WarehouseTransfer'
  | 'InventoryAdjustment'
  | 'Payment';

export interface TransactionApprovalRowDto {
  documentType: TransactionApprovalDocumentType;
  documentId: string;
  code: string;
  date: string;
  createdAt: string;
  contactId: string | null;
  contactName: string | null;
  reference: string | null;
  direction: PaymentDirection | null;
}

export interface TransactionApprovalQueueDto {
  rows: TransactionApprovalRowDto[];
}
