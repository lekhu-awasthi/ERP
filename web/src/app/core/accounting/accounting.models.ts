import { VatRate } from '../catalog/catalog.models';

export type AccountRootType = 'Asset' | 'Liability' | 'Equity' | 'Income' | 'Expense';

export interface AccountGroup {
  id: string;
  organizationId: string;
  name: string;
  rootType: AccountRootType;
  parentGroupId: string | null;
  isActive: boolean;
  createdAt: string;
}

export interface CreateAccountGroupRequest {
  name: string;
  rootType: AccountRootType;
  parentGroupId: string | null;
}

export interface CreateAccountGroupResult {
  id: string;
  name: string;
  rootType: AccountRootType;
  parentGroupId: string | null;
}

export interface UpdateAccountGroupRequest {
  name: string;
  parentGroupId: string | null;
  isActive: boolean;
}

export interface UpdateAccountGroupResult {
  id: string;
  name: string;
  rootType: AccountRootType;
  parentGroupId: string | null;
  isActive: boolean;
}

export interface Account {
  id: string;
  organizationId: string;
  code: string;
  name: string;
  rootType: AccountRootType;
  groupId: string;
  isActive: boolean;
  createdAt: string;
}

export interface CreateAccountRequest {
  name: string;
  groupId: string;
}

export interface CreateAccountResult {
  id: string;
  code: string;
  name: string;
  rootType: AccountRootType;
  groupId: string;
}

export interface UpdateAccountRequest {
  name: string;
  groupId: string;
  isActive: boolean;
}

export interface UpdateAccountResult {
  id: string;
  name: string;
  rootType: AccountRootType;
  groupId: string;
  isActive: boolean;
}

export type JournalVoucherStatus = 'Draft' | 'Approved' | 'Void';

export interface JournalVoucherLineInput {
  accountId: string;
  debit: number;
  credit: number;
}

export interface JournalVoucher {
  id: string;
  organizationId: string;
  code: string;
  date: string;
  reference: string | null;
  status: JournalVoucherStatus;
  approvedByUserId: string | null;
  approvedAt: string | null;
  createdAt: string;
}

export interface JournalVoucherLineDto {
  id: string;
  accountId: string;
  debit: number;
  credit: number;
}

export interface PostedGlLineDto {
  id: string;
  accountId: string;
  debit: number;
  credit: number;
}

export interface JournalVoucherDetail {
  id: string;
  organizationId: string;
  code: string;
  date: string;
  reference: string | null;
  status: JournalVoucherStatus;
  approvedByUserId: string | null;
  approvedAt: string | null;
  createdAt: string;
  lines: JournalVoucherLineDto[];
  glLines: PostedGlLineDto[] | null;
}

export interface JournalVoucherRequest {
  date: string;
  reference: string | null;
  lines: JournalVoucherLineInput[];
}

export interface CreateJournalVoucherResult {
  id: string;
  code: string;
  status: JournalVoucherStatus;
}

export interface UpdateJournalVoucherResult {
  id: string;
  code: string;
  status: JournalVoucherStatus;
}

export interface ApproveJournalVoucherResult {
  id: string;
  code: string;
  status: JournalVoucherStatus;
  approvedAt: string | null;
}

export interface VoidJournalVoucherResult {
  id: string;
  code: string;
  status: JournalVoucherStatus;
  voidedAt: string | null;
}

export type CashTransferStatus = 'Draft' | 'Approved' | 'Void';

export interface CashTransferLineInput {
  toAccountId: string;
  amount: number;
}

export interface CashTransfer {
  id: string;
  organizationId: string;
  code: string;
  date: string;
  reference: string | null;
  fromAccountId: string;
  status: CashTransferStatus;
  approvedByUserId: string | null;
  approvedAt: string | null;
  createdAt: string;
}

export interface CashTransferLineDto {
  id: string;
  toAccountId: string;
  amount: number;
}

export interface CashTransferDetail {
  id: string;
  organizationId: string;
  code: string;
  date: string;
  reference: string | null;
  fromAccountId: string;
  status: CashTransferStatus;
  approvedByUserId: string | null;
  approvedAt: string | null;
  createdAt: string;
  lines: CashTransferLineDto[];
  glLines: PostedGlLineDto[] | null;
}

export interface CashTransferRequest {
  date: string;
  reference: string | null;
  fromAccountId: string;
  lines: CashTransferLineInput[];
}

export interface CreateCashTransferResult {
  id: string;
  code: string;
  status: CashTransferStatus;
}

export interface UpdateCashTransferResult {
  id: string;
  code: string;
  status: CashTransferStatus;
}

export interface ApproveCashTransferResult {
  id: string;
  code: string;
  status: CashTransferStatus;
  approvedAt: string | null;
}

export interface VoidCashTransferResult {
  id: string;
  code: string;
  status: CashTransferStatus;
  voidedAt: string | null;
}

// --- Reports (Phase 8a) ---

export interface TrialBalanceRowDto {
  accountId: string;
  accountCode: string;
  accountName: string;
  debit: number;
  credit: number;
}

export interface TrialBalanceDto {
  asOfDate: string;
  rows: TrialBalanceRowDto[];
  totalDebit: number;
  totalCredit: number;
  isBalanced: boolean;
}

export interface AccountGroupBalanceDto {
  groupId: string;
  groupName: string;
  balance: number;
}

export interface BalanceSheetDto {
  asOfDate: string;
  assetGroups: AccountGroupBalanceDto[];
  liabilityGroups: AccountGroupBalanceDto[];
  equityGroups: AccountGroupBalanceDto[];
  netIncome: number;
  totalAssets: number;
  totalLiabilities: number;
  totalEquity: number;
  isBalanced: boolean;
}

export interface IncomeStatementRowDto {
  accountId: string;
  accountCode: string;
  accountName: string;
  rootType: AccountRootType;
  amount: number;
}

export interface IncomeStatementDto {
  fromDate: string;
  toDate: string;
  incomeRows: IncomeStatementRowDto[];
  expenseRows: IncomeStatementRowDto[];
  totalIncome: number;
  totalExpense: number;
  netIncome: number;
}

// --- Reports (Phase 8c) ---

export interface VatSummarySalesBucketDto {
  vatRate: VatRate;
  netSalesAmount: number;
  outputVatAmount: number;
}

export interface VatSummaryPurchaseBucketDto {
  vatRate: VatRate;
  netPurchaseAmount: number;
  inputVatAmount: number;
}

export interface VatSummaryReportDto {
  fromDate: string;
  toDate: string;
  salesBuckets: VatSummarySalesBucketDto[];
  purchaseBuckets: VatSummaryPurchaseBucketDto[];
  totalOutputVat: number;
  totalInputVat: number;
  netVatPayable: number;
}
