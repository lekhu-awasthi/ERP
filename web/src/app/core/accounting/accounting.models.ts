import { VatRate } from '../catalog/catalog.models';
import { PaymentDirection } from '../payments/payments.models';

export type AccountRootType = 'Asset' | 'Liability' | 'Equity' | 'Income' | 'Expense';

// Phase 17 -- Bank/Cash marker (docs/phase-17-status.md decision #3). No "Wallet" kind --
// e-wallets are Bank-kind accounts pointing at a wallet provider in the Bank lookup.
export type AccountKind = 'Other' | 'Bank' | 'Cash';

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
  kind: AccountKind;
  bankId: string | null;
  accountNumber: string | null;
  isActive: boolean;
  createdAt: string;
}

export interface CreateAccountRequest {
  name: string;
  groupId: string;
  kind?: AccountKind;
  bankId?: string | null;
  accountNumber?: string | null;
}

export interface CreateAccountResult {
  id: string;
  code: string;
  name: string;
  rootType: AccountRootType;
  groupId: string;
  kind: AccountKind;
  bankId: string | null;
  accountNumber: string | null;
}

export interface UpdateAccountRequest {
  name: string;
  groupId: string;
  isActive: boolean;
  kind?: AccountKind;
  bankId?: string | null;
  accountNumber?: string | null;
}

export interface UpdateAccountResult {
  id: string;
  name: string;
  rootType: AccountRootType;
  groupId: string;
  isActive: boolean;
  kind: AccountKind;
  bankId: string | null;
  accountNumber: string | null;
}

// --- Phase 17: Bank Accounts ---

export interface BankAccountDto {
  id: string;
  code: string;
  name: string;
  kind: AccountKind;
  bankId: string | null;
  bankName: string | null;
  accountNumber: string | null;
  isActive: boolean;
  balance: number;
}

// --- Phase 17: Opening Balances (Account tab) ---

export interface AccountOpeningBalanceDto {
  accountId: string;
  accountCode: string;
  accountName: string;
  rootType: string;
  groupName: string;
  debit: number;
  credit: number;
  // Phase 27a: the OpeningBalanceLine's own id, null until a balance has been set for this account.
  // Reporting tags are keyed by it -- both Opening Balances tabs carry a tag control in their row
  // form -- so the tag editor only appears once the row exists.
  lineId: string | null;
}

export interface OpeningBalanceLineRequest {
  debit: number;
  credit: number;
}

export interface OpeningBalanceLineResult {
  id: string;
  accountId: string;
  debit: number;
  credit: number;
}

export type JournalVoucherStatus = 'Draft' | 'Approved' | 'Void';

export interface JournalVoucherLineInput {
  accountId: string;
  debit: number;
  credit: number;
  /** Decision #2 (docs/phase-17-status.md) -- tags this line as posting against a Contact's own
   * AR/AP control account, making it an allocatable credit source once the voucher is Approved. */
  contactId: string | null;
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
  contactId: string | null;
}

export interface PostedGlLineDto {
  id: string;
  accountId: string;
  debit: number;
  credit: number;
}

