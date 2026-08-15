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
}
