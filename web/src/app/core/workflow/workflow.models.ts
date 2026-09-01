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
