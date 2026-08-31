import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';

import { environment } from '../../../environments/environment';
import { MAX_PAGE_SIZE, PagedResult } from '../common/paged-result';
import { CustomFieldValueDto, CustomFieldValueInput, DocumentType, TransactionReportingTagDto } from '../sales/sales.models';
import {
  Bank,
  CreateBankRequest,
  CreateCostTermRequest,
  CreateCreditTermRequest,
  CreateCustomTemplateRequest,
  CreateDealStageRequest,
  CreateLeadSourceRequest,
  CreatePaymentModeRequest,
  CreatePrintingTemplateRequest,
  CreateTaskTypeRequest,
  CreateTdsTypeRequest,
  CostTerm,
  CreditTerm,
  CreateReportingTagCategoryRequest,
  CreateReportingTagOptionRequest,
  CustomFieldDefinition,
  CustomStatus,
  CustomTemplate,
  DealStage,
  LeadSource,
  PaymentMode,
  PrintingTemplate,
  ReportingTagCategory,
  ReportingTagOption,
  TaskType,
  TdsType,
  UpdateBankRequest,
  UpdateCostTermRequest,
  UpdateCreditTermRequest,
  UpdateCustomTemplateRequest,
  UpdateDealStageRequest,
  UpdateLeadSourceRequest,
  UpdatePaymentModeRequest,
  UpdatePrintingTemplateRequest,
  UpdateReportingTagCategoryRequest,
  UpdateReportingTagOptionRequest,
  UpdateTaskTypeRequest,
  UpdateTdsTypeRequest,
} from './configuration.models';

@Injectable({ providedIn: 'root' })
export class ConfigurationService {
  private readonly http = inject(HttpClient);

  private baseUrl(organizationId: string): string {
    return `${environment.apiBaseUrl}/api/organizations/${organizationId}/configuration`;
  }

  /** Lookup screens (Phase 16c) are bounded master data -- no visible pager, just request
   * everything in one page and unwrap, keeping every caller's Observable<T[]> contract intact. */
  private listAll<T>(url: string): Observable<T[]> {
    return this.http
      .get<PagedResult<T>>(url, { withCredentials: true, params: { page: '1', pageSize: String(MAX_PAGE_SIZE) } })
      .pipe(map((result) => result.items));
  }

  listCreditTerms(organizationId: string): Observable<CreditTerm[]> {
    return this.listAll<CreditTerm>(`${this.baseUrl(organizationId)}/credit-terms`);
  }

  createCreditTerm(organizationId: string, request: CreateCreditTermRequest): Observable<CreditTerm> {
    return this.http.post<CreditTerm>(`${this.baseUrl(organizationId)}/credit-terms`, request, {
      withCredentials: true,
    });
  }

  updateCreditTerm(organizationId: string, id: string, request: UpdateCreditTermRequest): Observable<CreditTerm> {
    return this.http.put<CreditTerm>(`${this.baseUrl(organizationId)}/credit-terms/${id}`, request, {
      withCredentials: true,
    });
  }

