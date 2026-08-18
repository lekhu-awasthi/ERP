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
