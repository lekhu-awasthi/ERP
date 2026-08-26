import { VatRate } from '../catalog/catalog.models';

export type QuotationStatus = 'Draft' | 'Approved' | 'Void' | 'Converted';
export type InvoiceStatus = 'Draft' | 'Approved' | 'Void';
export type SalesOrderStatus = 'Draft' | 'Approved' | 'Void';
export type CreditNoteStatus = 'Draft' | 'Approved' | 'Void';
export type DocumentType =
  | 'Quotation'
  | 'SalesOrder'
  | 'Invoice'
  | 'CreditNote'
  | 'PurchaseOrder'
  | 'PurchaseBill'
  | 'Expense'
  | 'DebitNote'
  | 'Payment';

export interface QuotationLineInput {
  productId: string;
  quantity: number;
  rate: number;
  vatRate: VatRate;
  discountPct: number;
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
  discountPct: number;
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
  discountPct: number;
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

export interface VoidQuotationResult {
  id: string;
  code: string;
  status: QuotationStatus;
  voidedAt: string | null;
}

export interface InvoiceLineInput {
  productId: string;
  quantity: number;
  rate: number;
  vatRate: VatRate;
  discountPct: number;
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
  discountPct: number;
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
  discountPct: number;
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

export interface VoidInvoiceResult {
  id: string;
  code: string;
  status: InvoiceStatus;
  voidedAt: string | null;
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
  discountPct: number;
  lines: InvoiceLineInput[];
}

export interface SalesOrderLineInput {
  productId: string;
  quantity: number;
  rate: number;
  vatRate: VatRate;
  discountPct: number;
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
  discountPct: number;
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
  discountPct: number;
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

export interface VoidSalesOrderResult {
  id: string;
  code: string;
  status: SalesOrderStatus;
  voidedAt: string | null;
}

export interface CreditNoteLineInput {
  productId: string;
  quantity: number;
  rate: number;
  vatRate: VatRate;
  discountPct: number;
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
  discountPct: number;
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
  discountPct: number;
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

export interface VoidCreditNoteResult {
  id: string;
  code: string;
  status: CreditNoteStatus;
  voidedAt: string | null;
}

export interface CreditNoteConversionTemplate {
  contactId: string;
  date: string;
  reference: string | null;
  referrerType: DocumentType;
  referrerId: string;
  discountPct: number;
  lines: CreditNoteLineInput[];
}

// --- Sales Master Report (Phase 8b) ---

export interface SalesMasterReportRowDto {
  contactId: string;
  contactCode: string;
  contactName: string;
  type: DocumentType;
  contactGroupId: string | null;
  contactGroupName: string | null;
  warehouseId: string | null;
  warehouseName: string | null;
  entryNo: string;
  referenceNo: string | null;
  entryDate: string;
  productId: string;
  productCode: string;
  productName: string;
  quantity: number;
  rate: number;
  amount: number;
  itemDiscount: number;
  transactionDiscount: number;
  netSales: number;
  vatType: VatRate;
  vatAmount: number;
  totalAmount: number;
}

export interface SalesMasterReportDto {
  fromDate: string;
  toDate: string;
  rows: SalesMasterReportRowDto[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalAmount: number;
}

// --- Annex 5 Report (Phase 8f) ---

export interface AnnexFiveReportRowDto {
  contactId: string;
  contactCode: string;
  contactName: string;
  contactPan: string | null;
  documentType: DocumentType;
  billNo: string;
  billDate: string;
  amount: number;
  taxableAmount: number;
  taxAmount: number;
  totalAmount: number;
  isActive: boolean;
}

export interface AnnexFiveReportDto {
  fromDate: string;
  toDate: string;
  rows: AnnexFiveReportRowDto[];
  page: number;
  pageSize: number;
  totalCount: number;
}

// --- Sales Register (Phase 19) ---

export interface SalesRegisterRowDto {
  date: string;
  documentType: DocumentType;
  documentCode: string;
  contactId: string;
  contactName: string;
  contactPan: string | null;
  totalValue: number;
  taxExemptValue: number;
  taxableValue: number;
  vatAmount: number;
  exportValue: number;
  exportCountry: string | null;
  exportDeclarationNo: string | null;
  exportDeclarationDate: string | null;
}

export interface SalesRegisterDto {
  fromDate: string;
  toDate: string;
  items: SalesRegisterRowDto[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalValue: number;
  totalTaxExemptValue: number;
  totalTaxableValue: number;
  totalVatAmount: number;
}

// --- Reporting Tags (Phase 19) ---

export interface TransactionReportingTagDto {
  tagOptionId: string;
  tagOptionName: string;
  categoryId: string;
  categoryName: string;
}

// --- Custom Field Values (Phase 20a) ---

export interface CustomFieldValueDto {
  fieldDefinitionId: string;
  value: string;
}

export interface CustomFieldValueInput {
  fieldDefinitionId: string;
  value: string;
}
