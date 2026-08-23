// Phase 15 (Deals, the CRM module's first feature).

export type DealStatus = 'Pending' | 'Won' | 'Lost';

export interface DealAssigneeDto {
  userId: string;
  name: string;
}

export interface DealRow {
  id: string;
  title: string;
  contactId: string;
  contactName: string;
  leadSourceId: string | null;
  leadSourceName: string | null;
  description: string | null;
  expectedRevenue: number;
  expectedClosingDate: string | null;
  stageId: string | null;
  stageName: string | null;
  stageColor: string | null;
  status: DealStatus;
  isPrivate: boolean;
  closingDate: string | null;
  createdByUserId: string;
  createdByName: string;
  createdAt: string;
  assignees: DealAssigneeDto[];
}

export interface DealListDto {
  rows: DealRow[];
  page: number;
  pageSize: number;
  totalCount: number;
}

export interface CreateDealRequest {
  contactId: string;
  title: string;
  assigneeUserIds: string[];
  leadSourceId: string | null;
  description: string | null;
  expectedRevenue: number;
  expectedClosingDate: string | null;
  isPrivate: boolean;
}

export interface CreateDealResult {
  id: string;
  title: string;
  status: DealStatus;
  createdAt: string;
}

export interface UpdateDealRequest {
  title: string;
  assigneeUserIds: string[];
  leadSourceId: string | null;
  description: string | null;
  expectedRevenue: number;
  expectedClosingDate: string | null;
  isPrivate: boolean;
}

export interface MoveDealToStageRequest {
  dealStageId: string;
}

// Phase 18 (SMS module + Contact Personnel/Attachments/Comments/Activities).

export type SmsAudienceMode = 'All' | 'ContactGroup' | 'Custom';
export type SmsCreditLedgerEntryType = 'ManualAdjustment' | 'Send';

export interface SmsLogRowDto {
  id: string;
  batchId: string;
  contactId: string;
  contactName: string;
  title: string;
  content: string;
  phoneNumber: string;
  creditsUsed: number;
  sentAt: string;
}

export interface SmsLogListDto {
  rows: SmsLogRowDto[];
  page: number;
  pageSize: number;
  totalCount: number;
}

export interface SmsTemplateRowDto {
  id: string;
  title: string;
  content: string;
  createdAt: string;
}

export interface SmsTemplateListDto {
  rows: SmsTemplateRowDto[];
  page: number;
  pageSize: number;
  totalCount: number;
}

export interface SmsTemplateRequest {
  title: string;
  content: string;
}

export interface SmsTemplateResult {
  id: string;
  title: string;
  content: string;
}

export interface SmsCreditLedgerRowDto {
  id: string;
  type: SmsCreditLedgerEntryType;
  changeAmount: number;
  reason: string | null;
  createdByUserId: string;
  createdByName: string;
  createdAt: string;
}

/** balance is server-computed over the FULL ledger -- always trust this field for the headline
 * number, never sum the current page's rows client-side (CLAUDE.md's Phase 16c pagination gotcha). */
export interface SmsCreditLedgerDto {
  balance: number;
  rows: SmsCreditLedgerRowDto[];
  page: number;
  pageSize: number;
  totalCount: number;
}

export interface AdjustSmsCreditRequest {
  changeAmount: number;
  reason: string | null;
}

export interface SmsCreditAdjustmentResult {
  id: string;
  changeAmount: number;
  newBalance: number;
}

export interface SendSmsRequest {
  audienceMode: SmsAudienceMode;
  contactGroupId: string | null;
  contactIds: string[] | null;
  templateId: string | null;
  title: string;
  content: string;
}

export interface SendSmsResult {
  batchId: string;
  recipientCount: number;
  creditsUsed: number;
  remainingBalance: number;
}
