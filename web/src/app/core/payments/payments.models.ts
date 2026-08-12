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

export interface PaymentRequest {
  contactId: string;
  date: string;
  paymentModeId: string | null;
  accountId: string;
  amount: number;
  reference: string | null;
  allocations: PaymentAllocationInput[];
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

export interface GlLinePreviewDto {
  accountId: string;
  debit: number;
  credit: number;
}
