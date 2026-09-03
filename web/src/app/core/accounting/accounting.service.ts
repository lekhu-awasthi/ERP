import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';

import { environment } from '../../../environments/environment';
import { MAX_PAGE_SIZE, PagedResult } from '../common/paged-result';
import {
  Account,
  AccountGroup,
  AccountOpeningBalanceDto,
  AccountRootType,
  ApproveCashTransferResult,
  ApproveJournalVoucherResult,
  VoidCashTransferResult,
  VoidJournalVoucherResult,
  BalanceSheetDto,
  BankAccountDto,
  CashFlowSummaryDto,
  CashTransfer,
  CashTransferDetail,
  CashTransferRequest,
  CashTransferStatus,
  CreateAccountGroupRequest,
  CreateAccountGroupResult,
  CreateAccountRequest,
  CreateAccountResult,
  CreateCashTransferResult,
  CreateJournalVoucherResult,
  DetailGeneralLedgerAccountDto,
  GeneralLedgerMasterRowDto,
  GeneralLedgerSummaryRowDto,
  GlSourceDocumentType,
  IncomeStatementDto,
  JournalReportEntryDto,
  VatSummaryReportDto,
  JournalVoucher,
  JournalVoucherDetail,
  JournalVoucherLineInput,
  JournalVoucherRequest,
  JournalVoucherStatus,
  OpeningBalanceLineRequest,
  OpeningBalanceLineResult,
  PostedGlLineDto,
  RatioAnalysisDto,
  TrialBalanceDto,
  UpdateAccountGroupRequest,
  UpdateAccountGroupResult,
  UpdateAccountRequest,
  UpdateAccountResult,
  UpdateCashTransferResult,
  UpdateJournalVoucherResult,
} from './accounting.models';

@Injectable({ providedIn: 'root' })
export class AccountingService {
  private readonly http = inject(HttpClient);

  private baseUrl(organizationId: string): string {
    return `${environment.apiBaseUrl}/api/organizations/${organizationId}`;
  }

  /** Bounded master-data / picker lists (Phase 16c) -- no visible pager, just request everything
   * in one page and unwrap, keeping every caller's Observable<T[]> contract intact. */
  private listAll<T>(url: string, extraParams: Record<string, string> = {}): Observable<T[]> {
    return this.http
      .get<PagedResult<T>>(url, {
        withCredentials: true,
        params: { ...extraParams, page: '1', pageSize: String(MAX_PAGE_SIZE) },
      })
      .pipe(map((result) => result.items));
  }

  listAccountGroups(organizationId: string): Observable<AccountGroup[]> {
    return this.listAll<AccountGroup>(`${this.baseUrl(organizationId)}/account-groups`);
  }

  createAccountGroup(organizationId: string, request: CreateAccountGroupRequest): Observable<CreateAccountGroupResult> {
    return this.http.post<CreateAccountGroupResult>(`${this.baseUrl(organizationId)}/account-groups`, request, {
      withCredentials: true,
    });
  }

  updateAccountGroup(
    organizationId: string,
    id: string,
    request: UpdateAccountGroupRequest,
  ): Observable<UpdateAccountGroupResult> {
    return this.http.put<UpdateAccountGroupResult>(`${this.baseUrl(organizationId)}/account-groups/${id}`, request, {
      withCredentials: true,
    });
  }

