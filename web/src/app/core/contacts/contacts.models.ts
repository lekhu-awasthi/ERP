export type ContactType = 'Customer' | 'Supplier' | 'Lead';

export interface ContactGroup {
  id: string;
  organizationId: string;
  name: string;
  parentGroupId: string | null;
  isActive: boolean;
  createdAt: string;
}

export interface CreateContactGroupRequest {
  name: string;
  parentGroupId: string | null;
}

export interface CreateContactGroupResult {
  id: string;
  name: string;
  parentGroupId: string | null;
}

export interface UpdateContactGroupRequest {
  name: string;
  parentGroupId: string | null;
  isActive: boolean;
}

export interface UpdateContactGroupResult {
  id: string;
  name: string;
  parentGroupId: string | null;
  isActive: boolean;
}

export interface Contact {
  id: string;
  organizationId: string;
  type: ContactType;
  name: string;
  code: string;
  address: string | null;
  pan: string | null;
  phone: string | null;
  email: string | null;
  groupId: string | null;
  isActive: boolean;
  openingBalance: number;
  createdAt: string;
}

export interface CreateContactRequest {
  type: ContactType;
  name: string;
  address: string | null;
  pan: string | null;
  phone: string | null;
  email: string | null;
  groupId: string | null;
  openingBalance: number;
}

export interface CreateContactResult {
  id: string;
  code: string;
  type: ContactType;
  name: string;
}

export interface UpdateContactRequest {
  name: string;
  address: string | null;
  pan: string | null;
  phone: string | null;
  email: string | null;
  groupId: string | null;
  openingBalance: number;
}

export interface UpdateContactResult {
  id: string;
  name: string;
}

// Phase 9 (Customer & Supplier Ageing + Statement Reports). One shared shape answers both Customer
// and Supplier variants of each report -- see ContactAgeingSummaryQuery/ContactStatementQuery's own
// doc comments on why one backend handler serves both, discriminated by ContactType/route rather
// than forking into near-identical query shapes.

export interface ContactAgeingSummaryRowDto {
  contactId: string;
  contactCode: string;
  contactName: string;
  contactGroupName: string | null;
  days1To30: number;
  days31To60: number;
  days61To90: number;
  days91Plus: number;
  total: number;
}

export interface ContactAgeingSummaryDto {
  asOfDate: string;
  contactType: ContactType;
  rows: ContactAgeingSummaryRowDto[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalDays1To30: number;
  totalDays31To60: number;
  totalDays61To90: number;
  totalDays91Plus: number;
}

export type StatementDocumentType = 'Invoice' | 'CreditNote' | 'PurchaseBill' | 'DebitNote' | 'Expense' | 'Payment';

export type BalanceType = 'DR' | 'CR';

export interface ContactStatementRowDto {
  date: string;
  documentType: StatementDocumentType;
  code: string;
  reference: string | null;
  debit: number;
  credit: number;
  balance: number;
  balanceType: BalanceType;
}

export interface ContactStatementDto {
  contactId: string;
  contactCode: string;
  contactName: string;
  contactType: ContactType;
  fromDate: string;
  toDate: string;
  openingBalance: number;
  openingBalanceType: BalanceType;
  rows: ContactStatementRowDto[];
  closingBalance: number;
  closingBalanceType: BalanceType;
  page: number;
  pageSize: number;
  totalCount: number;
}

// Phase 10 (Contact Overview). A thin read over the same running-balance engine as
// ContactStatementDto -- no per-row running Balance (bounded recent-activity feed, not a ledger; see
// ContactOverviewQuery's own doc comment).
export interface ContactOverviewTransactionDto {
  date: string;
  documentType: StatementDocumentType;
  code: string;
  reference: string | null;
  debit: number;
  credit: number;
}

export interface ContactOverviewDto {
  contactId: string;
  contactCode: string;
  contactName: string;
  contactType: ContactType;
  openingBalance: number;
  openingBalanceType: BalanceType;
  closingBalance: number;
  closingBalanceType: BalanceType;
  recentTransactions: ContactOverviewTransactionDto[];
}

// Phase 18 (Contact Personnel, Attachments, Comments, Activities -- the Contact detail page's
// remaining tabs: "Contact Personnel", "Documents", "Activity").

export interface ContactPersonnelRowDto {
  id: string;
  name: string;
  address: string | null;
  code: string | null;
  phone: string | null;
  groupId: string | null;
  groupName: string | null;
  email: string | null;
  organizationTitle: string | null;
  createdAt: string;
}

export interface ContactPersonnelListDto {
  rows: ContactPersonnelRowDto[];
  page: number;
  pageSize: number;
  totalCount: number;
}

export interface ContactPersonnelRequest {
  name: string;
  address: string | null;
  code: string | null;
  phone: string | null;
  groupId: string | null;
  email: string | null;
  organizationTitle: string | null;
}

export interface ContactPersonnelResult {
  id: string;
  contactId: string;
  name: string;
  address: string | null;
  code: string | null;
  phone: string | null;
  groupId: string | null;
  email: string | null;
  organizationTitle: string | null;
}

export interface AttachmentRowDto {
  id: string;
  fileName: string;
  sizeBytes: number;
  contentType: string;
  uploadedByUserId: string;
  uploadedByName: string;
  uploadedAt: string;
}

export interface AttachmentListDto {
  rows: AttachmentRowDto[];
  page: number;
  pageSize: number;
  totalCount: number;
}

export interface AttachmentResult {
  id: string;
  parentType: string;
  parentId: string;
  fileName: string;
  sizeBytes: number;
  contentType: string;
  uploadedByUserId: string;
  uploadedByName: string;
  uploadedAt: string;
}

export interface CommentRowDto {
  id: string;
  content: string;
  authorUserId: string;
  authorName: string;
  createdAt: string;
}

export interface CommentListDto {
  rows: CommentRowDto[];
  page: number;
  pageSize: number;
  totalCount: number;
}

export type ActivityAction = 'Create' | 'Update' | 'Approve' | 'Void';

export interface ActivityRowDto {
  id: string;
  action: ActivityAction;
  userId: string;
  userName: string;
  createdAt: string;
}

export interface ActivityListDto {
  rows: ActivityRowDto[];
  page: number;
  pageSize: number;
  totalCount: number;
}
