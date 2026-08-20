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