  deleteAccountGroup(organizationId: string, id: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl(organizationId)}/account-groups/${id}`, { withCredentials: true });
  }

  listAccounts(organizationId: string, rootType?: AccountRootType, page = 1, pageSize = 50): Observable<PagedResult<Account>> {
    const params: Record<string, string> = { page: String(page), pageSize: String(pageSize) };
    if (rootType) params['rootType'] = rootType;
    return this.http.get<PagedResult<Account>>(`${this.baseUrl(organizationId)}/accounts`, { withCredentials: true, params });
  }

  /** Picker use (e.g. a GL Account dropdown) -- everything in one page, no pager. */
  listAllAccounts(organizationId: string, rootType?: AccountRootType): Observable<Account[]> {
    return this.listAccounts(organizationId, rootType, 1, MAX_PAGE_SIZE).pipe(map((result) => result.items));
  }

  getAccount(organizationId: string, id: string): Observable<Account> {
    return this.http.get<Account>(`${this.baseUrl(organizationId)}/accounts/${id}`, { withCredentials: true });
  }

  createAccount(organizationId: string, request: CreateAccountRequest): Observable<CreateAccountResult> {
    return this.http.post<CreateAccountResult>(`${this.baseUrl(organizationId)}/accounts`, request, {
      withCredentials: true,
    });
  }

  updateAccount(organizationId: string, id: string, request: UpdateAccountRequest): Observable<UpdateAccountResult> {
    return this.http.put<UpdateAccountResult>(`${this.baseUrl(organizationId)}/accounts/${id}`, request, {
      withCredentials: true,
    });
  }

  listJournalVouchers(
    organizationId: string, status?: JournalVoucherStatus, page = 1, pageSize = 50,
  ): Observable<PagedResult<JournalVoucher>> {
    const params: Record<string, string> = { page: String(page), pageSize: String(pageSize) };
    if (status) params['status'] = status;
    return this.http.get<PagedResult<JournalVoucher>>(`${this.baseUrl(organizationId)}/journal-vouchers`, {
      withCredentials: true,
      params,
    });
  }

  getJournalVoucher(organizationId: string, id: string): Observable<JournalVoucherDetail> {
    return this.http.get<JournalVoucherDetail>(`${this.baseUrl(organizationId)}/journal-vouchers/${id}`, {
      withCredentials: true,
    });
  }

  createJournalVoucher(organizationId: string, request: JournalVoucherRequest): Observable<CreateJournalVoucherResult> {
    return this.http.post<CreateJournalVoucherResult>(`${this.baseUrl(organizationId)}/journal-vouchers`, request, {
      withCredentials: true,
    });
  }

  updateJournalVoucher(
    organizationId: string,
    id: string,
    request: JournalVoucherRequest,
  ): Observable<UpdateJournalVoucherResult> {
    return this.http.put<UpdateJournalVoucherResult>(`${this.baseUrl(organizationId)}/journal-vouchers/${id}`, request, {
      withCredentials: true,
    });
  }

  approveJournalVoucher(organizationId: string, id: string): Observable<ApproveJournalVoucherResult> {
    return this.http.post<ApproveJournalVoucherResult>(
      `${this.baseUrl(organizationId)}/journal-vouchers/${id}/approve`,
      null,
      { withCredentials: true },
    );
  }

  voidJournalVoucher(organizationId: string, id: string): Observable<VoidJournalVoucherResult> {
    return this.http.post<VoidJournalVoucherResult>(
      `${this.baseUrl(organizationId)}/journal-vouchers/${id}/void`,
      null,
      { withCredentials: true },
    );
  }

  previewGlPosting(
    organizationId: string,
    date: string,
    reference: string | null,
    lines: JournalVoucherLineInput[],
  ): Observable<PostedGlLineDto[]> {
    return this.http.post<PostedGlLineDto[]>(
      `${this.baseUrl(organizationId)}/journal-vouchers/preview-gl-posting`,
      { date, reference, lines },
      { withCredentials: true },
    );
  }

  listCashTransfers(
    organizationId: string, status?: CashTransferStatus, page = 1, pageSize = 50,
  ): Observable<PagedResult<CashTransfer>> {
    const params: Record<string, string> = { page: String(page), pageSize: String(pageSize) };
    if (status) params['status'] = status;
    return this.http.get<PagedResult<CashTransfer>>(`${this.baseUrl(organizationId)}/cash-transfers`, {
      withCredentials: true,
      params,
    });
  }

  getCashTransfer(organizationId: string, id: string): Observable<CashTransferDetail> {
    return this.http.get<CashTransferDetail>(`${this.baseUrl(organizationId)}/cash-transfers/${id}`, {
      withCredentials: true,
    });
  }

  createCashTransfer(organizationId: string, request: CashTransferRequest): Observable<CreateCashTransferResult> {
    return this.http.post<CreateCashTransferResult>(`${this.baseUrl(organizationId)}/cash-transfers`, request, {
      withCredentials: true,
    });
  }

  updateCashTransfer(
    organizationId: string,
    id: string,
    request: CashTransferRequest,
  ): Observable<UpdateCashTransferResult> {
    return this.http.put<UpdateCashTransferResult>(`${this.baseUrl(organizationId)}/cash-transfers/${id}`, request, {
      withCredentials: true,
    });
  }

  approveCashTransfer(organizationId: string, id: string): Observable<ApproveCashTransferResult> {
    return this.http.post<ApproveCashTransferResult>(
      `${this.baseUrl(organizationId)}/cash-transfers/${id}/approve`,
      null,
      { withCredentials: true },
    );
  }

  voidCashTransfer(organizationId: string, id: string): Observable<VoidCashTransferResult> {
    return this.http.post<VoidCashTransferResult>(
      `${this.baseUrl(organizationId)}/cash-transfers/${id}/void`,
      null,
      { withCredentials: true },
    );
  }

  listBankAccounts(organizationId: string, isActive = true, page = 1, pageSize = 50): Observable<PagedResult<BankAccountDto>> {
    return this.http.get<PagedResult<BankAccountDto>>(`${this.baseUrl(organizationId)}/bank-accounts`, {
      withCredentials: true,
      params: { isActive: String(isActive), page: String(page), pageSize: String(pageSize) },
    });
  }

  listAccountOpeningBalances(
    organizationId: string,
    page = 1,
    pageSize = 50,
  ): Observable<PagedResult<AccountOpeningBalanceDto>> {
    return this.http.get<PagedResult<AccountOpeningBalanceDto>>(`${this.baseUrl(organizationId)}/opening-balances/accounts`, {
      withCredentials: true,
      params: { page: String(page), pageSize: String(pageSize) },
    });
  }

  saveAccountOpeningBalance(
    organizationId: string,
    accountId: string,
    request: OpeningBalanceLineRequest,
  ): Observable<OpeningBalanceLineResult> {
    return this.http.put<OpeningBalanceLineResult>(
      `${this.baseUrl(organizationId)}/opening-balances/accounts/${accountId}`,
      request,
      { withCredentials: true },
    );
  }

  /** `compare` (Phase 26a, FR-9.1) is sent only when it is on -- an absent param means the exact
   * response this screen had before, and the comparison window itself is never sent, only echoed
   * back (see TrialBalanceDto). Params are typed Record<string, string> deliberately: a union
   * including `{}` silently resolves HttpClient.get to its arraybuffer overload (phase-3 bug #4). */
  getTrialBalance(organizationId: string, asOfDate: string, compare = false): Observable<TrialBalanceDto> {
    const params: Record<string, string> = { asOfDate };
    if (compare) params['compare'] = 'true';
    return this.http.get<TrialBalanceDto>(`${this.baseUrl(organizationId)}/reports/trial-balance`, {
      withCredentials: true,
      params,
    });
  }

  exportTrialBalance(organizationId: string, asOfDate: string, compare = false): Observable<Blob> {
    const params: Record<string, string> = { asOfDate };
    if (compare) params['compare'] = 'true';
    return this.http.get(`${this.baseUrl(organizationId)}/reports/trial-balance/export`, {
      withCredentials: true,
      params,
      responseType: 'blob',
    });
  }

  getBalanceSheet(organizationId: string, asOfDate: string, compare = false): Observable<BalanceSheetDto> {
    const params: Record<string, string> = { asOfDate };
    if (compare) params['compare'] = 'true';
    return this.http.get<BalanceSheetDto>(`${this.baseUrl(organizationId)}/reports/balance-sheet`, {
      withCredentials: true,
      params,
    });
  }

  exportBalanceSheet(organizationId: string, asOfDate: string, compare = false): Observable<Blob> {
    const params: Record<string, string> = { asOfDate };
    if (compare) params['compare'] = 'true';
    return this.http.get(`${this.baseUrl(organizationId)}/reports/balance-sheet/export`, {
      withCredentials: true,
      params,
      responseType: 'blob',
    });
  }

  getIncomeStatement(
    organizationId: string, fromDate: string, toDate: string, compare = false,
  ): Observable<IncomeStatementDto> {
    const params: Record<string, string> = { fromDate, toDate };
    if (compare) params['compare'] = 'true';
    return this.http.get<IncomeStatementDto>(`${this.baseUrl(organizationId)}/reports/income-statement`, {
      withCredentials: true,
      params,
    });
  }

  exportIncomeStatement(
    organizationId: string, fromDate: string, toDate: string, compare = false,
  ): Observable<Blob> {
    const params: Record<string, string> = { fromDate, toDate };
    if (compare) params['compare'] = 'true';
    return this.http.get(`${this.baseUrl(organizationId)}/reports/income-statement/export`, {
      withCredentials: true,
      params,
      responseType: 'blob',
    });
  }

  getVatSummaryReport(organizationId: string, fromDate: string, toDate: string): Observable<VatSummaryReportDto> {
    return this.http.get<VatSummaryReportDto>(`${this.baseUrl(organizationId)}/reports/vat-summary`, {
      withCredentials: true,
      params: { fromDate, toDate },
    });
  }

  /** No "current view" vs "full dataset" distinction -- VAT Summary is always the complete fixed
   * 2x3-bucket result (see the endpoint's own comment), so there's only one export variant. */
  exportVatSummaryReport(organizationId: string, fromDate: string, toDate: string): Observable<Blob> {
    return this.http.get(`${this.baseUrl(organizationId)}/reports/vat-summary/export`, {
      withCredentials: true,
      params: { fromDate, toDate },
      responseType: 'blob',
    });
  }

  getCashFlowSummary(
    organizationId: string, fromDate: string, toDate: string, bankAccountId: string | null,
  ): Observable<CashFlowSummaryDto> {
    const params: Record<string, string> = { fromDate, toDate };
    if (bankAccountId) params['bankAccountId'] = bankAccountId;
    return this.http.get<CashFlowSummaryDto>(`${this.baseUrl(organizationId)}/reports/cash-flow-summary`, {
      withCredentials: true,
      params,
    });
  }

  exportCashFlowSummary(
    organizationId: string, fromDate: string, toDate: string, bankAccountId: string | null,
  ): Observable<Blob> {
    const params: Record<string, string> = { fromDate, toDate };
    if (bankAccountId) params['bankAccountId'] = bankAccountId;
    return this.http.get(`${this.baseUrl(organizationId)}/reports/cash-flow-summary/export`, {
      withCredentials: true,
      params,
      responseType: 'blob',
    });
  }

  // Phase 26a -- the four GL reports. All four are Period-filtered and paged, and all four take
  // the Current-View-vs-Full-List export split (`full`) phase-16c established. Params are typed
  // Record<string, string> deliberately: a union including `{}` silently resolves HttpClient.get
  // to its arraybuffer overload (phase-3 bug #4).
  getJournalReport(
    organizationId: string, fromDate: string, toDate: string, documentType: GlSourceDocumentType | null,
    page = 1, pageSize = 50,
  ): Observable<PagedResult<JournalReportEntryDto>> {
    return this.http.get<PagedResult<JournalReportEntryDto>>(
      `${this.baseUrl(organizationId)}/reports/journal-report`,
      { withCredentials: true, params: this.glReportParams(fromDate, toDate, page, pageSize, { documentType }) },
    );
  }

  exportJournalReport(
    organizationId: string, fromDate: string, toDate: string, documentType: GlSourceDocumentType | null,
    full: boolean, page: number, pageSize: number,
  ): Observable<Blob> {
    return this.http.get(`${this.baseUrl(organizationId)}/reports/journal-report/export`, {
      withCredentials: true,
      params: { ...this.glReportParams(fromDate, toDate, page, pageSize, { documentType }), full: String(full) },
      responseType: 'blob',
    });
  }

  getGeneralLedgerSummary(
    organizationId: string, fromDate: string, toDate: string, groupId: string | null, accountId: string | null,
    page = 1, pageSize = 50,
  ): Observable<PagedResult<GeneralLedgerSummaryRowDto>> {
    return this.http.get<PagedResult<GeneralLedgerSummaryRowDto>>(
      `${this.baseUrl(organizationId)}/reports/general-ledger-summary`,
      { withCredentials: true, params: this.glReportParams(fromDate, toDate, page, pageSize, { groupId, accountId }) },
    );
  }

  exportGeneralLedgerSummary(
    organizationId: string, fromDate: string, toDate: string, groupId: string | null, accountId: string | null,
    full: boolean, page: number, pageSize: number,
  ): Observable<Blob> {
    return this.http.get(`${this.baseUrl(organizationId)}/reports/general-ledger-summary/export`, {
      withCredentials: true,
      params: { ...this.glReportParams(fromDate, toDate, page, pageSize, { groupId, accountId }), full: String(full) },
      responseType: 'blob',
    });
  }

  getDetailGeneralLedger(
    organizationId: string, fromDate: string, toDate: string, accountId: string | null,
    page = 1, pageSize = 50,
  ): Observable<PagedResult<DetailGeneralLedgerAccountDto>> {
    return this.http.get<PagedResult<DetailGeneralLedgerAccountDto>>(
      `${this.baseUrl(organizationId)}/reports/detail-general-ledger`,
      { withCredentials: true, params: this.glReportParams(fromDate, toDate, page, pageSize, { accountId }) },
    );
  }

  exportDetailGeneralLedger(
    organizationId: string, fromDate: string, toDate: string, accountId: string | null,
    full: boolean, page: number, pageSize: number,
  ): Observable<Blob> {
    return this.http.get(`${this.baseUrl(organizationId)}/reports/detail-general-ledger/export`, {
      withCredentials: true,
      params: { ...this.glReportParams(fromDate, toDate, page, pageSize, { accountId }), full: String(full) },
      responseType: 'blob',
    });
  }

  getGeneralLedgerMaster(
    organizationId: string, fromDate: string, toDate: string, documentType: GlSourceDocumentType | null,
    page = 1, pageSize = 50,
  ): Observable<PagedResult<GeneralLedgerMasterRowDto>> {
    return this.http.get<PagedResult<GeneralLedgerMasterRowDto>>(
      `${this.baseUrl(organizationId)}/reports/general-ledger-master`,
      { withCredentials: true, params: this.glReportParams(fromDate, toDate, page, pageSize, { documentType }) },
    );
  }

  exportGeneralLedgerMaster(
    organizationId: string, fromDate: string, toDate: string, documentType: GlSourceDocumentType | null,
    full: boolean, page: number, pageSize: number,
  ): Observable<Blob> {
    return this.http.get(`${this.baseUrl(organizationId)}/reports/general-ledger-master/export`, {
      withCredentials: true,
      params: { ...this.glReportParams(fromDate, toDate, page, pageSize, { documentType }), full: String(full) },
      responseType: 'blob',
    });
  }

  private glReportParams(
    fromDate: string,
    toDate: string,
    page: number,
    pageSize: number,
    optional: Record<string, string | null | undefined>,
  ): Record<string, string> {
    const params: Record<string, string> = {
      fromDate,
      toDate,
      page: String(page),
      pageSize: String(pageSize),
    };
    for (const [key, value] of Object.entries(optional)) {
      if (value) {
        params[key] = value;
      }
    }
    return params;
  }

  getRatioAnalysis(organizationId: string, fromDate: string, toDate: string): Observable<RatioAnalysisDto> {
    return this.http.get<RatioAnalysisDto>(`${this.baseUrl(organizationId)}/reports/ratio-analysis`, {
      withCredentials: true,
      params: { fromDate, toDate },
    });
  }

  exportRatioAnalysis(organizationId: string, fromDate: string, toDate: string): Observable<Blob> {
    return this.http.get(`${this.baseUrl(organizationId)}/reports/ratio-analysis/export`, {
      withCredentials: true,
      params: { fromDate, toDate },
      responseType: 'blob',
    });
  }
}