export interface JournalVoucherDetail {
  /** Phase 28 (FR-2.5) -- the currency every amount above is denominated in, and its rate to the
   * base currency. glLines, by contrast, are always in the base currency. */
  currencyCode: string;
  exchangeRate: number;
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
  /** Phase 28 (FR-2.5). Optional: omitting both means the base currency at rate 1. */
  currencyCode?: string | null;
  exchangeRate?: number | null;
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
  /** Phase 28 (FR-2.5) -- the currency every amount above is denominated in, and its rate to the
   * base currency. glLines, by contrast, are always in the base currency. */
  currencyCode: string;
  exchangeRate: number;
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
  /** Phase 28 (FR-2.5). Optional: omitting both means the base currency at rate 1. */
  currencyCode?: string | null;
  exchangeRate?: number | null;
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

/**
 * Phase 26a adds FR-9.1's Compare (period-over-period) columns to all three financial statements.
 * Every `compare*` field is `null` when Compare is off -- not zero -- so a template can tell
 * "not compared" from "compared, and the figure was nil". The comparison window is chosen on the
 * server (ComparePeriod: prior-year same date for the as-of reports, same-length preceding period
 * for the range one) and echoed back on the response, so the screen labels the extra columns with
 * the real dates instead of the word "prior".
 */
export interface TrialBalanceRowDto {
  accountId: string;
  accountCode: string;
  accountName: string;
  debit: number;
  credit: number;
  compareDebit: number | null;
  compareCredit: number | null;
}

export interface TrialBalanceDto {
  asOfDate: string;
  rows: TrialBalanceRowDto[];
  totalDebit: number;
  totalCredit: number;
  isBalanced: boolean;
  compareAsOfDate: string | null;
  compareTotalDebit: number | null;
  compareTotalCredit: number | null;
}

export interface AccountGroupBalanceDto {
  groupId: string;
  groupName: string;
  balance: number;
  compareBalance: number | null;
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
  compareAsOfDate: string | null;
  compareNetIncome: number | null;
  compareTotalAssets: number | null;
  compareTotalLiabilities: number | null;
  compareTotalEquity: number | null;
}

export interface IncomeStatementRowDto {
  accountId: string;
  accountCode: string;
  accountName: string;
  rootType: AccountRootType;
  amount: number;
  compareAmount: number | null;
}

export interface IncomeStatementDto {
  fromDate: string;
  toDate: string;
  incomeRows: IncomeStatementRowDto[];
  expenseRows: IncomeStatementRowDto[];
  totalIncome: number;
  totalExpense: number;
  netIncome: number;
  compareFromDate: string | null;
  compareToDate: string | null;
  compareTotalIncome: number | null;
  compareTotalExpense: number | null;
  compareNetIncome: number | null;
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

// --- Reports (Phase 19) ---

export interface CashFlowSummaryDto {
  fromDate: string;
  toDate: string;
  startingBalance: number;
  receivedFromCustomerCashIn: number;
  receivedFromCustomerCashOut: number;
  otherReceiptsCashIn: number;
  otherReceiptsCashOut: number;
  paidToSupplierCashIn: number;
  paidToSupplierCashOut: number;
  otherPaymentsCashIn: number;
  otherPaymentsCashOut: number;
  endingBalance: number;
  receivedFromCustomerBalance: number;
  otherReceiptsBalance: number;
  paidToSupplierBalance: number;
  otherPaymentsBalance: number;
}

export interface RatioAnalysisDto {
  fromDate: string;
  toDate: string;
  currentRatio: number;
  quickRatio: number;
  cashRatio: number;
  debtToEquityRatio: number;
  debtRatio: number;
  inventoryTurnover: number;
  receivablesTurnover: number;
  assetTurnover: number;
  receivableDays: number;
  payableDays: number;
  inventoryHoldingPeriodDays: number;
  cashConversionCycleDays: number;
  grossProfitMarginPct: number;
  netProfitMarginPct: number;
  returnOnAssetsPct: number;
  returnOnEquityPct: number;
}

// --- Reports (Phase 26a): the four GL reports the catalog was missing ---

/**
 * The eleven document types that can post a GlJournalEntry -- grep-confirmed against every
 * GlJournalEntry.Post call site, not assumed from the DocumentType enum, which also contains four
 * transaction types that post nothing (Quotation, SalesOrder, PurchaseOrder, WarehouseTransfer)
 * and several non-document entries.
 */
export type GlSourceDocumentType =
  | 'Invoice'
  | 'CreditNote'
  | 'PurchaseBill'
  | 'Expense'
  | 'DebitNote'
  | 'JournalVoucher'
  | 'CashTransfer'
  | 'InventoryAdjustment'
  | 'Payment'
  | 'ProductionJournal'
  | 'OpeningBalance';

/** Balances travel as a non-negative magnitude plus this marker, never a signed number, so no
 * template has to know which side is normal for which account. */
export type GlBalanceType = 'DR' | 'CR';

export interface JournalReportLineDto {
  accountId: string;
  accountCode: string;
  accountName: string;
  debit: number;
  credit: number;
}

/** One posted document's journal entry. totalDebit and totalCredit always match -- the domain
 * enforces it on every posting -- and are shown anyway, the way the live report prints them. */
export interface JournalReportEntryDto {
  glJournalEntryId: string;
  date: string;
  documentType: GlSourceDocumentType;
  documentId: string;
  documentCode: string | null;
  reference: string | null;
  direction: PaymentDirection | null;
  lines: JournalReportLineDto[];
  totalDebit: number;
  totalCredit: number;
}

export interface GeneralLedgerSummaryRowDto {
  accountId: string;
  accountCode: string;
  accountName: string;
  parentGroupName: string;
  groupTypeName: string;
  rootType: AccountRootType;
  openingBalance: number;
  openingBalanceType: GlBalanceType;
  transactionDebit: number;
  transactionCredit: number;
  closingBalance: number;
  closingBalanceType: GlBalanceType;
}

export interface DetailGeneralLedgerRowDto {
  date: string;
  documentType: GlSourceDocumentType;
  documentId: string;
  documentCode: string | null;
  reference: string | null;
  description: string | null;
  debit: number;
  credit: number;
  balance: number;
  balanceType: GlBalanceType;
  direction: PaymentDirection | null;
}

/** One account section. periodDebit/periodCredit are what the live Closing Balance row prints in
 * its Debit and Credit cells -- the section's totals, not that row's own movement. */
export interface DetailGeneralLedgerAccountDto {
  accountId: string;
  accountCode: string;
  accountName: string;
  openingBalance: number;
  openingBalanceType: GlBalanceType;
  rows: DetailGeneralLedgerRowDto[];
  periodDebit: number;
  periodCredit: number;
  closingBalance: number;
  closingBalanceType: GlBalanceType;
}

export interface GeneralLedgerMasterRowDto {
  date: string;
  documentType: GlSourceDocumentType;
  documentId: string;
  documentCode: string | null;
  reference: string | null;
  accountId: string;
  accountCode: string;
  accountName: string;
  parentGroupName: string;
  groupTypeName: string;
  rootType: AccountRootType;
  debit: number;
  credit: number;
  direction: PaymentDirection | null;
}
