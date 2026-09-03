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

// Phase 13 (Tasks, the second Workflow-context feature).

export type TaskParentType = 'Contact' | 'Organization';

export type TaskPriority = 'Normal' | 'Urgent';

export type TaskStatus = 'Pending' | 'Started' | 'Done';

export interface TaskRow {
  id: string;
  title: string;
  description: string | null;
  dueDate: string | null;
  createdAt: string;
  taskTypeId: string;
  taskTypeName: string;
  taskTypeColor: string;
  priority: TaskPriority;
  status: TaskStatus;
  isPrivate: boolean;
  createdByUserId: string;
  createdByName: string;
  assignedToUserId: string | null;
  assignedToName: string | null;
}

// Named TaskListDto (not TaskList) to avoid colliding with the TaskList Angular *component*
// (features/workflow/task-list) -- both would otherwise be plausible names imported into the same
// file.
export interface TaskListDto {
  rows: TaskRow[];
  page: number;
  pageSize: number;
  totalCount: number;
}

export interface CreateTaskRequest {
  parentType: TaskParentType;
  parentId: string;
  title: string;
  description: string | null;
  assignedToUserId: string | null;
  dueDate: string | null;
  taskTypeId: string;
  priority: TaskPriority;
  isPrivate: boolean;
}

export interface CreateTaskResult {
  id: string;
  parentType: TaskParentType;
  parentId: string;
  title: string;
  status: TaskStatus;
  createdAt: string;
}

export interface UpdateTaskRequest {
  title: string;
  description: string | null;
  assignedToUserId: string | null;
  dueDate: string | null;
  taskTypeId: string;
  priority: TaskPriority;
  isPrivate: boolean;
}

export interface UpdateTaskStatusRequest {
  newStatus: TaskStatus;
}

// Phase 16d (System Audit report, the third Workflow-context feature).

/** Matches AuditBehavior's own AuditedActionPrefixes -- every audited command's type name starts
 * with exactly one of these four verbs. */
/** 'Extract' joined in Phase 22 (FR-10.3): running AI-assisted extraction on an inbox document is
 * the one action in the product that sends a customer's business document to a third party, and it
 * leaves no other trace. */
export type SystemAuditAction = 'Create' | 'Update' | 'Approve' | 'Void' | 'Extract';

/** AuditBehavior writes a row for the 13 ApprovableTransaction types -- the same set
 * TransactionApprovalDocumentType already names, reused here rather than duplicated -- plus
 * 'DocumentExtraction' (Phase 22), which is not a transaction at all and therefore has no detail
 * route to open. */
export type SystemAuditDocumentType = TransactionApprovalDocumentType | 'DocumentExtraction';

/** Phase 23: the Home dashboard's recent-activity feed. The five tabs the live product shows. */
export type RecentTransactionFilter = 'All' | 'Sales' | 'Purchase' | 'Payment' | 'Receipt';

/** The six document types the feed can carry -- a subset of the approval queue's thirteen, because
 * the tab list is the scope (no Journal Voucher / Cash Transfer / stock documents, and no
 * pre-transaction Quotation / Sales Order / Purchase Order). */
export type RecentTransactionDocumentType =
  | 'Invoice'
  | 'CreditNote'
  | 'PurchaseBill'
  | 'DebitNote'
  | 'Expense'
  | 'Payment';

export interface RecentTransactionRowDto {
  date: string;
  documentType: RecentTransactionDocumentType;
  documentId: string;
  documentCode: string;
  contactId: string | null;
  contactName: string | null;
  amount: number;
  /** Non-null only for Payment rows; decides which of the two Payment detail routes the row opens. */
  direction: PaymentDirection | null;
}

export interface AuditRowDto {
  id: string;
  createdAt: string;
  userId: string;
  userName: string;
  action: SystemAuditAction;
  documentType: SystemAuditDocumentType;
  documentId: string;
  direction: PaymentDirection | null;
}

// Phase 26a -- the Transaction list report (Reports > Accounting).

/** Every lifecycle state across the thirteen transaction types. All thirteen have
 * Draft/Approved/Void; only Quotation and Purchase Order also have Converted. */
export type TransactionListStatus = 'Draft' | 'Approved' | 'Void' | 'Converted';

/**
 * One transaction of any type. `amount` is each document's own headline figure in its own terms
 * (gross total, payment amount, journal debit side, adjustment value) -- which is exactly why the
 * screen has no footer total: the column is not additive across types. `createdByName` is derived
 * from the audit trail and is null for documents created before Phase 16d began writing one.
 */
export interface TransactionListRowDto {
  date: string;
  documentType: TransactionApprovalDocumentType;
  documentId: string;
  code: string;
  reference: string | null;
  status: TransactionListStatus;
  amount: number;
  createdByUserId: string | null;
  createdByName: string | null;
  approvedByUserId: string | null;
  approvedByName: string | null;
  approvedAt: string | null;
  createdAt: string;
  description: string | null;
  direction: PaymentDirection | null;
}
