import { DocumentType } from '../sales/sales.models';

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

export type CostTermCategory = 'AdditionalCost' | 'ProductionCost';

export interface CostTerm {
  id: string;
  organizationId: string;
  name: string;
  category: CostTermCategory;
  isActive: boolean;
  createdAt: string;
}

export interface CreateCostTermRequest {
  name: string;
  category: CostTermCategory;
}

export interface UpdateCostTermRequest {
  name: string;
  category: CostTermCategory;
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
// { Name, CategoryId }, referenced from Quotation/Invoice forms and Reports filters. Backend CRUD
// has existed since Phase 2; this phase adds both the read-only Quotation/Invoice tag picker and
// the admin management screen (Configurations > Reporting Tags), closing the pre-existing gap.
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

// Phase 20b -- Custom Status (config) -- tenant-defined status pipeline, one lookup row per
// (DocumentType, Name). Backend CRUD has existed since Phase 2; this phase adds the first
// consumer, the write-side assignment onto Quotation/PurchaseOrder (see sales.models.ts'
// setCustomStatus). No admin management screen yet -- definitions are curl-seeded, same gap
// Phase 20a left for CustomFieldDefinition (see phase-20b-status.md's Known limitations).
export interface CustomStatus {
  id: string;
  organizationId: string;
  name: string;
  documentType: DocumentType;
  isActive: boolean;
  createdAt: string;
}

// Phase 20a -- Custom Fields (config) -- CustomFieldDefinition { Name, Type, ApplicableDocumentTypes,
// ChoiceOptions }, EAV definition CRUD (Phase 2). The value-write side (CustomFieldValue) lives in
// sales.models.ts alongside TransactionReportingTagDto, since DocumentType is defined there.
export type CustomFieldType = 'Text' | 'Number' | 'Description' | 'Choices';

export interface CustomFieldDefinition {
  id: string;
  name: string;
  type: CustomFieldType;
  applicableDocumentTypes: DocumentType[];
  choiceOptions: string[];
  isActive: boolean;
}

// Phase 20d -- Printing Templates (config) -- metadata-only rows (see PrintingTemplate's backend
// doc comment): a Name + one IsDefault flag per (OrganizationId, DocumentType). No layout-
// definition field -- the real product's visual template designer was judged out of scope for
// this sub-phase; the Print action renders one shared layout per document "family" regardless of
// which row here is marked default.
export interface PrintingTemplate {
  id: string;
  organizationId: string;
  name: string;
  documentType: DocumentType;
  isDefault: boolean;
  isActive: boolean;
  createdAt: string;
}

export interface CreatePrintingTemplateRequest {
  name: string;
  documentType: DocumentType;
}

export interface UpdatePrintingTemplateRequest {
  name: string;
  documentType: DocumentType;
  isActive: boolean;
}

// Phase 20d -- Custom Templates (config) -- merge-field text templates, one of four fixed types.
export type CustomTemplateType = 'CustomerBalanceConfirmation' | 'SupplierBalanceConfirmation' | 'TermsAndConditions' | 'Email';

export interface CustomTemplate {
  id: string;
  organizationId: string;
  name: string;
  type: CustomTemplateType;
  body: string;
  isDefault: boolean;
  isActive: boolean;
  createdAt: string;
}

export interface CreateCustomTemplateRequest {
  name: string;
  type: CustomTemplateType;
  body: string;
}

export interface UpdateCustomTemplateRequest {
  name: string;
  type: CustomTemplateType;
  body: string;
  isActive: boolean;
}
