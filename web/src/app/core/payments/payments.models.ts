import { DocumentType, PostedGlLineDto } from '../sales/sales.models';

export type PaymentStatus = 'Draft' | 'Approved' | 'Void';
export type PaymentDirection = 'Received' | 'Paid';

export interface PaymentAllocationInput {
  targetDocumentType: DocumentType;
  targetDocumentId: string;
  amount: number;
}

export interface Payment {
  id: string;
  organizationId: string;
  contactId: string;
  direction: PaymentDirection;
  code: string;
  date: string;
  paymentModeId: string | null;
  accountId: string;
  amount: number;
  reference: string | null;
  status: PaymentStatus;
  approvedByUserId: string | null;
  approvedAt: string | null;
  createdAt: string;
}

export interface PaymentAllocationDto extends PaymentAllocationInput {
  id: string;
}

export interface PaymentDetail extends Payment {
  allocations: PaymentAllocationDto[];
  glLines: PostedGlLineDto[] | null;
}

// Phase 17 -- supplied on Create/UpdatePayment only when the chosen PaymentMode has
// requiresChequeDetails === true (docs/phase-17-status.md decision #6).
export interface ChequeDetailsInput {
  chequeNo: string;
  chequeDate: string;
  receivedDate: string | null;
}

export interface PaymentRequest {
  contactId: string;
  direction: PaymentDirection;
  date: string;
  paymentModeId: string | null;
  accountId: string;
  amount: number;
  reference: string | null;
  allocations: PaymentAllocationInput[];
  chequeDetails?: ChequeDetailsInput | null;
}

export interface CreatePaymentResult {
  id: string;
  code: string;
  status: PaymentStatus;
}

export interface UpdatePaymentResult {
  id: string;
  code: string;
  status: PaymentStatus;
}

export interface ApprovePaymentResult {
  id: string;
  code: string;
  status: PaymentStatus;
  approvedAt: string | null;
}

export interface VoidPaymentResult {
  id: string;
  code: string;
  status: PaymentStatus;
  voidedAt: string | null;
}

export interface GlLinePreviewDto {
  accountId: string;
  debit: number;
  credit: number;
}

// --- Phase 17: Cheque Register ---

export type ChequeStatus = 'Pending' | 'Deposited' | 'Cleared' | 'Bounced' | 'Cancelled';

export interface ChequeDto {
  id: string;
  linkedPaymentId: string;
  direction: PaymentDirection;
  contactId: string;
  contactName: string;
  accountId: string;
  accountName: string;
  chequeNo: string;
  chequeDate: string;
  receivedDate: string | null;
  amount: number;
  status: ChequeStatus;
}

export interface ChequeDashboardSummaryDto {
  receivedCount: number;
  issuedCount: number;
}

export interface TransitionChequeStatusResult {
  id: string;
  status: ChequeStatus;
}

// --- Phase 17: Allocate Customer/Supplier Payment ---

/** Decision #2 (docs/phase-17-status.md) -- sourceType/id identify the credit being applied
 * (Payment or a JournalVoucher line); parentDocumentId is only set for JournalVoucher (the line's
 * own parent voucher, needed by ApplyPaymentAllocationCommand's lock-date check). */
export interface AllocatablePaymentDto {
  sourceType: DocumentType;
  id: string;
  parentDocumentId: string | null;
  code: string;
  date: string;
  contactId: string;
  contactName: string;
  amount: number;
  allocated: number;
  balance: number;
}

export interface ApplyPaymentAllocationResult {
  id: string;
  amount: number;
  allocated: number;
  balance: number;
}
