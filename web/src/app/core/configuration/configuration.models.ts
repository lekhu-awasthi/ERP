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
  requiresChequeDetails: boolean;
  createdAt: string;
}

export interface CreatePaymentModeRequest {
  name: string;
  requiresChequeDetails?: boolean;
}

export interface UpdatePaymentModeRequest {
  name: string;
  isActive: boolean;
  requiresChequeDetails: boolean;
}

// Phase 17 -- Bank lookup (docs/phase-17-status.md decision #3), populates a Bank-kind Account's
// "Select Bank" picker.
export interface Bank {
  id: string;
  organizationId: string;
  name: string;
  isActive: boolean;
  createdAt: string;
}

export interface CreateBankRequest {
  name: string;
}

export interface UpdateBankRequest {
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

// Phase 19 -- Reporting Tags (config) -- ReportingTagCategory { Name } + ReportingTagOption
// { Name, CategoryId }, referenced from Quotation/Invoice forms and Reports filters.
export interface ReportingTagCategory {
  id: string;
  organizationId: string;
  name: string;
  isActive: boolean;
  createdAt: string;
}

export interface CreateReportingTagCategoryRequest {
  name: string;
}

export interface UpdateReportingTagCategoryRequest {
  name: string;
  isActive: boolean;
}

export interface ReportingTagOption {
  id: string;
  organizationId: string;
  name: string;
  categoryId: string;
  isActive: boolean;
  createdAt: string;
}

export interface CreateReportingTagOptionRequest {
  name: string;
  categoryId: string;
}

export interface UpdateReportingTagOptionRequest {
  name: string;
  categoryId: string;
  isActive: boolean;
}
