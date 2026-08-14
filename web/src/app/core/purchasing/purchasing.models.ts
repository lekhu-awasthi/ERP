import { VatRate } from '../catalog/catalog.models';
import { DocumentType } from '../sales/sales.models';

export type PurchaseOrderStatus = 'Draft' | 'Approved' | 'Void' | 'Converted';
export type PurchaseBillStatus = 'Draft' | 'Approved' | 'Void';
export type ExpenseStatus = 'Draft' | 'Approved' | 'Void';
export type DebitNoteStatus = 'Draft' | 'Approved' | 'Void';
export type ExpenditureClassification = 'Others' | 'Capital';

export interface PostedGlLineDto {
  id: string;
  accountId: string;
  debit: number;
  credit: number;
}

export interface GlLinePreviewDto {
  accountId: string;
  debit: number;
  credit: number;
}

// --- Purchase Order ---

export interface PurchaseOrderLineInput {
  productId: string;
  quantity: number;
  rate: number;
  vatRate: VatRate;
}

export interface PurchaseOrder {
  id: string;
  organizationId: string;
  contactId: string;
  code: string;
  date: string;
  reference: string | null;
  status: PurchaseOrderStatus;
  approvedByUserId: string | null;
  approvedAt: string | null;
  createdAt: string;
}

export interface PurchaseOrderLineDto extends PurchaseOrderLineInput {
  id: string;
  amount: number;
  vatAmount: number;
}

export interface PurchaseOrderDetail extends PurchaseOrder {
  lines: PurchaseOrderLineDto[];
}

export interface PurchaseOrderRequest {
  contactId: string;
  date: string;
  reference: string | null;
  lines: PurchaseOrderLineInput[];
}

export interface CreatePurchaseOrderResult {
  id: string;
  code: string;
  status: PurchaseOrderStatus;
}

export interface UpdatePurchaseOrderResult {
  id: string;
  code: string;
  status: PurchaseOrderStatus;
}

export interface ApprovePurchaseOrderResult {
  id: string;
  code: string;
  status: PurchaseOrderStatus;
  approvedAt: string | null;
}

// --- Purchase Bill ---

export interface PurchaseBillLineInput {
  productId: string;
  quantity: number;
  rate: number;
  vatRate: VatRate;
  expenditureClassification: ExpenditureClassification;
}

export interface PurchaseBill {
  id: string;
  organizationId: string;
  contactId: string;
  warehouseId: string;
  code: string;
  date: string;
  reference: string | null;
  supplierInvoiceReference: string | null;
  isImport: boolean;
  importCountry: string | null;
  importDate: string | null;
  importDocumentNo: string | null;
  tdsTypeId: string | null;
  tdsAmount: number;
  status: PurchaseBillStatus;
  approvedByUserId: string | null;
  approvedAt: string | null;
  createdAt: string;
  referrerType: DocumentType | null;
  referrerId: string | null;
}

export interface PurchaseBillLineDto extends PurchaseBillLineInput {
  id: string;
  amount: number;
  vatAmount: number;
}

export interface PurchaseBillDetail extends PurchaseBill {
  grandTotal: number;
  lines: PurchaseBillLineDto[];
  glLines: PostedGlLineDto[] | null;
}

export interface PurchaseBillRequest {
  contactId: string;
  warehouseId: string;
  date: string;
  reference: string | null;
  supplierInvoiceReference: string | null;
  isImport: boolean;
  importCountry: string | null;
  importDate: string | null;
  importDocumentNo: string | null;
  tdsTypeId: string | null;
  lines: PurchaseBillLineInput[];
  referrerType?: DocumentType | null;
  referrerId?: string | null;
}

export interface CreatePurchaseBillResult {
  id: string;
  code: string;
  status: PurchaseBillStatus;
}

export interface UpdatePurchaseBillResult {
  id: string;
  code: string;
  status: PurchaseBillStatus;
}

export interface ApprovePurchaseBillResult {
  id: string;
  code: string;
  status: PurchaseBillStatus;
  approvedAt: string | null;
}

export interface PurchaseBillConversionTemplate {
  contactId: string;
  date: string;
  reference: string | null;
  referrerType: DocumentType;
  referrerId: string;
  lines: PurchaseBillLineInput[];
}

// --- Expense ---

export interface ExpenseLineInput {
  accountId: string;
  amount: number;
  vatRate: VatRate;
}

export interface Expense {
  id: string;
  organizationId: string;
  contactId: string;
  code: string;
  date: string;
  dueDate: string | null;
  supplierInvoiceReference: string | null;
  notes: string | null;
  tdsApplicable: boolean;
  tdsTypeId: string | null;
  tdsAmount: number;
  status: ExpenseStatus;
  approvedByUserId: string | null;
  approvedAt: string | null;
  createdAt: string;
}

export interface ExpenseLineDto extends ExpenseLineInput {
  id: string;
  vatAmount: number;
}

export interface ExpenseDetail extends Expense {
  grandTotal: number;
  lines: ExpenseLineDto[];
  glLines: PostedGlLineDto[] | null;
}

export interface ExpenseRequest {
  contactId: string;
  date: string;
  dueDate: string | null;
  supplierInvoiceReference: string | null;
  notes: string | null;
  tdsApplicable: boolean;
  tdsTypeId: string | null;
  lines: ExpenseLineInput[];
}

export interface CreateExpenseResult {
  id: string;
  code: string;
  status: ExpenseStatus;
}

export interface UpdateExpenseResult {
  id: string;
  code: string;
  status: ExpenseStatus;
}

export interface ApproveExpenseResult {
  id: string;
  code: string;
  status: ExpenseStatus;
  approvedAt: string | null;
}

// --- Debit Note ---

export interface DebitNoteLineInput {
  productId: string;
  quantity: number;
  rate: number;
  vatRate: VatRate;
}

export interface DebitNote {
  id: string;
  organizationId: string;
  contactId: string;
  code: string;
  date: string;
  reference: string | null;
  tdsTypeId: string | null;
  tdsAmount: number;
  status: DebitNoteStatus;
  approvedByUserId: string | null;
  approvedAt: string | null;
  createdAt: string;
  referrerType: DocumentType | null;
  referrerId: string | null;
}

export interface DebitNoteLineDto extends DebitNoteLineInput {
  id: string;
  amount: number;
  vatAmount: number;
}

export interface DebitNoteDetail extends DebitNote {
  lines: DebitNoteLineDto[];
  glLines: PostedGlLineDto[] | null;
}

export interface DebitNoteRequest {
  contactId: string;
  date: string;
  reference: string | null;
  tdsTypeId: string | null;
  lines: DebitNoteLineInput[];
  referrerType?: DocumentType | null;
  referrerId?: string | null;
}

export interface CreateDebitNoteResult {
  id: string;
  code: string;
  status: DebitNoteStatus;
}

export interface UpdateDebitNoteResult {
  id: string;
  code: string;
  status: DebitNoteStatus;
}

export interface ApproveDebitNoteResult {
  id: string;
  code: string;
  status: DebitNoteStatus;
  approvedAt: string | null;
}

export interface DebitNoteConversionTemplate {
  contactId: string;
  date: string;
  reference: string | null;
  tdsTypeId: string | null;
  referrerType: DocumentType;
  referrerId: string;
  lines: DebitNoteLineInput[];
}

// --- Purchase Master Report (Phase 8b) ---

export interface PurchaseMasterReportRowDto {
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
  vatType: VatRate;
  vatAmount: number;
  totalAmount: number;
}

export interface PurchaseMasterReportDto {
  fromDate: string;
  toDate: string;
  rows: PurchaseMasterReportRowDto[];
}
