export interface CreditTerm {
  id: string;
  organizationId: string;
  name: string;
  dueDays: number;
  isActive: boolean;
  createdAt: string;
}

export interface CreateCreditTermRequest {
  name: string;
  dueDays: number;
}

export interface UpdateCreditTermRequest {
  name: string;
  dueDays: number;
  isActive: boolean;
}

export interface PaymentMode {
  id: string;
  organizationId: string;
  name: string;
  isActive: boolean;
  createdAt: string;
}

export interface CreatePaymentModeRequest {
  name: string;
}

export interface UpdatePaymentModeRequest {
  name: string;
  isActive: boolean;
}

export interface TdsType {
  id: string;
  organizationId: string;
  code: string;
  name: string;
  ratePct: number;
  isActive: boolean;
  createdAt: string;
}

export interface CreateTdsTypeRequest {
  code: string;
  name: string;
  ratePct: number;
}

export interface UpdateTdsTypeRequest {
  code: string;
  name: string;
  ratePct: number;
  isActive: boolean;
}

// Phase 13 -- Workflow (config) > Task Types (erp-module-scan.md line 315).
export interface TaskType {
  id: string;
  organizationId: string;
  name: string;
  color: string;
  isActive: boolean;
  createdAt: string;
}

export interface CreateTaskTypeRequest {
  name: string;
  color: string;
}

export interface UpdateTaskTypeRequest {
  name: string;
  color: string;
  isActive: boolean;
}

// Phase 15 -- CRM (config) > Lead Source / Deal Stage (erp-module-scan.md line 311-312).
export interface LeadSource {
  id: string;
  organizationId: string;
  name: string;
  isActive: boolean;
  createdAt: string;
}

export interface CreateLeadSourceRequest {
  name: string;
}

export interface UpdateLeadSourceRequest {
  name: string;
  isActive: boolean;
}

export interface DealStage {
  id: string;
  organizationId: string;
  name: string;
  sortOrder: number;
  color: string | null;
  isActive: boolean;
  createdAt: string;
}

export interface CreateDealStageRequest {
  name: string;
  sortOrder: number;
  color: string | null;
}

export interface UpdateDealStageRequest {
  name: string;
  sortOrder: number;
  color: string | null;
  isActive: boolean;
}
