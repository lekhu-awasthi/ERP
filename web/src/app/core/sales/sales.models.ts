import { VatRate } from '../catalog/catalog.models';

export type QuotationStatus = 'Draft' | 'Approved' | 'Void';
export type InvoiceStatus = 'Draft' | 'Approved' | 'Void';
export type SalesOrderStatus = 'Draft' | 'Approved' | 'Void';
export type CreditNoteStatus = 'Draft' | 'Approved' | 'Void';
export type DocumentType = 'Quotation' | 'SalesOrder' | 'Invoice' | 'CreditNote' | 'Payment';

export interface QuotationLineInput {
  productId: string;
  quantity: number;
  rate: number;
  vatRate: VatRate;
}

export interface Quotation {
  id: string;
  organizationId: string;
  contactId: string;
  code: string;
  date: string;
  expiryDate: string | null;
  reference: string | null;
  status: QuotationStatus;
  approvedByUserId: string | null;
  approvedAt: string | null;
  createdAt: string;
}

export interface QuotationLineDto extends QuotationLineInput {
  id: string;
  amount: number;
  vatAmount: number;
}

export interface QuotationDetail extends Quotation {
  lines: QuotationLineDto[];
}

export interface QuotationRequest {
  contactId: string;
  date: string;
  expiryDate: string | null;
  reference: string | null;
  lines: QuotationLineInput[];
}

export interface CreateQuotationResult {
  id: string;
  code: string;
  status: QuotationStatus;
}

export interface UpdateQuotationResult {
  id: string;
  code: string;
  status: QuotationStatus;
}

export interface ApproveQuotationResult {
  id: string;
  code: string;
  status: QuotationStatus;
  approvedAt: string | null;
}

export interface InvoiceLineInput {
  productId: string;
  quantity: number;
  rate: number;
  vatRate: VatRate;
}

export interface Invoice {
  id: string;
  organizationId: string;
  contactId: string;
  warehouseId: string;
  code: string;
  date: string;
  reference: string | null;
  status: InvoiceStatus;
  approvedByUserId: string | null;
  approvedAt: string | null;
  createdAt: string;
  referrerType: DocumentType | null;
  referrerId: string | null;
}

export interface InvoiceLineDto extends InvoiceLineInput {
  id: string;
  amount: number;
  vatAmount: number;
}

export interface PostedGlLineDto {
  id: string;
  accountId: string;
  debit: number;
  credit: number;
}

export interface InvoiceDetail extends Invoice {
  grandTotal: number;
  lines: InvoiceLineDto[];
  glLines: PostedGlLineDto[] | null;
}

export interface InvoiceRequest {
  contactId: string;
  warehouseId: string;
  date: string;
  reference: string | null;
  lines: InvoiceLineInput[];
  referrerType?: DocumentType | null;
  referrerId?: string | null;
}

export interface CreateInvoiceResult {
  id: string;
  code: string;
  status: InvoiceStatus;
}

export interface UpdateInvoiceResult {
  id: string;
  code: string;
  status: InvoiceStatus;
}

export interface ApproveInvoiceResult {
  id: string;
  code: string;
  status: InvoiceStatus;
  approvedAt: string | null;
}

export interface GlLinePreviewDto {
  accountId: string;
  debit: number;
  credit: number;
}

export interface InvoiceConversionTemplate {
  contactId: string;
  date: string;
  reference: string | null;
  referrerType: DocumentType;
  referrerId: string;
  lines: InvoiceLineInput[];
}

export interface SalesOrderLineInput {
  productId: string;
  quantity: number;
  rate: number;
  vatRate: VatRate;
}

export interface SalesOrder {
  id: string;
  organizationId: string;
  contactId: string;
  code: string;
  date: string;
  deliveryDate: string | null;
  reference: string | null;
  status: SalesOrderStatus;
  approvedByUserId: string | null;
  approvedAt: string | null;
  createdAt: string;
}

export interface SalesOrderLineDto extends SalesOrderLineInput {
  id: string;
  amount: number;
  vatAmount: number;
}

export interface SalesOrderDetail extends SalesOrder {
  lines: SalesOrderLineDto[];
}

export interface SalesOrderRequest {
  contactId: string;
  date: string;
  deliveryDate: string | null;
  reference: string | null;
  lines: SalesOrderLineInput[];
}

export interface CreateSalesOrderResult {
  id: string;
  code: string;
  status: SalesOrderStatus;
}

export interface UpdateSalesOrderResult {
  id: string;
  code: string;
  status: SalesOrderStatus;
}

export interface ApproveSalesOrderResult {
  id: string;
  code: string;
  status: SalesOrderStatus;
  approvedAt: string | null;
}

export interface CreditNoteLineInput {
  productId: string;
  quantity: number;
  rate: number;
  vatRate: VatRate;
}

export interface CreditNote {
  id: string;
  organizationId: string;
  contactId: string;
  code: string;
  date: string;
  reference: string | null;
  status: CreditNoteStatus;
  approvedByUserId: string | null;
  approvedAt: string | null;
  createdAt: string;
  referrerType: DocumentType | null;
  referrerId: string | null;
}

export interface CreditNoteLineDto extends CreditNoteLineInput {
  id: string;
  amount: number;
  vatAmount: number;
}

export interface CreditNoteDetail extends CreditNote {
  lines: CreditNoteLineDto[];
  glLines: PostedGlLineDto[] | null;
}

export interface CreditNoteRequest {
  contactId: string;
  date: string;
  reference: string | null;
  lines: CreditNoteLineInput[];
  referrerType?: DocumentType | null;
  referrerId?: string | null;
}

export interface CreateCreditNoteResult {
  id: string;
  code: string;
  status: CreditNoteStatus;
}

export interface UpdateCreditNoteResult {
  id: string;
  code: string;
  status: CreditNoteStatus;
}

export interface ApproveCreditNoteResult {
  id: string;
  code: string;
  status: CreditNoteStatus;
  approvedAt: string | null;
}

export interface CreditNoteConversionTemplate {
  contactId: string;
  date: string;
  reference: string | null;
  referrerType: DocumentType;
  referrerId: string;
  lines: CreditNoteLineInput[];
}
