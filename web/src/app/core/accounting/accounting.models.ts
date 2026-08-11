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
