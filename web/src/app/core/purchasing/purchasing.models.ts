import { VatRate } from '../catalog/catalog.models';
import { ContactType } from '../contacts/contacts.models';
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
  discountPct: number;
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
  discountPct: number;
  customStatusId: string | null;
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
  discountPct: number;
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

export interface VoidPurchaseOrderResult {
  id: string;
  code: string;
  status: PurchaseOrderStatus;
  voidedAt: string | null;
}

// --- Purchase Bill ---

export interface PurchaseBillLineInput {
  productId: string;
  quantity: number;
  rate: number;
  vatRate: VatRate;
  expenditureClassification: ExpenditureClassification;
  discountPct: number;
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
  discountPct: number;
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
  discountPct: number;
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

export interface VoidPurchaseBillResult {
  id: string;
  code: string;
  status: PurchaseBillStatus;
  voidedAt: string | null;
}

export interface PurchaseBillConversionTemplate {
  contactId: string;
  date: string;
  reference: string | null;
  referrerType: DocumentType;
  referrerId: string;
  discountPct: number;
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

export interface VoidExpenseResult {
  id: string;
  code: string;
  status: ExpenseStatus;
  voidedAt: string | null;
}

// --- Debit Note ---

export interface DebitNoteLineInput {
  productId: string;
  quantity: number;
  rate: number;
  vatRate: VatRate;
  discountPct: number;
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
  discountPct: number;
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
  discountPct: number;
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

export interface VoidDebitNoteResult {
  id: string;
  code: string;
  status: DebitNoteStatus;
  voidedAt: string | null;
}

export interface DebitNoteConversionTemplate {
  contactId: string;
  date: string;
  reference: string | null;
  tdsTypeId: string | null;
  referrerType: DocumentType;
  referrerId: string;
  discountPct: number;
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
  itemDiscount: number;
  transactionDiscount: number;
  netSales: number;
  vatType: VatRate;
  vatAmount: number;
  totalAmount: number;
}

export interface PurchaseMasterReportDto {
  fromDate: string;
  toDate: string;
  rows: PurchaseMasterReportRowDto[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalAmount: number;
}

// --- TDS Report (Phase 8d) ---

export interface TdsReportRowDto {
  contactId: string;
  contactCode: string;
  contactName: string;
  contactPan: string | null;
  documentType: DocumentType;
  entryNo: string;
  entryDate: string;
  tdsTypeCode: string;
  tdsTypeName: string;
  tdsRatePct: number;
  grossAmount: number;
  tdsAmount: number;
  netPayableAmount: number;
}

export interface TdsReportDto {
  fromDate: string;
  toDate: string;
  rows: TdsReportRowDto[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalGrossAmount: number;
  totalTdsAmount: number;
}

// --- Annex 13 Report (Phase 8e) ---

export interface AnnexThirteenReportRowDto {
  contactId: string;
  contactCode: string;
  contactPan: string | null;
  contactName: string;
  contactType: ContactType;
  openingBalance: number;
  servicePurchaseCapital: number;
  servicePurchaseOthers: number;
  goodsPurchaseCapital: number;
  goodsPurchaseOthers: number;
  serviceSales: number;
  goodsSales: number;
  totalActivity: number;
  closingBalance: number;
}

export interface AnnexThirteenReportDto {
  fromDate: string;
  toDate: string;
  thresholdAmount: number;
  rows: AnnexThirteenReportRowDto[];
  page: number;
  pageSize: number;
  totalCount: number;
}

// --- Purchase Register (Phase 19) ---

export interface PurchaseRegisterRowDto {
  date: string;
  documentType: DocumentType;
  documentCode: string;
  importDeclarationNo: string | null;
  contactId: string;
  contactName: string;
  contactPan: string | null;
  taxExemptValue: number;
  taxableNonCapitalLocalValue: number;
  taxableNonCapitalLocalVat: number;
  taxableNonCapitalImportValue: number;
  taxableNonCapitalImportVat: number;
  taxableCapitalValue: number;
  taxableCapitalVat: number;
}

export interface PurchaseRegisterDto {
  fromDate: string;
  toDate: string;
  items: PurchaseRegisterRowDto[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalTaxExemptValue: number;
  totalTaxableNonCapitalLocalValue: number;
  totalTaxableNonCapitalLocalVat: number;
  totalTaxableNonCapitalImportValue: number;
  totalTaxableNonCapitalImportVat: number;
  totalTaxableCapitalValue: number;
  totalTaxableCapitalVat: number;
}