  deleteCreditTerm(organizationId: string, id: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl(organizationId)}/credit-terms/${id}`, { withCredentials: true });
  }

  listCostTerms(organizationId: string): Observable<CostTerm[]> {
    return this.listAll<CostTerm>(`${this.baseUrl(organizationId)}/cost-terms`);
  }

  createCostTerm(organizationId: string, request: CreateCostTermRequest): Observable<CostTerm> {
    return this.http.post<CostTerm>(`${this.baseUrl(organizationId)}/cost-terms`, request, {
      withCredentials: true,
    });
  }

  updateCostTerm(organizationId: string, id: string, request: UpdateCostTermRequest): Observable<CostTerm> {
    return this.http.put<CostTerm>(`${this.baseUrl(organizationId)}/cost-terms/${id}`, request, {
      withCredentials: true,
    });
  }

  deleteCostTerm(organizationId: string, id: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl(organizationId)}/cost-terms/${id}`, { withCredentials: true });
  }

  listPaymentModes(organizationId: string): Observable<PaymentMode[]> {
    return this.listAll<PaymentMode>(`${this.baseUrl(organizationId)}/payment-modes`);
  }

  createPaymentMode(organizationId: string, request: CreatePaymentModeRequest): Observable<PaymentMode> {
    return this.http.post<PaymentMode>(`${this.baseUrl(organizationId)}/payment-modes`, request, {
      withCredentials: true,
    });
  }

  updatePaymentMode(organizationId: string, id: string, request: UpdatePaymentModeRequest): Observable<PaymentMode> {
    return this.http.put<PaymentMode>(`${this.baseUrl(organizationId)}/payment-modes/${id}`, request, {
      withCredentials: true,
    });
  }

  deletePaymentMode(organizationId: string, id: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl(organizationId)}/payment-modes/${id}`, { withCredentials: true });
  }

  listBanks(organizationId: string): Observable<Bank[]> {
    return this.listAll<Bank>(`${this.baseUrl(organizationId)}/banks`);
  }

  createBank(organizationId: string, request: CreateBankRequest): Observable<Bank> {
    return this.http.post<Bank>(`${this.baseUrl(organizationId)}/banks`, request, { withCredentials: true });
  }

  updateBank(organizationId: string, id: string, request: UpdateBankRequest): Observable<Bank> {
    return this.http.put<Bank>(`${this.baseUrl(organizationId)}/banks/${id}`, request, { withCredentials: true });
  }

  deleteBank(organizationId: string, id: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl(organizationId)}/banks/${id}`, { withCredentials: true });
  }

  listTdsTypes(organizationId: string): Observable<TdsType[]> {
    return this.listAll<TdsType>(`${this.baseUrl(organizationId)}/tds-types`);
  }

  createTdsType(organizationId: string, request: CreateTdsTypeRequest): Observable<TdsType> {
    return this.http.post<TdsType>(`${this.baseUrl(organizationId)}/tds-types`, request, { withCredentials: true });
  }

  updateTdsType(organizationId: string, id: string, request: UpdateTdsTypeRequest): Observable<TdsType> {
    return this.http.put<TdsType>(`${this.baseUrl(organizationId)}/tds-types/${id}`, request, { withCredentials: true });
  }

  deleteTdsType(organizationId: string, id: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl(organizationId)}/tds-types/${id}`, { withCredentials: true });
  }

  listTaskTypes(organizationId: string): Observable<TaskType[]> {
    return this.listAll<TaskType>(`${this.baseUrl(organizationId)}/task-types`);
  }

  createTaskType(organizationId: string, request: CreateTaskTypeRequest): Observable<TaskType> {
    return this.http.post<TaskType>(`${this.baseUrl(organizationId)}/task-types`, request, { withCredentials: true });
  }

  updateTaskType(organizationId: string, id: string, request: UpdateTaskTypeRequest): Observable<TaskType> {
    return this.http.put<TaskType>(`${this.baseUrl(organizationId)}/task-types/${id}`, request, {
      withCredentials: true,
    });
  }

  deleteTaskType(organizationId: string, id: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl(organizationId)}/task-types/${id}`, { withCredentials: true });
  }

  listLeadSources(organizationId: string): Observable<LeadSource[]> {
    return this.listAll<LeadSource>(`${this.baseUrl(organizationId)}/lead-sources`);
  }

  createLeadSource(organizationId: string, request: CreateLeadSourceRequest): Observable<LeadSource> {
    return this.http.post<LeadSource>(`${this.baseUrl(organizationId)}/lead-sources`, request, { withCredentials: true });
  }

  updateLeadSource(organizationId: string, id: string, request: UpdateLeadSourceRequest): Observable<LeadSource> {
    return this.http.put<LeadSource>(`${this.baseUrl(organizationId)}/lead-sources/${id}`, request, {
      withCredentials: true,
    });
  }

  deleteLeadSource(organizationId: string, id: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl(organizationId)}/lead-sources/${id}`, { withCredentials: true });
  }

  listDealStages(organizationId: string): Observable<DealStage[]> {
    return this.listAll<DealStage>(`${this.baseUrl(organizationId)}/deal-stages`);
  }

  createDealStage(organizationId: string, request: CreateDealStageRequest): Observable<DealStage> {
    return this.http.post<DealStage>(`${this.baseUrl(organizationId)}/deal-stages`, request, { withCredentials: true });
  }

  updateDealStage(organizationId: string, id: string, request: UpdateDealStageRequest): Observable<DealStage> {
    return this.http.put<DealStage>(`${this.baseUrl(organizationId)}/deal-stages/${id}`, request, {
      withCredentials: true,
    });
  }

  deleteDealStage(organizationId: string, id: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl(organizationId)}/deal-stages/${id}`, { withCredentials: true });
  }

  // Reporting Tags (Phase 2 backend) -- Phase 19 is the first frontend consumer: the picker on
  // Quotation/Invoice detail pages and every affected report's filter drawer, plus the admin
  // management screen (Configurations > Reporting Tags) below.
  listReportingTagCategories(organizationId: string): Observable<ReportingTagCategory[]> {
    return this.listAll<ReportingTagCategory>(`${this.baseUrl(organizationId)}/reporting-tag-categories`);
  }

  createReportingTagCategory(
    organizationId: string,
    request: CreateReportingTagCategoryRequest,
  ): Observable<ReportingTagCategory> {
    return this.http.post<ReportingTagCategory>(`${this.baseUrl(organizationId)}/reporting-tag-categories`, request, {
      withCredentials: true,
    });
  }

  updateReportingTagCategory(
    organizationId: string,
    id: string,
    request: UpdateReportingTagCategoryRequest,
  ): Observable<ReportingTagCategory> {
    return this.http.put<ReportingTagCategory>(
      `${this.baseUrl(organizationId)}/reporting-tag-categories/${id}`,
      request,
      { withCredentials: true },
    );
  }

  deleteReportingTagCategory(organizationId: string, id: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl(organizationId)}/reporting-tag-categories/${id}`, {
      withCredentials: true,
    });
  }

  listReportingTagOptions(organizationId: string): Observable<ReportingTagOption[]> {
    return this.listAll<ReportingTagOption>(`${this.baseUrl(organizationId)}/reporting-tag-options`);
  }

  createReportingTagOption(
    organizationId: string,
    request: CreateReportingTagOptionRequest,
  ): Observable<ReportingTagOption> {
    return this.http.post<ReportingTagOption>(`${this.baseUrl(organizationId)}/reporting-tag-options`, request, {
      withCredentials: true,
    });
  }

  updateReportingTagOption(
    organizationId: string,
    id: string,
    request: UpdateReportingTagOptionRequest,
  ): Observable<ReportingTagOption> {
    return this.http.put<ReportingTagOption>(`${this.baseUrl(organizationId)}/reporting-tag-options/${id}`, request, {
      withCredentials: true,
    });
  }

  deleteReportingTagOption(organizationId: string, id: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl(organizationId)}/reporting-tag-options/${id}`, {
      withCredentials: true,
    });
  }

  getTransactionReportingTags(
    organizationId: string, documentType: DocumentType, documentId: string,
  ): Observable<TransactionReportingTagDto[]> {
    return this.http.get<TransactionReportingTagDto[]>(
      `${this.baseUrl(organizationId)}/reporting-tags/${documentType}/${documentId}`,
      { withCredentials: true },
    );
  }

  setTransactionReportingTags(
    organizationId: string, documentType: DocumentType, documentId: string, tagOptionIds: string[],
  ): Observable<void> {
    return this.http.put<void>(
      `${this.baseUrl(organizationId)}/reporting-tags/${documentType}/${documentId}`,
      { tagOptionIds },
      { withCredentials: true },
    );
  }

  // --- Custom Fields (Phase 20a) ---

  listCustomFieldDefinitions(organizationId: string): Observable<CustomFieldDefinition[]> {
    return this.listAll<CustomFieldDefinition>(`${this.baseUrl(organizationId)}/custom-field-definitions`);
  }

  getCustomFieldValues(
    organizationId: string, documentType: DocumentType, documentId: string,
  ): Observable<CustomFieldValueDto[]> {
    return this.http.get<CustomFieldValueDto[]>(
      `${this.baseUrl(organizationId)}/custom-field-values/${documentType}/${documentId}`,
      { withCredentials: true },
    );
  }

  setCustomFieldValues(
    organizationId: string, documentType: DocumentType, documentId: string, values: CustomFieldValueInput[],
  ): Observable<void> {
    return this.http.put<void>(
      `${this.baseUrl(organizationId)}/custom-field-values/${documentType}/${documentId}`,
      { values },
      { withCredentials: true },
    );
  }

  // --- Custom Status (Phase 20b) ---

  listCustomStatuses(organizationId: string): Observable<CustomStatus[]> {
    return this.listAll<CustomStatus>(`${this.baseUrl(organizationId)}/custom-statuses`);
  }

  /** Write-only, no matching GET -- the target document's own DTO already carries
   * customStatusId (Quotation, PurchaseOrder). Pass customStatusId: null to clear it. */
  setCustomStatus(
    organizationId: string, documentType: DocumentType, documentId: string, customStatusId: string | null,
  ): Observable<void> {
    return this.http.put<void>(
      `${this.baseUrl(organizationId)}/custom-status/${documentType}/${documentId}`,
      { customStatusId },
      { withCredentials: true },
    );
  }

  // --- Printing Templates (Phase 20d) ---

  listPrintingTemplates(organizationId: string): Observable<PrintingTemplate[]> {
    return this.listAll<PrintingTemplate>(`${this.baseUrl(organizationId)}/printing-templates`);
  }

  createPrintingTemplate(organizationId: string, request: CreatePrintingTemplateRequest): Observable<PrintingTemplate> {
    return this.http.post<PrintingTemplate>(`${this.baseUrl(organizationId)}/printing-templates`, request, {
      withCredentials: true,
    });
  }

  updatePrintingTemplate(
    organizationId: string, id: string, request: UpdatePrintingTemplateRequest,
  ): Observable<PrintingTemplate> {
    return this.http.put<PrintingTemplate>(`${this.baseUrl(organizationId)}/printing-templates/${id}`, request, {
      withCredentials: true,
    });
  }

  setDefaultPrintingTemplate(organizationId: string, id: string): Observable<void> {
    return this.http.put<void>(`${this.baseUrl(organizationId)}/printing-templates/${id}/default`, {}, {
      withCredentials: true,
    });
  }

  deletePrintingTemplate(organizationId: string, id: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl(organizationId)}/printing-templates/${id}`, {
      withCredentials: true,
    });
  }

  // --- Custom Templates (Phase 20d) ---

  listCustomTemplates(organizationId: string): Observable<CustomTemplate[]> {
    return this.listAll<CustomTemplate>(`${this.baseUrl(organizationId)}/custom-templates`);
  }

  createCustomTemplate(organizationId: string, request: CreateCustomTemplateRequest): Observable<CustomTemplate> {
    return this.http.post<CustomTemplate>(`${this.baseUrl(organizationId)}/custom-templates`, request, {
      withCredentials: true,
    });
  }

  updateCustomTemplate(organizationId: string, id: string, request: UpdateCustomTemplateRequest): Observable<CustomTemplate> {
    return this.http.put<CustomTemplate>(`${this.baseUrl(organizationId)}/custom-templates/${id}`, request, {
      withCredentials: true,
    });
  }

  setDefaultCustomTemplate(organizationId: string, id: string): Observable<void> {
    return this.http.put<void>(`${this.baseUrl(organizationId)}/custom-templates/${id}/default`, {}, {
      withCredentials: true,
    });
  }

  deleteCustomTemplate(organizationId: string, id: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl(organizationId)}/custom-templates/${id}`, {
      withCredentials: true,
    });
  }
}
