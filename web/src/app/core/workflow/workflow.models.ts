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
